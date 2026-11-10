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
        [SerializeField, Min(0f)] private float activeDuration = 2.5f;

        [Header("Drift")]
        [Tooltip("On the floor the beam drifts this far (random in this range) to a random side.")]
        [SerializeField, Min(0f)] private float minDrift = 1f;
        [SerializeField, Min(0f)] private float maxDrift = 3f;
        [SerializeField, Min(0f)] private float driftSpeed = 1.5f;
        [Tooltip("The beam never comes down or drifts closer than this to the arena's left / right edge.")]
        [SerializeField, Min(0f)] private float leftSafeMargin = 2f;
        [SerializeField, Min(0f)] private float rightSafeMargin = 2f;

        [Header("Damage")]
        [SerializeField, Min(0f)] private float damage = 10f;
        [Tooltip("A fighter standing in the beam is hurt again after this many seconds.")]
        [SerializeField, Min(0.05f)] private float damageInterval = 0.5f;
        [Tooltip("Impulse on a burned fighter: x away from the beam, y up.")]
        [SerializeField] private Vector2 knockback = new Vector2(6f, 4f);
        [Tooltip("Width of the burning part of the beam (the painted core and its red halo).")]
        [SerializeField, Min(0.05f)] private float beamWidth = 0.5f;

        [Header("Look")]
        [Tooltip("Resources/Arena/Laser at this size: 100 makes the painted beam about ten units tall, well past the top of the view.")]
        [SerializeField, Min(1f)] private float pixelsPerUnit = 100f;
        [SerializeField] private LayerMask groundMask = 1; // Default: floors and ledges, not fighters or triggers
        [SerializeField] private Color burnColor = new Color(1f, 0.45f, 0.3f);

        private enum State { Idle, Descending, Active }

        private SpriteRenderer sprite;
        private Func<bool> roundLive;
        private float left, right;        // the arena's edges
        private State state;
        private float timer;
        private float beamHeight;         // of the painted core, world units
        private float driftTarget;
        private float seed;
        private readonly Dictionary<Fighter, float> nextBurn = new Dictionary<Fighter, float>();
        private readonly Collider2D[] hits = new Collider2D[8];
        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[1];

        /// <summary>Bottom of the beam (its floor contact point) while it shows.</summary>
        public Vector2 Foot => transform.position;
        public bool IsActive => state == State.Active;

        /// <summary>One beam for an arena spanning <paramref name="left"/>..<paramref name="right"/>; it only fires while <paramref name="roundLive"/>.</summary>
        public static FallingLaser Create(Transform root, float left, float right, Func<bool> roundLive)
        {
            var texture = Resources.Load<Texture2D>("Arena/Laser");
            var go = new GameObject("FallingLaser") { layer = 2 }; // Ignore Raycast: aim lines and AI ground checks see through it
            go.transform.SetParent(root, false);
            var laser = go.AddComponent<FallingLaser>();
            laser.sprite = go.AddComponent<SpriteRenderer>();
            // The painted beam's core runs down column 304 of 640 to row 1020 of 1108, where it splashes on the floor.
            laser.sprite.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.475f, 0.079f), laser.pixelsPerUnit);
            laser.sprite.sortingOrder = 30;
            laser.sprite.enabled = false;
            laser.beamHeight = 1020f / laser.pixelsPerUnit;
            laser.left = left;
            laser.right = right;
            laser.roundLive = roundLive;
            laser.seed = UnityEngine.Random.value * 100f;
            laser.timer = laser.NextInterval();
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
            sprite.enabled = true;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (!roundLive())
            {
                if (state != State.Idle)
                {
                    GoOut();
                }

                return;
            }

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
                        timer = NextInterval();
                    }

                    break;
            }

            // Energy flicker and a slow pulse, so it never reads as a still picture.
            float flicker = 0.75f + 0.25f * Mathf.PerlinNoise(seed, Time.time * 30f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            sprite.color = Color.Lerp(Color.white, new Color(1f, 0.8f, 0.8f), pulse * 0.5f) * new Color(1f, 1f, 1f, flicker);
            transform.localScale = new Vector3(1f + 0.1f * (flicker - 0.875f) * 4f, 1f, 1f);
        }

        private void Land()
        {
            state = State.Active;
            timer = activeDuration;
            float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            driftTarget = Mathf.Clamp(transform.position.x + side * UnityEngine.Random.Range(Mathf.Min(minDrift, maxDrift), Mathf.Max(minDrift, maxDrift)), MinX, MaxX);
            nextBurn.Clear();
            if (Fx.Instance != null)
            {
                Fx.Instance.Shatter(Foot, burnColor);
                Fx.Instance.Shake(0.3f);
            }
        }

        private void GoOut()
        {
            state = State.Idle;
            sprite.enabled = false;
        }

        /// <summary>Floor height under <paramref name="x"/>: the first solid surface below the beam's top (none: the arena's ground row).</summary>
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
    }
}
