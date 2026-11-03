using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Longbow (plumbing in <see cref="HeldWeapon"/>). The bow hand holds it out towards the aim
    /// (it is light, so it turns fast), the other hand is on the string. Hold the left mouse button
    /// (the driver sets <see cref="DrawHeld"/>; the shuriken is put away while the bow is held) to draw: the
    /// string hand comes back, a nocked arrow shows, and the shot builds up; release to loose. A
    /// short draw lobs a weak arrow, a full draw sends a fast, flat, hard one. Arrows fly on the
    /// shuriken's ballistic rules (<see cref="Shuriken.Launch(Fighter, Vector2, Vector2, float, float, Sprite, bool)"/>):
    /// point first, headshots, batted away by blades, stick in the ground. No melee: the bow
    /// itself never hurts, but it does block a blade. Thrown, it tumbles, lightly.
    /// Bow space: origin = grip, +x = where the arrow goes, string at x = <see cref="StringRest"/>.
    /// Art: Resources/Weapons/Bow.png (400 px/unit, 1.5 units tall, grip at x=83px) and Arrow.png (0.7 units, nock at the left).
    /// </summary>
    public sealed class BowWeapon : HeldWeapon
    {
        private const float PixelsPerUnit = 400f;
        private const float GripPixel = 83.5f;
        private const float StringRest = -0.2f;

        [Header("Hold")]
        [Tooltip("Bow hand: this far from the shoulder towards the aim.")]
        [SerializeField, Min(0.1f)] private float reach = 0.62f;
        [SerializeField, Min(0.01f)] private float turnSmoothTime = 0.04f;
        [SerializeField, Min(0.01f)] private float handSmoothTime = 0.06f;
        [SerializeField, Range(-90f, 0f)] private float minPitch = -80f;
        [SerializeField, Range(0f, 90f)] private float maxPitch = 85f;

        [Header("Draw (hold left click) and shot")]
        [SerializeField, Min(0.05f)] private float drawTime = 0.7f;
        [Tooltip("Shorter draws than this just relax the string.")]
        [SerializeField, Range(0f, 1f)] private float minDraw = 0.15f;
        [Tooltip("How far back the string hand comes at full draw.")]
        [SerializeField, Min(0f)] private float drawLength = 0.45f;
        [SerializeField] private Vector2 arrowSpeed = new Vector2(11f, 26f);
        [SerializeField] private Vector2 arrowDamage = new Vector2(8f, 30f);
        [SerializeField, Min(0f)] private float arrowGravity = 9f;
        [SerializeField, Min(0f)] private float shotCooldown = 0.25f;
        [SerializeField, Min(0f)] private float shotStaminaCost = 6f;

        [Header("Throw")]
        [SerializeField, Min(0f)] private float throwSpeed = 12f;
        [SerializeField, Min(0f)] private float throwGravity = 1.2f;
        [SerializeField, Min(0f)] private float throwDamage = 10f;

        private static Sprite art, arrowArt;

        private SpriteRenderer nockedArrow;
        private Vector2 grip, gripVelocity;
        private float angle, angleVelocity;
        private float draw, readyTime;
        private bool drawing;

        /// <summary>Set each frame by the driver: the draw button is down.</summary>
        [System.NonSerialized] public bool DrawHeld;

        public override WeaponType Type => WeaponType.Bow;
        public override bool IsAttacking => flying || drawing;
        /// <summary>The bow hand, on the grip.</summary>
        public override Vector2 LeadHand => transform.position;
        /// <summary>The string hand: on the string at rest, pulled back by the draw.</summary>
        public override Vector2 RearHand => transform.TransformPoint(new Vector3(StringRest - drawLength * draw, 0f, 0f));
        /// <summary>Leans back a little into a full draw.</summary>
        public override float PoseLean => -0.3f * draw;
        public override float ThrowSpeed => throwSpeed;
        /// <summary>0→1 how far the string is drawn (the aim line reads it).</summary>
        public float Draw => draw;
        public bool IsDrawing => drawing;
        /// <summary>Where a loosed arrow starts: the grip.</summary>
        public Vector2 ArrowOrigin => transform.position;
        /// <summary>The arrow's launch velocity for the current draw.</summary>
        public Vector2 ArrowVelocity => (Vector2)transform.right * Mathf.Lerp(arrowSpeed.x, arrowSpeed.y, draw);
        public float ArrowGravity => arrowGravity;
        protected override float ThrowGravity => throwGravity;
        protected override float ThrowDamage => throwDamage;
        protected override bool TrailOn => false;
        protected override bool SwingTrail => false;

        /// <summary>The bow art (also the menu card). Null, with an error, if the texture is missing.</summary>
        public static Sprite Art => art != null ? art : art = LoadArt("Bow", PixelsPerUnit, GripPixel);

        /// <summary>The arrow art, nock at the left edge. Null, with an error, if the texture is missing.</summary>
        public static Sprite ArrowArt => arrowArt != null ? arrowArt : arrowArt = LoadArt("Arrow", PixelsPerUnit, 0f);

        /// <summary>Builds the bow for <paramref name="owner"/> (unequipped). Null, with an error, if the art is missing.</summary>
        public static BowWeapon Create(Fighter owner)
        {
            // The "handle" box is the limbs (tall, thin: it blocks a blade); the head never hurts, so it is a speck.
            BowWeapon bow = Build<BowWeapon>(owner, "Bow", Art, 0.6f, -0.05f, 0.05f, 1.3f, Vector2.zero, new Vector2(0.02f, 0.02f), Vector2.zero);
            if (bow == null)
            {
                return null;
            }

            var arrow = new GameObject("NockedArrow");
            arrow.transform.SetParent(bow.transform, false);
            var renderer = arrow.AddComponent<SpriteRenderer>();
            renderer.sprite = ArrowArt;
            renderer.enabled = false;
            SpriteRenderer bowRenderer = bow.GetComponent<SpriteRenderer>();
            renderer.sharedMaterial = bowRenderer.sharedMaterial;
            renderer.sortingLayerID = bowRenderer.sortingLayerID;
            renderer.sortingOrder = bowRenderer.sortingOrder + 1;
            return bow;
        }

        protected override void Awake()
        {
            base.Awake();
            nockedArrow = transform.Find("NockedArrow").GetComponent<SpriteRenderer>();
        }

        protected override void ResetAttacks()
        {
            draw = 0f;
            drawing = false;
            if (nockedArrow != null)
            {
                nockedArrow.enabled = false;
            }
        }

        protected override void ResetPose()
        {
            angle = owner.Facing > 0 ? 0f : 180f;
            angleVelocity = 0f;
            grip = GripTarget();
            gripVelocity = Vector2.zero;
            body.position = grip;
            body.rotation = angle;
            transform.SetPositionAndRotation(grip, Quaternion.Euler(0f, 0f, angle));
        }

        protected override void OnThrown(Vector2 velocity)
        {
            body.angularVelocity = -Mathf.Sign(velocity.x == 0f ? owner.Facing : velocity.x) * 400f;
        }

        protected override void HeldUpdate(float dt)
        {
            UpdateDraw(dt);
            int facing = owner.Facing;
            float target;
            if (owner.IsRolling)
            {
                target = facing > 0 ? 100f : 80f; // held up out of the way
            }
            else
            {
                Vector2 aim = owner.aimPoint - owner.ShoulderPosition;
                float pitch = aim.sqrMagnitude > 0.0001f ? Mathf.Clamp(Mathf.Atan2(aim.y, Mathf.Abs(aim.x)) * Mathf.Rad2Deg, minPitch, maxPitch) : 0f;
                target = facing > 0 ? pitch : 180f - pitch;
            }

            angle = Mathf.SmoothDampAngle(angle, target, ref angleVelocity, turnSmoothTime, Mathf.Infinity, dt);
            grip = Vector2.SmoothDamp(grip, GripTarget(), ref gripVelocity, handSmoothTime, Mathf.Infinity, dt);
            body.MovePosition(grip);
            body.MoveRotation(angle);
            nockedArrow.transform.localPosition = new Vector3(StringRest - drawLength * draw, 0f, 0f);
        }

        private Vector2 GripTarget()
        {
            if (owner.IsRolling)
            {
                return owner.Body.position + new Vector2(0f, -0.3f);
            }

            float r = angle * Mathf.Deg2Rad;
            return owner.ShoulderPosition + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * reach + Vector2.down * 0.05f;
        }

        /// <summary>Hold the button to draw, release to loose; a hit, a roll or a stagger relaxes the string.</summary>
        private void UpdateDraw(float dt)
        {
            bool canDraw = owner.ControlsEnabled && !owner.IsStaggered && !owner.IsRolling;
            if (DrawHeld && canDraw && (drawing || Time.time >= readyTime))
            {
                if (!drawing)
                {
                    drawing = true;
                    draw = 0f;
                    nockedArrow.enabled = true;
                }

                draw = Mathf.Min(1f, draw + dt / drawTime);
                return;
            }

            if (!drawing)
            {
                return;
            }

            drawing = false;
            nockedArrow.enabled = false;
            if (canDraw && draw >= minDraw && owner.TrySpendStamina(shotStaminaCost))
            {
                Shuriken.Launch(owner, ArrowOrigin, ArrowVelocity, arrowGravity, Mathf.Lerp(arrowDamage.x, arrowDamage.y, draw), ArrowArt, true);
                readyTime = Time.time + shotCooldown;
                if (Fx.Instance != null)
                {
                    Fx.Instance.Swoosh(ArrowOrigin, Mathf.Lerp(8f, MaxSpeed, draw));
                }
            }
            else if (canDraw && draw >= minDraw)
            {
                Tired();
            }

            draw = 0f;
        }

        /// <summary>The bow has no edge.</summary>
        protected override void OnHeadTouch(Collider2D other, Fighter victim)
        {
        }
    }
}
