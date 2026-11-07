using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Two-handed heavy weapon (<see cref="AxeWeapon"/>, <see cref="MaceWeapon"/>; plumbing in
    /// <see cref="HeldWeapon"/>). Both hands hold it low on the handle; the mouse swings it
    /// (heavily, it lags the aim and carries the swing through) and a fast-moving head hits.
    /// Shift is the chop: the weapon goes up over the head, then comes straight down in front in a
    /// fraction of a second with a step in; if the head meets the opponent it hits hard and
    /// staggers them. Holding Shift chops again after each short cooldown. F is a leap slam: a hop
    /// forward with the same chop landing with the fighter, always a crit. A katana that clashes
    /// into it is knocked aside (heavy guard). Thrown, it tumbles end over end, slower than the
    /// katana, and bites into a wall or the floor if the head lands first.
    /// Subclasses only supply the art and the geometry (constants into <see cref="HeldWeapon.Build{T}"/>).
    /// Angles are kept facing-relative (pitch: 0 = straight ahead, 90 = up).
    /// </summary>
    public abstract class HeavyWeapon : HeldWeapon
    {
        protected const float PixelsPerUnit = 400f;
        private const float HandGap = 0.4f;

        [Header("Swing (mouse)")]
        [Tooltip("Midpoint between the hands: this far from the shoulder along the handle, and this far down.")]
        [SerializeField] private float holdForward = 0.22f;
        [SerializeField] private float holdDrop = 0.1f;
        [Tooltip("How sluggishly the heavy head follows the aim (a fast flick overshoots).")]
        [SerializeField, Min(0.01f)] private float turnSmoothTime = 0.09f;
        [SerializeField, Min(1f)] private float maxTurnSpeed = 750f;
        [SerializeField, Range(-90f, 0f)] private float minPitch = -70f;
        [SerializeField, Range(0f, 130f)] private float maxPitch = 115f;
        [SerializeField, Min(0.01f)] private float handSmoothTime = 0.06f;
        [SerializeField, Min(0f)] private float maxHandSpeed = 14f;
        [Tooltip("The blade must move at least this fast to cut outside a chop.")]
        [SerializeField, Min(0f)] private float minSwingSpeed = 5f;
        [SerializeField, Min(0f)] private float swingDamagePerSpeed = 2.4f;
        [SerializeField, Min(0f)] private float maxSwingDamage = 28f;
        [SerializeField, Min(0f)] private float swingHitCooldown = 0.7f;
        [SerializeField, Min(0f)] private float recoveryAfterSwingHit = 0.4f;

        [Header("Chop (Shift)")]
        [Tooltip("Raise over the head: hands and pitch, and how long it takes.")]
        [SerializeField] private Vector2 windUpHold = new Vector2(-0.05f, 0.38f);
        [SerializeField] private float windUpPitch = 140f;
        [SerializeField, Min(0.01f)] private float windUp = 0.16f;
        [Tooltip("Bring it down: hands and pitch at the bottom, and how long the drop takes (short = a snap).")]
        [SerializeField] private Vector2 strikeHold = new Vector2(0.6f, -0.32f);
        [SerializeField] private float strikePitch = -50f;
        [SerializeField, Min(0.01f)] private float strike = 0.07f;
        [SerializeField, Min(0f)] private float strikeHandSpeed = 45f;
        [Tooltip("Held down after the drop, then eased back to the ready pose.")]
        [SerializeField, Min(0f)] private float hold = 0.08f;
        [SerializeField, Min(0.01f)] private float recover = 0.22f;
        [Tooltip("Gap between chops while Shift is held.")]
        [SerializeField, Min(0f)] private float chopCooldown = 0.2f;
        [SerializeField, Min(0f)] private float chopDamage = 38f;
        [SerializeField, Min(0f)] private float chopKnockback = 1.7f;
        [SerializeField, Min(0f)] private float chopStagger = 0.4f;
        [Tooltip("Fraction of max stamina a chop costs (axe and mace alike).")]
        [SerializeField, Range(0f, 1f)] private float chopStaminaFraction = 0.3f;
        [Tooltip("Step into the chop (lunge speed and length multipliers; grounded only).")]
        [SerializeField] private Vector2 chopStep = new Vector2(0.8f, 0.5f);

        [Header("Leap slam (F)")]
        [Tooltip("Hop: forward and upward speed. The chop comes down as the fighter lands.")]
        [SerializeField] private Vector2 slamHop = new Vector2(5f, 6f);
        [SerializeField, Min(0.01f)] private float slamWindUp = 0.32f;
        [SerializeField, Min(1f)] private float critMultiplier = 1.5f;
        [SerializeField, Min(0f)] private float slamStagger = 0.5f;
        [SerializeField, Min(0f)] private float slamStaminaCost = 35f;
        [SerializeField, Min(0f)] private float slamCooldown = 1.4f;

        [Header("Guard")]
        [Tooltip("A katana hitting the axe at least this fast is knocked aside.")]
        [SerializeField, Min(0f)] private float deflectSpeed = 4f;
        [SerializeField, Min(0f)] private float deflectStagger = 0.3f;

        [Header("Throw")]
        [SerializeField, Min(0f)] private float throwSpeed = 13f;
        [SerializeField, Min(0f)] private float throwGravity = 1.1f;
        [SerializeField, Min(0f)] private float throwDamage = 34f;
        [Tooltip("Tumble speed in the air (the katana cartwheels at 900).")]
        [SerializeField, Min(0f)] private float throwSpin = 540f;

        private Vector2 grip, gripVelocity; // midpoint between the hands
        private float pitch, pitchVelocity;
        private float chopTimer = -1f, chopStartPitch, chopReadyTime, slamReadyTime;
        private int chopFacing = 1;
        private bool struck, slam;

                public override bool IsAttacking => flying || chopTimer >= 0f;
        public override Vector2 LeadHand => transform.TransformPoint(new Vector3(HandGap, 0f, 0f));
        /// <summary>Rears back through the wind-up, drives forward through the drop, eases off through the recovery.</summary>
        public override float PoseLean
        {
            get
            {
                if (chopTimer < 0f) return 0f;
                float up = WindUp;
                if (chopTimer < up) return -0.6f * (chopTimer / up);
                if (chopTimer < up + strike) return Mathf.Lerp(-0.6f, 1f, (chopTimer - up) / strike);
                if (chopTimer < up + strike + hold) return 1f;
                return Mathf.Lerp(1f, 0f, (chopTimer - up - strike - hold) / recover);
            }
        }

        public override float ThrowSpeed => throwSpeed;
        protected override float ThrowGravity => throwGravity;
        protected override float ThrowDamage => throwDamage;
        protected override bool TrailOn => chopTimer >= WindUp && chopTimer < WindUp + strike + hold;
        protected override bool CritTrail => slam;
        private float WindUp => slam ? slamWindUp : windUp;
        /// <summary>The blade hurts from the start of the drop until just after it stops.</summary>
        private bool ChopLive => chopTimer >= WindUp && chopTimer < WindUp + strike + hold;
        private int Facing => chopTimer >= 0f ? chopFacing : owner.Facing;

        protected override void ResetAttacks()
        {
            chopTimer = -1f;
            struck = slam = false;
        }

        protected override void ResetPose()
        {
            pitch = 40f;
            pitchVelocity = 0f;
            SetMirrored(owner.Facing < 0);
            grip = HoldTarget();
            gripVelocity = Vector2.zero;
            Place();
            transform.SetPositionAndRotation(body.position, Quaternion.Euler(0f, 0f, body.rotation));
        }

        protected override void OnThrown(Vector2 velocity)
        {
            int side = velocity.x != 0f ? (velocity.x > 0f ? 1 : -1) : owner.Facing;
            body.angularVelocity = -side * throwSpin; // head over handle, forwards
        }

        /// <summary>Leap slam: hop forward with the axe raised, the chop lands with the fighter. Always a crit.</summary>
        public override bool TrySpin()
        {
            if (!IsHeld || owner == null || !owner.ControlsEnabled || owner.IsDead || owner.IsRolling || owner.IsStaggered
                || !owner.IsGrounded || chopTimer >= 0f || Time.time < slamReadyTime)
            {
                return false;
            }

            if (!owner.TrySpendStamina(slamStaminaCost))
            {
                Tired();
                return false;
            }

            StartChop(true);
            owner.Body.linearVelocity = new Vector2(owner.Facing * slamHop.x, slamHop.y);
            slamReadyTime = Time.time + slamCooldown;
            if (Fx.Instance != null)
            {
                Fx.Instance.Swoosh(TipPosition, MaxSpeed * 0.6f);
            }

            return true;
        }

        /// <summary>A katana swung into the axe is knocked aside.</summary>
        protected override void OnBladeClash(Sword blade, Vector2 point, float speed)
        {
            base.OnBladeClash(blade, point, speed);
            if (speed < deflectSpeed || !blade.IsHeld || blade.IsStaggered)
            {
                return;
            }

            blade.Deflect(deflectStagger);
            if (blade.Owner != null)
            {
                blade.Owner.ApplyStagger(deflectStagger * 0.5f);
            }

            if (Fx.Instance != null)
            {
                Fx.Instance.Callout(point, "DEFLECT!", new Color(0.8f, 0.85f, 1f), 0.9f);
                Fx.Instance.Shake(0.15f);
            }
        }

        protected override void HeldUpdate(float dt)
        {
            UpdateChop(dt);
            SetMirrored(Facing < 0);
            if (chopTimer >= 0f)
            {
                pitch = ChopPitch();
                pitchVelocity = 0f;
            }
            else if (owner.IsRolling)
            {
                pitch = Mathf.MoveTowardsAngle(pitch, 160f, 900f * dt);
                pitchVelocity = 0f;
            }
            else
            {
                pitch = Mathf.SmoothDampAngle(pitch, AimPitch(), ref pitchVelocity, turnSmoothTime, maxTurnSpeed, dt);
            }

            bool dropping = chopTimer >= WindUp && chopTimer < WindUp + strike + hold;
            grip = Vector2.SmoothDamp(grip, HoldTarget(), ref gripVelocity, dropping ? handSmoothTime * 0.3f : handSmoothTime,
                dropping ? strikeHandSpeed : maxHandSpeed, dt);
            Place();
        }

        /// <summary>Rear hand half a hand-gap behind the hold point along the handle.</summary>
        private void Place()
        {
            float world = Facing > 0 ? pitch : 180f - pitch;
            float r = world * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
            body.MovePosition(grip - dir * (HandGap * 0.5f));
            body.MoveRotation(world);
        }

        /// <summary>Facing-relative pitch of the aim, clamped to what two hands can reach.</summary>
        private float AimPitch()
        {
            Vector2 aim = owner.aimPoint - owner.ShoulderPosition;
            return aim.sqrMagnitude > 0.0001f ? Mathf.Clamp(Mathf.Atan2(aim.y, Mathf.Abs(aim.x)) * Mathf.Rad2Deg, minPitch, maxPitch) : 40f;
        }

        /// <summary>Swinging: the hands sweep round the shoulder with the axe; chopping: scripted hand positions.</summary>
        private Vector2 HoldTarget()
        {
            Vector2 shoulder = owner.ShoulderPosition;
            int facing = Facing;
            if (owner.IsRolling && chopTimer < 0f)
            {
                return owner.Body.position + new Vector2(0f, -0.25f);
            }

            float r = pitch * Mathf.Deg2Rad;
            Vector2 swing = shoulder + new Vector2(facing * Mathf.Cos(r), Mathf.Sin(r)) * holdForward + Vector2.down * holdDrop;
            if (chopTimer < 0f)
            {
                return swing;
            }

            if (chopTimer < WindUp) return shoulder + new Vector2(facing * windUpHold.x, windUpHold.y);
            if (chopTimer < WindUp + strike + hold) return shoulder + new Vector2(facing * strikeHold.x, strikeHold.y);
            return Vector2.Lerp(shoulder + new Vector2(facing * strikeHold.x, strikeHold.y), swing,
                Mathf.SmoothStep(0f, 1f, (chopTimer - WindUp - strike - hold) / recover));
        }

        /// <summary>Raise eases out over the head; the drop accelerates (the head's weight) and stops dead; the recovery eases back to the aim.</summary>
        private float ChopPitch()
        {
            float up = WindUp;
            if (chopTimer < up)
            {
                float u = chopTimer / up;
                return Mathf.LerpAngle(chopStartPitch, windUpPitch, 1f - (1f - u) * (1f - u));
            }

            if (chopTimer < up + strike)
            {
                float u = (chopTimer - up) / strike;
                return Mathf.Lerp(windUpPitch, strikePitch, u * u * u);
            }

            if (chopTimer < up + strike + hold)
            {
                return strikePitch;
            }

            return Mathf.LerpAngle(strikePitch, AimPitch(), Mathf.SmoothStep(0f, 1f, (chopTimer - up - strike - hold) / recover));
        }

        /// <summary>Shift starts a chop; held, the next one follows after a short cooldown.</summary>
        private void UpdateChop(float dt)
        {
            if (chopTimer < 0f)
            {
                if (owner.thrustHeld && owner.ControlsEnabled && !owner.IsStaggered && !owner.IsRolling && Time.time >= chopReadyTime)
                {
                    if (!owner.TrySpendStamina(owner.MaxStamina * chopStaminaFraction))
                    {
                        Tired();
                        chopReadyTime = Time.time + 0.5f;
                        return;
                    }

                    StartChop(false);
                }

                return;
            }

            chopTimer += dt;
            if (!struck && chopTimer >= WindUp)
            {
                struck = true;
                attackHits.Clear();
                owner.Lunge(chopStep.x, chopStep.y);
                if (Fx.Instance != null)
                {
                    Fx.Instance.Swoosh(TipPosition, MaxSpeed);
                    if (slam)
                    {
                        Fx.Instance.Shake(0.3f);
                    }
                }
            }

            if (chopTimer >= WindUp + strike + hold + recover)
            {
                chopTimer = -1f;
                slam = false;
                chopReadyTime = Time.time + chopCooldown;
            }
        }

        private void StartChop(bool isSlam)
        {
            chopTimer = 0f;
            chopStartPitch = pitch;
            slam = isSlam;
            chopFacing = owner.Facing;
            struck = false;
        }

        /// <summary>A chop hits once, hard, and staggers (only on the drop); otherwise a fast enough swing of the blade cuts.</summary>
        protected override void OnHeadTouch(Collider2D other, Fighter victim)
        {
            if (chopTimer < 0f)
            {
                if (TrySwingHit(victim, minSwingSpeed, swingDamagePerSpeed, maxSwingDamage, swingHitCooldown, recoveryAfterSwingHit, out float swing))
                {
                    bool heavy = swing > 15f;
                    ApplyHit(other, victim, swing, 1.3f, false, 0f, heavy ? 0.05f : 0f, heavy ? 0.18f : 0f);
                }

                return;
            }

            if (!ChopLive || !attackHits.Add(victim))
            {
                return;
            }

            if (slam)
            {
                ApplyHit(other, victim, chopDamage * critMultiplier, chopKnockback * 1.3f, true, slamStagger);
            }
            else
            {
                ApplyHit(other, victim, chopDamage, chopKnockback, false, chopStagger, 0.09f, 0.4f);
            }
        }
    }
}
