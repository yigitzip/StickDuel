using UnityEngine;
using UnityEngine.InputSystem;

namespace StickDuel
{
    public sealed class ArmTargetController : MonoBehaviour
    {
        [SerializeField] private Camera aimCamera;
        [SerializeField] private Transform hand;
        [SerializeField] private Rigidbody2D handBody;
        [SerializeField] private Transform shoulder;
        [SerializeField] private Transform upperArm;
        [SerializeField] private Transform forearm;
        [SerializeField] private float upperArmLength = 1.05f;
        [SerializeField] private float forearmLength = 1.15f;
        [SerializeField, Min(0.01f)] private float armLength = 2.2f;
        [SerializeField, Min(0.01f)] private float targetSmoothTime = 0.06f;
        [SerializeField, Min(0f)] private float maxHandSpeed = 18f;
        [SerializeField] private float minimumAimHeight = -1.1f;

        private Vector2 smoothedTarget;
        private Vector2 targetVelocity;

        private void Awake()
        {
            ResolveReferences();

            if (aimCamera == null)
            {
                aimCamera = Camera.main;
            }

            if (shoulder == null)
            {
                shoulder = transform;
            }

            smoothedTarget = hand != null ? hand.position : shoulder.position;
        }

        private void ResolveReferences()
        {
            hand = transform.Find("Hand");
            handBody = hand != null ? hand.GetComponent<Rigidbody2D>() : null;
            Collider2D handCollider = hand != null ? hand.GetComponent<Collider2D>() : null;
            Collider2D bodyCollider = GetComponent<Collider2D>();
            if (handCollider != null)
            {
                handCollider.isTrigger = true;
            }

            if (handCollider != null && bodyCollider != null)
            {
                Physics2D.IgnoreCollision(handCollider, bodyCollider);
            }
            shoulder = transform.Find("Shoulder");
            upperArm = transform.Find("UpperArm");
            forearm = transform.Find("Forearm");
        }

        private void FixedUpdate()
        {
            ResolveReferences();

            if (hand == null || shoulder == null || aimCamera == null || Mouse.current == null)
            {
                return;
            }

            Vector3 mouseScreenPosition = Mouse.current.position.ReadValue();
            mouseScreenPosition.z = Mathf.Abs(aimCamera.transform.position.z - shoulder.position.z);
            Vector2 mouseWorldPosition = aimCamera.ScreenToWorldPoint(mouseScreenPosition);
            Vector2 shoulderPosition = shoulder.position;
            Vector2 aimDirection = mouseWorldPosition - shoulderPosition;

            if (aimDirection.sqrMagnitude < 0.0001f)
            {
                aimDirection = Vector2.right;
            }

            aimDirection = aimDirection.normalized;
            Vector2 desiredTarget = shoulderPosition + aimDirection * armLength;
            desiredTarget.y = Mathf.Max(desiredTarget.y, shoulderPosition.y + minimumAimHeight);
            smoothedTarget = Vector2.SmoothDamp(
                smoothedTarget,
                desiredTarget,
                ref targetVelocity,
                targetSmoothTime,
                maxHandSpeed,
                Time.fixedDeltaTime);

            Vector2 handPosition = smoothedTarget;
            Vector2 shoulderToHand = handPosition - shoulderPosition;
            float distance = shoulderToHand.magnitude;
            Vector2 direction = distance > 0.001f ? shoulderToHand / distance : Vector2.right;
            float elbowDistance = Mathf.Clamp(
                distance - forearmLength,
                0f,
                upperArmLength);
            Vector2 elbowPosition = shoulderPosition + direction * elbowDistance;

            SetSegment(upperArm, shoulderPosition, elbowPosition);
            SetSegment(forearm, elbowPosition, handPosition);
            hand.position = smoothedTarget;
        }

        private static void SetSegment(Transform segment, Vector2 start, Vector2 end)
        {
            if (segment == null)
            {
                return;
            }

            Vector2 midpoint = (start + end) * 0.5f;
            Vector2 direction = end - start;
            segment.position = midpoint;
            if (direction.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                segment.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (shoulder == null)
            {
                return;
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(shoulder.position, armLength);
        }
    }
}
