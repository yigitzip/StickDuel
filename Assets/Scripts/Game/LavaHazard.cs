using System.Collections.Generic;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Lava: hurts anyone who falls in and throws them back out, onto the nearest floating piece
    /// when there are some (volcano), otherwise onto the nearer bank.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class LavaHazard : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float damage = 12f;
        [SerializeField, Min(0.05f)] private float interval = 0.6f;
        [Tooltip("Impulse that throws a burned fighter up and out (x: towards the bank).")]
        [SerializeField] private Vector2 launch = new Vector2(14f, 38f); // clears the deeper lava back onto a piece

        [SerializeField] private string label = "BURN!";
        [SerializeField] private Color color = new Color(1f, 0.6f, 0.2f);

        private readonly Dictionary<Fighter, float> nextBurn = new Dictionary<Fighter, float>();

        private void OnTriggerStay2D(Collider2D other)
        {
            Fighter fighter = other.attachedRigidbody != null ? other.attachedRigidbody.GetComponent<Fighter>() : null;
            if (fighter == null || fighter.IsDead || (nextBurn.TryGetValue(fighter, out float next) && Time.time < next))
            {
                return;
            }

            nextBurn[fighter] = Time.time + interval;
            // Thrown out to safety, so one mistake costs health, not the round.
            float x = fighter.Body.position.x;
            float push = (x >= transform.position.x ? 1f : -1f) * launch.x;
            if (SinkingPlatform.All.Count > 0)
            {
                SinkingPlatform nearest = SinkingPlatform.All[0];
                foreach (SinkingPlatform piece in SinkingPlatform.All)
                {
                    if (Mathf.Abs(piece.X - x) < Mathf.Abs(nearest.X - x)) nearest = piece;
                }

                // About one second in the air: an impulse of mass * distance lands on the piece.
                push = Mathf.Clamp((nearest.X - x) * fighter.Body.mass, -launch.x, launch.x);
            }

            fighter.TakeHit(damage, new Vector2(push, launch.y), false);
            if (Fx.Instance != null)
            {
                Fx.Instance.Hit(fighter.Body.position + Vector2.down * 0.6f, Vector2.up, color, damage, false);
                Fx.Instance.Callout(fighter.ShoulderPosition + Vector2.up * 0.7f, label, color, 0.9f);
            }
        }
    }
}
