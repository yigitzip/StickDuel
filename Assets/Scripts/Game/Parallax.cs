using UnityEngine;

namespace StickDuel
{
    /// <summary>Background layer that follows the camera by <see cref="factor"/> (1 = glued to camera) and can drift.</summary>
    public sealed class Parallax : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float factor = 0.5f;
        [SerializeField] private float driftSpeed;
        [Tooltip("Drift wraps after this distance (use the sprite tile width). 0 = no wrap.")]
        [SerializeField, Min(0f)] private float wrapWidth;

        private Transform cam;
        private Vector3 start;
        private Vector3 camStart;
        private float drift;

        private void Start()
        {
            cam = Camera.main.transform;
            start = transform.position;
            camStart = cam.position;
        }

        private void LateUpdate()
        {
            drift += driftSpeed * Time.deltaTime;
            if (wrapWidth > 0f)
            {
                drift = Mathf.Repeat(drift, wrapWidth);
            }

            Vector3 delta = (cam.position - camStart) * factor;
            transform.position = new Vector3(start.x + delta.x + drift, start.y + delta.y, start.z);
        }
    }
}
