using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Sniper Mode's bot, in place of the sword <see cref="AIDriver"/> (which Sniper Mode disables).
    /// It only decides; the held <see cref="SniperRifleWeapon"/> does everything else (laser,
    /// raycast, walls, cooldown, damage, sway and recoil, which hit the bot like the player).
    /// <list type="bullet">
    /// <item>Sight: <see cref="SniperRifleWeapon.CalculateShot(Vector2)"/> from the muzzle to the opponent's head and chest, the very query the laser and the shot use, so a wall in between means "not seen".</item>
    /// <item>Seen: stand, swing the aim at a point a little off the target (<see cref="aimError"/>, re-rolled each decision) and, once seen for <see cref="reactionDelay"/>, fire when the laser is on them (sometimes early: <see cref="snapShotChance"/>).</item>
    /// <item>Not seen: never fire; walk to another standing spot (<see cref="Spots"/>) every <see cref="repositionInterval"/>, jumping onto what is in the way. No pathfinding.</item>
    /// </list>
    /// Writes the same Fighter input fields as the drivers, never devices.
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public sealed class SniperBotController : MonoBehaviour
    {
        [Header("Aim and fire")]
        [Tooltip("Seconds the target must stay in sight before the first shot.")]
        [SerializeField, Min(0f)] private float reactionDelay = 0.55f;
        [Tooltip("The aim lands up to this far (world units) off the target, re-rolled every decision.")]
        [SerializeField, Min(0f)] private float aimError = 0.55f;
        [SerializeField, Min(0.01f)] private float aimSmoothTime = 0.2f;
        [Tooltip("Seconds between fire decisions.")]
        [SerializeField, Min(0.05f)] private float fireDecisionInterval = 0.35f;
        [Tooltip("Chance to fire on a decision even though the laser is not on the target (usually a miss).")]
        [SerializeField, Range(0f, 1f)] private float snapShotChance = 0.2f;
        [Tooltip("Targets further than this are ignored (the rifle's own range also applies).")]
        [SerializeField, Min(1f)] private float aimRange = 26f;
        [Tooltip("Closer than this it backs off instead of shooting.")]
        [SerializeField, Min(0f)] private float minShotDistance = 1.5f;

        [Header("Movement")]
        [SerializeField, Min(0.5f)] private float repositionInterval = 3f;
        [Tooltip("Fraction of full walking speed.")]
        [SerializeField, Range(0.1f, 1f)] private float moveSpeed = 0.75f;
        [Tooltip("What counts as a step to jump onto while walking.")]
        [SerializeField] private LayerMask obstacleMask = 1;

        private static readonly RaycastHit2D[] wallHits = new RaycastHit2D[1];

        private Fighter self;
        private Vector2 aimVelocity, aimOffset;
        private Vector2 spot;
        private bool hasSpot;
        private float seenFor, nextDecision, nextReposition;
        private float stuckFor, lastX;
        private bool warned;

        /// <summary>Hip positions it may walk to when it can't see the target (Sniper Mode passes the map's).</summary>
        public Vector2[] Spots { get; set; }

        private void Awake()
        {
            self = GetComponent<Fighter>();
        }

        private void OnDisable()
        {
            if (self != null)
            {
                self.moveInput = 0f;
            }
        }

        private void Update()
        {
            Fighter target = self.opponent;
            if (self.IsDead || !self.ControlsEnabled || target == null || target.IsDead)
            {
                self.moveInput = 0f;
                seenFor = 0f;
                return;
            }

            var rifle = self.heldWeapon as SniperRifleWeapon;
            if (rifle == null || !rifle.IsHeld)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogError($"SniperBotController: {self.name} holds no sniper rifle; it won't aim or fire.", this);
                }

                self.moveInput = 0f;
                return;
            }

            float dt = Time.deltaTime;
            bool visible = InSight(rifle, target, out Vector2 aimAt);
            seenFor = visible ? seenFor + dt : 0f;
            if (Time.time >= nextDecision)
            {
                nextDecision = Time.time + fireDecisionInterval;
                aimOffset = Random.insideUnitCircle * aimError;
                if (visible && seenFor >= reactionDelay)
                {
                    DecideFire(rifle, target);
                }
            }

            // Out of sight it still points at them, ready to swing in when they show up.
            Vector2 look = visible ? aimAt + aimOffset : target.ShoulderPosition;
            self.aimPoint = Vector2.SmoothDamp(self.aimPoint, look, ref aimVelocity, aimSmoothTime, Mathf.Infinity, dt);
            self.moveInput = visible ? BackOff(target) : Reposition(dt);
        }

        /// <summary>Head or chest reachable by a shot from the muzzle right now (walls and the bot's own body don't count as the target).</summary>
        private bool InSight(SniperRifleWeapon rifle, Fighter target, out Vector2 point)
        {
            Vector2 muzzle = rifle.MuzzlePosition;
            Vector2 chest = target.Body.position + Vector2.up * 0.35f;
            Vector2 head = target.headCollider != null ? (Vector2)target.headCollider.bounds.center : target.ShoulderPosition + Vector2.up * 0.3f;
            foreach (Vector2 candidate in new[] { chest, head })
            {
                if (Vector2.Distance(muzzle, candidate) <= aimRange && rifle.CalculateShot(candidate - muzzle).Target == target)
                {
                    point = candidate;
                    return true;
                }
            }

            point = default;
            return false;
        }

        private void DecideFire(SniperRifleWeapon rifle, Fighter target)
        {
            if (Vector2.Distance(self.Body.position, target.Body.position) < minShotDistance)
            {
                return;
            }

            SniperShot barrel = rifle.CalculateShot(); // where the laser points this frame
            if (barrel.Blocked)
            {
                return; // the swaying barrel is on a wall: a wasted round
            }

            if (barrel.Target == target || Random.value < snapShotChance)
            {
                rifle.TryFire(); // the rifle enforces its own cooldown
            }
        }

        /// <summary>In sight: hold still to shoot, stepping away only when they are too close.</summary>
        private float BackOff(Fighter target)
        {
            hasSpot = false;
            float dx = self.Body.position.x - target.Body.position.x;
            return Mathf.Abs(dx) < minShotDistance ? Mathf.Sign(dx == 0f ? 1f : dx) * moveSpeed : 0f;
        }

        /// <summary>Out of sight: walk to a standing spot, jump onto steps and up to ledges, give up on a spot it is stuck on.</summary>
        private float Reposition(float dt)
        {
            if (Spots == null || Spots.Length == 0)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogError($"SniperBotController: {self.name} has no Spots to move between; it will hold position.", this);
                }

                return 0f;
            }

            Vector2 me = self.Body.position;
            if (!hasSpot || Time.time >= nextReposition)
            {
                PickSpot(me);
            }

            float dx = spot.x - me.x;
            if (Mathf.Abs(dx) < 0.4f)
            {
                stuckFor = 0f;
                if (spot.y > me.y + 1f && self.IsGrounded)
                {
                    self.RequestJump(); // the spot is a ledge right above
                }

                return 0f;
            }

            int direction = dx > 0f ? 1 : -1;
            if (self.IsGrounded && !GroundAhead(me, direction) && spot.y >= me.y - 0.5f)
            {
                PickSpot(me); // a drop (canal, a gap between girders, a raised bridge) is in the way: go elsewhere
                return 0f;
            }

            if (self.IsGrounded && (WallAhead(me, direction) || (spot.y > me.y + 1f && Mathf.Abs(dx) < 1.5f)))
            {
                self.RequestJump();
            }

            // Not getting anywhere (a wall too high to jump): try somewhere else.
            stuckFor = Mathf.Abs(me.x - lastX) < 0.02f ? stuckFor + dt : 0f;
            lastX = me.x;
            if (stuckFor > 1.5f)
            {
                stuckFor = 0f;
                PickSpot(me);
            }

            return direction * moveSpeed;
        }

        private void PickSpot(Vector2 me)
        {
            Vector2 next = Spots[Random.Range(0, Spots.Length)];
            if (Spots.Length > 1 && Vector2.Distance(next, me) < 1f)
            {
                next = Spots[(System.Array.IndexOf(Spots, next) + 1) % Spots.Length]; // not where it already stands
            }

            spot = next;
            hasSpot = true;
            nextReposition = Time.time + repositionInterval * Random.Range(0.7f, 1.3f);
        }

        /// <summary>Something to stand on under the next step, no more than a short drop down.</summary>
        private bool GroundAhead(Vector2 hip, int direction)
        {
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = obstacleMask };
            return Physics2D.Raycast(hip + new Vector2(direction * 0.7f, 0f), Vector2.down, filter, wallHits, Fighter.HipHeight + 2.5f) > 0;
        }

        private bool WallAhead(Vector2 hip, int direction)
        {
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = obstacleMask };
            return Physics2D.Raycast(hip + Vector2.down * 0.5f, new Vector2(direction, 0f), filter, wallHits, 0.8f) > 0;
        }
    }
}
