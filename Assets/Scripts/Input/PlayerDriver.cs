using UnityEngine;
using UnityEngine.InputSystem;

namespace StickDuel
{
    /// <summary>Keyboard + mouse (or gamepad) → Fighter input.</summary>
    [RequireComponent(typeof(Fighter))]
    public sealed class PlayerDriver : MonoBehaviour
    {
        [SerializeField] private Camera aimCamera;
        [SerializeField, Min(0.5f)] private float stickAimDistance = 3f;

        private Fighter fighter;
        private bool usingGamepadAim;
        private Vector2 stickAim = Vector2.right;

        private void Awake()
        {
            fighter = GetComponent<Fighter>();
            if (aimCamera == null)
            {
                aimCamera = Camera.main;
            }
        }

        private void Update()
        {
            float move = 0f;
            bool jump = false;
            bool backGuard = false;
            bool thrust = false;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move += 1f;
                jump |= keyboard.wKey.wasPressedThisFrame
                        || keyboard.spaceKey.wasPressedThisFrame
                        || keyboard.upArrowKey.wasPressedThisFrame;
                backGuard |= keyboard.qKey.isPressed;
                thrust |= keyboard.eKey.isPressed;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                move += gamepad.leftStick.x.ReadValue();
                jump |= gamepad.buttonSouth.wasPressedThisFrame;
                backGuard |= gamepad.rightShoulder.isPressed;
                thrust |= gamepad.rightTrigger.isPressed;
                Vector2 right = gamepad.rightStick.ReadValue();
                if (right.sqrMagnitude > 0.2f)
                {
                    usingGamepadAim = true;
                    stickAim = right.normalized;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                backGuard |= mouse.rightButton.isPressed;
                thrust |= mouse.leftButton.isPressed;
                if (mouse.delta.ReadValue().sqrMagnitude > 0.01f)
                {
                    usingGamepadAim = false;
                }
            }

            fighter.backGuard = backGuard;
            fighter.thrustHeld = thrust;

            fighter.moveInput = Mathf.Clamp(move, -1f, 1f);
            if (jump)
            {
                fighter.RequestJump();
            }

            if (usingGamepadAim)
            {
                fighter.aimPoint = fighter.ShoulderPosition + stickAim * stickAimDistance;
            }
            else if (mouse != null && aimCamera != null)
            {
                Vector3 screen = mouse.position.ReadValue();
                screen.z = -aimCamera.transform.position.z;
                fighter.aimPoint = aimCamera.ScreenToWorldPoint(screen);
            }
        }
    }
}
