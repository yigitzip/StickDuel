using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// A background layer that slides right forever while the scene in front of it stands still (the
    /// train arena: the ground rushing past sells the train's run to the left). Two copies of the sprite
    /// sit edge to edge; once the right copy has slid fully out past the camera's right edge it jumps to
    /// the far end of the left one, so the camera always sees an unbroken strip. The layer also follows
    /// the camera by <see cref="follow"/> (1 = glued to it, far away; 0 = fixed in the world) for parallax.
    /// Runs after <see cref="CameraRig"/> so it never lags a frame behind it.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class Treadmill : MonoBehaviour
    {
        private SpriteRenderer left, right;
        private Transform cam;
        private float width, speed, follow, scroll, y;

        /// <summary>
        /// <paramref name="sprite"/> (pivot at its bottom centre) with its bottom edge at world height
        /// <paramref name="y"/>, sliding right at <paramref name="speed"/> units a second. The sprite must
        /// be at least as wide as the camera's view, or a gap shows between the copies.
        /// </summary>
        public static Treadmill Create(Transform parent, string name, Sprite sprite, int sortingOrder, float speed, float follow, float y)
        {
            var layer = new GameObject(name).AddComponent<Treadmill>();
            layer.transform.SetParent(parent, false);
            layer.width = sprite.bounds.size.x;
            layer.speed = speed;
            layer.follow = follow;
            layer.y = y;
            layer.left = Copy(layer.transform, sprite, sortingOrder);
            layer.right = Copy(layer.transform, sprite, sortingOrder);
            return layer;
        }

        private static SpriteRenderer Copy(Transform parent, Sprite sprite, int sortingOrder)
        {
            var copy = new GameObject("Copy").AddComponent<SpriteRenderer>();
            copy.transform.SetParent(parent, false);
            copy.sprite = sprite;
            copy.sortingOrder = sortingOrder;
            return copy;
        }

        private void Start()
        {
            cam = Camera.main.transform;
            LateUpdate();
        }

        private void LateUpdate()
        {
            scroll = Mathf.Repeat(scroll + speed * Time.deltaTime, width);
            float camX = cam.position.x;
            // The left copy is the one whose centre is the last at or left of the camera, the right copy
            // follows it edge to edge. When the scroll carries the left copy's centre past the camera, the
            // right copy (by then wholly past the view's right edge) wraps round to become the new left one.
            float slide = camX * follow + scroll;
            float x = camX - Mathf.Repeat(camX - slide, width);
            if (Mathf.Abs(x - left.transform.position.x) > width * 0.5f)
            {
                (left, right) = (right, left); // the jump: the copy that went out on the right comes back in on the left
            }

            left.transform.position = new Vector3(x, y, 0f);
            right.transform.position = new Vector3(x + width, y, 0f);
        }
    }
}
