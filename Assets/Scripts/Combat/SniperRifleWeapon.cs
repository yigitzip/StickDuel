using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// One <see cref="SniperRifleWeapon.CalculateShot"/> result: the red laser draws it and
    /// <see cref="SniperRifleWeapon.TryFire"/> shoots it, so both always agree.
    /// </summary>
    public readonly struct SniperShot
    {
        public readonly Vector2 Origin;
        public readonly Vector2 Direction;
        public readonly float Range;
        /// <summary>Where the laser (and the bullet) stops: the first valid hit, or Origin + Direction * Range.</summary>
        public readonly Vector2 End;
        /// <summary>First valid collider on the line, null when nothing was hit.</summary>
        public readonly Collider2D Collider;
        /// <summary>The fighter that collider belongs to, null for an obstacle or a miss.</summary>
        public readonly Fighter Target;

        public SniperShot(Vector2 origin, Vector2 direction, float range, Vector2 end, Collider2D collider, Fighter target)
        {
            Origin = origin;
            Direction = direction;
            Range = range;
            End = end;
            Collider = collider;
            Target = target;
        }

        /// <summary>An obstacle came first: nothing behind it can be hit.</summary>
        public bool Blocked => Collider != null && Target == null;
    }

    /// <summary>
    /// Bolt-action sniper rifle (plumbing in <see cref="HeldWeapon"/>, like the bow): held at the
    /// shoulder, turned towards the aim. A red laser runs from the muzzle to the first thing in the
    /// way; left click fires a hitscan shot along that exact line (<see cref="CalculateShot"/>).
    /// Obstacles are the Collider2Ds on <see cref="blockerMask"/> (triggers ignored); fighters are
    /// found by their Fighter component, the owner's own colliders never count. Right click throws it.
    /// Rifle space: origin = trigger hand, +x = down the barrel; the "Tip" child is the muzzle.
    /// Art: Resources/Weapons/SniperRifle.png (640x137, 400 px/unit, trigger hand x=185px, muzzle x=638px y=45px from top).
    /// </summary>
    public sealed class SniperRifleWeapon : HeldWeapon
    {
        private const float PixelsPerUnit = 400f;
        private const float GripPixel = 185f;
        private const float Butt = -0.4625f;
        private static readonly Vector2 Muzzle = new Vector2(1.1325f, 0.06f);
        private static readonly Vector2 Forestock = new Vector2(0.36f, 0f);

        [Header("Hold")]
        [Tooltip("Trigger hand: this far from the shoulder towards the aim.")]
        [SerializeField, Min(0.1f)] private float reach = 0.42f;
        [SerializeField, Min(0.01f)] private float turnSmoothTime = 0.05f;
        [SerializeField, Min(0.01f)] private float handSmoothTime = 0.06f;
        [SerializeField, Range(-90f, 0f)] private float minPitch = -80f;
        [SerializeField, Range(0f, 90f)] private float maxPitch = 85f;

        [Header("Shot (left click)")]
        [Tooltip("Layers whose (non-trigger) Collider2Ds stop the laser and the bullet: ground, walls, castle, obstacles. Leave Fighter out; fighters are always checked.")]
        [SerializeField] private LayerMask blockerMask = (1 << 0) | (1 << SniperMap.ShotOnlyLayer); // Default (floors, walls) + Water (Sniper Mode's train)
        [SerializeField, Min(1f)] private float range = 30f;
        [SerializeField, Min(0f)] private float damage = 45f;
        [SerializeField, Min(1f)] private float headshotMultiplier = 1.5f;
        [SerializeField, Min(0f)] private float knockback = 6f;
        [SerializeField, Min(0.05f)] private float fireCooldown = 1.1f;

        [Header("Sway and recoil")]
        [Tooltip("Even with the mouse still, the barrel never rests: it rocks up and down about this many degrees (the laser shows it, the shot follows it).")]
        [SerializeField, Min(0f)] private float swayAngle = 5f;
        [Tooltip("Rocks per second, roughly (the rhythm wanders randomly around it).")]
        [SerializeField, Min(0f)] private float swaySpeed = 1.1f;
        [Tooltip("Running multiplies the sway by up to 1 + this.")]
        [SerializeField, Min(0f)] private float movingSwayBoost = 6f;
        [Tooltip("Off the ground (jumping, falling) the sway is multiplied by 1 + this.")]
        [SerializeField, Min(0f)] private float airSwayBoost = 10f;
        [Tooltip("Shaky hands: a fast tremor of up to this many degrees on top of the slow rock.")]
        [SerializeField, Min(0f)] private float jitterAngle = 1.2f;
        [SerializeField, Min(0f)] private float jitterSpeed = 9f;
        [Tooltip("Each shot kicks the muzzle up this many degrees.")]
        [SerializeField, Min(0f)] private float recoilKick = 42f;
        [Tooltip("Seconds for the muzzle to settle back after a shot.")]
        [SerializeField, Min(0.01f)] private float recoilRecoverTime = 0.6f;
        [Tooltip("The stock slams back into the shoulder this far.")]
        [SerializeField, Min(0f)] private float recoilShove = 0.28f;
        [Tooltip("Impulse pushing the shooter back.")]
        [SerializeField, Min(0f)] private float recoilPush = 7f;

        [Header("Laser")]
        [SerializeField, Min(0.001f)] private float laserWidth = 0.025f;
        [SerializeField] private Color laserColor = new Color(1f, 0.08f, 0.05f, 0.85f);
        [Tooltip("After a shot the laser flares this long (tracer).")]
        [SerializeField, Min(0f)] private float flashTime = 0.08f;
        [Tooltip("How fast the bullet flies (units/s). The hit lands when it arrives.")]
        [SerializeField, Min(1f)] private float bulletSpeed = 35f;

        [Header("Throw")]
        [SerializeField, Min(0f)] private float throwSpeed = 12f;
        [SerializeField, Min(0f)] private float throwGravity = 1.4f;
        [SerializeField, Min(0f)] private float throwDamage = 12f;

        private static Sprite art;

        private readonly RaycastHit2D[] hits = new RaycastHit2D[32];
        private LineRenderer laser;
        private Material laserMaterial;
        private Vector2 grip, gripVelocity;
        private float angle, angleVelocity;
        private float readyTime, flashUntil;
        private float recoil, recoilVelocity;
        private float swaySeed;

        public override WeaponType Type => WeaponType.SniperRifle;
        public override bool IsAttacking => flying;
        /// <summary>The trigger hand.</summary>
        public override Vector2 RearHand => transform.position;
        /// <summary>The support hand, under the forestock.</summary>
        public override Vector2 LeadHand => transform.TransformPoint(Forestock);
        public override float ThrowSpeed => throwSpeed;
        /// <summary>The end of the barrel (the "Tip" child, mirrored with the art).</summary>
        public Vector2 MuzzlePosition => TipPosition;
        protected override float ThrowGravity => throwGravity;
        protected override float ThrowDamage => throwDamage;
        protected override bool TrailOn => false;
        protected override bool SwingTrail => false;

        /// <summary>The rifle art (also the menu card). Null, with an error, if the texture is missing.</summary>
        public static Sprite Art => art != null ? art : art = LoadArt("SniperRifle", PixelsPerUnit, GripPixel);

        /// <summary>Builds the rifle for <paramref name="owner"/> (unequipped). Null, with an error, if the art is missing.</summary>
        public static SniperRifleWeapon Create(Fighter owner) => Build<SniperRifleWeapon>(owner, "Sniper Rifle", Art, 1.2f,
            Butt, Muzzle.x, 0.14f, new Vector2(0.75f, Muzzle.y), new Vector2(0.75f, 0.08f), Muzzle);

        protected override void Awake()
        {
            base.Awake();
            swaySeed = Random.Range(0f, 100f);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("SniperRifleWeapon: Sprites/Default shader not found, the laser can't be drawn.", this);
                return;
            }

            laserMaterial = new Material(shader);
            var go = new GameObject("Laser");
            go.transform.SetParent(transform, false);
            laser = go.AddComponent<LineRenderer>();
            laser.sharedMaterial = laserMaterial;
            laser.useWorldSpace = true;
            laser.positionCount = 2;
            laser.numCapVertices = 2;
            laser.sortingLayerID = GetComponent<SpriteRenderer>().sortingLayerID;
            laser.sortingOrder = 19; // over the fighters, like the aim line
            laser.enabled = false;
        }

        private void OnDestroy()
        {
            if (laserMaterial != null)
            {
                Destroy(laserMaterial);
            }
        }

        /// <summary>
        /// The one aim/shot query: from the muzzle down the barrel, up to <see cref="range"/>. The
        /// nearest non-trigger collider on <see cref="blockerMask"/> or living fighter (not the owner)
        /// ends it. A barrel poking through a wall is blocked at the wall.
        /// </summary>
        public SniperShot CalculateShot() => CalculateShot(transform.right);

        /// <summary>
        /// Same query along <paramref name="direction"/> instead of the barrel: what a shot from the
        /// muzzle that way would hit. Bots use it as their line-of-sight check, so they see exactly
        /// what the laser and the bullet would.
        /// </summary>
        public SniperShot CalculateShot(Vector2 direction)
        {
            Vector2 origin = MuzzlePosition;
            direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : (Vector2)transform.right;
            if (owner == null)
            {
                return new SniperShot(origin, direction, range, origin + direction * range, null, null);
            }

            // Shoulder to muzzle first: no shooting through a wall the barrel is stuck into.
            RaycastHit2D inside = Physics2D.Linecast(owner.ShoulderPosition, origin, blockerMask);
            if (inside.collider != null && !inside.collider.isTrigger)
            {
                return new SniperShot(origin, direction, range, origin, inside.collider, null);
            }

            var filter = new ContactFilter2D { useTriggers = true, useLayerMask = true };
            filter.SetLayerMask(blockerMask | LayerMask.GetMask(Fighter.FighterLayer));
            int count = Physics2D.Raycast(origin, direction, filter, hits, range);

            float best = float.PositiveInfinity;
            Collider2D bestCollider = null;
            Fighter bestTarget = null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = hits[i];
                if (hit.distance >= best)
                {
                    continue;
                }

                Rigidbody2D rb = hit.collider.attachedRigidbody;
                if (rb == owner.Body || rb == body)
                {
                    continue; // own body, head or rifle
                }

                Fighter fighter = rb != null ? rb.GetComponent<Fighter>() : null;
                if (fighter != null)
                {
                    if (fighter.IsDead)
                    {
                        continue;
                    }
                }
                else if (hit.collider.isTrigger || (blockerMask.value & (1 << hit.collider.gameObject.layer)) == 0)
                {
                    continue; // trigger zones and stray Fighter-layer colliders don't block
                }

                best = hit.distance;
                bestCollider = hit.collider;
                bestTarget = fighter;
            }

            Vector2 end = bestCollider != null ? origin + direction * best : origin + direction * range;
            return new SniperShot(origin, direction, range, end, bestCollider, bestTarget);
        }

        /// <summary>
        /// Left click. Fires <see cref="CalculateShot"/> (no ammo limit). False when it can't fire:
        /// not in the hands, owner can't act, or still cycling the bolt (<see cref="fireCooldown"/>).
        /// </summary>
        public bool TryFire()
        {
            if (!IsHeld || owner == null || owner.IsDead || !owner.ControlsEnabled || owner.IsRolling || owner.IsStaggered
                || Time.time < readyTime)
            {
                return false;
            }

            readyTime = Time.time + fireCooldown;
            flashUntil = Time.time + flashTime;
            SniperShot shot = CalculateShot();
            // Recoil after the shot is resolved: it spoils the next aim, not this one.
            recoil = recoilKick;
            owner.Body.AddForce(-shot.Direction * recoilPush, ForceMode2D.Impulse);
            if (Fx.Instance != null)
            {
                Fx.Instance.Shake(0.35f);
            }

            // The hit lands when the visible bullet gets there, not on the click.
            SniperTracer.Launch(shot.Origin, shot.End, bulletSpeed, GetComponent<SpriteRenderer>(), () => Impact(shot));
            return true;
        }

        /// <summary>The bullet arrived at <paramref name="shot"/>.End: deal the damage if the target is still there to take it.</summary>
        private void Impact(SniperShot shot)
        {
            Fighter victim = shot.Target;
            if (victim == null || owner == null || victim.IsDead || victim.IsRolling || (victim.isGuard && owner.isGuard))
            {
                return; // missed, hit a wall, or the target dodged or died while the bullet flew
            }

            bool headshot = shot.Collider == victim.headCollider;
            float dealt = damage * (headshot ? headshotMultiplier : 1f) * owner.damageScale;
            victim.TakeHit(dealt, new Vector2(shot.Direction.x * knockback, 1.5f), headshot);
            if (Fx.Instance != null)
            {
                Fx.Instance.Hit(shot.End, shot.Direction, victim.color, dealt, headshot);
                if (headshot)
                {
                    Fx.Instance.Callout(shot.End + Vector2.up, "HEADSHOT!", critColor, 1.2f);
                }
            }
        }

        private void LateUpdate()
        {
            if (laser == null)
            {
                return;
            }

            laser.enabled = IsHeld && owner != null && !owner.IsDead && owner.ControlsEnabled;
            if (!laser.enabled)
            {
                return;
            }

            SniperShot shot = CalculateShot();
            laser.SetPosition(0, shot.Origin);
            laser.SetPosition(1, shot.End);
            bool flash = Time.time < flashUntil;
            laser.widthMultiplier = flash ? laserWidth * 3f : laserWidth;
            Color color = flash ? Color.Lerp(laserColor, Color.white, 0.5f) : laserColor;
            laser.startColor = color;
            laser.endColor = new Color(color.r, color.g, color.b, color.a * 0.7f);
        }

        protected override void ResetAttacks()
        {
            recoil = recoilVelocity = 0f;
        }

        /// <summary>Degrees added to the pitch: a never-resting up/down rock (worse on the move or in the air) plus the decaying recoil climb.</summary>
        private float AimOffset(float dt)
        {
            recoil = Mathf.SmoothDamp(recoil, 0f, ref recoilVelocity, recoilRecoverTime, Mathf.Infinity, dt);
            float boost = owner.IsGrounded
                ? Mathf.Clamp01(Mathf.Abs(owner.Body.linearVelocity.x) / Mathf.Max(0.01f, owner.MoveSpeed)) * movingSwayBoost
                : airSwayBoost;
            // A sine always moves (Perlin alone can idle near zero); noise on its phase and size keeps it unpredictable.
            float t = Time.time;
            float phase = t * swaySpeed * 2f * Mathf.PI + Mathf.PerlinNoise(t * 0.4f, swaySeed) * 8f;
            float drift = Mathf.Sin(phase) * Mathf.Lerp(0.7f, 1f, Mathf.PerlinNoise(t * 1.3f, swaySeed + 17f));
            float jitter = (Mathf.PerlinNoise(t * jitterSpeed, swaySeed + 41f) * 2f - 1f) * jitterAngle;
            return (drift * swayAngle + jitter) * (1f + boost) + recoil;
        }

        protected override void ResetPose()
        {
            angle = owner.Facing > 0 ? 0f : 180f;
            angleVelocity = 0f;
            SetMirrored(owner.Facing < 0);
            grip = GripTarget();
            gripVelocity = Vector2.zero;
            body.position = grip;
            body.rotation = angle;
            transform.SetPositionAndRotation(grip, Quaternion.Euler(0f, 0f, angle));
        }

        protected override void OnThrown(Vector2 velocity)
        {
            body.angularVelocity = -Mathf.Sign(velocity.x == 0f ? owner.Facing : velocity.x) * 400f;
        }

        protected override void HeldUpdate(float dt)
        {
            int facing = owner.Facing;
            SetMirrored(facing < 0); // scope stays on top
            float target;
            if (owner.IsRolling)
            {
                target = facing > 0 ? 100f : 80f; // held up out of the way
            }
            else
            {
                Vector2 aim = owner.aimPoint - owner.ShoulderPosition;
                float pitch = aim.sqrMagnitude > 0.0001f ? Mathf.Clamp(Mathf.Atan2(aim.y, Mathf.Abs(aim.x)) * Mathf.Rad2Deg, minPitch, maxPitch) : 0f;
                pitch += AimOffset(dt);
                target = facing > 0 ? pitch : 180f - pitch;
            }

            angle = Mathf.SmoothDampAngle(angle, target, ref angleVelocity, turnSmoothTime, Mathf.Infinity, dt);
            grip = Vector2.SmoothDamp(grip, GripTarget(), ref gripVelocity, handSmoothTime, Mathf.Infinity, dt);
            body.MovePosition(grip);
            body.MoveRotation(angle);
        }

        private Vector2 GripTarget()
        {
            if (owner.IsRolling)
            {
                return owner.Body.position + new Vector2(0f, -0.3f);
            }

            float r = angle * Mathf.Deg2Rad;
            float shove = recoilKick > 0f ? recoilShove * recoil / recoilKick : 0f; // pulled back while the kick lasts
            return owner.ShoulderPosition + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * (reach - shove) + Vector2.down * 0.05f;
        }

        /// <summary>The rifle is no club while held.</summary>
        protected override void OnHeadTouch(Collider2D other, Fighter victim)
        {
        }
    }
}
