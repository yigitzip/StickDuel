using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// One painting repeated sideways forever: a handful of tiles side by side, every other one
    /// mirrored so neighbouring edges always match, re-placed around the camera every frame (only
    /// those few tiles ever exist). <see cref="factor"/> is how much the layer travels with the
    /// camera: 0 = fixed in the world (the ground you walk on), 0.5 = far away, slides past at
    /// half speed. Runs after <see cref="CameraRig"/> so the layers never lag a frame behind it.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class InfiniteBackdrop : MonoBehaviour
    {
        // Covers the widest view: a tile is ~17 units, the camera sees at most ~2 tiles wide.
        private const int TileCount = 5;

        private SpriteRenderer[] tiles;
        private Transform cam;
        private float width;
        private float factor;
        private float y;

        /// <summary>
        /// A layer of <paramref name="sprite"/> with its pivot at world height <paramref name="y"/>, drawn at
        /// <paramref name="scale"/> (below 1 = smaller, so it reads as further away).
        /// </summary>
        public static InfiniteBackdrop Create(Transform parent, string name, Sprite sprite, float factor, int sortingOrder, float scale = 1f, float y = 0f)
        {
            var layer = new GameObject(name).AddComponent<InfiniteBackdrop>();
            layer.transform.SetParent(parent, false);
            layer.transform.localScale = new Vector3(scale, scale, 1f);
            layer.factor = factor;
            layer.y = y;
            layer.width = sprite.bounds.size.x * scale;
            layer.tiles = new SpriteRenderer[TileCount];
            for (int i = 0; i < TileCount; i++)
            {
                var tile = new GameObject("Tile").AddComponent<SpriteRenderer>();
                tile.transform.SetParent(layer.transform, false);
                tile.sprite = sprite;
                tile.sortingOrder = sortingOrder;
                layer.tiles[i] = tile;
            }

            return layer;
        }

        private void Start()
        {
            cam = Camera.main.transform;
            LateUpdate();
        }

        private void LateUpdate()
        {
            float camX = cam.position.x;
            float origin = camX * factor;
            // Tile k (absolute, so its mirroring never changes as it is recycled) sits at origin + k * width.
            int centre = Mathf.RoundToInt((camX - origin) / width);
            for (int i = 0; i < TileCount; i++)
            {
                int k = centre + i - TileCount / 2;
                tiles[i].transform.position = new Vector3(origin + k * width, y, 0f);
                tiles[i].flipX = (k & 1) != 0;
            }
        }
    }
}
