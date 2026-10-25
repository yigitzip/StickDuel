using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Duelling AI: keeps a striking distance, winds up and slashes or thrusts, and raises
    /// its blade into the path of fast incoming swings after a reaction delay.
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public sealed class AIDriver : MonoBehaviour
    {
        private enum Mode { Guard, WindUp, Slash, ThrustPrep, Thrust, Block }

        [SerializeField, Range(0f, 1f)] private float skill = 0.55f;
        [SerializeField, Min(0.5f)] private float engageRange = 2.1f;
        [SerializeField, Min(1f)] private float arenaHalfWidth = 12f;

        private Fighter self;
        private Mode mode;
        private float modeTimer;
        private float threatTimer;
        private float strafeBias;
        private float strafeTimer;
        private float flankTimer;
        private int flankDirection;
        private Vector2 aimVelocity;
        private float minX;
        private float maxX;

        public float ArenaHalfWidth { set => SetBounds(-value, value); }
        public float Skill { get => skill; set => skill = Mathf.Clamp01(value); }

        private float ReactionTime => Mathf.Lerp(0.38f, 0.12f, skill);

        private void Awake()
        {
            self = GetComponent<Fighter>();
            modeTimer = 1f;
            SetBounds(-arenaHalfWidth, arenaHalfWidth);
        }

        /// <summary>The stretch of floor the bot fights on (a castle room, or the whole arena).</summary>
        public void SetBounds(float left, float right)
        {
            minX = left;
            maxX = right;
        }

        private void Update()
        {
            Fighter opponent = self.opponent;
            if (opponent == null || self.IsDead)
            {
                return;
            }

            float dt = Time.deltaTime;
            Vector2 me = self.Body.position;
            Vector2 them = opponent.Body.position;
            float dx = them.x - me.x;
            float distance = Mathf.Abs(dx);
            int toward = dx >= 0f ? 1 : -1;
            Vector2 shoulder = self.ShoulderPosition;
            modeTimer -= dt;

            // Opponent slipped behind us: cover the back until we have turned around.
            self.backGuard = self.IsBehind(opponent);
            UpdateDefense(opponent, shoulder, dt);
            if (modeTimer <= 0f)
            {
                NextMode(distance);
            }

            Vector2 target;
            float smoothTime;
            switch (mode)
            {
                case Mode.WindUp:
                    target = shoulder + new Vector2(toward * 0.3f, 1.7f); // in front: facing follows the aim
                    smoothTime = 0.09f;
                    break;
                case Mode.Slash:
                    target = opponent.ShoulderPosition + new Vector2(toward * 0.4f, -1.4f);
                    smoothTime = 0.025f;
                    break;
                case Mode.ThrustPrep:
                    target = shoulder + new Vector2(toward * 0.3f, -0.05f);
                    smoothTime = 0.06f;
                    break;
                case Mode.Thrust:
                    target = opponent.ShoulderPosition;
                    smoothTime = 0.02f;
                    break;
                case Mode.Block:
                    Sword enemyBlade = opponent.sword;
                    target = (enemyBlade.GripPosition + enemyBlade.TipPosition) * 0.5f;
                    smoothTime = 0.04f;
                    break;
                default:
                    target = shoulder + new Vector2(toward * 1.2f, -0.15f + Mathf.Sin(Time.time * 1.7f) * 0.35f);
                    smoothTime = 0.14f;
                    break;
            }

            smoothTime *= Mathf.Lerp(1.5f, 0.85f, skill);
            self.aimPoint = Vector2.SmoothDamp(self.aimPoint, target, ref aimVelocity, smoothTime, Mathf.Infinity, dt);
            self.thrustHeld = mode == Mode.ThrustPrep;
            self.moveInput = ChooseMove(distance, toward, me.x, dt);
            if (flankTimer > 0f)
            {
                flankTimer -= dt;
                self.moveInput = flankDirection;
            }

            if (self.IsGrounded && Random.value < dt * (opponent.IsGrounded ? 0.12f : 0.6f) * skill)
            {
                self.RequestJump();
            }

            // Never walk off a ledge or into lava; jump the gap when the opponent is across it.
            if (self.IsGrounded && Mathf.Abs(self.moveInput) > 0.1f)
            {
                int direction = self.moveInput > 0f ? 1 : -1;
                if (!GroundAhead(me, direction))
                {
                    if (direction == toward && distance > 1.2f)
                    {
                        self.RequestJump();
                    }
                    else
                    {
                        self.moveInput = 0f;
                    }
                }
            }
        }

        /// <summary>Solid ground about level with our feet under the next step (a pit floor or a lower island does not count).</summary>
        private static bool GroundAhead(Vector2 hip, int direction)
        {
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = 1 };
            return Physics2D.Raycast(hip + new Vector2(direction * 0.9f, 0f), Vector2.down, filter, groundHits, Fighter.HipHeight + 0.5f) > 0;
        }

        private static readonly RaycastHit2D[] groundHits = new RaycastHit2D[1];

        private void UpdateDefense(Fighter opponent, Vector2 shoulder, float dt)
        {
            Sword enemyBlade = opponent.sword;
            bool threat = enemyBlade != null && enemyBlade.IsHeld
                          && Vector2.Distance(enemyBlade.TipPosition, shoulder) < 2.6f
                          && enemyBlade.TipVelocity.magnitude > 7f;
            threatTimer = threat ? threatTimer + dt : 0f;

            bool committed = mode == Mode.Slash || mode == Mode.Thrust;
            if (threat && !committed && mode != Mode.Block && threatTimer >= ReactionTime && Random.value < 0.35f + skill * 0.6f)
            {
                SetMode(Mode.Block, 0.35f);
            }
        }

        private void NextMode(float distance)
        {
            switch (mode)
            {
                case Mode.WindUp:
                    SetMode(Mode.Slash, 0.28f);
                    break;
                case Mode.ThrustPrep:
                    SetMode(Mode.Thrust, 0.25f);
                    break;
                case Mode.Guard:
                    if (distance < engageRange + 0.6f && Random.value < 0.35f * skill && CanFlank())
                    {
                        // Dash through the opponent to strike their back before they turn.
                        flankDirection = self.Facing;
                        flankTimer = 0.45f + distance / self.MoveSpeed;
                        SetMode(Mode.WindUp, flankTimer);
                    }
                    else if (distance < engageRange + 0.6f)
                    {
                        if (Random.value < 0.6f)
                        {
                            SetMode(Mode.WindUp, Mathf.Lerp(0.45f, 0.25f, skill));
                        }
                        else
                        {
                            SetMode(Mode.ThrustPrep, Mathf.Lerp(0.4f, 0.22f, skill));
                        }
                    }
                    else
                    {
                        SetMode(Mode.Guard, 0.3f);
                    }

                    break;
                default:
                    SetMode(Mode.Guard, Random.Range(0.5f, 1.4f) * Mathf.Lerp(1.4f, 0.7f, skill));
                    break;
            }
        }

        private bool CanFlank()
        {
            float landing = self.opponent.Body.position.x + self.Facing * 1.5f;
            return landing > minX + 1.5f && landing < maxX - 1.5f;
        }

        private void SetMode(Mode next, float duration)
        {
            mode = next;
            modeTimer = duration;
        }

        private float ChooseMove(float distance, int toward, float x, float dt)
        {
            strafeTimer -= dt;
            if (strafeTimer <= 0f)
            {
                strafeTimer = Random.Range(0.4f, 1.3f);
                strafeBias = Random.Range(-0.45f, 0.45f);
            }

            float desired = mode == Mode.Slash || mode == Mode.Thrust ? engageRange - 0.6f : engageRange;
            float move = strafeBias;
            if (distance > desired + 0.35f)
            {
                move = toward;
            }
            else if (distance < desired - 0.5f)
            {
                move = -toward;
            }

            // Never back off the cliff.
            if (x < minX + 1.5f && move <= 0f)
            {
                move = 0.5f;
            }
            else if (x > maxX - 1.5f && move >= 0f)
            {
                move = -0.5f;
            }

            return move;
        }
    }
}
