using System;
using System.Collections.Generic;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Shared plumbing for the weapons with their own physics (<see cref="SpearWeapon"/>,
    /// <see cref="AxeWeapon"/>); nothing here comes from <see cref="Sword"/>. Held, the weapon is a
    /// kinematic body the subclass poses every physics step (<see cref="HeldUpdate"/>) with its own
    /// physics; the rig puts the hands on <see cref="RearHand"/> / <see cref="LeadHand"/> (one hand
    /// on <see cref="RearHand"/> when not <see cref="TwoHanded"/>). The handle is solid on
    /// the Sword layer (blocks blades, lands on the floor, never touches fighters); only the "Head"
    /// trigger on the SwordHit layer hurts. Thrown, it cuts once, sticks if the head meets a wall
    /// or the floor, otherwise falls; walking over it picks it up.
    /// Weapon space: origin = rear hand, +x along the handle towards the head.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public abstract class HeldWeapon : MonoBehaviour, IWeaponBehaviour
    {
        protected const float MaxSpeed = 22f;

        [Header("Hits")]
        [SerializeField, Min(1f)] private float headMultiplier = 1.5f;
        [SerializeField, Min(1f)] private float backstabMultiplier = 1.5f;
        [SerializeField, Min(0f)] private float baseKnockback = 3f;
        [SerializeField] protected Color critColor = new Color(1f, 0.15f, 0.12f);

        protected readonly HashSet<Fighter> attackHits = new HashSet<Fighter>(); // one hit per victim per attack / throw
        private readonly Dictionary<Fighter, float> lastSwingHit = new Dictionary<Fighter, float>();
        protected Rigidbody2D body;
        protected Fighter owner;
        protected bool flying;
        protected float recoverUntil;
        protected Vector2 tipVelocity;
        private BoxCollider2D head;
        private Collider2D shaft;
        private TrailRenderer trail;
        private SpriteRenderer sprite;
        private Gradient normalTrail, critTrail;
        private Sword replacedSword;
        private Vector2 headOffset, tipBase, tipLocal;
        private Vector2 previousTip;
        private bool mirrored;

        public abstract WeaponType Type { get; }
        public Fighter Owner => owner;
        public bool IsEquipped => owner != null;
        public bool IsHeld { get; private set; }
        /// <summary>Thrown and not yet landed.</summary>
        public bool IsFlying => flying;
        public abstract bool IsAttacking { get; }
        /// <summary>Rear hand, near the end of the handle (interpolated transform = what is drawn).</summary>
        public virtual Vector2 RearHand => transform.position;
        /// <summary>Lead hand, further up the handle (two-handed weapons only).</summary>
        public abstract Vector2 LeadHand { get; }
        /// <summary>False: only <see cref="RearHand"/> holds it (the grip) and the off hand stays free, like with the katana.</summary>
        public virtual bool TwoHanded => true;
        /// <summary>0→1 through a spin attack (the rig turns the body by it), -1 otherwise.</summary>
        public virtual float SpinProgress => -1f;
        /// <summary>Body drive from the current attack: +1 lunging forward, -1 rearing back, 0 at rest.</summary>
        public virtual float PoseLean => 0f;
        public abstract float ThrowSpeed { get; }
        /// <summary>The striking point (spear tip, middle of the axe's edge).</summary>
        public Vector2 TipPosition => transform.TransformPoint(tipLocal);
        protected abstract float ThrowGravity { get; }
        protected abstract float ThrowDamage { get; }
        /// <summary>Point first along the flight path instead of tumbling.</summary>
        protected virtual bool FliesStraight => false;
        /// <summary>The trail runs during attacks; <see cref="CritTrail"/> makes it red and wide.</summary>
        protected abstract bool TrailOn { get; }
        protected virtual bool CritTrail => false;
        /// <summary>A fast-moving striking point also draws the trail (off for weapons that don't swing).</summary>
        protected virtual bool SwingTrail => true;
        private Vector2 BodyTip => body.position + (Vector2)(Quaternion.Euler(0f, 0f, body.rotation) * (Vector3)tipLocal);

        /// <summary>Weapon-specific special move (F). False when the weapon has none or can't now.</summary>
        public virtual bool TrySpin() => false;

        /// <summary>Puts the weapon in the hands for a fresh start (position, angle, facing).</summary>
        protected abstract void ResetPose();
        /// <summary>Stops any attack in progress (regrip, thrown, dropped).</summary>
        protected abstract void ResetAttacks();
        /// <summary>Poses the held weapon and runs its attacks; called every physics step while held.</summary>
        protected abstract void HeldUpdate(float dt);
        /// <summary>The head touches a valid victim while held: decide whether it hits (see <see cref="ApplyHit"/>).</summary>
        protected abstract void OnHeadTouch(Collider2D other, Fighter victim);
        /// <summary>Sets the throw's rotation: spin, or point first for <see cref="FliesStraight"/>.</summary>
        protected abstract void OnThrown(Vector2 velocity);

        /// <summary>Loads Resources/Weapons/<paramref name="texture"/> with its pivot on the rear hand. Null, with an error, if missing.</summary>
        protected static Sprite LoadArt(string texture, float pixelsPerUnit, float rearHandPixel)
        {
            var image = Resources.Load<Texture2D>("Weapons/" + texture);
            if (image == null)
            {
                Debug.LogError($"HeldWeapon: Resources/Weapons/{texture}.png is missing.");
                return null;
            }

            return Sprite.Create(image, new Rect(0f, 0f, image.width, image.height),
                new Vector2(rearHandPixel / image.width, 0.5f), pixelsPerUnit);
        }

        /// <summary>
        /// Builds the weapon object for <paramref name="owner"/>, unequipped: sprite, kinematic body,
        /// solid handle from <paramref name="handleStart"/> to <paramref name="handleEnd"/>, damaging
        /// head box and the trail point <paramref name="tip"/>. Null if <paramref name="art"/> is null.
        /// </summary>
        protected static T Build<T>(Fighter owner, string name, Sprite art, float mass, float handleStart, float handleEnd,
            float handleThickness, Vector2 headCenter, Vector2 headSize, Vector2 tip) where T : HeldWeapon
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (art == null)
            {
                return null;
            }

            var go = new GameObject(owner.name + " " + name) { layer = LayerMask.NameToLayer(Fighter.SwordLayer) };
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = art;
            renderer.sortingOrder = 14; // between the back arm and the front arm, like the katana
            if (owner.sword != null && owner.sword.TryGetComponent(out SpriteRenderer katana))
            {
                renderer.sharedMaterial = katana.sharedMaterial;
                renderer.sortingLayerID = katana.sortingLayerID;
                renderer.sortingOrder = katana.sortingOrder;
            }

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var handle = go.AddComponent<BoxCollider2D>();
            handle.offset = new Vector2((handleStart + handleEnd) * 0.5f, 0f);
            handle.size = new Vector2(handleEnd - handleStart, handleThickness);

            var headObject = new GameObject("Head") { layer = LayerMask.NameToLayer(Fighter.SwordHitLayer) };
            headObject.transform.SetParent(go.transform, false);
            var trigger = headObject.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.offset = headCenter;
            trigger.size = headSize;

            // Slash ribbon from the striking point, drawn by the same BladeTrail the katana uses.
            var tipObject = new GameObject("Tip");
            tipObject.transform.SetParent(go.transform, false);
            tipObject.transform.localPosition = tip;
            var ribbon = tipObject.AddComponent<TrailRenderer>();
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
            return go.AddComponent<T>();
        }

        protected virtual void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            shaft = GetComponent<BoxCollider2D>();
            sprite = GetComponent<SpriteRenderer>();
            head = transform.Find("Head").GetComponent<BoxCollider2D>();
            headOffset = head.offset;
            trail = transform.Find("Tip").GetComponent<TrailRenderer>();
            tipBase = tipLocal = trail.transform.localPosition;
            normalTrail = trail.colorGradient;
            critTrail = new Gradient();
            critTrail.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(critColor, 0.25f), new GradientColorKey(critColor, 1f) },
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

            owner.heldWeapon = this;
            gameObject.SetActive(true);
            Regrip();
        }

        public void Unequip()
        {
            if (owner == null)
            {
                return;
            }

            if (owner.heldWeapon == this)
            {
                owner.heldWeapon = null;
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

        /// <summary>Back in both hands (round start, picked up).</summary>
        public void Regrip()
        {
            IsHeld = true;
            flying = false;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            head.enabled = true;
            recoverUntil = 0f;
            lastSwingHit.Clear();
            ResetAttacks();
            if (owner != null)
            {
                ResetPose();
                previousTip = BodyTip;
            }
        }

        /// <summary>Lets go with <paramref name="velocity"/>; it cuts until it lands.</summary>
        public void Throw(Vector2 velocity)
        {
            if (!IsHeld)
            {
                return;
            }

            Release();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = ThrowGravity;
            body.linearDamping = 0f;
            body.linearVelocity = velocity;
            flying = true;
            head.enabled = true;
            attackHits.Clear();
            OnThrown(velocity);
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

        /// <summary>Closest point of the handle (for picking it up off the floor).</summary>
        public Vector2 ClosestPoint(Vector2 point) => shaft.ClosestPoint(point);

        /// <summary>
        /// Mirrors the art and the head across the handle (an axe's blade must stay on the side it
        /// cuts with when the fighter turns round). Symmetric weapons never call it.
        /// </summary>
        protected void SetMirrored(bool value)
        {
            if (mirrored == value)
            {
                return;
            }

            mirrored = value;
            float side = value ? -1f : 1f;
            sprite.flipY = value;
            head.offset = new Vector2(headOffset.x, headOffset.y * side);
            tipLocal = new Vector2(tipBase.x, tipBase.y * side);
            trail.transform.localPosition = tipLocal;
        }

        private void Release()
        {
            IsHeld = false;
            ResetAttacks();
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
                Vector2 v = body.linearVelocity;
                if (FliesStraight && v.sqrMagnitude > 0.01f)
                {
                    body.MoveRotation(Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
                }

                return;
            }

            if (IsHeld && owner != null && !owner.IsDead)
            {
                HeldUpdate(dt);
            }
        }

        private void UpdateTrail()
        {
            bool crit = IsHeld && CritTrail;
            if (crit != trail.widthMultiplier > 1f)
            {
                trail.colorGradient = crit ? critTrail : normalTrail;
                trail.widthMultiplier = crit ? 1.6f : 1f; // BladeTrail reads > 1 as the wide spin ribbon
            }

            trail.emitting = flying || (IsHeld && (TrailOn || (SwingTrail && tipVelocity.magnitude > 7f)));
        }

        protected void Tired()
        {
            if (Fx.Instance != null)
            {
                Fx.Instance.Callout(owner.ShoulderPosition + Vector2.up * 0.6f, "TIRED", new Color(0.75f, 0.75f, 0.8f), 0.7f);
            }
        }

        /// <summary>
        /// A swing (no special attack running): the striking point must move at least
        /// <paramref name="minSpeed"/> relative to the victim, and the same victim is not hit again
        /// within <paramref name="cooldown"/>. Returns the damage, or false for no hit.
        /// </summary>
        protected bool TrySwingHit(Fighter victim, float minSpeed, float damagePerSpeed, float maxDamage, float cooldown, float recovery, out float damage)
        {
            damage = 0f;
            float speed = Mathf.Min((tipVelocity - victim.Body.linearVelocity).magnitude, MaxSpeed);
            if (speed < minSpeed || (lastSwingHit.TryGetValue(victim, out float last) && Time.time - last < cooldown))
            {
                return false;
            }

            damage = Mathf.Min(maxDamage, (speed - minSpeed * 0.5f) * damagePerSpeed);
            lastSwingHit[victim] = Time.time;
            recoverUntil = Time.time + recovery;
            return true;
        }

        /// <summary>
        /// Lands a hit: head and backstab bonuses, knockback away from the owner (scaled by
        /// <paramref name="knockScale"/>), optional stagger, effects. A crit adds the red CRIT
        /// callout, hit-stop and a heavy shake; otherwise <paramref name="hitstop"/> /
        /// <paramref name="shake"/> are used.
        /// </summary>
        protected void ApplyHit(Collider2D other, Fighter victim, float damage, float knockScale, bool crit,
            float stagger = 0f, float hitstop = 0f, float shake = 0f)
        {
            bool headshot = other == victim.headCollider;
            bool backstab = victim.IsBehind(owner);
            if (headshot) damage *= headMultiplier;
            if (backstab) damage *= backstabMultiplier;
            damage *= owner.damageScale;

            float away = Mathf.Sign(victim.Body.position.x - owner.Body.position.x);
            var knockback = new Vector2(away * (baseKnockback + damage * 0.08f) * knockScale, 1.5f);
            Vector2 point = head.ClosestPoint(other.bounds.center);
            Vector2 direction = tipVelocity.sqrMagnitude > 0.01f ? tipVelocity.normalized : (Vector2)transform.right;
            victim.TakeHit(damage, knockback, headshot);
            if (stagger > 0f)
            {
                victim.ApplyStagger(stagger);
            }

            if (Fx.Instance == null)
            {
                return;
            }

            Fx.Instance.Hit(point, direction, victim.color, damage, headshot);
            if (crit)
            {
                Fx.Instance.Crit(point, direction, damage);
                Fx.Instance.Callout(point + Vector2.up, "CRIT!", critColor, 1.3f);
                Fx.Instance.Hitstop(0.08f);
                Fx.Instance.Shake(0.35f);
                return;
            }

            if (hitstop > 0f) Fx.Instance.Hitstop(hitstop);
            if (shake > 0f) Fx.Instance.Shake(shake);
            if (backstab) Fx.Instance.Callout(point, "BACKSTAB!");
        }

        // Only the Head trigger reaches fighters: the handle is on the Sword layer, which ignores them.
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

            if (!flying)
            {
                OnHeadTouch(other, victim);
                return;
            }

            if (attackHits.Contains(victim) || tipVelocity.magnitude < 4f)
            {
                return;
            }

            attackHits.Add(victim);
            ApplyHit(other, victim, ThrowDamage, 1.2f, false, 0f, 0.06f, 0.25f);
            Fall(); // a thrown weapon hits once, then drops
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            Vector2 contact = collision.GetContact(0).point;
            if (flying && collision.rigidbody == null)
            {
                // Floor or wall: head first, it bites in and stays; otherwise it just lands.
                if (Vector2.Distance(contact, head.ClosestPoint(contact)) < 0.2f)
                {
                    flying = false;
                    head.enabled = false;
                    body.bodyType = RigidbodyType2D.Kinematic;
                    body.linearVelocity = Vector2.zero;
                    body.angularVelocity = 0f;
                    body.position += (contact - (Vector2)head.bounds.center).normalized * 0.06f;
                }
                else
                {
                    Fall();
                }

                return;
            }

            Sword blade = IsHeld && collision.rigidbody != null ? collision.rigidbody.GetComponent<Sword>() : null;
            if (blade != null)
            {
                OnBladeClash(blade, contact, Mathf.Min(collision.relativeVelocity.magnitude, MaxSpeed));
            }
        }

        /// <summary>The held weapon met a katana at <paramref name="speed"/>: sparks by default, more for a heavy weapon.</summary>
        protected virtual void OnBladeClash(Sword blade, Vector2 point, float speed)
        {
            if (speed >= 3f && Fx.Instance != null)
            {
                Fx.Instance.Clash(point, speed);
            }
        }
    }
}
