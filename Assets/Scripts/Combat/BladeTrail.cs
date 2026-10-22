using System.Collections.Generic;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Swept-blade slash: a ribbon between a point part-way up the blade and the tip, recorded
    /// every frame and smoothed with Catmull-Rom so fast swings draw a clean crescent instead of
    /// a polygon. The leading (tip) edge is bright, the inner edge and older parts fade out and
    /// the tail narrows towards the tip. Each sample keeps the colour it was drawn with, so the
    /// red of a spin (crit) attack stays red while it fades even after the spin ends. Follows the sword's TrailRenderer for when to emit and
    /// which colours to use; that renderer itself is switched off.
    /// </summary>
    public sealed class BladeTrail : MonoBehaviour
    {
        private const int Subdivisions = 5;

        private struct Sample
        {
            public Vector2 Base;
            public Vector2 Tip;
            public float Time;
            public Color Tint;
            public float White; // how white the leading edge starts
        }

        [SerializeField, Min(0.01f)] private float lifetime = 0.16f;
        [Tooltip("Where the slash starts along the blade, 0 = grip, 1 = tip.")]
        [SerializeField, Range(0f, 1f)] private float baseFraction = 0.4f;
        [SerializeField, Range(0f, 1f)] private float spinBaseFraction = 0.2f;
        [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.85f;

        private TrailRenderer source;
        private Transform grip;
        private Transform tip;
        private Gradient gradient;
        private float gradientWidth = -1f;
        private bool wasEmitting;
        private Mesh mesh;
        private readonly List<Sample> samples = new List<Sample>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();

        public static BladeTrail Attach(TrailRenderer source, Transform grip)
        {
            var go = new GameObject("BladeTrail");
            go.transform.SetParent(grip, false);
            var trail = go.AddComponent<BladeTrail>();
            trail.source = source;
            trail.grip = grip;
            trail.tip = source.transform;
            trail.mesh = new Mesh { name = "BladeTrail" };
            trail.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = trail.mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = source.sharedMaterial;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder;
            source.enabled = false;
            return trail;
        }

        private void LateUpdate()
        {
            float now = Time.time;
            bool emitting = source.emitting;
            if (emitting && !wasEmitting)
            {
                samples.Clear(); // a new swing never joins the last one
            }

            wasEmitting = emitting;
            while (samples.Count > 0 && now - samples[0].Time > lifetime)
            {
                samples.RemoveAt(0);
            }

            // The sword swaps gradient and width for the spin attack; re-read only then.
            if (!Mathf.Approximately(source.widthMultiplier, gradientWidth))
            {
                gradientWidth = source.widthMultiplier;
                gradient = source.colorGradient;
            }

            if (emitting)
            {
                Vector2 gripPosition = grip.position;
                Vector2 tipPosition = tip.position;
                bool spin = gradientWidth > 1f;
                samples.Add(new Sample
                {
                    Base = Vector2.Lerp(gripPosition, tipPosition, spin ? spinBaseFraction : baseFraction),
                    Tip = tipPosition,
                    Time = now,
                    Tint = gradient != null ? gradient.Evaluate(1f) : Color.white,
                    White = spin ? 0.3f : 1f,
                });
            }

            Rebuild(now);
        }

        private void Rebuild(float now)
        {
            vertices.Clear();
            colors.Clear();
            triangles.Clear();
            mesh.Clear();
            if (samples.Count < 2)
            {
                return;
            }

            // Newest first, so the bright head is column 0.
            for (int i = samples.Count - 1; i > 0; i--)
            {
                int steps = i == 1 ? Subdivisions + 1 : Subdivisions;
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)Subdivisions;
                    Sample a = samples[Mathf.Min(i + 1, samples.Count - 1)];
                    Sample b = samples[i];
                    Sample c = samples[i - 1];
                    Sample d = samples[Mathf.Max(i - 2, 0)];
                    Vector2 tipPoint = CatmullRom(a.Tip, b.Tip, c.Tip, d.Tip, t);
                    Vector2 basePoint = CatmullRom(a.Base, b.Base, c.Base, d.Base, t);
                    float age = Mathf.Clamp01((now - Mathf.Lerp(b.Time, c.Time, t)) / lifetime);
                    AddColumn(basePoint, tipPoint, age, Color.Lerp(b.Tint, c.Tint, t), Mathf.Lerp(b.White, c.White, t));
                }
            }

            int columns = vertices.Count / 3;
            for (int i = 0; i < columns - 1; i++)
            {
                int v = i * 3;
                for (int row = 0; row < 2; row++)
                {
                    int p = v + row;
                    triangles.Add(p);
                    triangles.Add(p + 3);
                    triangles.Add(p + 1);
                    triangles.Add(p + 1);
                    triangles.Add(p + 3);
                    triangles.Add(p + 4);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
        }

        /// <summary>Three vertices: faded inner edge, coloured body, bright tip edge.</summary>
        private void AddColumn(Vector2 basePoint, Vector2 tipPoint, float age, Color tint, float white)
        {
            float fade = (1f - age) * (1f - age) * maxAlpha;
            // The tail narrows towards the tip, giving the crescent its point.
            Vector2 inner = Vector2.Lerp(basePoint, tipPoint, age * 0.8f);
            Vector2 middle = Vector2.Lerp(inner, tipPoint, 0.7f);
            Color edge = Color.Lerp(Color.Lerp(tint, Color.white, white), tint, age);

            vertices.Add(transform.InverseTransformPoint(inner));
            vertices.Add(transform.InverseTransformPoint(middle));
            vertices.Add(transform.InverseTransformPoint(tipPoint));
            colors.Add(new Color(tint.r, tint.g, tint.b, 0f));
            colors.Add(new Color(tint.r, tint.g, tint.b, fade * 0.45f));
            colors.Add(new Color(edge.r, edge.g, edge.b, fade));
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }
    }
}
