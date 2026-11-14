using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Round shield held out in both hands towards the aim (plumbing in <see cref="HeldWeapon"/>), the
    /// RUN! mode's only gear. It never hurts and is never thrown; its face stops what comes at it:
    /// an arrow (<see cref="Shuriken"/>) bounces off it, a sniper bullet
    /// (<see cref="SniperRifleWeapon.CalculateShot(Vector2)"/>) ends on it.
    /// Shield space: origin = the middle of the shield, +x = out of its face, the long axis along y.
    /// Art: Resources/Weapons/Shield.png (182x420, face to the right, strap on the left; made by
    /// Tools/prepare_backgrounds.py shield()), drawn 1.15 units tall.
    /// </summary>
    public sealed class ShieldWeapon : HeldWeapon
    {
        private const float Height = 1.15f;
        private const float ArtHeight = 420f;

        [Header("Hold")]
        [Tooltip("The shield's middle: this far from the shoulder towards the aim.")]
        [SerializeField, Min(0.1f)] private float reach = 0.5f;
        [SerializeField, Min(0.01f)] private float turnSmoothTime = 0.05f;
        [SerializeField, Min(0.01f)] private float handSmoothTime = 0.05f;
        [SerializeField, Range(-90f, 0f)] private float minPitch = -70f;
        [SerializeField, Range(0f, 90f)] private float maxPitch = 89f;

        private static Sprite art;

        private Vector2 centre, centreVelocity;
        private float angle, angleVelocity;

        public override WeaponType Type => WeaponType.Shield;
        public override bool IsAttacking => false;
        /// <summary>The far hand, on the strap behind the upper half.</summary>
        public override Vector2 RearHand => transform.TransformPoint(new Vector3(-0.14f, 0.2f, 0f));
        /// <summary>The near hand, on the strap behind the lower half.</summary>
        public override Vector2 LeadHand => transform.TransformPoint(new Vector3(-0.14f, -0.2f, 0f));
        public override float ThrowSpeed => 0f;
        /// <summary>Out of the shield's face.</summary>
        public Vector2 Face => transform.right;
        protected override float ThrowGravity => 1.5f;
        protected override float ThrowDamage => 0f;
        protected override bool TrailOn => false;
        protected override bool SwingTrail => false;

        /// <summary>The shield art, pivot in its middle. Null, with an error, if the texture is missing.</summary>
        public static Sprite Art => art != null ? art : art = LoadArt("Shield", ArtHeight / Height, 91f);

        /// <summary>Builds the shield for <paramref name="owner"/> (unequipped). Null, with an error, if the art is missing.</summary>
        public static ShieldWeapon Create(Fighter owner)
        {
            // The solid "handle" is the whole plate (it is what arrows and bullets meet); the head never hurts.
            ShieldWeapon shield = Build<ShieldWeapon>(owner, "Shield", Art, 1f, -0.12f, 0.14f, Height * 0.95f,
                Vector2.zero, new Vector2(0.02f, 0.02f), new Vector2(0.14f, 0f));
            if (shield != null)
            {
                shield.GetComponent<SpriteRenderer>().sortingOrder = 18; // over both arms: the hands are behind it
            }

            return shield;
        }

        protected override void ResetAttacks()
        {
        }

        protected override void ResetPose()
        {
            angle = owner.Facing > 0 ? 0f : 180f;
            angleVelocity = 0f;
            SetMirrored(owner.Facing < 0); // strap stays at the back, rim the right way up
            centre = CentreTarget();
            centreVelocity = Vector2.zero;
            body.position = centre;
            body.rotation = angle;
            transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, 0f, angle));
        }

        protected override void OnThrown(Vector2 velocity)
        {
        }

        protected override void HeldUpdate(float dt)
        {
            int facing = owner.Facing;
            SetMirrored(facing < 0);
            float target;
            if (owner.IsRolling)
            {
                target = 90f; // over the back while tumbling
            }
            else
            {
                Vector2 aim = owner.aimPoint - owner.ShoulderPosition;
                float pitch = aim.sqrMagnitude > 0.0001f ? Mathf.Clamp(Mathf.Atan2(aim.y, Mathf.Abs(aim.x)) * Mathf.Rad2Deg, minPitch, maxPitch) : 0f;
                target = facing > 0 ? pitch : 180f - pitch;
            }

            angle = Mathf.SmoothDampAngle(angle, target, ref angleVelocity, turnSmoothTime, Mathf.Infinity, dt);
            centre = Vector2.SmoothDamp(centre, CentreTarget(), ref centreVelocity, handSmoothTime, Mathf.Infinity, dt);
            body.MovePosition(centre);
            body.MoveRotation(angle);
        }

        private Vector2 CentreTarget()
        {
            if (owner.IsRolling)
            {
                return owner.Body.position + new Vector2(0f, 0.1f);
            }

            float r = angle * Mathf.Deg2Rad;
            return owner.ShoulderPosition + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * reach + Vector2.down * 0.12f;
        }

        /// <summary>The shield has no edge.</summary>
        protected override void OnHeadTouch(Collider2D other, Fighter victim)
        {
        }
    }
}
