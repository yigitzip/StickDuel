using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// A stalactite dropped from the cave ceiling: falls, hurts the first fighter it hits (rolling
    /// dodges it) and shatters on that hit or on the ground. A glow on the floor marks where it lands.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class Icicle : MonoBehaviour
    {
        private const float Damage = 18f;

        private SpriteRenderer marker;
        private Color markerColor;
        private float startY;

        public static void Drop(Sprite sprite, Vector2 top, SpriteRenderer marker)
        {
            var go = new GameObject("Icicle") { layer = 2 }; // Ignore Raycast: aim lines and AI ground checks see through it
            go.transform.position = top;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 20;
            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0.7f; // slow enough to see coming and step aside
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(0.22f, 1.1f);
            box.offset = new Vector2(0f, -0.85f); // lower part, where the point is
            var icicle = go.AddComponent<Icicle>();
            icicle.marker = marker;
            icicle.markerColor = marker.color;
            icicle.startY = top.y;
        }

        private void Update()
        {
            // The floor marker brightens as the icicle comes down.
            float fallen = Mathf.Clamp01((startY - transform.position.y) / startY);
            marker.color = new Color(markerColor.r, markerColor.g, markerColor.b, markerColor.a * (0.5f + 0.5f * fallen));
            if (transform.position.y < -10f)
            {
                Shatter();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Fighter fighter = other.attachedRigidbody != null ? other.attachedRigidbody.GetComponent<Fighter>() : null;
            if (fighter != null)
            {
                if (fighter.IsDead || fighter.IsRolling)
                {
                    return;
                }

                bool headshot = other == fighter.headCollider;
                float side = Mathf.Sign(fighter.Body.position.x - transform.position.x);
                fighter.TakeHit(headshot ? Damage * 1.5f : Damage, new Vector2(side * 3f, 3f), headshot);
                if (Fx.Instance != null)
                {
                    Fx.Instance.Hit(other.ClosestPoint(transform.position), Vector2.down, fighter.color, Damage, headshot);
                }

                Shatter();
            }
            else if (!other.isTrigger && other.attachedRigidbody == null)
            {
                Shatter(); // the floor (static colliders; blades and fighters have bodies)
            }
        }

        private void Shatter()
        {
            if (Fx.Instance != null)
            {
                Fx.Instance.Shatter(transform.position + Vector3.down * 1.2f, new Color(0.75f, 0.95f, 1f));
            }

            if (marker != null)
            {
                Destroy(marker.gameObject);
            }

            Destroy(gameObject);
        }
    }
}
