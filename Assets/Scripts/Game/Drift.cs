using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Moves a small sprite at a steady speed with a sideways sway, looping inside a box: torch
    /// embers rising and fading out, snow falling past a window.
    /// </summary>
    public sealed class Drift : MonoBehaviour
    {
        public Vector2 Velocity;
        public float Sway;
        public Rect Box;
        public bool FadeOut;                       // fades as it crosses the box (embers burning out)
        public System.Func<Vector2, bool> Visible; // hidden where this is false (snow outside the glass)

        private SpriteRenderer sprite;
        private Color baseColor;
        private Vector2 position;
        private float phase;

        private void Start()
        {
            sprite = GetComponent<SpriteRenderer>();
            baseColor = sprite.color;
            phase = Random.value * 10f;
            position = new Vector2(Random.Range(Box.xMin, Box.xMax), Random.Range(Box.yMin, Box.yMax));
        }

        private void Update()
        {
            position += Velocity * Time.deltaTime;
            if (position.y > Box.yMax || position.y < Box.yMin)
            {
                position = new Vector2(Random.Range(Box.xMin, Box.xMax), Velocity.y > 0f ? Box.yMin : Box.yMax);
            }

            Vector2 shown = position + Vector2.right * Mathf.Sin(Time.time * 2f + phase) * Sway;
            transform.position = shown;
            float alpha = baseColor.a;
            if (FadeOut)
            {
                alpha *= 1f - Mathf.InverseLerp(Box.yMin, Box.yMax, position.y);
            }

            if (Visible != null && !Visible(shown))
            {
                alpha = 0f;
            }

            sprite.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }
    }
}
