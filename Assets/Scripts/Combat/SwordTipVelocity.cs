using UnityEngine;

namespace StickDuel
{
    public sealed class SwordTipVelocity : MonoBehaviour
    {
        [SerializeField] private Transform tip;
        [SerializeField] private Rigidbody2D swordBody;

        public Vector2 WorldVelocity { get; private set; }
        public float Speed => WorldVelocity.magnitude;

        private Vector2 previousPosition;

        private void Awake()
        {
            if (tip == null)
            {
                tip = transform;
            }

            if (swordBody == null)
            {
                swordBody = GetComponentInParent<Rigidbody2D>();
            }

            previousPosition = tip.position;
        }

        private void FixedUpdate()
        {
            Vector2 currentPosition = tip.position;
            WorldVelocity = (currentPosition - previousPosition) / Time.fixedDeltaTime;
            previousPosition = currentPosition;
        }

        private void OnDrawGizmosSelected()
        {
            if (tip == null)
            {
                return;
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(tip.position, WorldVelocity * 0.1f);
        }
    }
}
