using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// A Sniper Mode moving part (train, lift, drawbridge leaf): a kinematic body that waits, slides
    /// (and/or swings) to the far end, waits, and comes back, forever. Its colliders stop shots like
    /// any wall, so lanes open and close as it moves. <see cref="Restart"/> puts it back at rest.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class SniperMapMover : MonoBehaviour
    {
        private Rigidbody2D body;
        private Vector2 start, travel;
        private float swing, hold0, move, hold1, offset, clock;

        /// <param name="travel">World offset at the far end.</param>
        /// <param name="swing">Degrees turned at the far end (drawbridge).</param>
        /// <param name="offset">0-1 of a cycle, to run two parts out of step.</param>
        public void Init(Vector2 travel, float swing, float hold0, float move, float hold1, float offset)
        {
            body = GetComponent<Rigidbody2D>();
            start = body.position;
            this.travel = travel;
            this.swing = swing;
            this.hold0 = Mathf.Max(0f, hold0);
            this.move = Mathf.Max(0.05f, move);
            this.hold1 = Mathf.Max(0f, hold1);
            this.offset = offset;
            Restart();
        }

        /// <summary>Back to the start of the cycle (a new round).</summary>
        public void Restart()
        {
            clock = offset * Cycle;
            Apply(Phase(clock));
        }

        private float Cycle => hold0 + move * 2f + hold1;

        /// <summary>0 at rest, 1 at the far end, eased in between.</summary>
        private float Phase(float t)
        {
            float u = Mathf.Repeat(t, Cycle);
            if (u < hold0) return 0f;
            u -= hold0;
            if (u < move) return Mathf.SmoothStep(0f, 1f, u / move);
            u -= move;
            if (u < hold1) return 1f;
            return Mathf.SmoothStep(1f, 0f, (u - hold1) / move);
        }

        private void FixedUpdate()
        {
            clock += Time.fixedDeltaTime;
            float p = Phase(clock);
            body.MovePosition(start + travel * p);
            if (swing != 0f)
            {
                body.MoveRotation(swing * p);
            }
        }

        private void Apply(float p)
        {
            body.position = start + travel * p;
            body.rotation = swing * p;
            transform.SetPositionAndRotation(body.position, Quaternion.Euler(0f, 0f, body.rotation));
        }
    }

    /// <summary>The canal, the drop off a skyscraper: a fighter that falls in dies on the spot.</summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class SniperKillZone : MonoBehaviour
    {
        private void OnTriggerEnter2D(Collider2D other)
        {
            Fighter fighter = other.attachedRigidbody != null ? other.attachedRigidbody.GetComponent<Fighter>() : null;
            if (fighter != null && !fighter.IsDead)
            {
                fighter.Die();
            }
        }
    }

    /// <summary>
    /// Lighthouse Isle: a dark layer over the fighters with a hole cut by the sweeping beam (a
    /// SpriteMask cone turning round the lamp). Lasers and hit effects sort above the dark, so a
    /// sniper's red laser still gives them away. Visual only: shots ignore it.
    /// </summary>
    public sealed class SniperLighthouseBeam : MonoBehaviour
    {
        private const int DarkOrder = 16; // over fighters (2-15) and rifles (14), under lasers (19) and effects
        private const float Spread = 13f;  // half-angle of the cone, degrees
        private const float Length = 45f;

        private float period;
        private static Sprite cone, square;

        public static SniperLighthouseBeam Create(Transform root, Vector2 lamp, Rect painting, float period)
        {
            var dark = new GameObject("Darkness").AddComponent<SpriteRenderer>();
            dark.transform.SetParent(root, false);
            dark.transform.position = painting.center;
            dark.transform.localScale = new Vector3(painting.width + 2f, painting.height + 2f, 1f);
            dark.sprite = Square;
            dark.color = new Color(0.01f, 0.02f, 0.06f, 0.8f);
            dark.sortingOrder = DarkOrder;
            dark.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;

            var beam = new GameObject("LighthouseBeam").AddComponent<SniperLighthouseBeam>();
            beam.transform.SetParent(root, false);
            beam.transform.position = lamp;
            beam.period = Mathf.Max(1f, period);

            var mask = new GameObject("Light").AddComponent<SpriteMask>();
            mask.transform.SetParent(beam.transform, false);
            mask.sprite = Cone;
            mask.alphaCutoff = 0.1f;

            var glow = new GameObject("Glow").AddComponent<SpriteRenderer>();
            glow.transform.SetParent(beam.transform, false);
            glow.sprite = Cone;
            glow.color = new Color(0.85f, 0.93f, 1f, 0.12f);
            glow.sortingOrder = DarkOrder + 1;

            // The lamp itself always shows a little pool of light.
            var lampMask = new GameObject("LampLight").AddComponent<SpriteMask>();
            lampMask.transform.SetParent(root, false);
            lampMask.transform.position = lamp;
            lampMask.transform.localScale = Vector3.one * 2.2f;
            lampMask.sprite = Square;
            return beam;
        }

        private void Update()
        {
            transform.rotation = Quaternion.Euler(0f, 0f, Time.time / period * 360f);
        }

        private static Sprite Square
        {
            get
            {
                if (square == null)
                {
                    Texture2D white = Texture2D.whiteTexture;
                    square = Sprite.Create(white, new Rect(0f, 0f, white.width, white.height), new Vector2(0.5f, 0.5f), white.width);
                }

                return square;
            }
        }

        /// <summary>A soft-edged wedge pointing along +x from its apex (the pivot).</summary>
        private static Sprite Cone
        {
            get
            {
                if (cone != null)
                {
                    return cone;
                }

                const int w = 256, h = 128;
                float tan = Mathf.Tan(Spread * Mathf.Deg2Rad);
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[w * h];
                float halfHeight = 0.5f * h * Length / w; // the texture's half height in world units
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float along = (x + 0.5f) / w * Length;
                        float across = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f * halfHeight;
                        float edge = along * tan;
                        float a = Mathf.Clamp01((edge - across) / Mathf.Max(0.3f, edge * 0.35f));
                        pixels[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply();
                cone = Sprite.Create(texture, new Rect(0f, 0f, w, h), new Vector2(0f, 0.5f), w / Length);
                return cone;
            }
        }
    }
}
