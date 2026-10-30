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
        private static Sprite square, brick, block, arch, bars, glow, crown, sill, column, window, flame, torch;
        private static readonly Sprite[] banners = new Sprite[3];

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
            // Painted gothic stone columns, floor to ceiling, at the art's own proportions.
            var pillar = new Color(0.62f, 0.58f, 0.68f); // window sills
            float pillarWidth = inner * column.rect.width / column.rect.height;
            for (float x = Tower; x <= Tower + RoomLength * 4f + 0.1f; x += 10f)
            {
                Quad(root, "Pillar", new Vector2(x - pillarWidth * 0.5f, y), new Vector2(pillarWidth, inner), Color.white, -90, column);
            }

            for (int slot = 0; slot < 4; slot++)
            {
                float centre = Tower + slot * RoomLength + RoomLength * 0.5f;
                foreach (float side in new[] { -7.5f, 7.5f })
                {
                    // Arched window onto the starry night, with a stone sill and a cold moonlit spill.
                    float wx = centre + side;
                    Quad(root, "Window", new Vector2(wx - 0.9f, y + 2.2f), new Vector2(1.8f, 3.6f), Color.white, -88, window);
                    Quad(root, "Sill", new Vector2(wx - 1.05f, y + 2.05f), new Vector2(2.1f, 0.2f), pillar * 0.75f, -87, sill);
                    Glow(root, new Vector2(wx, y + 3.8f), 3.6f, new Color(0.45f, 0.55f, 1f, 0.14f), glowMaterial);

                    // Snow falling past the glass, hidden outside the arch (straight sides, round top).
                    var arch = new Vector2(wx, y + 4.9f);
                    System.Func<Vector2, bool> glass = p => p.y > y + 2.4f &&
                        (p.y <= arch.y ? Mathf.Abs(p.x - wx) < 0.66f : (p - arch).magnitude < 0.66f);
                    for (int i = 0; i < 30; i++)
                    {
                        float size = 0.035f + Hash(i, slot) * 0.03f;
                        var flake = Quad(root, "Snow", new Vector2(wx, y + 3f), Vector2.one * size, new Color(1f, 1f, 1f, 0.85f), -87).AddComponent<Drift>();
                        flake.Velocity = new Vector2(0f, -0.3f - Hash(slot, i) * 0.3f);
                        flake.Sway = 0.06f;
                        flake.Box = new Rect(wx - 0.7f, y + 2.3f, 1.4f, 3.3f);
                        flake.Visible = glass;
                    }
                }

                // Wall torch between the windows: the painted torch, a live flame, drifting embers and its warm light.
                Quad(root, "Torch", new Vector2(centre - 0.34f, y + 2.05f), new Vector2(0.68f, 1.65f), Color.white, -85, torch);
                Quad(root, "Flame", new Vector2(centre - 0.3f, y + 3.58f), new Vector2(0.6f, 0.85f), Color.white, -83, flame).AddComponent<Flicker>();
                for (int i = 0; i < 9; i++)
                {
                    // Embers rise off the flame, sway and burn out.
                    var ember = Quad(root, "Ember", new Vector2(centre, y + 4.2f), Vector2.one * 0.05f, new Color(1f, 0.6f, 0.2f), -82).AddComponent<Drift>();
                    ember.Velocity = new Vector2(0f, 0.35f + Hash(slot * 9 + i, floor) * 0.35f);
                    ember.Sway = 0.08f;
                    ember.Box = new Rect(centre - 0.15f, y + 4.1f, 0.3f, 1.1f);
                    ember.FadeOut = true;
                }

                Glow(root, new Vector2(centre, y + 4f), 1.6f, new Color(1f, 0.85f, 0.5f, 0.7f), glowMaterial).AddComponent<Flicker>();
                Glow(root, new Vector2(centre, y + 4f), 6f, new Color(1f, 0.55f, 0.2f, 0.45f), glowMaterial).AddComponent<Flicker>();

                // Banners hang on rods, corded to a nail under the ceiling, either side of the torch.
                foreach (float side in new[] { -3.85f, 3.85f })
                {
                    Quad(root, "Banner", new Vector2(centre + side - 0.7f, y + inner - 3.46f), new Vector2(1.4f, 3.36f), Color.white, -83, banners[floor]);
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
            // One cloth per floor: courtyard red, rampart blue, palace crimson.
            Color[] cloth = { new Color(0.62f, 0.1f, 0.15f), new Color(0.16f, 0.25f, 0.58f), new Color(0.56f, 0.1f, 0.32f) };
            for (int f = 0; f < 3; f++)
            {
                banners[f] = Banner(cloth[f]);
            }

            column = PaintedSprite("Castle/Pillar");
            torch = PaintedSprite("Castle/Torch", FilterMode.Point); // painted, flameless; the fire is drawn below
            sill = Sprite.Create(Smooth(Paint(32, 8, (x, y) =>
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

                // Leaded glass onto the night: a vertical mullion and a transom, then dark sky (the snow falls in front).
                if (Mathf.Abs(x - 31.5f) < 1.5f || Mathf.Abs(y - 70f) < 1.2f) return new Color(0.1f, 0.08f, 0.12f);
                float t = y / 128f;
                Color sky = Color.Lerp(new Color(0.12f, 0.16f, 0.36f), new Color(0.04f, 0.05f, 0.14f), t);
                return sky;
            })), new Rect(0, 0, 64, 128), Vector2.zero, 32f);
            flame = Sprite.Create(Paint(48, 60, FlamePixel), new Rect(0, 0, 48, 60), Vector2.zero, 60f); // same pixel size as the torch art
            crown = Sprite.Create(Paint(32, 24, (x, y) =>
            {
                if (y < 8) return Color.white;
                float tooth = Mathf.Abs((x % 11) - 5f);
                return y < 24 - tooth * 2.2f ? Color.white : Color.clear;
            }), new Rect(0, 0, 32, 24), Vector2.zero, 32f);
        }

        /// <summary>Hanging banner: crenellated top on a wooden rod, corded to a nail, gold inner trim, pointed tail.</summary>
        private static Sprite Banner(Color cloth)
        {
            var gold = new Color(0.95f, 0.72f, 0.25f);
            var wood = new Color(0.36f, 0.22f, 0.14f);
            var iron = new Color(0.32f, 0.32f, 0.36f);
            return Sprite.Create(Paint(40, 96, (x, y) => Opaque(BannerPixel(x, y))), new Rect(0, 0, 40, 96), Vector2.zero, 32f);

            Color BannerPixel(int x, int y)
            {
                // Nail and the cord running down to both rod ends.
                if (new Vector2(x - 19.5f, y - 92.5f).sqrMagnitude < 4f) return iron;
                float t = (92f - y) / 14f;
                if (t >= 0f && t <= 1f && Mathf.Abs(Mathf.Abs(x - 19.5f) - t * 16.5f) < 0.7f) return iron * 0.8f;

                // Rod with round knobs, sitting on the cloth's battlements.
                if (y >= 74 && y <= 77 && x >= 2 && x <= 37) return wood * (y == 77 ? 1.3f : 1f);
                if (new Vector2(Mathf.Min(x - 2f, 37f - x), y - 75.5f).sqrMagnitude < 5f) return wood * 1.2f;

                float tail = 22f * (1f - Mathf.Abs(x - 19.5f) / 14f); // bottom edge, a V down to a point
                bool merlon = y < 74 || (y <= 80 && (x - 6) / 4 % 2 == 0);
                if (x < 6 || x > 33 || y < tail || !merlon) return Color.clear;

                float d = Mathf.Min(Mathf.Min(x - 6f, 33f - x), Mathf.Min((y - tail) * 0.54f, 73f - y));
                if (d < 1f && y < 74) return cloth * 0.55f;
                if (d >= 2.5f && d < 3.6f) return gold;
                float fold = 0.88f + 0.12f * Mathf.Sin((x - 6f) * 0.45f) - (x < 11 ? 0.12f : 0f);
                return cloth * fold;
            }
        }

        /// <summary>Pixel-art fire: a wide base and three licking tongues, banded red, orange, yellow, pale core.</summary>
        private static Color FlamePixel(int x, int y)
        {
            float v = (y + 0.5f) / 60f;
            float u = (x + 0.5f - 24f) / 24f - Mathf.Sin(v * 5f) * 0.08f * v;
            float height = Mathf.Max(Mathf.Max(
                Tongue(u, 0.02f, 0.32f, 0.98f), Tongue(u, -0.36f, 0.2f, 0.8f)),
                Mathf.Max(Tongue(u, 0.4f, 0.18f, 0.68f), 0.55f * Mathf.Clamp01((0.75f - Mathf.Abs(u)) * 5f)));
            if (v > height || Mathf.Abs(u) > 0.45f + v * 1.6f) return Color.clear; // rounded base
            float heat = (1f - v / height) * 0.65f + (1f - Mathf.Abs(u) / 0.9f) * 0.45f;
            if (heat > 0.95f) return new Color(1f, 0.95f, 0.7f);
            if (heat > 0.6f) return new Color(1f, 0.8f, 0.25f);
            if (heat > 0.35f) return new Color(1f, 0.5f, 0.1f);
            return new Color(0.85f, 0.2f, 0.05f);
        }

        private static float Tongue(float u, float centre, float width, float height)
        {
            float d = (u - centre) / width;
            return height * Mathf.Exp(-d * d);
        }

        /// <summary>Shading multiplies alpha too; painted art is either fully there or clear.</summary>
        private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, c.a > 0f ? 1f : 0f);

        /// <summary>A painted Resources texture as a sprite with its pivot at the bottom left (Quad scales it).</summary>
        private static Sprite PaintedSprite(string path, FilterMode filter = FilterMode.Bilinear)
        {
            var texture = Resources.Load<Texture2D>(path);
            texture.filterMode = filter;
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero, 60f);
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
