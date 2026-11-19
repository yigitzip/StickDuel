using System.Collections.Generic;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// A background layer that slides right forever while the scene in front of it stands still (the
    /// train arena: the ground rushing past sells the train's run to the left). Copies of the sprite sit
    /// edge to edge, as many as the camera's view needs (a layer narrower than the view gets more than
    /// two), and the row snaps back by one copy width as it scrolls, so the camera always sees an unbroken
    /// strip. The layer also follows the camera by <see cref="follow"/> (1 = glued to it, far away; 0 =
    /// fixed in the world) for parallax. Runs after <see cref="CameraRig"/> so it never lags a frame behind it.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class Treadmill : MonoBehaviour
    {
        private readonly List<SpriteRenderer> copies = new List<SpriteRenderer>();
        private Sprite sprite;
        private int sortingOrder;
        private Camera cam;
        private float width, speed, follow, scroll, y;

        /// <summary>
        /// <paramref name="sprite"/> (pivot at its bottom centre) with its bottom edge at world height
        /// <paramref name="y"/>, sliding right at <paramref name="speed"/> units a second.
        /// </summary>
        public static Treadmill Create(Transform parent, string name, Sprite sprite, int sortingOrder, float speed, float follow, float y)
        {
            var layer = new GameObject(name).AddComponent<Treadmill>();
            layer.transform.SetParent(parent, false);
            layer.sprite = sprite;
            layer.sortingOrder = sortingOrder;
            layer.width = sprite.bounds.size.x;
            layer.speed = speed;
            layer.follow = follow;
            layer.y = y;
            layer.AddCopy();
            layer.AddCopy();
            return layer;
        }

        private void AddCopy()
        {
            var copy = new GameObject("Copy").AddComponent<SpriteRenderer>();
            copy.transform.SetParent(transform, false);
            copy.sprite = sprite;
            copy.sortingOrder = sortingOrder;
            copies.Add(copy);
        }

        private void Start()
        {
            cam = Camera.main;
            LateUpdate();
        }

        private void LateUpdate()
        {
            scroll = Mathf.Repeat(scroll + speed * Time.deltaTime, width);
            float camX = cam.transform.position.x;
            // The anchor copy is the one whose centre is the last at or left of the camera: anywhere in
            // (camX - width, camX]. Enough copies go left and right of it to cover the view in either case.
            float slide = camX * follow + scroll;
            float x = camX - Mathf.Repeat(camX - slide, width);
            float half = cam.orthographicSize * cam.aspect / width;
            int before = Mathf.Max(0, Mathf.CeilToInt(half - 0.5f)) + 1; // one spare each side: a shaken, tilted view or a wider render target
            int after = Mathf.Max(1, Mathf.CeilToInt(half + 0.5f)) + 1;
            while (copies.Count < before + after + 1)
            {
                AddCopy();
            }

            for (int i = 0; i < copies.Count; i++)
            {
                copies[i].transform.position = new Vector3(x + (i - before) * width, y, 0f);
            }
        }
    }
}
