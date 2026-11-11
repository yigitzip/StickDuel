using System;
using System.Collections.Generic;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Arena hazard: now and then a missile flies straight across the arena along one of a few
    /// lanes, from a random side, fast. It touches nothing in the arena (no collider: it goes
    /// through floors, decks and the ladder) and only fighters in its way are hurt and thrown,
    /// once each, through <see cref="Fighter.TakeHit"/> like the laser. Only runs while the round is live.
    /// </summary>
    public sealed class MissileHazard : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds between missiles (random in this range).")]
        [SerializeField, Min(0f)] private float minInterval = 3f;
        [SerializeField, Min(0f)] private float maxInterval = 7f;
        [Tooltip("Units per second across the arena.")]
        [SerializeField, Min(0.1f)] private float speed = 16f;

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

        private Sprite sprite;
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
            hazard.lanes = lanes;
            hazard.halfWidth = halfWidth;
            hazard.roundLive = roundLive;
            hazard.timer = hazard.NextInterval();
            return hazard;
        }

        private float NextInterval() => UnityEngine.Random.Range(Mathf.Min(minInterval, maxInterval), Mathf.Max(minInterval, maxInterval));

        /// <summary>Launches a missile now along lane <paramref name="lane"/> (or a random one).</summary>
        public void Fire(int? lane = null)
        {
            Vector2 at = lanes[lane ?? UnityEngine.Random.Range(0, lanes.Length)];
            float direction = at.x < 0f ? 1f : -1f;
            var go = new GameObject("Missile") { layer = 2 }; // Ignore Raycast: aim lines and AI ground checks see through it
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector2(-direction * (halfWidth + 2f), at.y); // starts out of view
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.flipX = direction < 0f; // the painted nose points right
            renderer.sortingOrder = 30;
            shots.Add(new Shot { Transform = go.transform, Direction = direction });
        }

        private void Update()
        {
            if (!roundLive())
            {
                foreach (Shot shot in shots) Destroy(shot.Transform.gameObject);
                shots.Clear();
                return;
            }

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                Fire();
                timer = NextInterval();
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
