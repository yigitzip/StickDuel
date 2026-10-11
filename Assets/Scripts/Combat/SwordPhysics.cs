using UnityEngine;

namespace StickDuel
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class SwordPhysics : MonoBehaviour
    {
        [Header("Grip")]
        [SerializeField] private Transform hand;
        [SerializeField] private Transform shoulder;
        [SerializeField] private Vector2 gripAnchor = new Vector2(-0.08f, 0f);
        [SerializeField] private Vector2 localTipDirection = Vector2.right;

        [Header("Direct Arm Follow")]
        [SerializeField, Range(0f, 1f)] private float armFollowRate = 1f;
        [SerializeField, Range(0f, 2f)] private float swingFollowGain = 0.75f;
        [SerializeField, Range(0f, 60f)] private float inwardAngleLimit = 60f;
        [SerializeField, Range(0f, 180f)] private float outwardAngleLimit = 120f;
        [SerializeField, Range(60f, 120f)] private float restingAngleOffset = 100f;
        [SerializeField] private float initialAngleOffset;

        [Header("Rigidbody")]
        [SerializeField, Min(0f)] private float mass = 1.2f;
        [SerializeField, Min(0f)] private float angularDamping = 0f;
        [SerializeField, Min(0f)] private float linearDamping = 0.1f;
        [SerializeField, Min(0f)] private float gravityScale;

        private Rigidbody2D swordBody;
        private HingeJoint2D gripJoint;
        private float previousArmAngle;
        private float unwrappedArmAngle;
        private float controlledSwordAngle;
        private float relativeTipAngle;

        private void Awake()
        {
            ResolveReferences();

            swordBody = GetComponent<Rigidbody2D>();
            swordBody.bodyType = RigidbodyType2D.Dynamic;
            swordBody.mass = mass;
            swordBody.gravityScale = gravityScale;
            swordBody.linearDamping = linearDamping;
            swordBody.angularDamping = angularDamping;
            swordBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            IgnoreFighterCollisions();
            ConfigureGripJoint();

            float armAngle = GetArmAngle();
            previousArmAngle = armAngle;
            unwrappedArmAngle = armAngle;
            controlledSwordAngle = armAngle + initialAngleOffset - GetLocalTipAngle();
            if (Mathf.Approximately(initialAngleOffset, 0f))
            {
                controlledSwordAngle = armAngle + restingAngleOffset - GetLocalTipAngle();
            }
            relativeTipAngle = Mathf.DeltaAngle(
                armAngle,
                controlledSwordAngle + GetLocalTipAngle());

        }

        private void ResolveReferences()
        {
            Transform fighter = transform.parent;
            if (fighter == null)
            {
                return;
            }

            hand = fighter.Find("Hand");
            shoulder = fighter.Find("Shoulder");
        }

        private void IgnoreFighterCollisions()
        {
            Collider2D swordCollider = GetComponent<Collider2D>();
            if (swordCollider == null || transform.parent == null)
            {
                return;
            }

            Collider2D[] fighterColliders = transform.parent.GetComponentsInChildren<Collider2D>();
            foreach (Collider2D fighterCollider in fighterColliders)
            {
                if (fighterCollider != swordCollider)
                {
                    Physics2D.IgnoreCollision(swordCollider, fighterCollider);
                }
            }
        }

        private void ConfigureGripJoint()
        {
            gripJoint = GetComponent<HingeJoint2D>();
            if (gripJoint == null)
            {
                gripJoint = gameObject.AddComponent<HingeJoint2D>();
            }

            gripJoint.connectedBody = null;
            gripJoint.autoConfigureConnectedAnchor = false;
            gripJoint.anchor = gripAnchor;
            gripJoint.connectedAnchor = hand != null ? hand.position : swordBody.position;
            gripJoint.useLimits = false;
            gripJoint.useMotor = false;
        }

        private void FixedUpdate()
        {
            if (swordBody == null || hand == null || shoulder == null)
            {
                return;
            }

            Vector2 handPosition = hand.position;
            UpdateGripAnchor(handPosition);
            UpdateDirectSwordRotation();
        }

        private void UpdateGripAnchor(Vector2 handPosition)
        {
            if (gripJoint != null &&
                (gripJoint.connectedAnchor - handPosition).sqrMagnitude > 0.000001f)
            {
                gripJoint.connectedAnchor = handPosition;
            }
        }

        private void UpdateDirectSwordRotation()
        {
            float armAngle = GetArmAngle();
            float armDelta = Mathf.DeltaAngle(previousArmAngle, armAngle);
            previousArmAngle = armAngle;
            unwrappedArmAngle += armDelta;

            controlledSwordAngle += armDelta * armFollowRate;
            relativeTipAngle += armDelta * swingFollowGain;
            relativeTipAngle = Mathf.Clamp(
                relativeTipAngle,
                -inwardAngleLimit,
                outwardAngleLimit);
            controlledSwordAngle = unwrappedArmAngle +
                relativeTipAngle - GetLocalTipAngle();

            swordBody.angularVelocity = 0f;
            swordBody.MoveRotation(controlledSwordAngle);
        }

        private float GetArmAngle()
        {
            Vector2 armDirection = (Vector2)hand.position - (Vector2)shoulder.position;
            if (armDirection.sqrMagnitude < 0.0001f)
            {
                return previousArmAngle;
            }

            return Mathf.Atan2(armDirection.y, armDirection.x) * Mathf.Rad2Deg;
        }

        private float GetLocalTipAngle()
        {
            Vector2 direction = localTipDirection.sqrMagnitude > 0.0001f
                ? localTipDirection.normalized
                : Vector2.right;
            return Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        }

    }
}
