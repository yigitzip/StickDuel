using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// A RUN! bot posted in an archer tower or a sniper hut: it never moves, it only shoots at its
    /// <see cref="Fighter.opponent"/>, and only while both of them are on screen. The held weapon does
    /// the rest (the arrow's flight, the rifle's laser, sway, walls, damage).
    /// <list type="bullet">
    /// <item>Bow: draws to full, lobbed so the arrow comes down on where the runner will be, then looses.</item>
    /// <item>Rifle: the laser tracks the runner's chest (a little off); it fires once the runner has been in view for a moment and the laser is on them or on their shield, never into the roof.</item>
    /// </list>
    /// Writes the same Fighter input fields as the drivers, never devices.
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public sealed class RunGunner : MonoBehaviour
    {
        [Tooltip("Seconds between shots, rolled between the two.")]
        [SerializeField] private Vector2 interval = new Vector2(2.2f, 3.6f);
        [Tooltip("The aim lands up to this far (world units) off the target, re-rolled every shot.")]
        [SerializeField, Min(0f)] private float aimError = 0.5f;
        [Tooltip("Rifle: seconds the runner must be in view before the first shot (the laser is the warning).")]
        [SerializeField, Min(0f)] private float sniperWarmUp = 1.2f;
        [SerializeField, Min(0.01f)] private float aimSmoothTime = 0.15f;
        [SerializeField, Min(1f)] private float range = 22f;

        private Fighter self;
        private Vector2 aimOffset, aimVelocity;
        private float nextShot, inViewFor, releaseAt = -1f;

        private void Awake()
        {
            self = GetComponent<Fighter>();
        }

        private void Update()
        {
            Fighter target = self.opponent;
            var bow = self.heldWeapon as BowWeapon;
            var rifle = self.heldWeapon as SniperRifleWeapon;
            self.moveInput = 0f;
            bool live = !self.IsDead && self.ControlsEnabled && target != null && !target.IsDead && target.ControlsEnabled && self.heldWeapon != null && self.heldWeapon.IsHeld;
            bool inView = live && OnScreen(self.Body.position) && OnScreen(target.Body.position)
                && Vector2.Distance(self.Body.position, target.Body.position) <= range;
            inViewFor = inView ? inViewFor + Time.deltaTime : 0f;
            if (!live)
            {
                if (bow != null) bow.DrawHeld = false;
                return;
            }

            Vector2 chest = target.Body.position + Vector2.up * 0.35f + aimOffset;
            if (bow != null)
            {
                Aim(Lob(bow, chest, target.Body.linearVelocity));
                Archer(bow, inView);
            }
            else if (rifle != null)
            {
                Aim(chest);
                Sniper(rifle, target, inView);
            }
        }

        private void Aim(Vector2 point)
        {
            self.aimPoint = Vector2.SmoothDamp(self.aimPoint, point, ref aimVelocity, aimSmoothTime, Mathf.Infinity, Time.deltaTime);
        }

        /// <summary>Hold the draw until it is full, then let go; the next draw waits for the interval.</summary>
        private void Archer(BowWeapon bow, bool inView)
        {
            if (releaseAt >= 0f)
            {
                bow.DrawHeld = Time.time < releaseAt;
                if (!bow.DrawHeld)
                {
                    releaseAt = -1f;
                    Reload();
                }

                return;
            }

            bow.DrawHeld = false;
            if (inView && Time.time >= nextShot)
            {
                releaseAt = Time.time + 0.8f; // the bow is fully drawn after 0.7 s
            }
        }

        private void Sniper(SniperRifleWeapon rifle, Fighter target, bool inView)
        {
            if (!inView || inViewFor < sniperWarmUp || Time.time < nextShot)
            {
                return;
            }

            SniperShot shot = rifle.CalculateShot(); // where the laser points this frame
            if ((shot.Target == target || SniperRifleWeapon.IsShield(shot.Collider)) && rifle.TryFire())
            {
                Reload();
            }
        }

        private void Reload()
        {
            nextShot = Time.time + Random.Range(interval.x, interval.y);
            aimOffset = Random.insideUnitCircle * aimError;
        }

        /// <summary>
        /// Where to point the bow so a full-draw arrow lands on <paramref name="target"/>, led by the
        /// runner's speed over the flight: the low arc of the projectile equation, 45 degrees out of reach.
        /// </summary>
        private Vector2 Lob(BowWeapon bow, Vector2 target, Vector2 targetVelocity)
        {
            Vector2 from = bow.ArrowOrigin;
            float v = bow.FullDrawSpeed, g = bow.ArrowGravity;
            float angle = LowArc(target - from, v, g);
            target.x += targetVelocity.x * Mathf.Abs(target.x - from.x) / Mathf.Max(v * Mathf.Cos(angle), 0.1f); // where the runner will be
            angle = LowArc(target - from, v, g);

            float side = target.x >= self.Body.position.x ? 1f : -1f;
            return self.ShoulderPosition + new Vector2(side * Mathf.Cos(angle), Mathf.Sin(angle)) * 3f;
        }

        /// <summary>Launch angle (radians, up from level) of the flatter arc through <paramref name="offset"/>; 45 degrees when out of reach.</summary>
        private static float LowArc(Vector2 offset, float v, float g)
        {
            float dx = Mathf.Max(Mathf.Abs(offset.x), 0.01f), dy = offset.y;
            float root = v * v * v * v - g * (g * dx * dx + 2f * dy * v * v);
            return root >= 0f ? Mathf.Atan2(v * v - Mathf.Sqrt(root), g * dx) : Mathf.PI * 0.25f;
        }

        private static bool OnScreen(Vector2 point)
        {
            Camera view = Camera.main;
            if (view == null)
            {
                return true;
            }

            Vector3 p = view.WorldToViewportPoint(point);
            return p.x >= 0f && p.x <= 1f && p.y >= 0f && p.y <= 1f;
        }
    }
}
