using System.Collections.Generic;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Thrown shuriken: follows an exact ballistic arc (<see cref="PositionAt"/>, shared with the aim
    /// line so the preview matches the flight), spinning, circle-casting each frame along the arc.
    /// Hits the first fighter it touches, bounces off an enemy blade, sticks in the ground.
    /// </summary>
    public sealed class Shuriken : MonoBehaviour
    {
        private const float Size = 0.34f;
        private const float HitRadius = 0.08f;
        private const float SpinSpeed = 1080f;
        private const float StuckLifetime = 2f;
        private const float FlightLifetime = 4f;

        private static readonly RaycastHit2D[] Hits = new RaycastHit2D[16];
        private static Sprite sprite;

        private Fighter owner;
        private Vector2 origin;
        private Vector2 launchVelocity;
        private Vector2 velocity;
        private float gravity;
        private float flightTime;
        private float damage;
        private float life = FlightLifetime;
        private bool stuck;

        /// <summary>Procedural four-bladed steel shuriken, shared by the projectile and the HUD.</summary>
        public static Sprite Sprite => sprite != null ? sprite : sprite = CreateSprite();

        public static Vector2 PositionAt(Vector2 origin, Vector2 velocity, float gravity, float t)
            => origin + velocity * t + 0.5f * gravity * t * t * Vector2.down;

        public static void Launch(Fighter owner, Vector2 position, Vector2 velocity, float gravity, float damage)
        {
            var go = new GameObject("Shuriken");
            go.transform.position = position;
            var shuriken = go.AddComponent<Shuriken>();
            shuriken.owner = owner;
            shuriken.origin = position;
            shuriken.launchVelocity = velocity;
            shuriken.velocity = velocity;
            shuriken.gravity = gravity;
            shuriken.damage = damage;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Sprite;
            renderer.sortingOrder = 20;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            life -= dt;
            if (life <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            if (stuck)
            {
                return;
            }

            // Clockwise when flying right, like a real throw.
            transform.Rotate(0f, 0f, -Mathf.Sign(velocity.x == 0f ? 1f : velocity.x) * SpinSpeed * dt);

            Vector2 from = transform.position;
            flightTime += dt;
            Vector2 next = PositionAt(origin, launchVelocity, gravity, flightTime);
            velocity = launchVelocity + gravity * flightTime * Vector2.down;
            Vector2 step = next - from;
            var filter = new ContactFilter2D { useTriggers = true };
            int count = Physics2D.CircleCast(from, HitRadius, step.normalized, filter, Hits, step.magnitude);
            System.Array.Sort(Hits, 0, count, HitDistance.Instance);
            for (int i = 0; i < count; i++)
            {
                if (Resolve(Hits[i]))
                {
                    return;
                }
            }

            transform.position = next;
        }

        /// <summary>Returns true when the shuriken stops (hit, deflected or stuck).</summary>
        private bool Resolve(RaycastHit2D hit)
        {
            Collider2D other = hit.collider;
            Rigidbody2D rb = other.attachedRigidbody;
            if (rb == null)
            {
                if (other.isTrigger)
                {
                    return false;
                }

                // Ground / walls: bite in a little and stay.
                transform.position = hit.centroid + velocity.normalized * Size * 0.2f;
                stuck = true;
                life = StuckLifetime;
                return true;
            }

            Fighter victim = rb.GetComponent<Fighter>();
            if (victim != null)
            {
                if (victim == owner || victim.IsDead || victim.IsRolling)
                {
                    return false;
                }

                bool headshot = other == victim.headCollider;
                float dealt = headshot ? damage * 1.5f : damage;
                Vector2 direction = velocity.normalized;
                victim.TakeHit(dealt, new Vector2(Mathf.Sign(direction.x) * 2.5f, 1.2f), headshot);
                if (Fx.Instance != null)
                {
                    Fx.Instance.Hit(hit.point, direction, victim.color, dealt, headshot);
                }

                Destroy(gameObject);
                return true;
            }

            Sword sword = rb.GetComponent<Sword>();
            if (sword != null && sword.Owner != owner && !other.isTrigger)
            {
                // Batted away by the enemy blade.
                if (Fx.Instance != null)
                {
                    Fx.Instance.Clash(hit.point, velocity.magnitude);
                }

                Destroy(gameObject);
                return true;
            }

            return false;
        }

        private sealed class HitDistance : IComparer<RaycastHit2D>
        {
            public static readonly HitDistance Instance = new HitDistance();
            public int Compare(RaycastHit2D a, RaycastHit2D b) => a.distance.CompareTo(b.distance);
        }

        // ------------------------------------------------------------------ sprite

        private const int Blades = 4;
        private const float TipRadius = 0.97f;
        private const float NotchRadius = 0.38f;
        private const float NotchSkew = 16f; // degrees past the midpoint: gives the pinwheel hook
        private const float HoleRadius = 0.11f;
        private const float HubRadius = 0.25f;
        private const float OutlineWidth = 0.055f;
        private const float EdgeWidth = 0.085f;

        /// <summary>
        /// Draws a hira-shuriken in [-1, 1]² with 3×3 supersampling: hooked blades, each with two
        /// bevelled faces lit from the upper left, a polished cutting edge, a raised hub and a hole.
        /// </summary>
        private static Sprite CreateSprite()
        {
            const int n = 128;
            const int ss = 3;
            var outline = new Vector2[Blades * 2];
            for (int k = 0; k < Blades; k++)
            {
                float tip = k * 360f / Blades;
                outline[k * 2] = Polar(tip, TipRadius);
                outline[k * 2 + 1] = Polar(tip + 180f / Blades + NotchSkew, NotchRadius);
            }

            var pixels = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    Color sum = Color.clear;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        for (int sx = 0; sx < ss; sx++)
                        {
                            var p = new Vector2((x + (sx + 0.5f) / ss) / n * 2f - 1f, (y + (sy + 0.5f) / ss) / n * 2f - 1f);
                            Color c = Shade(p, outline);
                            sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                        }
                    }

                    float a = sum.a / (ss * ss);
                    pixels[y * n + x] = sum.a > 0f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, a) : Color.clear;
                }
            }

            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                name = "Shuriken",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), n / Size);
        }

        private static Color Shade(Vector2 p, Vector2[] outline)
        {
            Color ink = new Color(0.06f, 0.03f, 0.09f, 1f);
            float r = p.magnitude;
            if (r < HoleRadius)
            {
                return Color.clear;
            }

            if (r < HoleRadius + OutlineWidth * 0.8f || !Inside(p, outline))
            {
                return r < HoleRadius + OutlineWidth * 0.8f ? ink : Color.clear;
            }

            float edge = EdgeDistance(p, outline);
            if (edge < OutlineWidth)
            {
                return ink;
            }

            // Hub: raised ring around the hole with a dark groove at its rim.
            if (r < HubRadius)
            {
                float hubLight = 0.55f + 0.3f * Vector2.Dot(p / r, new Vector2(-0.7071f, 0.7071f));
                return Steel(hubLight);
            }

            if (r < HubRadius + 0.035f)
            {
                return Color.Lerp(ink, Steel(0.3f), 0.35f);
            }

            // Which blade: blade k spans from the previous notch to its own notch.
            float angle = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
            float span = 360f / Blades;
            float trailing = span - (span * 0.5f + NotchSkew);
            int blade = Mathf.FloorToInt(Mathf.Repeat(angle + trailing, 360f) / span);
            Vector2 ridge = Polar(blade * span, 1f);

            // Each blade is ground into two faces that slope away from the centre ridge; the face
            // normal leans to its side, so the light (upper left) catches one face more than the other.
            float side = Mathf.Sign(ridge.x * p.y - ridge.y * p.x);
            Vector2 normal = new Vector2(-ridge.y, ridge.x) * side;
            float light = 0.5f + 0.38f * Vector2.Dot(normal, new Vector2(-0.7071f, 0.7071f));
            light += 0.08f * (r - HubRadius); // a touch brighter towards the tip
            if (edge < EdgeWidth)
            {
                light = Mathf.Max(light, 0.95f); // polished cutting edge
            }

            return Steel(light);
        }

        private static Color Steel(float light)
        {
            Color dark = new Color(0.22f, 0.24f, 0.3f);
            Color bright = new Color(0.9f, 0.92f, 0.97f);
            return Color.Lerp(dark, bright, Mathf.Clamp01(light));
        }

        private static Vector2 Polar(float degrees, float radius)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        private static bool Inside(Vector2 p, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static float EdgeDistance(Vector2 p, Vector2[] polygon)
        {
            float best = float.MaxValue;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[j];
                Vector2 ab = polygon[i] - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }

            return best;
        }
    }
}
