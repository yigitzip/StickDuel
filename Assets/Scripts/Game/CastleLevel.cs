using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// The Survival castle: three long floors of four rooms each, climbed in a zigzag through stair
    /// towers at the ends, with the crown at the end of the top floor. Every room's exit is a gate
    /// that opens once its guards are down. Graybox for now: plain tinted boxes, no painted art.
    /// World units: x from 0 (left wall) to Width, floor tops at y = 0, FloorHeight, 2 * FloorHeight.
    /// </summary>
    public sealed class CastleLevel
    {
        public const float Tower = 12f;        // stair tower width at each end
        public const float RoomLength = 30f;
        public const float FloorHeight = 7f;
        public const float Width = Tower * 2f + RoomLength * 4f;
        private const float Slab = 0.6f;       // floor thickness

        /// <summary>One room. Guards: n = guard, e = elite, b = the Guard Captain.</summary>
        public sealed class Room
        {
            public string Name;
            public int Floor;
            public int Slot;     // 0..3 from the left
            public string Guards;

            public int Direction => Floor == 1 ? -1 : 1; // the middle floor runs right to left
            public float Left => Tower + Slot * RoomLength;
            public float Right => Left + RoomLength;
            public float FloorY => Floor * FloorHeight;
            public float Entry => Direction > 0 ? Left : Right;
            public bool Contains(Vector2 feet) => feet.x >= Left && feet.x <= Right && FloorOf(feet.y) == Floor;
            public bool HasPassedEntry(Vector2 feet) =>
                FloorOf(feet.y) == Floor && (Direction > 0 ? feet.x >= Entry : feet.x <= Entry);
            public bool HasPassedProgress(Vector2 feet, float distance) =>
                FloorOf(feet.y) == Floor && (feet.x - Entry) * Direction >= distance;
        }

        // ponytail: room list as data; tune guard counts here after timing a run (target 5-6 minutes).
        public readonly Room[] Rooms =
        {
            new Room { Name = "GATEHOUSE", Floor = 0, Slot = 0, Guards = "n" },
            new Room { Name = "COURTYARD", Floor = 0, Slot = 1, Guards = "nn" },
            new Room { Name = "BARRACKS", Floor = 0, Slot = 2, Guards = "n" },
            new Room { Name = "FORGE", Floor = 0, Slot = 3, Guards = "nn" },
            new Room { Name = "WALL GATE", Floor = 1, Slot = 3, Guards = "n" },
            new Room { Name = "ARCHER TOWER", Floor = 1, Slot = 2, Guards = "e" },
            new Room { Name = "RAMPARTS", Floor = 1, Slot = 1, Guards = "nn" },
            new Room { Name = "WATCHTOWER", Floor = 1, Slot = 0, Guards = "ne" },
            new Room { Name = "PALACE HALL", Floor = 2, Slot = 0, Guards = "n" },
            new Room { Name = "BANQUET HALL", Floor = 2, Slot = 1, Guards = "nnn" },
            new Room { Name = "TREASURY", Floor = 2, Slot = 2, Guards = "e" },
            new Room { Name = "THRONE ROOM", Floor = 2, Slot = 3, Guards = "bnn" },
        };

        public Vector2 Start => new Vector2(Tower * 0.5f, Fighter.HipHeight + 0.05f);
        public Vector2 Crown { get; private set; }
        public GameObject CrownObject { get; private set; }

        public static int FloorOf(float feetY) => Mathf.Clamp(Mathf.FloorToInt((feetY + 1f) / FloorHeight), 0, 2);

        public static Color StoneColor => new Color(0.42f, 0.37f, 0.52f);
        // Procedural art, made once per session (pixel textures, tinted per use).
        private static Sprite square, brick, block, arch, bars, glow, banner, crown, column, window, flame;

        /// <summary>Builds the castle. <paramref name="glowMaterial"/> (additive lantern glow) lights the torches when given.</summary>
        public static CastleLevel Build(Material glowMaterial = null)
        {
            MakeArt();
            var level = new CastleLevel();
            var root = new GameObject("Castle").transform;
            float h = FloorHeight;
            float roomsRight = Tower + RoomLength * 4f;

            // Brick back walls, one tint per floor: courtyard stone, cold rampart grey, warm palace red.
            Color[] backs = { new Color(0.23f, 0.2f, 0.27f), new Color(0.19f, 0.21f, 0.28f), new Color(0.27f, 0.17f, 0.21f) };
            for (int f = 0; f < 3; f++)
            {
                Quad(root, "Wall", new Vector2(0f, f * h), new Vector2(Width, h), backs[f], -100, brick);
                // The stair towers are darker shafts.
                Quad(root, "TowerShade", new Vector2(0f, f * h), new Vector2(Tower, h), new Color(0f, 0f, 0f, 0.35f), -99);
                Quad(root, "TowerShade", new Vector2(roomsRight, f * h), new Vector2(Tower, h), new Color(0f, 0f, 0f, 0.35f), -99);
                Decorate(root, f, glowMaterial);
            }

            // Floors and ceilings. Floor 2's slab stops short of the right tower, floor 3's of the left one,
            // so the stairs come up through the gap.
            Solid(root, "Ground", 0f, Width, 0f, 4f);
            Solid(root, "Floor2", 0f, roomsRight, h, Slab);
            Solid(root, "Floor3", Tower, Width, h * 2f, Slab);
            Solid(root, "Roof", 0f, Width, h * 3f, Slab);
            Solid(root, "WallL", -1f, 0f, h * 3f + 1f, h * 3f + 1f);
            Solid(root, "WallR", Width, Width + 1f, h * 3f + 1f, h * 3f + 1f);
            Solid(root, "ThroneWall", Width - 0.6f, Width, h * 3f, h); // the throne room ends at the far side of the right tower

            // Stairs: one-way ramps, so you can walk underneath them to the far end and turn back up.
            Ramp(root, new Vector2(Width - 1f, 0f), new Vector2(roomsRight, h));
            Ramp(root, new Vector2(1f, h), new Vector2(Tower, h * 2f));

            // Throne room: red carpet up to the throne, the crown on its seat.
            float y3 = h * 2f;
            Quad(root, "Carpet", new Vector2(roomsRight - RoomLength + 3f, y3), new Vector2(RoomLength - 4f, 0.08f), new Color(0.65f, 0.08f, 0.12f), 3);
            float throneX = roomsRight - 2.5f;
            Quad(root, "ThroneBack", new Vector2(throneX - 0.8f, y3), new Vector2(1.6f, 3.4f), new Color(0.55f, 0.1f, 0.16f), -80, arch);
            Quad(root, "ThroneTrim", new Vector2(throneX - 0.95f, y3), new Vector2(1.9f, 3.6f), new Color(0.9f, 0.7f, 0.3f), -81, arch);
            Quad(root, "ThroneSeat", new Vector2(throneX - 1f, y3 + 0.7f), new Vector2(2f, 0.3f), new Color(0.9f, 0.7f, 0.3f), -79);
            level.Crown = new Vector2(throneX, y3 + 1.4f);
            level.CrownObject = new GameObject("Crown");
            level.CrownObject.transform.SetParent(root, false);
            Quad(level.CrownObject.transform, "Crown", level.Crown - new Vector2(0.45f, 0.3f), new Vector2(0.9f, 0.6f), new Color(1f, 0.82f, 0.3f), 20, crown);
            Glow(level.CrownObject.transform, level.Crown, 2.2f, new Color(1f, 0.8f, 0.3f, 0.5f), glowMaterial);
            level.CrownObject.SetActive(false);
            return level;
        }

        /// <summary>Pillars, arched windows, torches and banners along one floor.</summary>
        private static void Decorate(Transform root, int floor, Material glowMaterial)
        {
            float y = floor * FloorHeight;
            float inner = FloorHeight - Slab;
            // Round stone columns: fluted shaft, stepped base and capital, all shaded as cylinders.
            var pillar = new Color(0.62f, 0.58f, 0.68f);
            for (float x = Tower; x <= Tower + RoomLength * 4f + 0.1f; x += 10f)
            {
                Quad(root, "Shaft", new Vector2(x - 0.38f, y + 0.5f), new Vector2(0.76f, inner - 1.1f), pillar, -90, column);
                Quad(root, "Plinth", new Vector2(x - 0.62f, y), new Vector2(1.24f, 0.22f), pillar * 0.8f, -89, column);
                Quad(root, "Torus", new Vector2(x - 0.52f, y + 0.22f), new Vector2(1.04f, 0.28f), pillar * 0.9f, -89, column);
                Quad(root, "Echinus", new Vector2(x - 0.52f, y + inner - 0.6f), new Vector2(1.04f, 0.3f), pillar * 0.9f, -89, column);
                Quad(root, "Abacus", new Vector2(x - 0.66f, y + inner - 0.3f), new Vector2(1.32f, 0.3f), pillar * 0.8f, -89, column);
            }

            Color[] cloth = { new Color(0.6f, 0.1f, 0.14f), new Color(0.16f, 0.25f, 0.55f), new Color(0.55f, 0.1f, 0.3f) };
            for (int slot = 0; slot < 4; slot++)
            {
                float centre = Tower + slot * RoomLength + RoomLength * 0.5f;
                foreach (float side in new[] { -7.5f, 7.5f })
                {
                    // Arched window onto the starry night, with a stone sill and a cold moonlit spill.
                    float wx = centre + side;
                    Quad(root, "Window", new Vector2(wx - 0.9f, y + 2.2f), new Vector2(1.8f, 3.6f), Color.white, -88, window);
                    Quad(root, "Sill", new Vector2(wx - 1.05f, y + 2.05f), new Vector2(2.1f, 0.2f), pillar * 0.75f, -87, column);
                    Glow(root, new Vector2(wx, y + 3.8f), 3.6f, new Color(0.45f, 0.55f, 1f, 0.14f), glowMaterial);
                }

                // Wall torch between the windows: iron bracket, wooden haft, a live flame and its warm light.
                Quad(root, "Bracket", new Vector2(centre - 0.3f, y + 2.85f), new Vector2(0.6f, 0.12f), new Color(0.12f, 0.1f, 0.1f), -86);
                Quad(root, "Plate", new Vector2(centre - 0.14f, y + 2.6f), new Vector2(0.28f, 0.5f), new Color(0.16f, 0.13f, 0.13f), -87);
                Quad(root, "Haft", new Vector2(centre - 0.07f, y + 2.7f), new Vector2(0.14f, 0.9f), new Color(0.42f, 0.26f, 0.14f), -85);
                Quad(root, "Cup", new Vector2(centre - 0.16f, y + 3.5f), new Vector2(0.32f, 0.16f), new Color(0.2f, 0.16f, 0.14f), -84);
                Quad(root, "Flame", new Vector2(centre - 0.25f, y + 3.6f), new Vector2(0.5f, 0.75f), Color.white, -83, flame).AddComponent<Flicker>();
                Glow(root, new Vector2(centre, y + 3.9f), 1.4f, new Color(1f, 0.85f, 0.5f, 0.7f), glowMaterial).AddComponent<Flicker>();
                Glow(root, new Vector2(centre, y + 3.9f), 6f, new Color(1f, 0.55f, 0.2f, 0.45f), glowMaterial).AddComponent<Flicker>();

                // Banners hang from the ceiling either side of the torch.
                foreach (float side in new[] { -3.5f, 3.5f })
                {
                    Quad(root, "Banner", new Vector2(centre + side - 0.5f, y + inner - 2.6f), new Vector2(1f, 2.6f), cloth[floor], -83, banner);
                }
            }
        }

        private static GameObject Glow(Transform root, Vector2 centre, float size, Color color, Material glowMaterial)
        {
            GameObject go = Quad(root, "Glow", centre - Vector2.one * size * 0.5f, Vector2.one * size, color, -70, glow);
            if (glowMaterial != null)
            {
                go.GetComponent<SpriteRenderer>().sharedMaterial = glowMaterial;
            }

            return go;
        }

        /// <summary>A tinted, tiled sprite with its bottom-left corner at <paramref name="corner"/>.</summary>
        private static GameObject Quad(Transform root, string name, Vector2 corner, Vector2 size, Color color, int order, Sprite art = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = corner;
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = art != null ? art : square;
            sprite.drawMode = art == brick || art == block || art == null ? SpriteDrawMode.Tiled : SpriteDrawMode.Simple;
            if (sprite.drawMode == SpriteDrawMode.Tiled)
            {
                sprite.size = size;
            }
            else
            {
                Vector2 native = sprite.sprite.bounds.size;
                go.transform.localScale = new Vector3(size.x / native.x, size.y / native.y, 1f);
            }

            sprite.color = color;
            sprite.sortingOrder = order;
            return go;
        }

        private static void AddBox(GameObject go, Vector2 size)
        {
            var box = go.AddComponent<BoxCollider2D>();
            Vector2 scale = go.transform.localScale;
            box.size = new Vector2(size.x / scale.x, size.y / scale.y);
            box.offset = box.size * 0.5f;
        }

        /// <summary>Solid stone slab from x0 to x1 whose top is at <paramref name="top"/>.</summary>
        private static void Solid(Transform root, string name, float x0, float x1, float top, float depth)
        {
            var size = new Vector2(x1 - x0, depth);
            GameObject go = Quad(root, name, new Vector2(x0, top - depth), size, StoneColor, 2, block);
            AddBox(go, size);
            Quad(root, "Edge", new Vector2(x0, top - 0.08f), new Vector2(x1 - x0, 0.08f), StoneColor * 1.4f, 3);
        }

        /// <summary>Straight one-way ramp from <paramref name="from"/> up to <paramref name="to"/>, drawn as stone steps.</summary>
        private static void Ramp(Transform root, Vector2 from, Vector2 to)
        {
            // A solid flight of stone steps built against the back wall (drawn behind the fighters, so
            // walking beneath it reads as passing in front). Each tread's middle sits on the walking
            // line, so feet are never more than half a step off.
            const int steps = 20;
            Vector2 step = (to - from) / steps;
            for (int i = 0; i < steps; i++)
            {
                float x = from.x + step.x * i;
                float top = from.y + step.y * (i + 0.5f);
                Quad(root, "Step", new Vector2(Mathf.Min(x, x + step.x), from.y), new Vector2(Mathf.Abs(step.x), top - from.y), new Color(0.3f, 0.26f, 0.36f), -60, block);
                Quad(root, "Tread", new Vector2(Mathf.Min(x, x + step.x), top - 0.06f), new Vector2(Mathf.Abs(step.x), 0.06f), new Color(0.6f, 0.55f, 0.66f), -59);
            }

            var go = new GameObject("Ramp");
            go.transform.SetParent(root, false);
            Vector2 run = to - from;
            go.transform.position = (from + to) * 0.5f;
            go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(run.y, run.x) * Mathf.Rad2Deg + (run.x < 0f ? 180f : 0f));
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(run.magnitude, 0.3f);
            box.offset = new Vector2(0f, -0.15f); // top face on the line between the two points
            box.usedByEffector = true;
            var effector = go.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true;
            effector.surfaceArc = 120f;
        }

        // ================================================================ procedural art

        private static void MakeArt()
        {
            if (square != null)
            {
                return;
            }

            square = Tiled(Paint(4, 4, (x, y) => Color.white), 4f);
            brick = Tiled(Masonry(64, 32, 32, 16, 0.18f), 32f);  // 1 x 0.5 unit bricks
            block = Tiled(Masonry(64, 64, 32, 32, 0.12f), 48f);  // chunkier dressed stone
            arch = Sprite.Create(Paint(32, 64, (x, y) =>
            {
                float dx = (x + 0.5f) / 16f - 1f;
                float dy = (y + 0.5f - 48f) / 16f;
                return y < 48 || dx * dx + dy * dy <= 1f ? Color.white : Color.clear;
            }), new Rect(0, 0, 32, 64), Vector2.zero, 32f);
            bars = Sprite.Create(Paint(32, 64, (x, y) =>
            {
                bool post = x % 8 >= 2 && x % 8 <= 4;
                bool rail = y % 14 >= 6 && y % 14 <= 7 && y > 4;
                bool spike = y <= 4 && post && Mathf.Abs(x % 8 - 3) <= y * 0.5f;
                if (y <= 4) return spike ? Color.white : Color.clear;
                return post || rail ? Color.Lerp(Color.white, Color.gray, (x % 8 - 2) / 3f) : Color.clear;
            }), new Rect(0, 0, 32, 64), Vector2.zero, 32f);
            glow = Sprite.Create(Paint(64, 64, (x, y) =>
            {
                float d = new Vector2(x - 31.5f, y - 31.5f).magnitude / 32f;
                float a = Mathf.Clamp01(1f - d);
                return new Color(1f, 1f, 1f, a * a);
            }), new Rect(0, 0, 64, 64), Vector2.zero, 64f);
            banner = Sprite.Create(Paint(16, 48, (x, y) =>
            {
                if (y < 8 && Mathf.Abs(x - 7.5f) < 8 - y) return Color.clear; // swallowtail notch
                bool trim = x < 2 || x > 13 || y > 44;
                bool emblem = Mathf.Abs(x - 7.5f) + Mathf.Abs(y - 28f) < 4f;
                return trim || emblem ? new Color(1f, 0.85f, 0.45f) : Color.white;
            }), new Rect(0, 0, 16, 48), Vector2.zero, 16f);
            column = Sprite.Create(Smooth(Paint(32, 8, (x, y) =>
            {
                // Cylinder lit from the upper left, with vertical flutes.
                float u = (x + 0.5f) / 32f * 2f - 1f;
                float light = 0.55f + 0.45f * Mathf.Sqrt(1f - u * u) - 0.25f * u;
                bool flute = x % 6 == 0 && x > 2 && x < 29;
                float v = light * (flute ? 0.78f : 1f);
                return new Color(v, v, v);
            })), new Rect(0, 0, 32, 8), Vector2.zero, 32f);
            window = Sprite.Create(Smooth(Paint(64, 128, (x, y) =>
            {
                float dx = (x + 0.5f) / 32f - 1f;
                float dy = (y + 0.5f - 96f) / 32f;
                float r = y < 96 ? Mathf.Abs(dx) : Mathf.Sqrt(dx * dx + dy * dy);
                if (r > 1f) return Color.clear;
                var stone = new Color(0.36f, 0.32f, 0.4f);
                if (r > 0.78f || y < 6)
                {
                    // Frame of dressed voussoirs: alternate shades, dark joints.
                    float angle = y < 96 ? y / 14f : 7f + Mathf.Atan2(dy, dx) * 4f;
                    bool joint = Mathf.Abs(angle - Mathf.Round(angle)) < 0.06f;
                    return joint ? stone * 0.55f : stone * (Mathf.FloorToInt(angle) % 2 == 0 ? 1f : 0.85f);
                }

                // Leaded glass onto the night: a vertical mullion and a transom, then sky and stars.
                if (Mathf.Abs(x - 31.5f) < 1.5f || Mathf.Abs(y - 70f) < 1.2f) return new Color(0.1f, 0.08f, 0.12f);
                float t = y / 128f;
                Color sky = Color.Lerp(new Color(0.12f, 0.16f, 0.36f), new Color(0.04f, 0.05f, 0.14f), t);
                if (Hash(x, y) > 0.985f) sky = Color.Lerp(sky, Color.white, 0.8f);
                return sky;
            })), new Rect(0, 0, 64, 128), Vector2.zero, 32f);
            flame = Sprite.Create(Smooth(Paint(32, 48, (x, y) =>
            {
                // Teardrop: white-hot core, yellow, orange, red rim.
                float u = (x + 0.5f - 16f) / 16f;
                float v = (y + 0.5f) / 48f;
                float half = Mathf.Sin(Mathf.Clamp01(v * 1.15f) * Mathf.PI) * (1f - v * 0.65f);
                float d = Mathf.Abs(u) / Mathf.Max(half, 0.001f);
                if (d > 1f || v > 0.98f) return Color.clear;
                float heat = (1f - d) * (1f - v * 0.7f);
                Color c = heat > 0.55f ? Color.Lerp(new Color(1f, 0.85f, 0.4f), new Color(1f, 1f, 0.9f), (heat - 0.55f) / 0.45f)
                    : Color.Lerp(new Color(0.9f, 0.25f, 0.08f), new Color(1f, 0.6f, 0.15f), heat / 0.55f);
                c.a = Mathf.Clamp01((1f - d) * 3f);
                return c;
            })), new Rect(0, 0, 32, 48), Vector2.zero, 32f);
            crown = Sprite.Create(Paint(32, 24, (x, y) =>
            {
                if (y < 8) return Color.white;
                float tooth = Mathf.Abs((x % 11) - 5f);
                return y < 24 - tooth * 2.2f ? Color.white : Color.clear;
            }), new Rect(0, 0, 32, 24), Vector2.zero, 32f);
        }

        private static Texture2D Smooth(Texture2D texture)
        {
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        private static Sprite Tiled(Texture2D texture, float pixelsPerUnit)
        {
            texture.wrapMode = TextureWrapMode.Repeat;
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero, pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }

        private static Texture2D Paint(int width, int height, System.Func<int, int, Color> pixel)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = pixel(x, y);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>Running-bond masonry: light stones with a dark mortar line, each stone a slightly different shade.</summary>
        private static Texture2D Masonry(int width, int height, int stoneWidth, int stoneHeight, float variation)
        {
            return Paint(width, height, (x, y) =>
            {
                int row = y / stoneHeight;
                int shifted = x + (row % 2) * stoneWidth / 2;
                int column = shifted / stoneWidth;
                bool mortar = y % stoneHeight == 0 || shifted % stoneWidth == 0;
                if (mortar) return new Color(0.35f, 0.35f, 0.38f);
                float shade = 0.85f + (Hash(column % (width / stoneWidth + 1), row) - 0.5f) * 2f * variation;
                float grain = (Hash(x * 7 + 3, y * 13 + 1) - 0.5f) * 0.06f;
                bool lit = y % stoneHeight == stoneHeight - 1; // a highlight along each stone's top
                float v = shade + grain + (lit ? 0.08f : 0f);
                return new Color(v, v, v);
            });
        }

        private static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }
}
