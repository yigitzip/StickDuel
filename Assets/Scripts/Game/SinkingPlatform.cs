using System.Collections.Generic;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// A floor piece floating on lava: sinks slowly while someone stands on it and slowly rises
    /// back to where it started once they leave. Standing still too long dips you into the lava.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class SinkingPlatform : MonoBehaviour
    {
        public static readonly List<SinkingPlatform> All = new List<SinkingPlatform>();

        [SerializeField, Min(0f)] private float sinkSpeed = 0.5f;
        [SerializeField, Min(0f)] private float riseSpeed = 0.55f;
        [Tooltip("How far below its start the top can go (deep enough to put the fighter's feet in the lava).")]
        [SerializeField, Min(0f)] private float maxDepth = 1.9f;

        private Rigidbody2D body;
        private float startY;
        private float halfWidth;
        private Fighter[] fighters;

        public float X => body.position.x;

        public void Init(float width)
        {
            halfWidth = width * 0.5f;
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            startY = body.position.y;
            All.Add(this);
        }

        private void OnDestroy() => All.Remove(this);

        private void Start()
        {
            fighters = FindObjectsByType<Fighter>(FindObjectsSortMode.None);
        }

        /// <summary>Back to the start height at once (new round).</summary>
        public void ResetHeight()
        {
            body.position = new Vector2(body.position.x, startY);
            transform.position = body.position;
        }

        private void FixedUpdate()
        {
            bool occupied = false;
            float top = body.position.y;
            foreach (Fighter fighter in fighters)
            {
                Vector2 feet = fighter.Body.position + Vector2.down * Fighter.HipHeight;
                occupied |= !fighter.IsDead && fighter.IsGrounded
                            && Mathf.Abs(feet.x - body.position.x) <= halfWidth + 0.1f
                            && Mathf.Abs(feet.y - top) < 0.3f;
            }

            float target = occupied ? startY - maxDepth : startY;
            float y = Mathf.MoveTowards(top, target, (occupied ? sinkSpeed : riseSpeed) * Time.fixedDeltaTime);
            body.MovePosition(new Vector2(body.position.x, y));
        }
    }
}
