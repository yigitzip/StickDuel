using UnityEngine;

namespace StickDuel
{
    /// <summary>Candle-like brightness/scale noise for lantern glows.</summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class Flicker : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float amount = 0.25f;
        [SerializeField, Min(0f)] private float speed = 5f;

        private SpriteRenderer sprite;
        private Color baseColor;
        private Vector3 baseScale;
        private float seed;

        private void Awake()
        {
            sprite = GetComponent<SpriteRenderer>();
            baseColor = sprite.color;
            baseScale = transform.localScale;
            seed = Random.value * 100f;
        }

        private void Update()
        {
            float k = 1f + (Mathf.PerlinNoise(seed, Time.time * speed) - 0.5f) * 2f * amount;
            sprite.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * k);
            transform.localScale = baseScale * (0.94f + 0.06f * k);
        }
    }
}
