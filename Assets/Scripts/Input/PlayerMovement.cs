using UnityEngine;
using UnityEngine.InputSystem;

namespace StickDuel
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float maxSpeed = 5f;
        [SerializeField, Min(0f)] private float acceleration = 30f;
        [SerializeField, Min(0f)] private float deceleration = 40f;

        private Rigidbody2D body;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
        }

        private void FixedUpdate()
        {
            float input = ReadHorizontalInput();
            float targetSpeed = input * maxSpeed;
            float rate = Mathf.Abs(targetSpeed) > 0.01f ? acceleration : deceleration;
            float newSpeed = Mathf.MoveTowards(body.linearVelocity.x, targetSpeed, rate * Time.fixedDeltaTime);

            body.linearVelocity = new Vector2(newSpeed, body.linearVelocity.y);
        }

        private static float ReadHorizontalInput()
        {
            if (Keyboard.current == null)
            {
                return 0f;
            }

            float input = 0f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
            {
                input -= 1f;
            }

            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
            {
                input += 1f;
            }

            return input;
        }
    }
}
