using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Two-handed spear (plumbing in <see cref="HeldWeapon"/>): the rear hand near the butt, the
    /// lead hand up the shaft. It turns towards the aim slower than the katana and only the front
    /// of the spearhead hurts.
    /// Moves: Shift jabs at once, holding it keeps jabbing (the shaft slides through the lead hand
    /// while the rear hand drives it, with a small step in); a fast swing of the point also cuts;
    /// F hops into a full turn with a red trail and finishes with a jab, and both always crit;
    /// thrown, it flies point first. No charge.
    /// Art: Resources/Weapons/IronSpear.png, 400 px/unit, butt at x=2px, 2.5 units long.
    /// </summary>
    public sealed class SpearWeapon : HeldWeapon
    {
        private const float PixelsPerUnit = 400f;
        private const float ButtPixel = 2f;
        private const float Butt = -0.3f;
        private const float TipX = 2.2f;
        private const float HeadStart = 1.75f; // only the front of the spearhead cuts
        private const float HandGap = 0.45f;

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

        [Header("Spin (F)")]
        [SerializeField, Min(0.05f)] private float spinDuration = 0.7f;
        [SerializeField, Min(0f)] private float spinCooldown = 1.2f;
        [SerializeField, Min(0f)] private float spinStaminaCost = 35f;
        [SerializeField, Min(0f)] private float spinDamage = 26f;
        [Tooltip("Spin hits and the jab that ends the spin always crit for this much.")]
        [SerializeField, Min(1f)] private float critMultiplier = 1.5f;
        [Tooltip("Upward speed of the little hop that starts the spin.")]
        [SerializeField, Min(0f)] private float spinHop = 2.5f;

        [Header("Throw")]
        [SerializeField, Min(0f)] private float throwSpeed = 17f;
        [SerializeField, Min(0f)] private float throwGravity = 0.5f;
        [SerializeField, Min(0f)] private float throwDamage = 30f;

        private static Sprite art;

        private float angle, angleVelocity;
        private int lastFacing = 1;
        private float slide;
        private float jabTimer = -1f, jabStartSlide, jabReadyTime;
        private bool critJab; // the jab that ends a spin
        private float spinTimer, spinStart, spinReadyTime;
        private bool spinHalfway;
        private int spinFacing = 1;

        public override WeaponType Type => WeaponType.Spear;
        public override bool IsAttacking => flying || jabTimer >= 0f || spinTimer > 0f;
        /// <summary>The shaft slides through the lead hand on a jab: it moves 60% as far as the rear hand.</summary>
        public override Vector2 LeadHand => transform.TransformPoint(new Vector3(HandGap - 0.4f * slide, 0f, 0f));
        /// <summary>
        /// 0→1 through a spin (eased: slow start and finish), -1 when not spinning. The rig turns
        /// the body by this same value, so the body and the spear stay in step all the way round.
        /// </summary>
        public override float SpinProgress => spinTimer > 0f ? 0.5f - 0.5f * Mathf.Cos(Mathf.PI * (1f - spinTimer / spinDuration)) : -1f;
        /// <summary>How far the jab is out: the body leans into it.</summary>
        public override float PoseLean => jabSlide > 0f ? Mathf.Clamp01(slide / jabSlide) : 0f;
        public override float ThrowSpeed => throwSpeed;
        protected override float ThrowGravity => throwGravity;
        protected override float ThrowDamage => throwDamage;
        protected override bool FliesStraight => true;
        protected override bool TrailOn => spinTimer > 0f || jabTimer >= 0f;
        protected override bool CritTrail => spinTimer > 0f || critJab;
        private bool JabLive => jabTimer >= 0f && jabTimer < jabOut + jabHold + jabBack * 0.3f;

        /// <summary>The spear art (also the menu card). Null, with an error, if the texture is missing.</summary>
        public static Sprite Art => art != null ? art : art = LoadArt("IronSpear", PixelsPerUnit, ButtPixel - Butt * PixelsPerUnit);

        /// <summary>Builds the spear for <paramref name="owner"/> (unequipped). Null, with an error, if the art is missing.</summary>
        public static SpearWeapon Create(Fighter owner) => Build<SpearWeapon>(owner, "Spear", Art, 1.2f, Butt, TipX, 0.06f,
            new Vector2((HeadStart + TipX) * 0.5f, 0f), new Vector2(TipX - HeadStart, 0.16f), new Vector2(TipX, 0f));

        protected override void ResetAttacks()
        {
            jabTimer = -1f;
            critJab = false;
            spinTimer = 0f;
            slide = 0f;
        }

        protected override void ResetPose()
        {
            lastFacing = owner.Facing;
            angle = lastFacing > 0 ? -10f : 190f;
            angleVelocity = 0f;
            Vector2 rear = HoldPoint(Direction(angle)) + Direction(angle) * (-HandGap * 0.5f);
            body.position = rear;
            body.rotation = angle;
            transform.SetPositionAndRotation(rear, Quaternion.Euler(0f, 0f, angle));
        }

        protected override void OnThrown(Vector2 velocity)
        {
            body.angularVelocity = 0f;
            body.rotation = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        }

        /// <summary>Spin attack: a full turn with the spear, then a jab. False when it can't (busy, cooldown, no stamina).</summary>
        public override bool TrySpin()
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

        protected override void HeldUpdate(float dt)
        {
            int facing = owner.Facing;
            if (facing != lastFacing && spinTimer <= 0f)
            {
                // Turning around mirrors the guard instead of swinging the spear through the body.
                angle = 180f - angle;
                angleVelocity = -angleVelocity;
                lastFacing = facing;
            }

            UpdateJab(dt);
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
        private void UpdateJab(float dt)
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

        protected override void OnHeadTouch(Collider2D other, Fighter victim)
        {
            bool spinning = spinTimer > 0f;
            bool jabbing = JabLive;
            if (spinning || jabbing)
            {
                if (!attackHits.Add(victim))
                {
                    return;
                }

                bool crit = spinning || critJab;
                float damage = (spinning ? spinDamage : jabDamage) * (crit ? critMultiplier : 1f);
                ApplyHit(other, victim, damage, crit ? 1.6f : jabKnockback, crit, 0f, 0.035f, 0.12f);
                return;
            }

            if (TrySwingHit(victim, minSwingSpeed, swingDamagePerSpeed, maxSwingDamage, swingHitCooldown, recoveryAfterSwingHit, out float swing))
            {
                ApplyHit(other, victim, swing, 1f, false);
            }
        }
    }
}
