using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Visual only: the sniper bullet streaking from the muzzle to where <see cref="SniperRifleWeapon.CalculateShot"/>
    /// ended, then gone. No collider, no damage (the hitscan already resolved the shot). Runs on
    /// unscaled time so the hit-stop freeze doesn't hang it mid-air.
    /// Art: Resources/Weapons/SniperBullet.png (512x64 at 220 px/unit, head at x=476px, tail to the left).
    /// </summary>
    public sealed class SniperTracer : MonoBehaviour
    {
        private const float PixelsPerUnit = 220f; // 512 px = 2.3 units long, 0.29 thick
        private const float HeadPixel = 476f;

        private static Sprite art;
        private Vector2 end;
        private float speed;

        /// <summary>The bullet art, pivot on its glowing head. Null, with an error, if the texture is missing.</summary>
        public static Sprite Art
        {
            get
            {
                if (art != null)
                {
                    return art;
                }

                var image = Resources.Load<Texture2D>("Weapons/SniperBullet");
                if (image == null)
                {
                    Debug.LogError("SniperTracer: Resources/Weapons/SniperBullet.png is missing.");
                    return null;
                }

                return art = Sprite.Create(image, new Rect(0f, 0f, image.width, image.height),
                    new Vector2(HeadPixel / image.width, 0.5f), PixelsPerUnit);
            }
        }

        /// <summary>Fires one streak from <paramref name="from"/> to <paramref name="to"/>, drawn like <paramref name="like"/> (material, sorting layer) just under it.</summary>
        public static void Launch(Vector2 from, Vector2 to, float speed, SpriteRenderer like)
        {
            Sprite sprite = Art;
            Vector2 path = to - from;
            if (sprite == null || path.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var go = new GameObject("SniperTracer");
            go.transform.SetPositionAndRotation(from, Quaternion.Euler(0f, 0f, Mathf.Atan2(path.y, path.x) * Mathf.Rad2Deg));
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = like.sharedMaterial;
            renderer.sortingLayerID = like.sortingLayerID;
            renderer.sortingOrder = like.sortingOrder - 1; // the tail hides behind the rifle as it leaves the barrel
            var tracer = go.AddComponent<SniperTracer>();
            tracer.end = to;
            tracer.speed = speed;
        }

        private void Update()
        {
            Vector2 position = Vector2.MoveTowards(transform.position, end, speed * Time.unscaledDeltaTime);
            transform.position = position;
            if (position == end)
            {
                Destroy(gameObject);
            }
        }
    }
}
