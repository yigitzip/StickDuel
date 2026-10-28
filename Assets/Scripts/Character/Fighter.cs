using System;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Body, movement and health of one duelist. Drivers (PlayerDriver / AIDriver) write the
    /// input fields every frame; Fighter itself never reads devices.
    /// Root transform sits at hip height (feet are <see cref="HipHeight"/> below it).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class Fighter : MonoBehaviour
    {
        public const string FighterLayer = "Fighter";
        public const string SwordLayer = "Sword";
        public const string SwordHitLayer = "SwordHit";
        public const float HipHeight = 0.95f;
        public const float ShoulderHeight = 0.64f;
        private const float CorpseHorizontalDeceleration = 4f;

        [Header("Identity")]
        public string displayName = "Fighter";
        public Color color = Color.white;
        public Fighter opponent;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 4.5f;
        [SerializeField, Min(0f)] private float groundAcceleration = 55f;
        [SerializeField, Min(0f)] private float airAcceleration = 22f;
        [SerializeField, Min(0f)] private float jumpVelocity = 12f;
        [SerializeField, Min(0f)] private float coyoteTime = 0.1f;
        [SerializeField, Min(0f)] private float jumpBuffer = 0.12f;
        [SerializeField] private float killY = -7f;
        [SerializeField, Min(1f)] private float maxSpeed = 10f;
        [Tooltip("The fighter faces the side it aims at (where the blade goes), not the opponent. The aim must cross this far past the body to turn around.")]
        [SerializeField, Min(0f)] private float turnDeadZone = 0.15f;
        [SerializeField, Min(0f)] private float lungeSpeed = 6.5f;
        [SerializeField, Min(0f)] private float lungeDuration = 0.16f;

        [Header("Roll")]
        [SerializeField, Min(0f)] private float rollSpeed = 8.5f;
        [SerializeField, Min(0.05f)] private float rollDuration = 0.42f;
        [SerializeField, Min(0f)] private float rollCooldown = 0.25f;
        [SerializeField, Min(0f)] private float rollStaminaCost = 25f;

        [Header("Punch (no sword)")]
        [SerializeField, Min(0f)] private float punchDamage = 8f;
        [SerializeField, Min(0.05f)] private float punchDuration = 0.24f;
        [SerializeField, Min(0f)] private float punchCooldown = 0.15f;
        [SerializeField, Min(0.1f)] private float punchReach = 0.85f;
        [SerializeField, Min(0.05f)] private float punchRadius = 0.28f;

        [Header("Kick")]
        [SerializeField, Min(0f)] private float kickDamage = 14f;
        [Tooltip("Whole move: chamber (knee up), snap out, hold, pull back. Phases are fractions of this.")]
        [SerializeField, Min(0.05f)] private float kickDuration = 0.46f;
        [SerializeField, Min(0f)] private float kickCooldown = 0.2f;
        [SerializeField, Min(0.1f)] private float kickReach = 0.95f;
        [SerializeField, Min(0.05f)] private float kickRadius = 0.32f;
        [SerializeField, Min(0f)] private float kickPush = 8f;
        [SerializeField, Min(0f)] private float kickLift = 3f;
        [Tooltip("Small step into the kick as the leg snaps out.")]
        [SerializeField, Min(0f)] private float kickStep = 3f;
        [Tooltip("Extra stun on a landed kick so the victim visibly reels.")]
        [SerializeField, Min(0f)] private float kickStagger = 0.35f;

        [Header("Health")]
        [SerializeField, Min(1f)] private float maxHealth = 150f;

        [Header("Stamina")]
        [SerializeField, Min(1f)] private float maxStamina = 100f;
        [SerializeField, Min(0f)] private float staminaRegen = 22f;
        [Tooltip("Seconds after spending stamina before it starts refilling.")]
        [SerializeField, Min(0f)] private float staminaRegenDelay = 0.7f;

        [Header("Parts")]
        public Sword sword;
        public StickFigureRig rig;
        public Collider2D headCollider;

        // Input, written by drivers.
        [NonSerialized] public float moveInput;
        [NonSerialized] public Vector2 aimPoint;
        /// <summary>Hold to swing the blade round behind your back.</summary>
        [NonSerialized] public bool backGuard;
        /// <summary>Hold to chamber a thrust, release to stab.</summary>
        [NonSerialized] public bool thrustHeld;
        /// <summary>Multiplies every hit this fighter lands (Survival weakens its bots).</summary>
        [NonSerialized] public float damageScale = 1f;
        /// <summary>Castle guards: their blades pass through each other's bodies.</summary>
        [NonSerialized] public bool isGuard;

        public bool ControlsEnabled { get; set; } = true;
        public float Health { get; private set; }
        /// <summary>Takes effect on the next ResetFighter.</summary>
        public float MaxHealth { get => maxHealth; set => maxHealth = Mathf.Max(1f, value); }
        public float Stamina { get; private set; }
        public float MaxStamina => maxStamina;
        public bool IsDead { get; private set; }
        public bool IsGrounded { get; private set; }
        public int Facing { get; private set; } = 1;
        public float MoveSpeed => moveSpeed;
        public Rigidbody2D Body { get; private set; }
        public Vector2 ShoulderPosition => Body.position + Vector2.up * ShoulderHeight;
        /// <summary>Rolling fighters cannot be hit.</summary>
        public bool IsRolling => rollTimer > 0f;
        public int RollDirection { get; private set; } = 1;
        /// <summary>0→1 through a roll, -1 when not rolling (drives the tuck in the rig).</summary>
        public float RollProgress => rollTimer > 0f ? 1f - rollTimer / rollDuration : -1f;
        /// <summary>0 (arm in) → 1 (fully extended) → 0 through a punch.</summary>
        public float PunchExtension => punchTimer > 0f ? Mathf.Sin((1f - punchTimer / punchDuration) * Mathf.PI) : 0f;
        public Vector2 PunchDirection { get; private set; } = Vector2.right;
        // Kick phases, as fractions of kickDuration: chamber → snap → hold (hitbox live) → recover.
        public const float KickSnapStart = 0.32f;
        public const float KickHoldStart = 0.42f;
        public const float KickRecoverStart = 0.7f;
        /// <summary>0→1 through a kick, -1 when not kicking (drives the leg pose in the rig).</summary>
        public float KickProgress => kickTimer > 0f ? 1f - kickTimer / kickDuration : -1f;
        public Vector2 KickDirection { get; private set; } = Vector2.right;

        public event Action<Fighter, float> Damaged;
        public event Action<Fighter> Died;
        public event Action<Fighter> Jumped;

        private ContactFilter2D groundFilter;
        private float lastGroundedTime = -1f;
        private float jumpRequestTime = -1f;
        private float staggerTimer;
        private float lungeTimer;
        private float lastStaminaUse = -10f;
        private float rollTimer;
        private float rollReadyTime;
        private float punchTimer;
        private float punchReadyTime;
        private bool punchLanded;
        private float kickTimer;
        private float kickReadyTime;
        private bool kickLanded;
        private readonly Collider2D[] punchHits = new Collider2D[8];
        private readonly ContactPoint2D[] groundContacts = new ContactPoint2D[4];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureLayerCollisions()
        {
            int fighter = LayerMask.NameToLayer(FighterLayer);
            int sword = LayerMask.NameToLayer(SwordLayer);
            int swordHit = LayerMask.NameToLayer(SwordHitLayer);
            if (fighter < 0 || sword < 0 || swordHit < 0)
            {
                return;
            }

            // Blades pass through bodies (damage comes from the SwordHit trigger) but still block each other.
            Physics2D.IgnoreLayerCollision(sword, fighter, true);
            // Fighters can walk through each other to get behind their opponent.
            Physics2D.IgnoreLayerCollision(fighter, fighter, true);
            for (int layer = 0; layer < 32; layer++)
            {
                Physics2D.IgnoreLayerCollision(swordHit, layer, layer != fighter);
            }
        }

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            groundFilter = new ContactFilter2D
            {
                useNormalAngle = true,
                minNormalAngle = 50f,
                maxNormalAngle = 130f,
                useTriggers = false,
            };
            Health = maxHealth;
            Stamina = maxStamina;
            aimPoint = ShoulderPosition + new Vector2(Facing * 2f, 0f);
        }

        public void RequestJump() => jumpRequestTime = Time.time;

        private void Update()
        {
            if (IsDead)
            {
                return;
            }

            // Swing the blade round behind you and you turn with it.
            float dx = aimPoint.x - Body.position.x;
            if (Mathf.Abs(dx) > turnDeadZone)
            {
                Facing = dx > 0f ? 1 : -1;
            }
        }

        /// <summary>True when <paramref name="attacker"/> stands behind this fighter's back.</summary>
        public bool IsBehind(Fighter attacker)
        {
            return Mathf.Sign(attacker.Body.position.x - Body.position.x) == -Facing;
        }

        private void FixedUpdate()
        {
            if (IsDead)
            {
                if (Body.IsTouching(groundFilter))
                {
                    Vector2 corpseVelocity = Body.linearVelocity;
                    corpseVelocity.x = Mathf.MoveTowards(corpseVelocity.x, 0f, CorpseHorizontalDeceleration * Time.fixedDeltaTime);
                    Body.linearVelocity = corpseVelocity;
                }

                return;
            }

            if (Body.position.y < killY)
            {
                Die();
                return;
            }

            float dt = Time.fixedDeltaTime;
            if (Time.time - lastStaminaUse > staminaRegenDelay)
            {
                Stamina = Mathf.Min(maxStamina, Stamina + staminaRegen * dt);
            }

            bool wasGrounded = IsGrounded;
            // Rising through a one-way platform still touches it; that must not count as landing
            // (it handed out a second jump mid-air).
            IsGrounded = Body.IsTouching(groundFilter) && Body.linearVelocity.y <= 0.5f + SlopeRise();
            if (IsGrounded && !wasGrounded && Fx.Instance != null)
            {
                Fx.Instance.Land(Body.position + Vector2.down * HipHeight);
            }

            if (IsGrounded)
            {
                lastGroundedTime = Time.time;
            }

            staggerTimer -= dt;
            lungeTimer -= dt;
            rollTimer -= dt;
            UpdatePunch(dt);
            UpdateKick(dt);
            bool canAct = ControlsEnabled && staggerTimer <= 0f;
            Vector2 velocity = Body.linearVelocity;

            if (rollTimer > 0f)
            {
                // Burst off the feet, then bleed speed as the roll opens up.
                velocity.x = RollDirection * rollSpeed * Mathf.Lerp(1.2f, 0.55f, RollProgress);
            }
            else if (staggerTimer <= 0f && lungeTimer <= 0f)
            {
                // Kicking plants the standing foot.
                float target = canAct && kickTimer <= 0f ? Mathf.Clamp(moveInput, -1f, 1f) * moveSpeed : 0f;
                float accel = IsGrounded ? groundAcceleration : airAcceleration;
                velocity.x = Mathf.MoveTowards(velocity.x, target, accel * dt);
            }

            bool buffered = Time.time - jumpRequestTime <= jumpBuffer;
            bool coyote = Time.time - lastGroundedTime <= coyoteTime;
            if (canAct && buffered && coyote)
            {
                velocity.y = jumpVelocity;
                jumpRequestTime = -1f;
                lastGroundedTime = -1f;
                Jumped?.Invoke(this);
            }

            // Safety net: nothing in a duel should move a fighter faster than this.
            velocity.x = Mathf.Clamp(velocity.x, -maxSpeed, maxSpeed);
            velocity.y = Mathf.Min(velocity.y, jumpVelocity * 1.1f);
            Body.linearVelocity = velocity;
        }

        /// <summary>
        /// Upward speed that walking up the slope under us explains (castle stairs). Zero on flat
        /// ground and one-way ledges, so rising through a ledge still is not a landing.
        /// </summary>
        private float SlopeRise()
        {
            float rise = 0f;
            int count = Body.GetContacts(groundFilter, groundContacts);
            for (int i = 0; i < count; i++)
            {
                Vector2 n = groundContacts[i].normal;
                if (n.y > 0.1f)
                {
                    rise = Mathf.Max(rise, Mathf.Abs(Body.linearVelocity.x * n.x / n.y));
                }
            }

            return rise;
        }

        /// <summary>Spends stamina if there is enough; returns false (and spends nothing) otherwise.</summary>
        public bool TrySpendStamina(float cost)
        {
            if (Stamina < cost)
            {
                return false;
            }

            Stamina -= cost;
            lastStaminaUse = Time.time;
            return true;
        }

        /// <summary>Short forward burst that carries a released thrust.</summary>
        public void Lunge()
        {
            if (!ControlsEnabled || IsDead || staggerTimer > 0f || !IsGrounded)
            {
                return;
            }

            lungeTimer = lungeDuration;
            Body.linearVelocity = new Vector2(Facing * lungeSpeed, Body.linearVelocity.y);
        }

        /// <summary>Ground roll to the left (-1) or right (+1); invulnerable while it lasts.</summary>
        public void Roll(int direction)
        {
            if (!ControlsEnabled || IsDead || !IsGrounded || staggerTimer > 0f || rollTimer > 0f || Time.time < rollReadyTime)
            {
                return;
            }

            if (!TrySpendStamina(rollStaminaCost))
            {
                if (Fx.Instance != null)
                {
                    Fx.Instance.Callout(ShoulderPosition + Vector2.up * 0.6f, "TIRED", new Color(0.75f, 0.75f, 0.8f), 0.7f);
                }

                return;
            }

            RollDirection = direction >= 0 ? 1 : -1;
            rollTimer = rollDuration;
            rollReadyTime = Time.time + rollDuration + rollCooldown;
            punchTimer = 0f;
            kickTimer = 0f;
        }

        /// <summary>Unarmed jab towards the aim point.</summary>
        public void Punch()
        {
            if (!ControlsEnabled || IsDead || staggerTimer > 0f || rollTimer > 0f || kickTimer > 0f || Time.time < punchReadyTime)
            {
                return;
            }

            Vector2 aim = aimPoint - ShoulderPosition;
            PunchDirection = aim.sqrMagnitude > 0.0001f ? aim.normalized : new Vector2(Facing, 0f);
            punchTimer = punchDuration;
            punchReadyTime = Time.time + punchDuration + punchCooldown;
            punchLanded = false;
        }

        /// <summary>The fist connects once, around full extension.</summary>
        private void UpdatePunch(float dt)
        {
            if (punchTimer <= 0f)
            {
                return;
            }

            punchTimer -= dt;
            if (punchLanded || PunchExtension < 0.8f)
            {
                return;
            }

            punchLanded = Strike(ShoulderPosition + PunchDirection * punchReach, punchRadius, punchDamage, PunchDirection, 3f, 1.2f) != null;
        }

        /// <summary>Front kick straight ahead, a little upward; hits harder and shoves further than a punch.</summary>
        public void Kick()
        {
            if (!ControlsEnabled || IsDead || staggerTimer > 0f || rollTimer > 0f || punchTimer > 0f || Time.time < kickReadyTime)
            {
                return;
            }

            KickDirection = new Vector2(Facing, 0.25f).normalized;
            kickTimer = kickDuration;
            kickReadyTime = Time.time + kickDuration + kickCooldown;
            kickLanded = false;
        }

        /// <summary>The foot connects once, around full extension.</summary>
        private void UpdateKick(float dt)
        {
            if (kickTimer <= 0f)
            {
                return;
            }

            float before = KickProgress;
            kickTimer -= dt;
            float now = kickTimer > 0f ? KickProgress : 1f;
            if (before < KickSnapStart && now >= KickSnapStart)
            {
                if (IsGrounded)
                {
                    Body.linearVelocity = new Vector2(Facing * kickStep, Body.linearVelocity.y);
                }

                if (Fx.Instance != null)
                {
                    Fx.Instance.Swoosh(Body.position, 20f);
                }
            }

            // Hitbox is live from mid-snap until the leg starts coming back.
            if (kickLanded || now < (KickSnapStart + KickHoldStart) * 0.5f || before > KickRecoverStart)
            {
                return;
            }

            Fighter victim = Strike(Body.position + KickDirection * kickReach, kickRadius, kickDamage, KickDirection, kickPush, kickLift);
            if (victim != null)
            {
                kickLanded = true;
                victim.ApplyStagger(kickStagger);
                if (Fx.Instance != null)
                {
                    Fx.Instance.Hitstop(0.1f);
                    Fx.Instance.Shake(0.35f);
                }
            }
        }

        /// <summary>Hits the first other living fighter around <paramref name="point"/> and returns it (null on a miss).</summary>
        private Fighter Strike(Vector2 point, float radius, float baseDamage, Vector2 direction, float push, float lift)
        {
            var filter = new ContactFilter2D { useTriggers = true };
            int count = Physics2D.OverlapCircle(point, radius, filter, punchHits);
            for (int i = 0; i < count; i++)
            {
                Rigidbody2D rb = punchHits[i].attachedRigidbody;
                Fighter victim = rb != null ? rb.GetComponent<Fighter>() : null;
                if (victim == null || victim == this || victim.IsDead || victim.IsRolling)
                {
                    continue;
                }

                bool headshot = false;
                for (int j = 0; j < count; j++)
                {
                    headshot |= punchHits[j] == victim.headCollider;
                }

                float damage = (headshot ? baseDamage * 1.5f : baseDamage) * damageScale;
                victim.TakeHit(damage, new Vector2(Mathf.Sign(direction.x) * push, lift), headshot);
                if (Fx.Instance != null)
                {
                    Fx.Instance.Hit(point, direction, victim.color, damage, headshot);
                }

                return victim;
            }

            return null;
        }

        /// <summary>Locks movement briefly, e.g. after your blade was parried.</summary>
        public void ApplyStagger(float seconds) => staggerTimer = Mathf.Max(staggerTimer, seconds);

        public void TakeHit(float damage, Vector2 knockback, bool headshot)
        {
            if (IsDead)
            {
                return;
            }

            Health = Mathf.Max(0f, Health - damage);
            Body.linearVelocity = new Vector2(Body.linearVelocity.x * 0.3f, Mathf.Max(Body.linearVelocity.y, 0f));
            Body.AddForce(knockback, ForceMode2D.Impulse);
            staggerTimer = 0.18f + damage * 0.005f;
            if (rig != null)
            {
                rig.Flinch(knockback, headshot);
            }

            Damaged?.Invoke(this, damage);
            if (Health <= 0f)
            {
                Die();
            }
        }

        public void Die()
        {
            if (IsDead)
            {
                return;
            }

            IsDead = true;
            Health = 0f;
            Body.constraints = RigidbodyConstraints2D.None;
            float pushDirection = Mathf.Abs(Body.linearVelocity.x) > 0.1f ? Mathf.Sign(Body.linearVelocity.x) : -Facing;
            Body.AddTorque(-pushDirection * 4f, ForceMode2D.Impulse);
            if (sword != null)
            {
                sword.Drop();
            }

            Died?.Invoke(this);
        }

        public void RestoreHealth() => Health = maxHealth;

        public void ResetFighter(Vector2 position)
        {
            IsDead = false;
            Health = maxHealth;
            Stamina = maxStamina;
            staggerTimer = 0f;
            moveInput = 0f;
            backGuard = false;
            thrustHeld = false;
            lungeTimer = 0f;
            rollTimer = 0f;
            punchTimer = 0f;
            kickTimer = 0f;
            jumpRequestTime = -1f;

            Body.constraints = RigidbodyConstraints2D.FreezeRotation;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0f;
            Body.position = position;
            Body.rotation = 0f;
            transform.SetPositionAndRotation(position, Quaternion.identity);

            if (opponent != null)
            {
                Facing = opponent.transform.position.x >= position.x ? 1 : -1;
            }

            aimPoint = ShoulderPosition + new Vector2(Facing * 2f, -0.3f);
            if (sword != null)
            {
                sword.Regrip();
            }

            if (rig != null)
            {
                rig.Snap();
            }
        }
    }
}
