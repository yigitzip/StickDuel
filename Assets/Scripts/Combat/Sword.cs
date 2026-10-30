using System.Collections.Generic;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Blade mechanic from the original SwordPhysics: the grip is pinned to the hand with a soft
    /// HingeJoint2D, the hand sits at full arm length towards the aim (smoothed), and the blade
    /// angle is driven directly with MoveRotation (around the centre of mass, which is what
    /// makes the blade whip). The only addition: the grip may not drift further than
    /// <see cref="maxGripSlack"/> from the hand, so the whip never tears it out of the hand. Swinging the arm accumulates a relative
    /// tip angle (armDelta × swingFollowGain) clamped between the inward/outward limits; it does
    /// not spring back. Everything is mirrored when the owner faces left.
    /// Damage comes from the child trigger on the SwordHit layer. Transform origin = grip.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class Sword : MonoBehaviour
    {
        [SerializeField] private Fighter owner;
        [SerializeField] private Transform tip;
        [SerializeField] private Collider2D hitCollider;
        [SerializeField] private TrailRenderer trail;

        [Header("Arm")]
        [SerializeField, Min(0.1f)] private float reach = 0.8f;
        [SerializeField] private float minHandHeight = -0.75f;
        [SerializeField, Min(0.01f)] private float handSmoothTime = 0.08f;
        [SerializeField, Min(0f)] private float maxHandSpeed = 15f;
        [Tooltip("How far the hilt may whip away from the hand before it is pulled back.")]
        [SerializeField, Min(0f)] private float maxGripSlack = 0.12f;

        [Header("Direct Arm Follow")]
        [SerializeField, Range(0f, 2f)] private float swingFollowGain = 0.82f;
        [SerializeField, Range(0f, 60f)] private float inwardAngleLimit = 60f;
        [SerializeField, Range(0f, 180f)] private float outwardAngleLimit = 120f;
        [SerializeField, Range(60f, 120f)] private float restingAngleOffset = 105f;

        [Header("Idle Return")]
        [Tooltip("Arm angle changes below this threshold are treated as idle movement rather than a swing.")]
        [SerializeField, Min(0f)] private float idleSwingThreshold = 7f;
        [SerializeField, Min(0f)] private float idleReturnDelay = 2f;
        [SerializeField, Min(0f)] private float idleReturnSpeed = 360f;

        [Header("Back Guard")]
        [Tooltip("World angle below horizontal the blade points to behind the back (wakigamae-like).")]
        [SerializeField, Range(0f, 80f)] private float backGuardDroop = 30f;
        [SerializeField, Min(0f)] private float backGuardTurnSpeed = 900f;

        [Header("Thrust")]
        [Tooltip("Hand distance from the shoulder while the thrust is chambered.")]
        [SerializeField, Min(0.1f)] private float chamberReach = 0.35f;
        [SerializeField, Min(0f)] private float minChamberTime = 0.12f;
        [SerializeField, Min(0f)] private float thrustHandSpeed = 34f;
        [SerializeField, Min(0f)] private float thrustDuration = 0.18f;

        [Header("Thrust Charge")]
        [Tooltip("Hold time for charge level 2 and for full charge (level 3). Level 1 is minChamberTime.")]
        [SerializeField, Min(0f)] private float chargeLevel2Time = 0.45f;
        [SerializeField, Min(0f)] private float chargeFullTime = 0.9f;
        [Tooltip("Multipliers for charge levels 1, 2 and full (x, y, z).")]
        [SerializeField] private Vector3 chargeDamage = new Vector3(1f, 1.3f, 1.7f);
        [SerializeField] private Vector3 chargeKnockback = new Vector3(1f, 1.3f, 1.8f);
        [Tooltip("Dash speed and dash length multipliers per charge level.")]
        [SerializeField] private Vector3 chargeLunge = new Vector3(1.1f, 1.3f, 1.5f);
        [SerializeField] private Vector3 chargeLungeLength = new Vector3(1f, 1.5f, 2f);
        [Tooltip("After a thrust is released, no new charge for this long.")]
        [SerializeField, Min(0f)] private float thrustCooldown = 3f;
        [Tooltip("Chambered hand: this far below the shoulder, pulled back level with the blade pointing straight ahead.")]
        [SerializeField, Min(0f)] private float chamberDrop = 0.12f;
        [Tooltip("A released thrust keeps its charge bonus for this long (the lunge can land the hit a little after the snap).")]
        [SerializeField, Min(0f)] private float chargedHitWindow = 0.3f;
        [Tooltip("A full-charge thrust that lands staggers the victim this long.")]
        [SerializeField, Min(0f)] private float fullChargeStagger = 0.45f;
        [Tooltip("A full-charge thrust into a blade breaks the guard: the defender and their blade reel this long, the attacker does not.")]
        [SerializeField, Min(0f)] private float guardBreakStagger = 0.6f;
        [Tooltip("Stamina per second while charging past level 1; with none left the charge stops growing.")]
        [SerializeField, Min(0f)] private float chargeStaminaPerSecond = 15f;
        [Tooltip("Holding a full charge longer than this tires the arm: the charge drops back to level 1.")]
        [SerializeField, Min(0f)] private float chargeHoldLimit = 1.6f;
        [Tooltip("Walk speed multiplier while charging.")]
        [SerializeField, Range(0f, 1f)] private float chargeMoveScale = 0.55f;
        [Tooltip("Colour of the charge sparks and callouts (the blade itself keeps its colour).")]
        [SerializeField] private Color chargeColor = new Color(1f, 0.72f, 0.3f);

        [Header("Parry")]
        [Tooltip("Blade-on-blade impacts at least this fast stagger both blades.")]
        [SerializeField, Min(0f)] private float clashStaggerSpeed = 5f;
        [Tooltip("The faster (attacking) blade is staggered this long, scaled up by impact speed.")]
        [SerializeField, Min(0f)] private float attackerStagger = 0.4f;
        [SerializeField, Min(0f)] private float defenderStagger = 0.12f;

        [Header("Spin Attack")]
        [Tooltip("Turning around while swinging fast triggers a full Garen-style spin.")]
        [SerializeField, Min(0f)] private float spinTriggerTipSpeed = 8f;
        [SerializeField, Min(0f)] private float spinTriggerArmSpeed = 500f;
        [SerializeField, Min(0.05f)] private float spinDuration = 0.4f;
        [SerializeField, Min(0f)] private float spinCooldown = 1f;
        [SerializeField, Min(0f)] private float spinStaminaCost = 40f;
        [SerializeField] private Color spinTrailColor = new Color(1f, 0.15f, 0.12f);
        [SerializeField, Min(1f)] private float spinDamageMultiplier = 1.6f;
        [SerializeField, Min(1f)] private float spinKnockbackMultiplier = 1.8f;

        [Header("Damage")]
        [SerializeField, Min(0f)] private float minHitSpeed = 4f;
        [SerializeField, Min(0f)] private float damagePerSpeed = 2f;
        [SerializeField, Min(0f)] private float maxDamage = 34f;
        [SerializeField, Min(1f)] private float headMultiplier = 1.5f;
        [Tooltip("Swing speeds are clamped to this before damage/knockback. MoveRotation can jump the blade in a single step (turning around, slow motion), which would otherwise read as a huge speed and launch fighters.")]
        [SerializeField, Min(1f)] private float maxSwingSpeed = 22f;
        [SerializeField, Min(0f)] private float baseKnockback = 3f;
        [SerializeField, Min(0f)] private float knockbackPerSpeed = 0.2f;
        [SerializeField, Min(0f)] private float hitCooldown = 0.8f;
        [Tooltip("After landing a hit the blade cannot hit anyone for this long. Sets the pace of a duel.")]
        [SerializeField, Min(0f)] private float recoveryAfterHit = 0.5f;
        [SerializeField, Min(1f)] private float backstabMultiplier = 1.5f;

        private readonly Dictionary<Fighter, float> lastHitTime = new Dictionary<Fighter, float>();
        private Rigidbody2D body;
        private HingeJoint2D gripJoint;
        // Katana values, captured in Awake so Equip can scale from them.
        private Sprite defaultSprite;
        private float defaultDamagePerSpeed, defaultMaxDamage, defaultKnockback, defaultKnockbackPerSpeed, defaultHitCooldown, defaultRecovery;
        private Vector2 hand;
        private Vector2 handVelocity;
        private float previousArmAngle;
        private float unwrappedArmAngle;
        private float relativeTipAngle;
        private int previousFacing = 1;
        private float outwardLimit;
        private Vector2 previousGrip;
        private float previousRotation;
        private bool wasFast;
        private float lastSwooshTime;
        private float staggerTimer;
        private float spinTimer;
        private float spinStartAngle;
        private float spinReadyTime;
        private float lastTipSpeed;
        private float recoverUntil;
        private Gradient normalTrail;
        private Gradient spinTrail;
        private bool trailIsSpin;
        private float chamberTime;
        private float thrustTimer;
        private float charge;         // seconds of charge actually built (stamina-limited)
        private float heldAtFull;
        private bool chargeTired, warnedNoStamina;
        private int thrustLevel;      // charge level of the last released thrust
        private float chargedHitUntil;
        private float thrustReadyTime;
        private float idleTimer;
        private bool flying; // thrown and not yet landed: the edge still cuts
        private bool wasRolling;

        /// <summary>Settable so a cloned fighter (castle guard) can take its cloned blade.</summary>
        public Fighter Owner { get => owner; set => owner = value; }
        public bool IsHeld { get; private set; } = true;
        /// <summary>Thrown and not yet landed.</summary>
        public bool IsFlying => flying;
        public Vector2 GripPosition => body.position;
        public Vector2 TipPosition => tip.position;
        public Vector2 TipVelocity => Vector2.ClampMagnitude(PointVelocity(tip.position), maxSwingSpeed);
        public bool IsStaggered => staggerTimer > 0f;
        /// <summary>0→1 through a spin attack, -1 when not spinning (drives the body spin in the rig).</summary>
        public float SpinProgress => spinTimer > 0f ? 1f - spinTimer / spinDuration : -1f;
        /// <summary>0 not charging, 1..3 charge level of the chambered thrust (3 = full).</summary>
        public int ChargeLevel => charge >= chargeFullTime ? 3 : charge >= chargeLevel2Time ? 2 : charge >= minChamberTime ? 1 : 0;
        /// <summary>0→1 charge build-up while chambering (drives the crouch in the rig and the blade glow).</summary>
        public float Charge01 => chargeFullTime > 0f ? Mathf.Clamp01(charge / chargeFullTime) : 0f;
        /// <summary>Walk speed multiplier for the owner while chambering.</summary>
        public float MoveScale => chamberTime > 0f ? chargeMoveScale : 1f;
        /// <summary>Holding thrust with the cooldown over: the hand is pulled back and the charge builds.</summary>
        private bool Chambering => owner.thrustHeld && !owner.backGuard && Time.time >= thrustReadyTime;
        private bool FullChargeThrust => thrustLevel == 3 && Time.time < chargedHitUntil;
        private Vector2 BodyTip => body.position + (Vector2)(Quaternion.Euler(0f, 0f, body.rotation) * tip.localPosition);

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.linearDamping = 0.1f;
            body.angularDamping = 0f;

            defaultSprite = GetComponent<SpriteRenderer>().sprite;
            defaultDamagePerSpeed = damagePerSpeed;
            defaultMaxDamage = maxDamage;
            defaultKnockback = baseKnockback;
            defaultKnockbackPerSpeed = knockbackPerSpeed;
            defaultHitCooldown = hitCooldown;
            defaultRecovery = recoveryAfterHit;

            gripJoint = GetComponent<HingeJoint2D>();
            if (gripJoint == null)
            {
                gripJoint = gameObject.AddComponent<HingeJoint2D>();
            }

            gripJoint.connectedBody = null;
            gripJoint.autoConfigureConnectedAnchor = false;
            gripJoint.anchor = Vector2.zero;
            gripJoint.connectedAnchor = body.position;
            gripJoint.useLimits = false;
            gripJoint.useMotor = false;

            if (trail != null)
            {
                normalTrail = trail.colorGradient;
                spinTrail = new Gradient();
                spinTrail.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(spinTrailColor, 0.25f), new GradientColorKey(spinTrailColor, 1f) },
                    new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });

                BladeTrail.Attach(trail, transform);
            }
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            // Measure the last step's tip speed before the previous pose is overwritten.
            lastTipSpeed = Mathf.Min(PointVelocity(BodyTip).magnitude, maxSwingSpeed);
            previousGrip = body.position;
            previousRotation = body.rotation;
            if (!IsHeld || owner == null)
            {
                return;
            }

            UpdateThrust(dt);
            float handSpeed = thrustTimer > 0f ? thrustHandSpeed : maxHandSpeed;
            hand = Vector2.SmoothDamp(hand, HandTarget(), ref handVelocity, handSmoothTime, handSpeed, dt);
            gripJoint.connectedAnchor = hand;
            KeepGripInHand();

            float armAngle = ArmAngle();
            float armDelta = Mathf.DeltaAngle(previousArmAngle, armAngle);
            previousArmAngle = armAngle;
            unwrappedArmAngle += armDelta;
            if (owner.backGuard || Chambering || thrustTimer > 0f)
            {
                idleTimer = 0f;
            }
            else if (Mathf.Abs(armDelta) > idleSwingThreshold)
            {
                idleTimer = 0f;
            }
            else
            {
                idleTimer += dt;
            }

            if (owner.IsRolling)
            {
                // Held flat, trailing behind the roll, so it never digs into the floor.
                float behind = owner.RollDirection > 0 ? 180f : 0f;
                body.angularVelocity = 0f;
                body.MoveRotation(Mathf.MoveTowardsAngle(body.rotation, behind, 1500f * dt));
                previousFacing = owner.Facing;
                spinTimer = 0f;
                wasRolling = true;
                return;
            }

            if (wasRolling)
            {
                wasRolling = false;
                ResyncBlade(owner.Facing);
            }

            int facing = owner.Facing;
            if (facing != previousFacing)
            {
                // Keep the blade where it is when turning around; the clamp only kicks in if needed.
                relativeTipAngle = -relativeTipAngle;
                previousFacing = facing;

                // Spin only when whipping round to face the opponent, and only if there is stamina for it.
                bool fastTurn = lastTipSpeed > spinTriggerTipSpeed || Mathf.Abs(armDelta) / dt > spinTriggerArmSpeed;
                Fighter target = owner.opponent;
                bool towardOpponent = target != null && !target.IsDead
                                      && Mathf.Sign(target.Body.position.x - owner.Body.position.x) == facing;
                if (fastTurn && towardOpponent && staggerTimer <= 0f && spinTimer <= 0f && Time.time >= spinReadyTime && !owner.backGuard)
                {
                    if (owner.TrySpendStamina(spinStaminaCost))
                    {
                        StartSpin();
                    }
                    else if (Fx.Instance != null)
                    {
                        Fx.Instance.Callout(owner.ShoulderPosition + Vector2.up * 0.6f, "TIRED", new Color(0.75f, 0.75f, 0.8f), 0.7f);
                    }
                }
            }

            if (spinTimer > 0f)
            {
                // Spin attack: the blade sweeps a full circle in the facing direction.
                spinTimer -= dt;
                body.angularVelocity = 0f;
                body.MoveRotation(spinStartAngle - facing * 360f * SpinEase());
                if (spinTimer <= 0f)
                {
                    spinReadyTime = Time.time + spinCooldown;
                    ResyncBlade(facing);
                }

                UpdateSwingFeedback();
                return;
            }

            if (staggerTimer > 0f)
            {
                // Knocked aside by a parry: the blade spins freely (still in the hand) until it recovers.
                staggerTimer -= dt;
                body.angularVelocity *= 0.92f;
                if (staggerTimer <= 0f)
                {
                    ResyncBlade(facing);
                }

                UpdateSwingFeedback();
                return;
            }

            relativeTipAngle += armDelta * facing * swingFollowGain;
            if (Chambering || thrustTimer > 0f)
            {
                // Chambered and thrusting: the blade held dead level, point first along the facing.
                float level = Mathf.DeltaAngle(armAngle, facing > 0 ? 0f : 180f) * facing;
                relativeTipAngle = Mathf.MoveTowards(relativeTipAngle, level, backGuardTurnSpeed * dt);
            }

            UpdateBackGuard(armAngle, facing, dt);
            if (!owner.backGuard && !Chambering && idleTimer >= idleReturnDelay)
            {
                outwardLimit = Mathf.Max(outwardLimit, restingAngleOffset);
                relativeTipAngle = Mathf.MoveTowards(
                    relativeTipAngle,
                    restingAngleOffset,
                    idleReturnSpeed * dt);
            }

            relativeTipAngle = Mathf.Clamp(relativeTipAngle, -inwardAngleLimit, outwardLimit);

            body.angularVelocity = 0f;
            body.MoveRotation(unwrappedArmAngle + relativeTipAngle * facing);

            UpdateSwingFeedback();
        }

        /// <summary>
        /// While back guard is held the blade turns over the head (the outward way) until it points
        /// down-behind the back. Letting go eases it back inside the normal limits instead of snapping.
        /// </summary>
        private void UpdateBackGuard(float armAngle, int facing, float dt)
        {
            float step = backGuardTurnSpeed * dt;
            if (!owner.backGuard)
            {
                outwardLimit = Mathf.MoveTowards(Mathf.Max(outwardLimit, relativeTipAngle), outwardAngleLimit, step);
                return;
            }

            float behind = facing > 0 ? 180f + backGuardDroop : -backGuardDroop;
            float target = Mathf.DeltaAngle(armAngle, behind) * facing;
            if (target < -inwardAngleLimit)
            {
                target += 360f; // go over the head rather than through the body
            }

            outwardLimit = Mathf.Max(outwardAngleLimit, target);
            relativeTipAngle = Mathf.MoveTowards(relativeTipAngle, target, step);
        }

        private void StartSpin()
        {
            spinTimer = spinDuration;
            spinStartAngle = body.rotation;
            chamberTime = 0f;
            lastHitTime.Clear(); // a spin may hit someone who was just hit
            if (Fx.Instance != null)
            {
                Fx.Instance.Swoosh(TipPosition, maxSwingSpeed);
            }
        }

        private float SpinEase()
        {
            float t = 1f - spinTimer / spinDuration;
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Hand control back to the arm from wherever the blade ended up. Its angle is re-expressed
        /// next to the arm's so MoveRotation never spins a full extra turn to "catch up".
        /// </summary>
        private void ResyncBlade(int facing)
        {
            float offset = Mathf.DeltaAngle(unwrappedArmAngle, body.rotation);
            body.rotation = unwrappedArmAngle + offset;
            relativeTipAngle = Mathf.Clamp(offset * facing, -inwardAngleLimit, outwardLimit);
        }

        /// <summary>
        /// Hold = pull the hand back (chamber) and build charge; release after a short hold = snap it
        /// out and lunge. Longer holds reach charge levels 2 and 3 (harder, further, full charge
        /// breaks guards) at a stamina cost; a hit cancels the charge.
        /// </summary>
        private void UpdateThrust(float dt)
        {
            thrustTimer -= dt;
            if (Chambering && staggerTimer <= 0f && !owner.IsStaggered)
            {
                chamberTime += dt;
                UpdateCharge(dt);
                return;
            }

            if (chamberTime >= minChamberTime)
            {
                // Release: the body dashes forward and the blade shoots out level for the whole dash.
                thrustLevel = Mathf.Max(1, ChargeLevel);
                float dash = owner.Lunge(chargeLunge[thrustLevel - 1], chargeLungeLength[thrustLevel - 1]);
                thrustTimer = Mathf.Max(thrustDuration, dash);
                chargedHitUntil = Time.time + Mathf.Max(chargedHitWindow, dash);
                thrustReadyTime = Time.time + thrustCooldown;
                if (thrustLevel == 3 && Fx.Instance != null)
                {
                    Fx.Instance.Swoosh(TipPosition, maxSwingSpeed);
                    Fx.Instance.Shake(0.25f);
                }
            }

            CancelCharge();
        }

        private void UpdateCharge(float dt)
        {
            int before = ChargeLevel;
            if (chargeTired)
            {
                return;
            }

            if (charge < minChamberTime)
            {
                charge += dt; // the first level is free
            }
            else if (charge < chargeFullTime)
            {
                if (owner.TrySpendStamina(chargeStaminaPerSecond * dt))
                {
                    charge += dt;
                }
                else if (!warnedNoStamina && Fx.Instance != null)
                {
                    warnedNoStamina = true;
                    Fx.Instance.Callout(owner.ShoulderPosition + Vector2.up * 0.6f, "TIRED", new Color(0.75f, 0.75f, 0.8f), 0.7f);
                }
            }
            else
            {
                heldAtFull += dt;
                if (heldAtFull > chargeHoldLimit)
                {
                    // Held too long: the arm tires and the charge falls back to the weakest level.
                    chargeTired = true;
                    charge = minChamberTime;
                    if (Fx.Instance != null)
                    {
                        Fx.Instance.Callout(owner.ShoulderPosition + Vector2.up * 0.6f, "TIRED", new Color(0.75f, 0.75f, 0.8f), 0.7f);
                    }
                }
            }

            int after = ChargeLevel;
            if (after > before && Fx.Instance != null)
            {
                Fx.Instance.ChargeLevelUp(TipPosition, after, chargeColor);
            }
        }

        /// <summary>Drops a chambered thrust without striking (hit, parried, dropped the blade).</summary>
        public void CancelCharge()
        {
            chamberTime = 0f;
            charge = 0f;
            heldAtFull = 0f;
            chargeTired = false;
            warnedNoStamina = false;
        }


        /// <summary>Lets the hilt whip around the hand, but only within <see cref="maxGripSlack"/>.</summary>
        private void KeepGripInHand()
        {
            Vector2 offset = body.position - hand;
            if (offset.sqrMagnitude <= maxGripSlack * maxGripSlack)
            {
                return;
            }

            Vector2 clamped = hand + offset.normalized * maxGripSlack;
            body.position = clamped;
            transform.position = clamped;
        }

        private Vector2 HandTarget()
        {
            Vector2 shoulder = owner.ShoulderPosition;
            Vector2 aim = owner.aimPoint - shoulder;
            Vector2 direction = aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.right * owner.Facing;
            if (spinTimer > 0f)
            {
                // The hand travels round the body with the spin (front → behind → front).
                float turn = Mathf.Cos(SpinEase() * Mathf.PI * 2f);
                return shoulder + new Vector2(owner.Facing * reach * turn, -0.1f);
            }

            if (owner.IsRolling)
            {
                // Tucked in: the hand rides along with the ball near the ground.
                return owner.Body.position + new Vector2(owner.RollDirection * 0.2f, -0.45f);
            }

            // Chamber and thrust run level along the facing, so the blade goes in straight.
            if (thrustTimer > 0f)
            {
                return shoulder + new Vector2(owner.Facing * reach, -chamberDrop);
            }

            if (Chambering)
            {
                return shoulder + new Vector2(owner.Facing * chamberReach, -chamberDrop);
            }

            Vector2 target = shoulder + direction * reach;
            target.y = Mathf.Max(target.y, shoulder.y + minHandHeight);
            return target;
        }

        private float ArmAngle()
        {
            Vector2 arm = hand - owner.ShoulderPosition;
            return arm.sqrMagnitude < 0.0001f ? previousArmAngle : Mathf.Atan2(arm.y, arm.x) * Mathf.Rad2Deg;
        }

        /// <summary>World velocity of a point on the blade over the last physics step.</summary>
        private Vector2 PointVelocity(Vector2 worldPoint)
        {
            Vector2 local = Quaternion.Euler(0f, 0f, -body.rotation) * (worldPoint - body.position);
            Vector2 previous = previousGrip + (Vector2)(Quaternion.Euler(0f, 0f, previousRotation) * local);
            return (worldPoint - previous) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
        }

        private void UpdateSwingFeedback()
        {
            float tipSpeed = lastTipSpeed;
            bool fast = tipSpeed > 9f;
            if (fast && !wasFast && Time.time - lastSwooshTime > 0.22f)
            {
                lastSwooshTime = Time.time;
                if (Fx.Instance != null)
                {
                    Fx.Instance.Swoosh(TipPosition, tipSpeed);
                }
            }

            wasFast = fast;
            if (trail != null)
            {
                bool spinning = spinTimer > 0f;
                if (spinning != trailIsSpin)
                {
                    trail.colorGradient = spinning ? spinTrail : normalTrail;
                    trail.widthMultiplier = spinning ? 1.6f : 1f;
                    trailIsSpin = spinning;
                }

                trail.emitting = spinning || tipSpeed > 6f;
            }
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (!(IsHeld || flying) || IsStaggered || Time.time < recoverUntil || !owner.ControlsEnabled || owner.IsDead || other.attachedRigidbody == null)
            {
                return;
            }

            Fighter victim = other.attachedRigidbody.GetComponent<Fighter>();
            if (victim == null || victim == owner || victim.IsDead || victim.IsRolling || (victim.isGuard && owner.isGuard))
            {
                return;
            }

            float cooldown = spinTimer > 0f ? spinDuration : hitCooldown; // one hit per victim per spin
            if (lastHitTime.TryGetValue(victim, out float last) && Time.time - last < cooldown)
            {
                return;
            }

            Vector2 point = hitCollider.ClosestPoint(other.bounds.center);
            Vector2 relative = PointVelocity(point) - victim.Body.linearVelocity;
            float speed = Mathf.Min(relative.magnitude, maxSwingSpeed);
            if (speed < minHitSpeed)
            {
                return;
            }

            lastHitTime[victim] = Time.time;
            recoverUntil = Time.time + recoveryAfterHit;
            if (flying)
            {
                Land(); // a thrown blade hits once, then just falls
            }

            bool headshot = other == victim.headCollider;
            bool backstab = victim.IsBehind(owner);
            float damage = Mathf.Min(maxDamage, (speed - minHitSpeed * 0.5f) * damagePerSpeed);
            if (headshot)
            {
                damage *= headMultiplier;
            }

            if (backstab)
            {
                damage *= backstabMultiplier;
            }

            bool spinning = spinTimer > 0f;
            if (spinning)
            {
                damage *= spinDamageMultiplier;
            }

            int level = Time.time < chargedHitUntil ? thrustLevel : 0;
            if (level > 0)
            {
                damage *= chargeDamage[level - 1];
            }

            damage *= owner.damageScale;

            Vector2 direction = relative.normalized;
            // Push away from the attacker, mostly sideways, never a launch.
            float away = Mathf.Sign(victim.Body.position.x - owner.Body.position.x);
            Vector2 knockback = new Vector2(away * (baseKnockback + speed * knockbackPerSpeed), 1.5f);
            if (spinning)
            {
                knockback.x *= spinKnockbackMultiplier;
            }

            if (level > 0)
            {
                knockback.x *= chargeKnockback[level - 1];
            }

            victim.TakeHit(damage, knockback, headshot);
            if (level == 3)
            {
                victim.ApplyStagger(fullChargeStagger);
                chargedHitUntil = 0f;
                if (Fx.Instance != null)
                {
                    Fx.Instance.Crit(point, direction, damage);
                    Fx.Instance.Callout(point + Vector2.up * 1f, "PIERCE!", chargeColor, 1.2f);
                }
            }
            if (Fx.Instance != null)
            {
                Fx.Instance.Hit(point, direction, victim.color, damage, headshot);
                if (spinning)
                {
                    Fx.Instance.Crit(point, direction, damage);
                }
                if (backstab)
                {
                    Fx.Instance.Callout(point, "BACKSTAB!");
                }
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (flying && collision.rigidbody == null)
            {
                Land();
            }

            if (!IsHeld || collision.rigidbody == null)
            {
                return;
            }

            Sword other = collision.rigidbody.GetComponent<Sword>();
            // Both blades receive the event; let one of them handle the clash.
            if (other == null || GetInstanceID() < other.GetInstanceID())
            {
                return;
            }

            Vector2 point = collision.GetContact(0).point;
            float speed = Mathf.Min((PointVelocity(point) - other.PointVelocity(point)).magnitude, maxSwingSpeed);
            if (speed >= 3f && Fx.Instance != null)
            {
                Fx.Instance.Clash(point, speed);
            }

            if (IsStaggered || other.IsStaggered)
            {
                return;
            }

            // A full-charge thrust breaks a guard: the blocking blade and its owner reel, the thrust does not.
            if (FullChargeThrust || other.FullChargeThrust)
            {
                Sword breaker = FullChargeThrust ? this : other;
                Sword broken = breaker == this ? other : this;
                broken.Stagger(guardBreakStagger);
                broken.owner.ApplyStagger(guardBreakStagger);
                breaker.chargedHitUntil = Time.time + chargedHitWindow; // the thrust carries on through
                if (Fx.Instance != null)
                {
                    Fx.Instance.Callout(point, "GUARD BREAK!", chargeColor, 1.1f);
                    Fx.Instance.Shake(0.4f);
                }

                return;
            }

            if (speed < clashStaggerSpeed)
            {
                return;
            }

            // Parry: whoever swung harder into the other blade is knocked off balance longest,
            // so meeting an attack with your blade is rewarded.
            float mine = TipVelocity.magnitude;
            float theirs = other.TipVelocity.magnitude;
            Sword attacker = mine >= theirs ? this : other;
            Sword defender = attacker == this ? other : this;
            float k = Mathf.Clamp01(speed / maxSwingSpeed);
            attacker.Stagger(attackerStagger * (0.6f + k));
            defender.Stagger(defenderStagger);
            attacker.owner.ApplyStagger(attackerStagger * k);

            bool cleanParry = Mathf.Max(mine, theirs) > 1.6f * Mathf.Min(mine, theirs) && k > 0.35f;
            if (cleanParry && Fx.Instance != null)
            {
                Fx.Instance.Callout(point, "PARRY!");
            }
        }

        /// <summary>Swaps in another weapon: sprite, reach, hitbox and damage/knockback/pace.</summary>
        public void Equip(Weapon weapon)
        {
            GetComponent<SpriteRenderer>().sprite = weapon.Sprite != null ? weapon.Sprite : defaultSprite;
            tip.localPosition = new Vector3(weapon.Tip, 0f, 0f);
            var offset = new Vector2((weapon.BladeStart + weapon.Tip) * 0.5f, weapon.CenterOffset);
            float length = weapon.Tip - weapon.BladeStart;
            var blade = GetComponent<BoxCollider2D>();
            blade.offset = offset;
            blade.size = new Vector2(length, weapon.Thickness);
            var edge = (BoxCollider2D)hitCollider;
            edge.offset = offset;
            edge.size = new Vector2(length, weapon.Thickness + 0.07f);

            damagePerSpeed = defaultDamagePerSpeed * weapon.Damage;
            maxDamage = defaultMaxDamage * weapon.Damage;
            baseKnockback = defaultKnockback * weapon.Knockback;
            knockbackPerSpeed = defaultKnockbackPerSpeed * weapon.Knockback;
            hitCooldown = defaultHitCooldown * weapon.HitCooldown;
            recoveryAfterHit = defaultRecovery * weapon.Recovery;
        }

        private void Stagger(float seconds)
        {
            staggerTimer = Mathf.Max(staggerTimer, seconds);
            spinTimer = 0f; // a parry stops a spin dead
            CancelCharge();
            thrustTimer = 0f;
            chargedHitUntil = 0f;
            // Bounce the blade back the way it came.
            float spin = body.angularVelocity;
            body.angularVelocity = -Mathf.Sign(spin == 0f ? 1f : spin) * Mathf.Max(240f, Mathf.Abs(spin) * 0.5f);
        }

        public void Drop()
        {
            Release();
            // Let it fall where the fighter fell instead of sliding across the arena.
            body.linearVelocity *= 0.3f;
            body.angularVelocity = Random.Range(-250f, 250f);
            Land();
        }

        /// <summary>Lets go of the blade with <paramref name="velocity"/>, cartwheeling; it cuts until it lands.</summary>
        public void Throw(Vector2 velocity)
        {
            Release();
            body.linearVelocity = velocity;
            body.linearDamping = 0.1f;
            body.angularVelocity = -Mathf.Sign(velocity.x == 0f ? owner.Facing : velocity.x) * 900f;
            flying = true;
            hitCollider.enabled = true;
            lastHitTime.Clear();
            recoverUntil = 0f;
            if (trail != null)
            {
                trail.emitting = true;
            }
        }

        private void Release()
        {
            IsHeld = false;
            gripJoint.enabled = false;
            body.gravityScale = 1.5f;
            staggerTimer = 0f;
            spinTimer = 0f;
            CancelCharge();
            chargedHitUntil = 0f;
        }

        /// <summary>On the ground (or after a thrown hit): harmless and sliding to a stop.</summary>
        private void Land()
        {
            flying = false;
            body.linearDamping = 2f;
            hitCollider.enabled = false;
            if (trail != null)
            {
                trail.emitting = false;
            }
        }

        public void Regrip()
        {
            IsHeld = true;
            flying = false;
            body.gravityScale = 0f;
            body.linearDamping = 0.1f;
            staggerTimer = 0f;
            spinTimer = 0f;
            recoverUntil = 0f;
            CancelCharge();
            thrustTimer = 0f;
            chargedHitUntil = 0f;
            idleTimer = 0f;
            hitCollider.enabled = true;
            lastHitTime.Clear();

            hand = HandTarget();
            handVelocity = Vector2.zero;
            gripJoint.connectedAnchor = hand;
            gripJoint.enabled = true;

            float armAngle = ArmAngle();
            previousArmAngle = armAngle;
            unwrappedArmAngle = armAngle;
            previousFacing = owner.Facing;
            relativeTipAngle = restingAngleOffset;
            outwardLimit = outwardAngleLimit;
            float angle = armAngle + relativeTipAngle * owner.Facing;

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = hand;
            body.rotation = angle;
            transform.SetPositionAndRotation(hand, Quaternion.Euler(0f, 0f, angle));
            previousGrip = hand;
            previousRotation = angle;
            if (trail != null)
            {
                trail.Clear();
                trail.emitting = false;
            }
        }
    }
}
