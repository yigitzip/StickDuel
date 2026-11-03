using System;
using System.Collections.Generic;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Two-handed spear with its own physics (nothing from <see cref="Sword"/>). Held, it is a
    /// kinematic pole: the rear hand near the butt, the lead hand up the shaft, both placed by
    /// the rig from <see cref="RearHand"/> / <see cref="LeadHand"/>. It turns towards the aim
    /// slower than the katana (it is heavy), its shaft physically blocks blades, and only the
    /// spearhead trigger hurts.
    /// Moves: Shift jabs at once, holding it keeps jabbing (the shaft slides through the lead hand
    /// while the rear hand drives it, with a small step in); a fast swing of the point also cuts;
    /// F hops into a full turn with a red trail and finishes with a jab, and both always crit;
    /// right click throws it point first, and it sticks where it lands until picked up. No charge.
    /// Spear space: origin = rear hand, +x towards the point.
    /// Art: Resources/Weapons/IronSpear.png, 400 px/unit, butt at x=2px, 2.5 units long.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class SpearWeapon : MonoBehaviour, IWeaponBehaviour
    {
        private const float PixelsPerUnit = 400f;
        private const float ButtPixel = 2f;
        private const float Butt = -0.3f;
        private const float TipX = 2.2f;
        private const float HeadStart = 1.75f; // only the front of the spearhead cuts
        private const float HeadThickness = 0.16f;
        private const float ShaftThickness = 0.06f;
        private const float HandGap = 0.45f;
        private const float MaxSpeed = 22f;

        [Header("Hold")]
        [Tooltip("Midpoint between the hands: this far from the shoulder along the spear, and this far down.")]
        [SerializeField] private float holdForward = 0.25f;
        [SerializeField] private float holdDrop = 0.12f;
        [Tooltip("How sluggishly the heavy spear follows the aim.")]
        [SerializeField, Min(0.01f)] private float turnSmoothTime = 0.07f;
        [SerializeField, Min(1f)] private float maxTurnSpeed = 650f;
        [SerializeField, Range(-90f, 0f)] private float minPitch = -55f;
        [SerializeField, Range(0f, 90f)] private float maxPitch = 70f;

        [Header("Jab (Shift)")]
        [SerializeField] private float jabSlide = 0.6f;
        [SerializeField, Min(0.01f)] private float jabOut = 0.06f;
        [SerializeField, Min(0f)] private float jabHold = 0.05f;
        [SerializeField, Min(0.01f)] private float jabBack = 0.12f;
        [Tooltip("Gap between jabs while Shift is held.")]
        [SerializeField, Min(0f)] private float jabCooldown = 0.1f;
        [SerializeField, Min(0f)] private float jabDamage = 16f;
        [SerializeField, Min(0f)] private float jabKnockback = 1.2f;
        [Tooltip("Small step into each jab (lunge speed and length multipliers; grounded only).")]
        [SerializeField] private Vector2 jabStep = new Vector2(0.6f, 0.35f);

        [Header("Swing")]
        [Tooltip("The point must move at least this fast to cut outside a jab or spin.")]
        [SerializeField, Min(0f)] private float minSwingSpeed = 5f;
        [SerializeField, Min(0f)] private float swingDamagePerSpeed = 1.6f;
        [SerializeField, Min(0f)] private float maxSwingDamage = 18f;
        [SerializeField, Min(0f)] private float swingHitCooldown = 0.6f;
        [SerializeField, Min(0f)] private float recoveryAfterSwingHit = 0.3f;

        [Header("Spin")]
        [SerializeField, Min(0.05f)] private float spinDuration = 0.7f;
        [SerializeField, Min(0f)] private float spinCooldown = 1.2f;
        [SerializeField, Min(0f)] private float spinStaminaCost = 35f;
        [SerializeField, Min(0f)] private float spinDamage = 26f;
        [Tooltip("Spin hits and the jab that ends the spin always crit for this much.")]
        [SerializeField, Min(1f)] private float critMultiplier = 1.5f;
        [Tooltip("Upward speed of the little hop that starts the spin.")]
        [SerializeField, Min(0f)] private float spinHop = 2.5f;
        [SerializeField] private Color spinTrailColor = new Color(1f, 0.15f, 0.12f);

        [Header("Throw")]
        [SerializeField, Min(0f)] private float throwGravity = 0.5f;
        [SerializeField, Min(0f)] private float throwDamage = 30f;

        [Header("Hits")]
        [SerializeField, Min(1f)] private float headMultiplier = 1.5f;
        [SerializeField, Min(1f)] private float backstabMultiplier = 1.5f;
        [SerializeField, Min(0f)] private float baseKnockback = 3f;

        private static Sprite art;

        private readonly Dictionary<Fighter, float> lastSwingHit = new Dictionary<Fighter, float>();
        private readonly HashSet<Fighter> attackHits = new HashSet<Fighter>(); // one hit per victim per jab / spin / throw
        private Rigidbody2D body;
        private Collider2D head;
        private Collider2D shaft;
        private TrailRenderer trail;
        private Gradient normalTrail, spinTrail;
        private Fighter owner;
        private Sword replacedSword;
        private float angle, angleVelocity;
        private int lastFacing = 1;
        private float slide;
        private float jabTimer = -1f, jabStartSlide, jabReadyTime;
        private bool critJab; // the jab that ends a spin
        private float spinTimer, spinStart, spinReadyTime;
        private bool spinHalfway;
        private int spinFacing = 1;
        private bool flying;
        private float recoverUntil;
        private Vector2 previousTip, tipVelocity;

        public WeaponType Type => WeaponType.Spear;
        public Fighter Owner => owner;
        public bool IsEquipped => owner != null;
        public bool IsHeld { get; private set; }
        /// <summary>Thrown and not yet landed.</summary>
        public bool IsFlying => flying;
        public bool IsAttacking => flying || jabTimer >= 0f || spinTimer > 0f;
        public Vector2 TipPosition => transform.TransformPoint(new Vector3(TipX, 0f, 0f));
        /// <summary>Where the rig puts the hands (interpolated transform = what is drawn).</summary>
        public Vector2 RearHand => transform.position;
        /// <summary>The shaft slides through the lead hand on a jab: it moves 60% as far as the rear hand.</summary>
        public Vector2 LeadHand => transform.TransformPoint(new Vector3(HandGap - 0.4f * slide, 0f, 0f));
        /// <summary>
        /// 0→1 through a spin (eased: slow start and finish), -1 when not spinning. The rig turns
        /// the body by this same value, so the body and the spear stay in step all the way round.
        /// </summary>
        public float SpinProgress => spinTimer > 0f ? 0.5f - 0.5f * Mathf.Cos(Mathf.PI * (1f - spinTimer / spinDuration)) : -1f;
        /// <summary>0→1 how far the jab is out (drives the forward lean in the rig).</summary>
        public float JabExtension => jabSlide > 0f ? Mathf.Clamp01(slide / jabSlide) : 0f;
        private bool JabLive => jabTimer >= 0f && jabTimer < jabOut + jabHold + jabBack * 0.3f;
        private Vector2 BodyTip => body.position + (Vector2)(Quaternion.Euler(0f, 0f, body.rotation) * new Vector3(TipX, 0f, 0f));

        /// <summary>The spear art (also the menu card). Null, with an error, if the texture is missing.</summary>
        public static Sprite Art
        {
            get
            {
                if (art == null)
                {
                    var texture = Resources.Load<Texture2D>("Weapons/IronSpear");
                    if (texture == null)
                    {
                        Debug.LogError("SpearWeapon: Resources/Weapons/IronSpear.png is missing.");
                        return null;
                    }

                    // Pivot on the rear hand, 0.3 units up from the butt, on the shaft axis.
                    art = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2((ButtPixel - Butt * PixelsPerUnit) / texture.width, 0.5f), PixelsPerUnit);
                }

                return art;
            }
        }

        /// <summary>Builds the spear object for <paramref name="owner"/> (unequipped). Null, with an error, if the art is missing.</summary>
        public static SpearWeapon Create(Fighter owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            Sprite sprite = Art;
            if (sprite == null)
            {
                return null;
            }

            var go = new GameObject(owner.name + " Spear") { layer = LayerMask.NameToLayer(Fighter.SwordLayer) };
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 14; // between the back arm and the front arm, like the katana
            if (owner.sword != null && owner.sword.TryGetComponent(out SpriteRenderer katana))
            {
                renderer.sharedMaterial = katana.sharedMaterial;
                renderer.sortingLayerID = katana.sortingLayerID;
                renderer.sortingOrder = katana.sortingOrder;
            }

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.mass = 1.2f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // Shaft: solid on the Sword layer (blocks blades, lands on the floor, never touches fighters).
            var pole = go.AddComponent<BoxCollider2D>();
            pole.offset = new Vector2((Butt + TipX) * 0.5f, 0f);
            pole.size = new Vector2(TipX - Butt, ShaftThickness);

            // Point: the only part that hurts, a trigger on the SwordHit layer (sees fighters only).
            var point = new GameObject("Head") { layer = LayerMask.NameToLayer(Fighter.SwordHitLayer) };
            point.transform.SetParent(go.transform, false);
            var trigger = point.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.offset = new Vector2((HeadStart + TipX) * 0.5f, 0f);
            trigger.size = new Vector2(TipX - HeadStart, HeadThickness);

            // Slash ribbon from the point, drawn by the same BladeTrail the katana uses.
            var tip = new GameObject("Tip");
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = new Vector3(TipX, 0f, 0f);
            var ribbon = tip.AddComponent<TrailRenderer>();
            TrailRenderer katanaTrail = owner.sword != null ? owner.sword.GetComponentInChildren<TrailRenderer>(true) : null;
            if (katanaTrail != null)
            {
                ribbon.sharedMaterial = katanaTrail.sharedMaterial;
                ribbon.sortingLayerID = katanaTrail.sortingLayerID;
            }
            else
            {
                ribbon.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            }

            ribbon.sortingOrder = 13;
            ribbon.time = 0.12f;
            ribbon.widthCurve = AnimationCurve.EaseInOut(0f, 0.16f, 1f, 0f);
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(owner.color, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            ribbon.colorGradient = gradient;
            ribbon.emitting = false;
            BladeTrail.Attach(ribbon, go.transform);

            go.SetActive(false); // Equip turns it on
            return go.AddComponent<SpearWeapon>();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            shaft = GetComponent<BoxCollider2D>();
            head = transform.Find("Head").GetComponent<Collider2D>();
            trail = transform.Find("Tip").GetComponent<TrailRenderer>();
            normalTrail = trail.colorGradient;
            spinTrail = new Gradient();
            spinTrail.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(spinTrailColor, 0.25f), new GradientColorKey(spinTrailColor, 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
        }

        /// <summary>Takes the place of the owner's sword (hidden, restored by <see cref="Unequip"/>).</summary>
        public void Equip(Fighter newOwner)
        {
            if (newOwner == null) throw new ArgumentNullException(nameof(newOwner));
            owner = newOwner;
            if (owner.sword != null)
            {
                replacedSword = owner.sword;
                replacedSword.gameObject.SetActive(false);
                owner.sword = null;
            }

            owner.spear = this;
            gameObject.SetActive(true);
            Regrip();
        }

        public void Unequip()
        {
            if (owner == null)
            {
                return;
            }

            if (owner.spear == this)
            {
                owner.spear = null;
            }

            if (replacedSword != null)
            {
                owner.sword = replacedSword;
                replacedSword.gameObject.SetActive(true);
                replacedSword.Regrip();
                replacedSword = null;
            }

            owner = null;
            IsHeld = false;
            gameObject.SetActive(false);
        }

        /// <summary>Back in both hands, in the guard pose (round start, picked up).</summary>
        public void Regrip()
        {
            IsHeld = true;
            flying = false;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            head.enabled = true;
            jabTimer = -1f;
            critJab = false;
            spinTimer = 0f;
            slide = 0f;
            recoverUntil = 0f;
            lastSwingHit.Clear();
            if (owner == null)
            {
                return;
            }

            lastFacing = owner.Facing;
            angle = lastFacing > 0 ? -10f : 190f;
            angleVelocity = 0f;
            Vector2 rear = HoldPoint(Direction(angle)) + Direction(angle) * (-HandGap * 0.5f);
            body.position = rear;
            body.rotation = angle;
            transform.SetPositionAndRotation(rear, Quaternion.Euler(0f, 0f, angle));
            previousTip = BodyTip;
        }

        /// <summary>Spin attack: a full turn with the spear, then a jab. False when it can't (busy, cooldown, no stamina).</summary>
        public bool TrySpin()
        {
            if (!IsHeld || owner == null || !owner.ControlsEnabled || owner.IsDead || owner.IsRolling || owner.IsStaggered
                || spinTimer > 0f || jabTimer >= 0f || Time.time < spinReadyTime)
            {
                return false;
            }

            if (!owner.TrySpendStamina(spinStaminaCost))
            {
                Tired();
                return false;
            }

            spinTimer = spinDuration;
            spinStart = angle;
            spinFacing = owner.Facing;
            spinHalfway = false;
            attackHits.Clear();
            if (owner.IsGrounded)
            {
                // A little hop: the turn happens off the ground and lands into the finishing jab.
                Vector2 v = owner.Body.linearVelocity;
                owner.Body.linearVelocity = new Vector2(v.x, Mathf.Max(v.y, spinHop));
            }

            if (Fx.Instance != null)
            {
                Fx.Instance.Swoosh(TipPosition, MaxSpeed);
            }

            return true;
        }

        /// <summary>Lets go with <paramref name="velocity"/>; it flies point first and cuts until it lands.</summary>
        public void Throw(Vector2 velocity)
        {
            if (!IsHeld)
            {
                return;
            }

            Release();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = throwGravity;
            body.linearDamping = 0f;
            body.angularVelocity = 0f;
            body.rotation = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
            body.linearVelocity = velocity;
            flying = true;
            head.enabled = true;
            attackHits.Clear();
        }

        /// <summary>Falls from a dead owner's hands.</summary>
        public void Drop()
        {
            if (!IsHeld)
            {
                return;
            }

            Release();
            Fall();
            body.linearVelocity *= 0.3f;
            body.angularVelocity = UnityEngine.Random.Range(-200f, 200f);
        }

        /// <summary>Closest point of the shaft (for picking it up off the floor).</summary>
        public Vector2 ClosestPoint(Vector2 point) => shaft.ClosestPoint(point);

        private void Release()
        {
            IsHeld = false;
            jabTimer = -1f;
            critJab = false;
            spinTimer = 0f;
            slide = 0f;
        }

        /// <summary>Loose on the floor: harmless, sliding to a stop.</summary>
        private void Fall()
        {
            flying = false;
            head.enabled = false;
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = 1.5f;
            body.linearDamping = 2f;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector2 tip = BodyTip;
            tipVelocity = Vector2.ClampMagnitude((tip - previousTip) / dt, MaxSpeed);
            previousTip = tip;

            UpdateTrail();
            if (flying)
            {
                // Point first along the flight path, like an arrow.
                Vector2 v = body.linearVelocity;
                if (v.sqrMagnitude > 0.01f)
                {
                    body.MoveRotation(Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
                }

                return;
            }

            if (!IsHeld || owner == null || owner.IsDead)
            {
                return;
            }

            int facing = owner.Facing;
            if (facing != lastFacing && spinTimer <= 0f)
            {
                // Turning around mirrors the guard instead of swinging the spear through the body.
                angle = 180f - angle;
                angleVelocity = -angleVelocity;
                lastFacing = facing;
            }

            UpdateThrust(dt);
            Vector2 hold;
            if (spinTimer > 0f)
            {
                spinTimer -= dt;
                float t = spinTimer > 0f ? SpinProgress : 1f;
                angle = spinStart - spinFacing * 360f * t;
                if (!spinHalfway && t >= 0.5f && Fx.Instance != null)
                {
                    spinHalfway = true;
                    Fx.Instance.Swoosh(TipPosition, MaxSpeed);
                }

                if (spinTimer <= 0f)
                {
                    // Out of the spin straight into a jab, which crits too. The angle stays one full
                    // turn on (not reset to the start): a 360° jump would read as an extra spin.
                    angleVelocity = 0f;
                    spinReadyTime = Time.time + spinCooldown;
                    StartJab(true);
                }

                hold = HoldPoint(Direction(angle));
            }
            else if (owner.IsRolling)
            {
                // Tucked along the back, trailing the roll, close to the hip.
                float behind = owner.RollDirection > 0 ? 165f : 15f;
                angle = Mathf.MoveTowardsAngle(angle, behind, 1500f * dt);
                angleVelocity = 0f;
                hold = owner.Body.position + new Vector2(0f, -0.25f);
            }
            else
            {
                Vector2 aim = owner.aimPoint - owner.ShoulderPosition;
                float pitch = Mathf.Clamp(Mathf.Atan2(aim.y, Mathf.Abs(aim.x)) * Mathf.Rad2Deg, minPitch, maxPitch);
                float target = facing > 0 ? pitch : 180f - pitch;
                // A jab goes in straight: the spear barely turns while it is out.
                float smooth = jabTimer >= 0f ? turnSmoothTime * 4f : turnSmoothTime;
                angle = Mathf.SmoothDampAngle(angle, target, ref angleVelocity, smooth, maxTurnSpeed, dt);
                hold = HoldPoint(Direction(angle));
            }

            Vector2 dir = Direction(angle);
            body.MovePosition(hold + dir * (slide - HandGap * 0.5f));
            body.MoveRotation(angle);
        }

        private Vector2 HoldPoint(Vector2 dir) => owner.ShoulderPosition + dir * holdForward + Vector2.down * holdDrop;

        private static Vector2 Direction(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        /// <summary>Shift jabs at once; held, it jabs again after each short cooldown.</summary>
        private void UpdateThrust(float dt)
        {
            if (jabTimer < 0f)
            {
                if (owner.thrustHeld && owner.ControlsEnabled && !owner.IsStaggered && !owner.IsRolling && spinTimer <= 0f && Time.time >= jabReadyTime)
                {
                    StartJab(false);
                }

                return;
            }

            jabTimer += dt;
            if (jabTimer < jabOut)
            {
                float u = jabTimer / jabOut;
                slide = Mathf.Lerp(jabStartSlide, jabSlide, 1f - (1f - u) * (1f - u));
            }
            else if (jabTimer < jabOut + jabHold)
            {
                slide = jabSlide;
            }
            else if (jabTimer < jabOut + jabHold + jabBack)
            {
                slide = Mathf.Lerp(jabSlide, 0f, (jabTimer - jabOut - jabHold) / jabBack);
            }
            else
            {
                slide = 0f;
                jabTimer = -1f;
                critJab = false;
                jabReadyTime = Time.time + jabCooldown;
            }
        }

        private void StartJab(bool crit)
        {
            jabTimer = 0f;
            critJab = crit;
            jabStartSlide = slide;
            attackHits.Clear();
            owner.Lunge(crit ? 1.2f : jabStep.x, crit ? 0.8f : jabStep.y);
            if (Fx.Instance != null)
            {
                Fx.Instance.Swoosh(TipPosition, MaxSpeed);
            }
        }

        /// <summary>The ribbon shows jabs, spins (red, wide), throws and fast swings.</summary>
        private void UpdateTrail()
        {
            bool spinning = spinTimer > 0f || critJab;
            if (spinning != trail.widthMultiplier > 1f)
            {
                trail.colorGradient = spinning ? spinTrail : normalTrail;
                trail.widthMultiplier = spinning ? 1.6f : 1f;
            }

            trail.emitting = (IsHeld || flying) && (spinning || jabTimer >= 0f || flying || tipVelocity.magnitude > 7f);
        }

        private void Tired()
        {
            if (Fx.Instance != null)
            {
                Fx.Instance.Callout(owner.ShoulderPosition + Vector2.up * 0.6f, "TIRED", new Color(0.75f, 0.75f, 0.8f), 0.7f);
            }
        }

        // Only the Head trigger reaches fighters: the shaft is on the Sword layer, which ignores them.
        private void OnTriggerStay2D(Collider2D other)
        {
            if (owner == null || !(IsHeld || flying) || owner.IsDead || !owner.ControlsEnabled || (IsHeld && owner.IsRolling)
                || Time.time < recoverUntil || other.attachedRigidbody == null)
            {
                return;
            }

            Fighter victim = other.attachedRigidbody.GetComponent<Fighter>();
            if (victim == null || victim == owner || victim.IsDead || victim.IsRolling || (victim.isGuard && owner.isGuard))
            {
                return;
            }

            bool spinning = spinTimer > 0f;
            bool jabbing = JabLive;
            float damage;
            if (flying || spinning || jabbing)
            {
                if (attackHits.Contains(victim) || (flying && tipVelocity.magnitude < 4f))
                {
                    return;
                }

                damage = flying ? throwDamage : spinning ? spinDamage : jabDamage;
                attackHits.Add(victim);
            }
            else
            {
                // A swing: only a fast-moving point cuts, and not the same fighter again right away.
                float speed = Mathf.Min((tipVelocity - victim.Body.linearVelocity).magnitude, MaxSpeed);
                if (speed < minSwingSpeed || (lastSwingHit.TryGetValue(victim, out float last) && Time.time - last < swingHitCooldown))
                {
                    return;
                }

                damage = Mathf.Min(maxSwingDamage, (speed - minSwingSpeed * 0.5f) * swingDamagePerSpeed);
                lastSwingHit[victim] = Time.time;
                recoverUntil = Time.time + recoveryAfterSwingHit;
            }

            bool crit = spinning || (jabbing && critJab);
            if (crit) damage *= critMultiplier;
            bool headshot = other == victim.headCollider;
            bool backstab = victim.IsBehind(owner);
            if (headshot) damage *= headMultiplier;
            if (backstab) damage *= backstabMultiplier;
            damage *= owner.damageScale;

            float away = Mathf.Sign(victim.Body.position.x - owner.Body.position.x);
            var knockback = new Vector2(away * (baseKnockback + damage * 0.08f), 1.5f);
            if (crit) knockback.x *= 1.6f;
            else if (jabbing) knockback.x *= jabKnockback;

            Vector2 point = head.ClosestPoint(other.bounds.center);
            Vector2 direction = tipVelocity.sqrMagnitude > 0.01f ? tipVelocity.normalized : (Vector2)transform.right;
            victim.TakeHit(damage, knockback, headshot);
            if (Fx.Instance != null)
            {
                Fx.Instance.Hit(point, direction, victim.color, damage, headshot);
                if (crit)
                {
                    Fx.Instance.Crit(point, direction, damage);
                    Fx.Instance.Callout(point + Vector2.up, "CRIT!", spinTrailColor, 1.3f);
                    Fx.Instance.Hitstop(0.08f);
                    Fx.Instance.Shake(0.35f);
                }
                else
                {
                    if (jabbing)
                    {
                        Fx.Instance.Hitstop(0.035f);
                        Fx.Instance.Shake(0.12f);
                    }

                    if (backstab)
                    {
                        Fx.Instance.Callout(point, "BACKSTAB!");
                    }
                }
            }

            if (flying)
            {
                Fall(); // a thrown spear hits once, then drops
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            Vector2 contact = collision.GetContact(0).point;
            if (flying && collision.rigidbody == null)
            {
                // Floor or wall: point first, it sticks in; flat, it just lands.
                if (Vector2.Distance(contact, TipPosition) < 0.5f)
                {
                    flying = false;
                    head.enabled = false;
                    body.bodyType = RigidbodyType2D.Kinematic;
                    body.linearVelocity = Vector2.zero;
                    body.angularVelocity = 0f;
                    body.position += (Vector2)transform.right * 0.12f;
                }
                else
                {
                    Fall();
                }

                return;
            }

            if (IsHeld && collision.rigidbody != null && collision.rigidbody.GetComponent<Sword>() != null && Fx.Instance != null)
            {
                float speed = collision.relativeVelocity.magnitude;
                if (speed >= 3f)
                {
                    Fx.Instance.Clash(contact, Mathf.Min(speed, MaxSpeed));
                }
            }
        }
    }
}
