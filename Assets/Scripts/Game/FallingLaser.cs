using System;
using System.Collections.Generic;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Arena hazard: now and then a laser beam comes down from above at a random spot, burns on the
    /// floor for a few seconds while drifting a little to one side, then goes out. One instance per
    /// arena, shown and hidden; it only runs while the round is live and keeps clear of a safe
    /// margin at either edge. Damage goes through <see cref="Fighter.TakeHit"/> like the lava.
    /// Drawn in code, no painted art: a white-gold core, a red-pink halo as wide as the burn box and
    /// a faint wide glow (three LineRenderers), a splash where it meets the floor and a few sparks.
    /// The object sits at the beam's foot; the beam runs <see cref="beamHeight"/> up from there.
    /// </summary>
    public sealed class FallingLaser : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds between one beam going out and the next coming down (random in this range).")]
        [SerializeField, Min(0f)] private float minInterval = 4f;
        [SerializeField, Min(0f)] private float maxInterval = 9f;
        [Tooltip("How fast the beam comes down, units per second.")]
        [SerializeField, Min(0.1f)] private float descentSpeed = 14f;
        [Tooltip("Seconds the beam burns on the floor.")]
        [SerializeField, Min(0f)] private float activeDuration = 2.75f;

        [Header("Drift")]
        [Tooltip("On the floor the beam drifts this far (random in this range) to a random side.")]
        [SerializeField, Min(0f)] private float minDrift = 1f;
        [SerializeField, Min(0f)] private float maxDrift = 3f;
        [SerializeField, Min(0f)] private float driftSpeed = 1.5f;
        [Tooltip("The beam never comes down or drifts closer than this to the arena's left / right edge.")]
        [SerializeField, Min(0f)] private float leftSafeMargin = 2f;
        [SerializeField, Min(0f)] private float rightSafeMargin = 2f;

        [Header("Damage")]
        [SerializeField, Min(0f)] private float damage = 20f;
        [Tooltip("A fighter standing in the beam is hurt again after this many seconds.")]
        [SerializeField, Min(0.05f)] private float damageInterval = 0.4f;
        [Tooltip("Impulse on a burned fighter: x away from the beam, y up.")]
        [SerializeField] private Vector2 knockback = new Vector2(7f, 4.5f);
        [Tooltip("Width of the burn box, world units. The red halo is drawn exactly this wide.")]
        [SerializeField, Min(0.05f)] private float beamWidth = 0.5f;

        [Header("Shape")]
        [Tooltip("Beam length above its foot, world units: well past the top of the view, so its top never shows.")]
        [SerializeField, Min(1f)] private float beamHeight = 16f;
        [Tooltip("Width of the white-gold core, world units.")]
        [SerializeField, Min(0.01f)] private float coreWidth = 0.14f;
        [Tooltip("The faint outer glow is this many times beamWidth.")]
        [SerializeField, Min(1f)] private float outerGlowScale = 3.2f;
        [Tooltip("Width of the splash on the floor, world units (looks only, it does not burn).")]
        [SerializeField, Min(0f)] private float splashWidth = 1.6f;
        [Tooltip("Seconds the landing flash takes to fade.")]
        [SerializeField, Min(0.01f)] private float flashTime = 0.25f;

        [Header("Look")]
        [SerializeField] private LayerMask groundMask = 1; // Default: floors and ledges, not fighters or triggers
        [SerializeField] private Color burnColor = new Color(1f, 0.45f, 0.3f);
        [SerializeField] private Color coreColor = new Color(1f, 0.9f, 0.62f);
        [SerializeField] private Color haloColor = new Color(1f, 0.22f, 0.42f, 0.85f);
        [SerializeField] private Color outerGlowColor = new Color(1f, 0.15f, 0.5f, 0.22f);
        [SerializeField] private Color splashColor = new Color(1f, 0.72f, 0.38f, 0.9f);

        // Over the arena painting and its glows, under the hit FX (38+) and the HUD canvas.
        private const int OuterOrder = 28, HaloOrder = 29, CoreOrder = 30, SplashOrder = 31, SparkOrder = 32;

        private enum State { Idle, Descending, Active }

        private Func<bool> roundLive;
        private float left, right;        // the arena's edges
        private State state;
        private float timer;
        private float driftTarget;
        private float seed;
        private float flash;              // 1 on landing, fades to 0
        private readonly Dictionary<Fighter, float> nextBurn = new Dictionary<Fighter, float>();
        private readonly Collider2D[] hits = new Collider2D[8];
        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[1];

        // Built once in Create, shown and hidden with `visual`.
        private GameObject visual;
        private LineRenderer core, halo, outer, splash;
        private ParticleSystem sparks;
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>(); // materials and textures to destroy

        /// <summary>Bottom of the beam (its floor contact point) while it shows.</summary>
        public Vector2 Foot => transform.position;
        public bool IsActive => state == State.Active;

        /// <summary>
        /// One beam for an arena spanning <paramref name="left"/>..<paramref name="right"/>; it only fires while
        /// <paramref name="roundLive"/>. Null, with an error, if the sprite shader is missing.
        /// </summary>
        public static FallingLaser Create(Transform root, float left, float right, Func<bool> roundLive)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("FallingLaser: Sprites/Default shader not found, no laser in this arena.");
                return null;
            }

            var go = new GameObject("FallingLaser") { layer = 2 }; // Ignore Raycast: aim lines and AI ground checks see through it
            go.transform.SetParent(root, false);
            var laser = go.AddComponent<FallingLaser>();
            laser.left = left;
            laser.right = right;
            laser.roundLive = roundLive;
            laser.seed = UnityEngine.Random.value * 100f;
            laser.timer = laser.NextInterval();
            laser.BuildVisual(shader);
            return laser;
        }

        private float NextInterval() => UnityEngine.Random.Range(Mathf.Min(minInterval, maxInterval), Mathf.Max(minInterval, maxInterval));
        private float MinX => left + leftSafeMargin;
        private float MaxX => right - rightSafeMargin;

        /// <summary>Brings the beam down now, at <paramref name="x"/> (clamped to the safe range) or a random spot.</summary>
        public void Fire(float? x = null)
        {
            float at = Mathf.Clamp(x ?? UnityEngine.Random.Range(MinX, MaxX), MinX, MaxX);
            transform.position = new Vector2(at, GroundAt(at) + beamHeight); // the foot starts a full beam up: the top is way out of view
            state = State.Descending;
            flash = 0f;
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            splash.enabled = false;
            visual.SetActive(true);
        }

        private void Update()
        {
            if (!roundLive())
            {
                if (state != State.Idle)
                {
                    GoOut();
                }

                return;
            }

            float dt = Time.deltaTime;
            switch (state)
            {
                case State.Idle:
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        Fire();
                    }

                    break;
                case State.Descending:
                    Vector2 p = transform.position;
                    float ground = GroundAt(p.x);
                    p.y = Mathf.Max(ground, p.y - descentSpeed * dt);
                    transform.position = p;
                    if (p.y <= ground)
                    {
                        Land();
                    }

                    break;
                case State.Active:
                    Vector2 q = transform.position;
                    q.x = Mathf.MoveTowards(q.x, driftTarget, driftSpeed * dt);
                    q.y = GroundAt(q.x);
                    transform.position = q;
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        GoOut();
                    }

                    break;
            }

            if (state != State.Idle)
            {
                Animate(dt);
            }
        }

        /// <summary>Energy flicker, a slow pulse and the fading landing flash, so it never reads as a still picture.</summary>
        private void Animate(float dt)
        {
            float flicker = Mathf.PerlinNoise(seed, Time.time * 30f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            flash = Mathf.MoveTowards(flash, 0f, dt / flashTime);

            core.widthMultiplier = coreWidth * (0.8f + 0.4f * flicker) * (1f + flash);
            halo.widthMultiplier = beamWidth * (0.94f + 0.12f * pulse) * (1f + 0.5f * flash);
            outer.widthMultiplier = beamWidth * outerGlowScale * (0.9f + 0.2f * pulse);
            core.sharedMaterial.color = new Color(1f, 1f, 1f, 0.8f + 0.2f * flicker);
            halo.sharedMaterial.color = new Color(1f, 1f, 1f, 0.75f + 0.25f * flicker);
            outer.sharedMaterial.color = new Color(1f, 1f, 1f, 0.6f + 0.4f * pulse + flash);
            if (splash.enabled)
            {
                splash.sharedMaterial.color = new Color(1f, 1f, 1f, 0.6f + 0.4f * flicker);
                splash.transform.localScale = new Vector3(1f + 1.5f * flash, 1f + 0.8f * flash, 1f);
            }
        }

        private void Land()
        {
            state = State.Active;
            timer = activeDuration;
            float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            driftTarget = Mathf.Clamp(transform.position.x + side * UnityEngine.Random.Range(Mathf.Min(minDrift, maxDrift), Mathf.Max(minDrift, maxDrift)), MinX, MaxX);
            nextBurn.Clear();
            flash = 1f;
            splash.enabled = true;
            sparks.Play();
            if (Fx.Instance != null)
            {
                Fx.Instance.Shatter(Foot, burnColor);
                Fx.Instance.Shake(0.3f);
            }
        }

        /// <summary>Hides the beam and its effects and starts the wait for the next one (also when the round ends).</summary>
        private void GoOut()
        {
            state = State.Idle;
            timer = NextInterval();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            visual.SetActive(false);
        }

        /// <summary>Floor height under <paramref name="x"/>: the first solid surface on <see cref="groundMask"/> below the beam's top (none: the arena's ground row).</summary>
        private float GroundAt(float x)
        {
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = groundMask };
            int count = Physics2D.Raycast(new Vector2(x, beamHeight * 2f), Vector2.down, filter, groundHits, beamHeight * 2f + 10f);
            return count > 0 ? groundHits[0].point.y : 0f;
        }

        private void FixedUpdate()
        {
            if (state != State.Active)
            {
                return;
            }

            // Everyone standing in the beam burns, once per interval each (a fighter has several colliders).
            var filter = new ContactFilter2D { useTriggers = true, useLayerMask = true, layerMask = LayerMask.GetMask(Fighter.FighterLayer) };
            Vector2 foot = Foot;
            int count = Physics2D.OverlapBox(foot + Vector2.up * beamHeight * 0.5f, new Vector2(beamWidth, beamHeight), 0f, filter, hits);
            for (int i = 0; i < count; i++)
            {
                Rigidbody2D body = hits[i].attachedRigidbody;
                Fighter fighter = body != null ? body.GetComponent<Fighter>() : null;
                if (fighter == null || fighter.IsDead || fighter.IsRolling || (nextBurn.TryGetValue(fighter, out float next) && Time.time < next))
                {
                    continue;
                }

                nextBurn[fighter] = Time.time + damageInterval;
                float side = fighter.Body.position.x >= foot.x ? 1f : -1f;
                fighter.TakeHit(damage, new Vector2(side * knockback.x, knockback.y), false);
                if (Fx.Instance != null)
                {
                    Fx.Instance.Hit(fighter.ShoulderPosition, new Vector2(side, 0.3f), burnColor, damage, false);
                }
            }
        }

        private void OnDestroy()
        {
            foreach (UnityEngine.Object o in owned)
            {
                Destroy(o);
            }
        }

        // ------------------------------------------------------------------ look

        private void BuildVisual(Shader shader)
        {
            visual = new GameObject("LaserVisual") { layer = 2 };
            visual.transform.SetParent(transform, false);
            Texture2D soft = Falloff(2f), sharp = Falloff(0.6f);
            owned.Add(soft);
            owned.Add(sharp);

            // Along the beam: 0 is the foot, 1 the top (out of view, faded anyway).
            var bulge = new AnimationCurve(new Keyframe(0f, 1.3f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0.9f)); // the halo swells where it hits
            outer = Line("LaserOuterGlow", shader, soft, OuterOrder, Fade(outerGlowColor, outerGlowColor), bulge);
            halo = Line("LaserInnerGlow", shader, soft, HaloOrder, Fade(haloColor, haloColor), bulge);
            // White-hot at the floor, gold higher up, and a little wavy, so it is no flat white stripe.
            core = Line("LaserCore", shader, sharp, CoreOrder, Fade(Color.white, coreColor),
                new AnimationCurve(new Keyframe(0f, 1.2f), new Keyframe(0.3f, 0.85f), new Keyframe(0.6f, 1f), new Keyframe(1f, 0.8f)));
            foreach (LineRenderer line in new[] { outer, halo, core })
            {
                line.SetPosition(0, Vector3.zero);
                line.SetPosition(1, new Vector3(0f, beamHeight, 0f));
            }

            // A flat lens of light on the floor, wider than the beam.
            splash = Line("LaserLandingSplash", shader, soft, SplashOrder, Fade(splashColor, splashColor, false),
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f)));
            splash.widthMultiplier = 0.3f;
            splash.SetPosition(0, new Vector3(-splashWidth * 0.5f, 0.05f, 0f));
            splash.SetPosition(1, new Vector3(splashWidth * 0.5f, 0.05f, 0f));

            sparks = Sparks(shader);
            visual.SetActive(false);
        }

        private LineRenderer Line(string lineName, Shader shader, Texture2D texture, int order, Gradient colors, AnimationCurve width)
        {
            var go = new GameObject(lineName) { layer = 2 };
            go.transform.SetParent(visual.transform, false);
            var material = new Material(shader) { mainTexture = texture };
            owned.Add(material);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material; // its colour carries the per-frame flicker, the gradient the hue
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.textureMode = LineTextureMode.Stretch;
            line.colorGradient = colors;
            line.widthCurve = width;
            line.sortingOrder = order;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        /// <summary><paramref name="from"/> at the foot to <paramref name="to"/> higher up, fading out at the top (or, for the splash, at both ends).</summary>
        private static Gradient Fade(Color from, Color to, bool beam = true)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 0.35f), new GradientColorKey(to, 1f) },
                beam
                    ? new[] { new GradientAlphaKey(from.a, 0f), new GradientAlphaKey(to.a, 0.6f), new GradientAlphaKey(0f, 1f) }
                    : new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(from.a, 0.5f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        /// <summary>Across-the-line alpha: opaque in the middle, 0 at the edges; higher <paramref name="sharpness"/> = softer edge.</summary>
        private static Texture2D Falloff(float sharpness)
        {
            const int n = 32;
            var texture = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LaserFalloff" };
            for (int i = 0; i < n; i++)
            {
                float middle = 1f - Mathf.Abs((i + 0.5f) / n * 2f - 1f);
                texture.SetPixel(0, i, new Color(1f, 1f, 1f, Mathf.Pow(middle, sharpness)));
            }

            texture.Apply();
            return texture;
        }

        /// <summary>A light spray of sparks off the foot while the beam burns (at most 40 alive).</summary>
        private ParticleSystem Sparks(Shader shader)
        {
            var go = new GameObject("LaserParticles") { layer = 2 };
            go.transform.SetParent(visual.transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(splashColor, burnColor);
            main.gravityModifier = 1.5f;
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 30f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.1f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // the cone opens up, not along z
            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var material = new Material(shader); // plain sprite shader, the FX dot for a round spark, like the arena motes
            if (Fx.Instance != null) material.mainTexture = Fx.Instance.DotMaterial.mainTexture;
            owned.Add(material);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = SparkOrder;
            return system;
        }
    }
}
