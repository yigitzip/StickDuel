using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Sniper Mode's four painted maps (Resources/SniperMaps), each a background painting with
    /// colliders laid over what is painted, the way the duel arenas are built. Layouts were placed
    /// in the map editor artifact on a 2000x848 copy of each painting: every number below is a pixel
    /// of that frame (x right, y down), and the painting spans <see cref="WidthUnits"/> world units,
    /// so a fighter (about 2 units) stands as tall as a painted door. Ground row = world y 0.
    /// Solid and ledge colliders are non-trigger on the Default layer (they stop the sniper laser,
    /// the bullet and the bot's sight); death zones are triggers on Ignore Raycast. Moving parts:
    /// a shuttle train, a drawbridge, two lifts, a lighthouse beam over a dark layer.
    /// </summary>
    public static class SniperMap
    {
        public const float WidthUnits = 35f;
        public const float HalfWidth = WidthUnits * 0.5f;
        private const float FrameWidth = 2000f, FrameHeight = 848f;
        private const float FramePixelsPerUnit = FrameWidth / WidthUnits;
        /// <summary>
        /// Built-in "Water" layer, unused elsewhere: colliders on it stop sniper shots (it is in the rifle's
        /// blocker mask) but fighters walk through them. The train lives on it.
        /// </summary>
        public const int ShotOnlyLayer = 4;

        private enum Kind { Solid, Ledge, Hazard }
        private enum MoverKind { Train, Bridge, Lift, Beam }

        private readonly struct Box
        {
            public readonly Kind Kind;
            public readonly float X0, X1, Y0, Y1; // frame px, y0 = top
            public Box(Kind kind, float x0, float x1, float y0, float y1) { Kind = kind; X0 = x0; X1 = x1; Y0 = y0; Y1 = y1; }
        }

        private readonly struct Mover
        {
            public readonly MoverKind Kind;
            public readonly float X0, X1, Y0, Y1;   // rest box, frame px (beam: its pivot is the box centre)
            public readonly Vector2 Travel;         // frame px (y down)
            public readonly float Hold0, Move, Hold1, Offset;
            public Mover(MoverKind kind, float x0, float x1, float y0, float y1, Vector2 travel, float hold0, float move, float hold1, float offset = 0f)
            {
                Kind = kind; X0 = x0; X1 = x1; Y0 = y0; Y1 = y1; Travel = travel; Hold0 = hold0; Move = move; Hold1 = hold1; Offset = offset;
            }
        }

        private sealed class Def
        {
            public string Name, Blurb, Texture;
            public float Ground;
            public Box[] Boxes;
            public Vector2 SpawnA, SpawnB;
            public Vector2[] Spots;
            public Mover[] Movers;
        }

        private static Box S(float x0, float x1, float y0, float y1) => new Box(Kind.Solid, x0, x1, y0, y1);
        private static Box L(float x0, float x1, float y0, float y1) => new Box(Kind.Ledge, x0, x1, y0, y1);
        private static Box H(float x0, float x1, float y0, float y1) => new Box(Kind.Hazard, x0, x1, y0, y1);
        private static Vector2 P(float x, float y) => new Vector2(x, y);

        // Measured on 2000x848 copies of the paintings (grid overlays), edges to the pixel.
        private static readonly Def[] All =
        {
            new Def
            {
                Name = "BORDER STATION", Blurb = "A shuttle train runs the line", Texture = "Station", Ground = 720,
                Boxes = new[]
                {
                    S(0, 2000, 720, 848),                                        // platform (snow top)
                    S(385, 397, 545, 720), L(0, 410, 450, 468),                  // station house wall, roof
                    S(418, 562, 678, 720), S(442, 542, 636, 678), S(467, 518, 594, 636), // crate pyramid
                    L(600, 683, 590, 600), L(600, 683, 488, 498),                // left tower rungs (clear of a standing head)
                    L(593, 1405, 400, 414),                                      // footbridge deck
                    L(1317, 1400, 488, 498), L(1317, 1400, 590, 600),            // right tower rungs
                    S(735, 858, 655, 720),                                       // luggage cart
                    L(1430, 1555, 652, 664),                                     // bench back
                    S(1556, 1620, 686, 720), S(1562, 1608, 651, 686),            // crates by the bench
                    S(1662, 1760, 678, 720), S(1694, 1745, 636, 678),            // crates by the cabin
                    S(1762, 1775, 550, 720), L(1750, 1992, 526, 544), S(1802, 1947, 352, 480), // cabin wall, roof, water tank
                },
                SpawnA = P(250, 720), SpawnB = P(1880, 720),
                Spots = new[] { P(200, 450), P(492, 594), P(800, 400), P(1000, 400), P(1200, 400), P(680, 720), P(1490, 652), P(1720, 636), P(1778, 526) },
                Movers = new[] { new Mover(MoverKind.Train, 360, 1120, 604, 785, new Vector2(520, 0), 2.5f, 4f, 2.5f) },
            },
            new Def
            {
                Name = "HARBOR BRIDGE", Blurb = "The bridge lifts and splits the map", Texture = "Harbor", Ground = 722,
                Boxes = new[]
                {
                    S(0, 842, 722, 848), S(1157, 2000, 722, 848), H(842, 1157, 780, 848), // quays, canal
                    L(0, 398, 370, 388),                                         // warehouse roof
                    S(385, 572, 507, 615), S(385, 677, 615, 722),                // containers
                    S(1347, 1407, 662, 722),                                     // right bollard (the left one stands in front of the containers)
                    S(1529, 1720, 643, 722), S(1583, 1686, 565, 643),            // crates
                    S(1760, 1950, 406, 462), S(1735, 1975, 245, 370),            // crane base and cab (a roof over spawn B)
                },
                SpawnA = P(200, 722), SpawnB = P(1850, 722),
                Spots = new[] { P(250, 370), P(480, 507), P(640, 615), P(760, 722), P(1000, 722), P(1440, 722), P(1634, 565) },
                Movers = new[] { new Mover(MoverKind.Bridge, 842, 1157, 722, 752, Vector2.zero, 8f, 1.2f, 4f) },
            },
            new Def
            {
                Name = "LIGHTHOUSE ISLE", Blurb = "Dark. Only the beam shows you", Texture = "Lighthouse", Ground = 734,
                Boxes = new[]
                {
                    S(0, 2000, 734, 848),                                        // rock shelf
                    L(120, 310, 585, 600), S(12, 60, 640, 734), S(305, 332, 600, 734), // upturned boat: keel (jump out through it), stern, bow
                    S(352, 525, 622, 734), L(534, 757, 510, 525),                // left rock, left ledge
                    S(660, 1320, 690, 734), S(795, 1240, 595, 690),              // plateau: one step, then the top
                    L(1242, 1465, 510, 525), S(1467, 1652, 622, 734),            // right ledge, right rock
                    S(1715, 2000, 562, 734),                                     // keeper's cottage
                },
                SpawnA = P(180, 734), SpawnB = P(1690, 734),
                Spots = new[] { P(200, 585), P(440, 622), P(646, 510), P(850, 595), P(1150, 595), P(1354, 510), P(1560, 622), P(1850, 562) },
                Movers = new[] { new Mover(MoverKind.Beam, 985, 1015, 115, 145, Vector2.zero, 0f, 7f, 0f) },
            },
            new Def
            {
                Name = "SKYSCRAPER", Blurb = "Ride the lifts, mind the gaps", Texture = "Skyscraper", Ground = 745,
                Boxes = new[]
                {
                    S(0, 610, 745, 800), S(720, 1279, 745, 800), S(1382, 2000, 745, 800), // lowest girders
                    L(160, 600, 506, 540), L(797, 1202, 506, 540), L(1392, 1840, 506, 540), // middle girders
                    L(0, 137, 297, 325), L(172, 600, 295, 325), L(877, 1122, 295, 325), L(1392, 1827, 295, 325), L(1860, 2000, 295, 325), // top girders
                    S(188, 200, 590, 745), L(0, 200, 590, 602),                  // site office A: front wall, roof
                    S(1800, 1812, 590, 745), L(1800, 2000, 590, 602),            // site office B
                    S(320, 410, 700, 745), S(420, 580, 700, 745), S(1420, 1580, 700, 745), S(1590, 1680, 700, 745), // cement bags, beam stacks
                    H(0, 2000, 830, 900),                                        // the drop
                },
                SpawnA = P(100, 745), SpawnB = P(1900, 745),
                Spots = new[] { P(100, 590), P(380, 506), P(390, 295), P(1000, 745), P(1000, 506), P(1000, 295), P(1620, 506), P(1610, 295), P(1900, 590) },
                Movers = new[]
                {
                    new Mover(MoverKind.Lift, 624, 696, 608, 745, new Vector2(0, -450), 1.5f, 3.5f, 1.5f),
                    new Mover(MoverKind.Lift, 1304, 1376, 608, 745, new Vector2(0, -450), 1.5f, 3.5f, 1.5f, 0.5f),
                },
            },
        };

        public static int Count => All.Length;
        public static string Name(int index) => All[Mathf.Clamp(index, 0, All.Length - 1)].Name;
        public static string Blurb(int index) => All[Mathf.Clamp(index, 0, All.Length - 1)].Blurb;

        /// <summary>The whole painting, for a menu card. Null, with an error, if it is missing.</summary>
        public static Sprite Preview(int index)
        {
            Texture2D texture = LoadTexture(All[Mathf.Clamp(index, 0, All.Length - 1)].Texture);
            return texture != null ? Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f)) : null;
        }

        /// <summary>What <see cref="Build"/> made: the root (destroy it to clear the map) and where to stand.</summary>
        public sealed class Layout
        {
            public Transform Root;
            public string Name;
            /// <summary>Fighter positions (hip height), ready for <see cref="Fighter.ResetFighter"/>.</summary>
            public Vector2 PlayerSpawn, EnemySpawn;
            /// <summary>Standing spots for bots, as hip positions.</summary>
            public Vector2[] Spots;
            /// <summary>The painting's world rect (the camera keeps inside it).</summary>
            public float Bottom, Top;
        }

        /// <summary>Builds map <paramref name="index"/> under a new root. Destroy the root before building the next one.</summary>
        public static Layout Build(int index)
        {
            int fighters = LayerMask.NameToLayer(Fighter.FighterLayer);
            if (fighters >= 0)
            {
                Physics2D.IgnoreLayerCollision(ShotOnlyLayer, fighters, true);
            }

            Def def = All[Mathf.Clamp(index, 0, All.Length - 1)];
            var root = new GameObject("SniperMap " + def.Name).transform;
            Texture2D texture = LoadTexture(def.Texture);
            if (texture != null)
            {
                var background = new GameObject("Painting").AddComponent<SpriteRenderer>();
                background.transform.SetParent(root, false);
                float groundFromBottom = (FrameHeight - def.Ground) / FrameHeight;
                background.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, groundFromBottom), texture.width / WidthUnits);
                background.sortingOrder = -100;
            }

            foreach (Box box in def.Boxes)
            {
                AddBox(root, def, box);
            }

            // Arena bounds just outside the painting's edges.
            foreach (int side in new[] { -1, 1 })
            {
                var wall = new GameObject("Bound");
                wall.transform.SetParent(root, false);
                wall.transform.position = new Vector2(side * (HalfWidth + 0.5f), 10f);
                wall.AddComponent<BoxCollider2D>().size = new Vector2(1f, 40f);
            }

            foreach (Mover mover in def.Movers)
            {
                AddMover(root, def, mover);
            }

            var spots = new Vector2[def.Spots.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                spots[i] = Hip(def, def.Spots[i]);
            }

            return new Layout
            {
                Root = root, Name = def.Name, Spots = spots,
                PlayerSpawn = Hip(def, def.SpawnA), EnemySpawn = Hip(def, def.SpawnB),
                Bottom = WorldY(def, FrameHeight), Top = WorldY(def, 0f),
            };
        }

        private static Texture2D LoadTexture(string name)
        {
            var texture = Resources.Load<Texture2D>("SniperMaps/" + name);
            if (texture == null)
            {
                Debug.LogError($"SniperMap: Resources/SniperMaps/{name} is missing.");
            }

            return texture;
        }

        private static float WorldX(float px) => (px - FrameWidth * 0.5f) / FramePixelsPerUnit;
        private static float WorldY(Def def, float py) => (def.Ground - py) / FramePixelsPerUnit;
        private static Vector2 Hip(Def def, Vector2 feet) => new Vector2(WorldX(feet.x), WorldY(def, feet.y) + Fighter.HipHeight + 0.05f);

        private static void AddBox(Transform root, Def def, Box box)
        {
            var go = new GameObject(box.Kind.ToString()) { layer = box.Kind == Kind.Hazard ? 2 : 0 };
            go.transform.SetParent(root, false);
            go.transform.position = new Vector2((WorldX(box.X0) + WorldX(box.X1)) * 0.5f, (WorldY(def, box.Y0) + WorldY(def, box.Y1)) * 0.5f);
            var collider = go.AddComponent<BoxCollider2D>();
            collider.size = new Vector2((box.X1 - box.X0) / FramePixelsPerUnit, (box.Y1 - box.Y0) / FramePixelsPerUnit);
            if (box.Kind == Kind.Ledge)
            {
                MakeOneWay(go, collider);
            }
            else if (box.Kind == Kind.Hazard)
            {
                collider.isTrigger = true;
                go.AddComponent<SniperKillZone>();
            }
        }

        private static void MakeOneWay(GameObject go, BoxCollider2D collider)
        {
            collider.usedByEffector = true;
            var effector = go.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true;
            effector.surfaceArc = 160f;
        }

        private static void AddMover(Transform root, Def def, Mover m)
        {
            float width = (m.X1 - m.X0) / FramePixelsPerUnit, height = (m.Y1 - m.Y0) / FramePixelsPerUnit;
            Vector2 travel = new Vector2(m.Travel.x, -m.Travel.y) / FramePixelsPerUnit;
            switch (m.Kind)
            {
                case MoverKind.Train:
                {
                    // Fighters pass through the body (it never shoves or pins anyone: ShotOnlyLayer ignores
                    // them), but it stops bullets and sight like a wall. The boxcar roof is a one-way ledge to ride.
                    GameObject train = Body(root, "Train", new Vector2((WorldX(m.X0) + WorldX(m.X1)) * 0.5f, (WorldY(def, m.Y0) + WorldY(def, m.Y1)) * 0.5f));
                    train.layer = ShotOnlyLayer;
                    train.AddComponent<BoxCollider2D>().size = new Vector2(width, height * 0.92f);
                    var roof = new GameObject("Roof") { layer = 0 };
                    roof.transform.SetParent(train.transform, false);
                    var roofBox = roof.AddComponent<BoxCollider2D>();
                    roofBox.size = new Vector2(width * 0.5f, 0.2f);
                    roofBox.offset = new Vector2(width * 0.23f, height * 0.5f - height * 0.08f - 0.1f);
                    MakeOneWay(roof, roofBox);
                    Art(train.transform, "Train", new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), width, height, 1);
                    train.AddComponent<SniperMapMover>().Init(travel, 0f, m.Hold0, m.Move, m.Hold1, m.Offset);
                    break;
                }
                case MoverKind.Lift:
                {
                    // The cage is drawn over the whole box; only its floor is solid (one-way, so you can jump in from below).
                    GameObject lift = Body(root, "Lift", new Vector2((WorldX(m.X0) + WorldX(m.X1)) * 0.5f, WorldY(def, m.Y1)));
                    var floor = lift.AddComponent<BoxCollider2D>();
                    floor.size = new Vector2(width, 0.2f);
                    floor.offset = new Vector2(0f, -0.1f);
                    MakeOneWay(lift, floor);
                    Art(lift.transform, "Elevator", new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0f), width, height, 1);
                    lift.AddComponent<SniperMapMover>().Init(travel, 0f, m.Hold0, m.Move, m.Hold1, m.Offset);
                    break;
                }
                case MoverKind.Bridge:
                {
                    // Two leaves hinged on the canal edges; lifted, each stands up as a wall.
                    float half = width * 0.5f;
                    foreach (int side in new[] { -1, 1 })
                    {
                        Vector2 hinge = new Vector2(side < 0 ? WorldX(m.X0) : WorldX(m.X1), WorldY(def, m.Y0));
                        GameObject leaf = Body(root, "BridgeLeaf", hinge);
                        var deck = leaf.AddComponent<BoxCollider2D>();
                        deck.size = new Vector2(half, height);
                        deck.offset = new Vector2(-side * half * 0.5f, -height * 0.5f);
                        Art(leaf.transform, "Bridge", new Rect(side < 0 ? 0f : 0.5f, 0f, 0.5f, 1f), new Vector2(side < 0 ? 0f : 1f, 0.78f), half, 0f, 1);
                        leaf.AddComponent<SniperMapMover>().Init(Vector2.zero, -side * 90f, m.Hold0, m.Move, m.Hold1, m.Offset);
                    }

                    break;
                }
                case MoverKind.Beam:
                    SniperLighthouseBeam.Create(root, new Vector2((WorldX(m.X0) + WorldX(m.X1)) * 0.5f, (WorldY(def, m.Y0) + WorldY(def, m.Y1)) * 0.5f),
                        new Rect(-HalfWidth, WorldY(def, FrameHeight), WidthUnits, (FrameHeight) / FramePixelsPerUnit), m.Move);
                    break;
            }
        }

        /// <summary>A kinematic body (moving parts push fighters, carry them and stop shots) on the Default layer.</summary>
        private static GameObject Body(Transform root, string name, Vector2 position)
        {
            var go = new GameObject(name) { layer = 0 };
            go.transform.SetParent(root, false);
            go.transform.position = position;
            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            return go;
        }

        /// <summary>
        /// Part of Resources/SniperMaps/<paramref name="texture"/> (<paramref name="uv"/>, 0-1) drawn
        /// <paramref name="width"/> wide (and <paramref name="height"/> tall, or in proportion when 0).
        /// </summary>
        private static void Art(Transform parent, string texture, Rect uv, Vector2 pivot, float width, float height, int order)
        {
            Texture2D image = LoadTexture(texture);
            if (image == null)
            {
                return;
            }

            var rect = new Rect(uv.x * image.width, uv.y * image.height, uv.width * image.width, uv.height * image.height);
            var sprite = new GameObject("Art").AddComponent<SpriteRenderer>();
            sprite.transform.SetParent(parent, false);
            sprite.sprite = Sprite.Create(image, rect, pivot, rect.width / width);
            if (height > 0f)
            {
                sprite.transform.localScale = new Vector3(1f, height / (rect.height / (rect.width / width)), 1f);
            }

            sprite.sortingOrder = order;
        }
    }
}
