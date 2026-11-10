using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Tarzan ivy: a pendulum hung from an anchor, resting at full swing to one side. A fighter
    /// jumping into its lower end rides it (<see cref="Fighter"/> hangs under <see cref="Tip"/>)
    /// across to the other side, where it comes to rest again once let go. Scripted rather than a
    /// HingeJoint2D so the swing is exact (it always reaches the far side) and parks itself there.
    /// </summary>
    public sealed class SwingingIvy : MonoBehaviour
    {
        /// <summary>The rider's hip hangs this far above the tip: clears the floor at the bottom of the swing.</summary>
        public const float RiderLift = 0.6f;
        private const float GrabRadius = 0.9f;
        private const float Gravity = 9.81f * 2.6f; // the fighters' gravity, so the swing feels like a jump
        private const float ParkTolerance = 0.02f;  // radians short of the rest angle that still counts as arrived

        private Vector2 anchor;
        private float length;
        private float restAngle;    // radians from straight down; the ivy parks at ±restAngle
        private float angle;
        private float angularVelocity;
        private float spriteOffset; // sprite rotation (degrees) at angle 0
        private readonly Collider2D[] hits = new Collider2D[4];

        public Fighter Rider { get; private set; }
        public Vector2 Tip => anchor + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * length;
        public Vector2 TipVelocity => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (angularVelocity * length);
        /// <summary>Where the rider's hands hold on, up the vine from the tip.</summary>
        public Vector2 HandHold => anchor + (Tip - anchor) * (1f - 1.5f / length);

        /// <summary>Hangs Resources/Arena/Ivy from <paramref name="anchor"/>: its root is the top-right corner, its tip the bottom-left one.</summary>
        public static SwingingIvy Create(Transform root, Vector2 anchor, float pixelsPerUnit)
        {
            var texture = Resources.Load<Texture2D>("Arena/Ivy");
            var go = new GameObject("SwingingIvy");
            go.transform.SetParent(root, false);
            go.transform.position = anchor;
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(1f, 1f), pixelsPerUnit);
            sprite.sortingOrder = -20;
            var ivy = go.AddComponent<SwingingIvy>();
            ivy.anchor = anchor;
            Vector2 size = sprite.sprite.bounds.size; // the painting is a diagonal: swing length and rest angle come off its box
            ivy.length = size.magnitude;
            ivy.restAngle = Mathf.Atan2(size.x, size.y);
            ivy.spriteOffset = ivy.restAngle * Mathf.Rad2Deg; // rotation 0 is the painted pose, hanging to the left
            ivy.angle = -ivy.restAngle;
            ivy.Pose();
            return ivy;
        }

        private void Pose() => transform.rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + spriteOffset);

        private void FixedUpdate()
        {
            if (Rider != null && Rider.Vine != this)
            {
                Rider = null;
            }

            bool parked = Rider == null && Mathf.Abs(angle) >= restAngle - ParkTolerance && angle * angularVelocity >= 0f;
            if (parked)
            {
                angle = Mathf.Sign(angle) * restAngle;
                angularVelocity = 0f;
            }
            else
            {
                float dt = Time.fixedDeltaTime;
                angularVelocity -= Gravity / length * Mathf.Sin(angle) * dt;
                angle += angularVelocity * dt;
                if (Mathf.Abs(angle) >= restAngle)
                {
                    angle = Mathf.Sign(angle) * restAngle;
                    angularVelocity = 0f;
                }
            }

            Pose();
            if (Rider == null)
            {
                TryGrab();
            }
        }

        /// <summary>A fighter in the air at the tip takes hold.</summary>
        private void TryGrab()
        {
            var filter = new ContactFilter2D { useTriggers = true, useLayerMask = true, layerMask = LayerMask.GetMask(Fighter.FighterLayer) };
            int count = Physics2D.OverlapCircle(Tip, GrabRadius, filter, hits);
            for (int i = 0; i < count; i++)
            {
                Rigidbody2D body = hits[i].attachedRigidbody;
                Fighter fighter = body != null ? body.GetComponent<Fighter>() : null;
                if (fighter != null && fighter.TryGrab(this))
                {
                    Rider = fighter;
                    return;
                }
            }
        }
    }
}
