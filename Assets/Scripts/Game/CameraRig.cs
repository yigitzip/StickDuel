using UnityEngine;

namespace StickDuel
{
    /// <summary>Frames both fighters (zooming with their distance), clamps to the arena and shakes.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] private Fighter[] targets;
        [SerializeField] private float minSize = 3.8f;
        [SerializeField] private float maxSize = 6.2f;
        [SerializeField] private float horizontalPadding = 3f;
        [SerializeField] private float minY = 2.7f;
        [SerializeField] private float heightOffset = 1.2f;
        [SerializeField] private float arenaHalfWidth = 15f;
        [SerializeField] private float smoothTime = 0.3f;
        [SerializeField] private float maxShakeOffset = 0.35f;
        [SerializeField] private float maxShakeAngle = 1.5f;

        private Camera cam;
        private Vector3 basePosition;
        private Vector3 velocity;
        private float sizeVelocity;
        private float trauma;
        private float size;
        private float zoomPunch;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            basePosition = transform.position;
            size = cam.orthographicSize;
        }

        /// <summary>Snap the camera in by <paramref name="amount"/> world units; it eases back out.</summary>
        public void Punch(float amount) => zoomPunch = Mathf.Max(zoomPunch, amount);

        public void Shake(float amount) => trauma = Mathf.Min(1f, trauma + amount);

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (targets != null && targets.Length > 0)
            {
                Frame(dt);
            }

            zoomPunch = Mathf.MoveTowards(zoomPunch, 0f, dt * (0.6f + zoomPunch * 4f));
            cam.orthographicSize = size - zoomPunch;

            trauma = Mathf.Max(0f, trauma - dt * 1.8f);
            float shake = trauma * trauma;
            float t = Time.unscaledTime * 28f;
            Vector3 offset = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * 2f * maxShakeOffset * shake;
            float angle = (Mathf.PerlinNoise(t, t * 0.5f) - 0.5f) * 2f * maxShakeAngle * shake;
            transform.SetPositionAndRotation(basePosition + offset, Quaternion.Euler(0f, 0f, angle));
        }

        private void Frame(float dt)
        {
            float minX = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Fighter fighter in targets)
            {
                Vector2 p = fighter.transform.position;
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }

            float aspect = Mathf.Max(cam.aspect, 0.5f);
            float targetSize = Mathf.Clamp((maxX - minX + horizontalPadding * 2f) * 0.5f / aspect, minSize, maxSize);
            size = Mathf.SmoothDamp(size, targetSize, ref sizeVelocity, smoothTime, Mathf.Infinity, dt);

            float halfWidth = size * aspect;
            float limit = Mathf.Max(0f, arenaHalfWidth - halfWidth);
            Vector3 target = new Vector3(
                Mathf.Clamp((minX + maxX) * 0.5f, -limit, limit),
                Mathf.Max(minY, maxY + heightOffset - size * 0.4f),
                basePosition.z);
            basePosition = Vector3.SmoothDamp(basePosition, target, ref velocity, smoothTime, Mathf.Infinity, dt);
        }
    }
}
