using System;
using System.Collections.Generic;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Arena hazard: now and then a missile flies straight across the arena along one of a few
    /// lanes, from a random side, fast. First a warning sign (Arena/warning1) blinks at the lane's
    /// height on the side it will come from, for <c>warningTime</c>; then the missile flies, trailing
    /// flame and smoke. It touches nothing in the arena (no collider: it goes through floors, decks
    /// and the ladder) and only fighters in its way are hurt and thrown, once each, through
    /// <see cref="Fighter.TakeHit"/> like the laser. Only runs while the round is live: when it ends,
    /// pending warnings and missiles in flight are removed.
    /// </summary>
    public sealed class MissileHazard : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds between missiles (random in this range).")]
        [SerializeField, Min(0f)] private float minInterval = 3f;
        [SerializeField, Min(0f)] private float maxInterval = 7f;
        [Tooltip("Units per second across the arena.")]
        [SerializeField, Min(0.1f)] private float speed = 23f;
        [Tooltip("Seconds the warning shows before its missile flies.")]
        [SerializeField, Min(0f)] private float warningTime = 0.8f;

        [Header("Look")]
        [Tooltip("Height of the warning sign, world units.")]
        [SerializeField, Min(0.1f)] private float warningSize = 1.4f;
        [Tooltip("How far in from the visible edge the warning sits, world units.")]
        [SerializeField, Min(0f)] private float warningInset = 1.1f;
        [Tooltip("Alpha pulses a second while the warning shows.")]
        [SerializeField, Min(0f)] private float warningPulse = 4f;

        [Header("Damage")]
        [SerializeField, Min(0f)] private float damage = 15f;
        [Tooltip("Impulse on a hit fighter: x along the flight, y up.")]
        [SerializeField] private Vector2 knockback = new Vector2(18f, 20f); // fighters weigh 3: a hard shove and most of a jump up
        [Tooltip("Size of the part of the missile that hurts, world units.")]
        [SerializeField] private Vector2 hitBox = new Vector2(1.2f, 0.6f);
        [SerializeField] private Color hitColor = new Color(1f, 0.6f, 0.25f);

        private sealed class Shot
        {
            public Transform Transform;
            public float Direction; // +1 flies right, -1 left
            public readonly HashSet<Fighter> Hit = new HashSet<Fighter>();
        }

        /// <summary>A missile announced by its warning sign, waiting to fly.</summary>
        private sealed class Warning
        {
            public SpriteRenderer Sign;
            public Vector2 Lane;
            public float Left;
        }

        private const int WarningOrder = 45; // over the fighters, missiles and motes, under the callouts (the HUD is a canvas)
        private const int MissileOrder = 30, TrailOrder = 29;

        private Sprite sprite;
        private Sprite warningSprite;
        private Material trailMaterial;
        private readonly List<Warning> warnings = new List<Warning>();
        private Func<bool> roundLive;
        private Vector2[] lanes;          // where each missile comes in: x is the arena edge it starts from, y its height
        private float halfWidth;
        private float timer;
        private readonly List<Shot> shots = new List<Shot>();
        private readonly Collider2D[] hits = new Collider2D[8];

        public int LaneCount => lanes.Length;

        /// <summary>One launcher for an arena reaching ±<paramref name="halfWidth"/>; it only fires while <paramref name="roundLive"/>.</summary>
        public static MissileHazard Create(Transform root, Vector2[] lanes, float halfWidth, float pixelsPerUnit, Func<bool> roundLive)
        {
            var texture = Resources.Load<Texture2D>("Arena/Missile");
            var go = new GameObject("Missiles");
            go.transform.SetParent(root, false);
            var hazard = go.AddComponent<MissileHazard>();
            hazard.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
            var sign = Resources.Load<Texture2D>("Arena/warning1");
            if (sign == null)
            {
                Debug.LogError("MissileHazard: Resources/Arena/warning1.png is missing, missiles fly in unannounced.");
            }
            else
            {
                hazard.warningSprite = Sprite.Create(sign, new Rect(0f, 0f, sign.width, sign.height), new Vector2(0.5f, 0.5f), sign.height / hazard.warningSize);
            }

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("MissileHazard: Sprites/Default shader not found, missiles fly without their trail.");
            }
            else
            {
                hazard.trailMaterial = new Material(shader);
                if (Fx.Instance != null) hazard.trailMaterial.mainTexture = Fx.Instance.DotMaterial.mainTexture; // soft dots, like the motes
            }

            hazard.lanes = lanes;
            hazard.halfWidth = halfWidth;
            hazard.roundLive = roundLive;
            hazard.timer = hazard.NextInterval();
            return hazard;
        }

        private float NextInterval() => UnityEngine.Random.Range(Mathf.Min(minInterval, maxInterval), Mathf.Max(minInterval, maxInterval));

        /// <summary>Announces a missile along lane <paramref name="lane"/> (or a random one): its warning shows, then it flies.</summary>
        public void Fire(int? lane = null)
        {
            Vector2 at = lanes[lane ?? UnityEngine.Random.Range(0, lanes.Length)];
            if (warningSprite == null || warningTime <= 0f)
            {
                Launch(at);
                return;
            }

            var go = new GameObject("MissileWarning") { layer = 2 }; // no collider; Ignore Raycast all the same
            go.transform.SetParent(transform, false);
            var sign = go.AddComponent<SpriteRenderer>();
            sign.sprite = warningSprite;
            sign.sortingOrder = WarningOrder;
            var warning = new Warning { Sign = sign, Lane = at, Left = warningTime };
            PlaceWarning(warning);
            warnings.Add(warning);
        }

        /// <summary>Keeps the sign at its lane's height, just inside whichever is nearer: the arena's edge or the view's.</summary>
        private void PlaceWarning(Warning warning)
        {
            float side = warning.Lane.x < 0f ? -1f : 1f;
            float edge = halfWidth;
            Camera view = Camera.main;
            if (view != null && view.orthographic)
            {
                float viewEdge = side * (view.transform.position.x - transform.position.x) + view.orthographicSize * view.aspect;
                edge = Mathf.Min(edge, viewEdge);
            }

            warning.Sign.transform.position = new Vector2(side * (edge - warningInset), warning.Lane.y);
            float blink = 0.65f + 0.35f * Mathf.Cos((warningTime - warning.Left) * warningPulse * 2f * Mathf.PI); // starts fully lit
            warning.Sign.color = new Color(1f, 1f, 1f, blink);
        }

        /// <summary>Sends a missile across from lane <paramref name="at"/>'s side, starting out of view.</summary>
        private void Launch(Vector2 at)
        {
            float direction = at.x < 0f ? 1f : -1f;
            var go = new GameObject("Missile") { layer = 2 }; // Ignore Raycast: aim lines and AI ground checks see through it
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector2(-direction * (halfWidth + 2f), at.y); // starts out of view
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.flipX = direction < 0f; // the painted nose points right
            renderer.sortingOrder = MissileOrder;
            AddTrail(go.transform, direction, renderer.bounds.extents.x);
            shots.Add(new Shot { Transform = go.transform, Direction = direction });
        }

        /// <summary>
        /// Flame turning to smoke out of the missile's tail: a small world-space ParticleSystem on the missile,
        /// so the puffs stay behind as it flies and go with it when it is destroyed. No collider, no damage.
        /// </summary>
        private void AddTrail(Transform missile, float direction, float halfLength)
        {
            if (trailMaterial == null)
            {
                return;
            }

            var go = new GameObject("Trail") { layer = 2 };
            go.transform.SetParent(missile, false);
            go.transform.localPosition = new Vector3(-direction * halfLength * 0.9f, 0f, 0f);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.32f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.35f), new Color(1f, 0.5f, 0.15f));
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 7f; // puffs per unit flown: close enough to read as one plume
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.05f;
            shape.rotation = new Vector3(0f, direction > 0f ? -90f : 90f, 0f); // out of the tail, backwards
            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.2f), new GradientColorKey(new Color(0.45f, 0.42f, 0.4f), 0.45f), new GradientColorKey(new Color(0.35f, 0.33f, 0.32f), 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.3f, 0.45f), new GradientAlphaKey(0f, 1f) }); // light smoke, never a wall
            fade.color = gradient;
            ParticleSystem.SizeOverLifetimeModule grow = system.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = trailMaterial;
            renderer.sortingOrder = TrailOrder;
            system.Play();
        }

        private void Update()
        {
            if (!roundLive())
            {
                foreach (Shot shot in shots) Destroy(shot.Transform.gameObject);
                shots.Clear();
                foreach (Warning warning in warnings) Destroy(warning.Sign.gameObject);
                warnings.Clear();
                return;
            }

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                Fire();
                timer = NextInterval();
            }

            for (int i = warnings.Count - 1; i >= 0; i--)
            {
                Warning warning = warnings[i];
                warning.Left -= Time.deltaTime;
                if (warning.Left > 0f)
                {
                    PlaceWarning(warning);
                    continue;
                }

                Destroy(warning.Sign.gameObject);
                warnings.RemoveAt(i);
                Launch(warning.Lane);
            }

            for (int i = shots.Count - 1; i >= 0; i--)
            {
                Shot shot = shots[i];
                shot.Transform.position += Vector3.right * (shot.Direction * speed * Time.deltaTime);
                if (Mathf.Abs(shot.Transform.position.x) > halfWidth + 3f)
                {
                    Destroy(shot.Transform.gameObject);
                    shots.RemoveAt(i);
                }
            }
        }

        private void FixedUpdate()
        {
            var filter = new ContactFilter2D { useTriggers = true, useLayerMask = true, layerMask = LayerMask.GetMask(Fighter.FighterLayer) };
            foreach (Shot shot in shots)
            {
                Vector2 at = shot.Transform.position;
                int count = Physics2D.OverlapBox(at, hitBox, 0f, filter, hits);
                for (int i = 0; i < count; i++)
                {
                    Rigidbody2D body = hits[i].attachedRigidbody;
                    Fighter fighter = body != null ? body.GetComponent<Fighter>() : null;
                    if (fighter == null || fighter.IsDead || fighter.IsRolling || !shot.Hit.Add(fighter))
                    {
                        continue;
                    }

                    fighter.TakeHit(damage, new Vector2(shot.Direction * knockback.x, knockback.y), false);
                    if (Fx.Instance != null)
                    {
                        Fx.Instance.Hit(fighter.ShoulderPosition, new Vector2(shot.Direction, 0.4f), hitColor, damage, false);
                        Fx.Instance.Shake(0.25f);
                    }
                }
            }
        }
    }
}
