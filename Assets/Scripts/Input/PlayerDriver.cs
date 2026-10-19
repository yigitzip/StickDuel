using UnityEngine;
using UnityEngine.InputSystem;

namespace StickDuel
{
    /// <summary>
    /// Keyboard + mouse (or gamepad) → Fighter input. Hold right mouse to charge a shuriken throw;
    /// left click throws the katana at the cursor (or punches when it is gone), and walking over
    /// the fallen katana picks it back up. Q / E roll left / right.
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public sealed class PlayerDriver : MonoBehaviour
    {
        [SerializeField] private Camera aimCamera;
        [SerializeField, Min(0.5f)] private float stickAimDistance = 3f;

        [Header("Sword Throw")]
        [SerializeField, Min(0f)] private float swordThrowSpeed = 14f;
        [Tooltip("Walking this close (from the hip) to the fallen sword picks it up.")]
        [SerializeField, Min(0.1f)] private float swordPickupRange = 1.2f;

        [Header("Shuriken Throw")]
        [SerializeField, Min(1)] private int maxShuriken = 3;
        [Tooltip("Once every shuriken is thrown, a full set comes back after this many seconds.")]
        [SerializeField, Min(0f)] private float shurikenReloadTime = 10f;
        [Tooltip("Holding longer than this does not add more power.")]
        [SerializeField, Min(0.05f)] private float maxChargeTime = 1f;
        [SerializeField, Min(0f)] private float minThrowSpeed = 5f;
        [SerializeField, Min(0f)] private float maxThrowSpeed = 15f;
        [SerializeField, Min(0f)] private float throwGravity = 9f;
        [SerializeField, Min(0f)] private float shurikenDamage = 15f;
        [Tooltip("The aim line stops where the arc meets these layers.")]
        [SerializeField] private LayerMask groundMask = 1;

        private Fighter fighter;
        private bool usingGamepadAim;
        private Vector2 stickAim = Vector2.right;
        private bool charging;
        private float chargeTimer;
        private Material lineMaterial;
        private LineRenderer aimLine;
        private readonly Vector3[] arc = new Vector3[100];
        private int shurikenCount;
        private float reloadTimer;

        public int ShurikenCount => shurikenCount;
        public int MaxShuriken => maxShuriken;
        /// <summary>0→1 while the empty set is refilling, 0 otherwise.</summary>
        public float ReloadProgress => shurikenCount == 0 ? Mathf.Clamp01(1f - reloadTimer / Mathf.Max(shurikenReloadTime, 0.0001f)) : 0f;

        private float Charge01 => Mathf.Clamp01(chargeTimer / maxChargeTime);
        private float ThrowSpeed => Mathf.Lerp(minThrowSpeed, maxThrowSpeed, Charge01);

        private void Awake()
        {
            fighter = GetComponent<Fighter>();
            if (aimCamera == null)
            {
                aimCamera = Camera.main;
            }

            lineMaterial = new Material(Shader.Find("Sprites/Default"));
            aimLine = new GameObject("AimLine").AddComponent<LineRenderer>();
            aimLine.transform.SetParent(transform, false);
            aimLine.sharedMaterial = lineMaterial;
            aimLine.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.5f));
            aimLine.sortingOrder = 19;
            aimLine.enabled = false;
            RefillShuriken();
        }

        public void RefillShuriken()
        {
            shurikenCount = maxShuriken;
            reloadTimer = 0f;
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
                thrust |= keyboard.leftShiftKey.isPressed;
                if (keyboard.qKey.wasPressedThisFrame) fighter.Roll(-1);
                if (keyboard.eKey.wasPressedThisFrame) fighter.Roll(1);
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
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    ThrowSwordOrPunch();
                }
                if (mouse.delta.ReadValue().sqrMagnitude > 0.01f)
                {
                    usingGamepadAim = false;
                }
            }

            UpdateThrow(mouse);
            PickUpSword();
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

        private void UpdateThrow(Mouse mouse)
        {
            if (shurikenCount == 0)
            {
                reloadTimer -= Time.deltaTime;
                if (reloadTimer <= 0f)
                {
                    RefillShuriken();
                }
            }

            bool canThrow = fighter.ControlsEnabled && !fighter.IsDead;
            if (!canThrow)
            {
                charging = false;
                return;
            }

            if (mouse == null)
            {
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame && shurikenCount > 0)
            {
                charging = true;
                chargeTimer = 0f;
            }
            else if (charging && mouse.rightButton.isPressed)
            {
                chargeTimer = Mathf.Min(chargeTimer + Time.deltaTime, maxChargeTime);
            }
            else if (charging)
            {
                charging = false;
                Shuriken.Launch(fighter, fighter.ShoulderPosition, ThrowDirection() * ThrowSpeed, throwGravity, shurikenDamage);
                if (--shurikenCount == 0)
                {
                    reloadTimer = shurikenReloadTime;
                }
            }
        }

        private void ThrowSwordOrPunch()
        {
            Sword sword = fighter.sword;
            if (!fighter.ControlsEnabled || fighter.IsDead)
            {
                return;
            }

            if (sword != null && sword.IsHeld)
            {
                sword.Throw(ThrowDirection() * swordThrowSpeed);
            }
            else
            {
                fighter.Punch();
            }
        }

        private void PickUpSword()
        {
            Sword sword = fighter.sword;
            if (sword == null || sword.IsHeld || sword.IsFlying || !fighter.ControlsEnabled || fighter.IsDead)
            {
                return;
            }

            Vector2 middle = (sword.GripPosition + sword.TipPosition) * 0.5f;
            if (Vector2.Distance(middle, fighter.Body.position) <= swordPickupRange)
            {
                sword.Regrip();
            }
        }

        /// <summary>From the shoulder towards the cursor (or stick aim).</summary>
        private Vector2 ThrowDirection()
        {
            Vector2 aim = fighter.aimPoint - fighter.ShoulderPosition;
            return aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.right * fighter.Facing;
        }

        /// <summary>Aim line: exactly where the shuriken will go if released now.</summary>
        private void LateUpdate()
        {
            aimLine.enabled = charging;
            if (!charging)
            {
                return;
            }

            float charge = Charge01;
            // Sample the same arc the shuriken will fly, up to where it hits the ground.
            Vector2 start = fighter.ShoulderPosition;
            Vector2 velocity = ThrowDirection() * ThrowSpeed;
            arc[0] = start;
            int count = 1;
            while (count < arc.Length)
            {
                Vector2 previous = arc[count - 1];
                Vector2 point = Shuriken.PositionAt(start, velocity, throwGravity, count * 0.03f);
                RaycastHit2D hit = Physics2D.Linecast(previous, point, groundMask);
                arc[count++] = hit.collider != null ? hit.point : point;
                if (hit.collider != null)
                {
                    break;
                }
            }

            aimLine.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                aimLine.SetPosition(i, arc[i]);
            }
            aimLine.widthMultiplier = Mathf.Lerp(0.03f, 0.06f, charge);
            Color color = Color.Lerp(Color.white, fighter.color, charge);
            aimLine.startColor = new Color(color.r, color.g, color.b, 0.9f);
            aimLine.endColor = new Color(color.r, color.g, color.b, 0.25f);
        }
    }
}
