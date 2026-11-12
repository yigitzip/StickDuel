using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StickDuel
{
    /// <summary>
    /// Main menu (weapon + arena select) and the arenas. Each arena is one painted background
    /// (Resources/Arena, made by Tools/prepare_backgrounds.py from Tools/reference) with
    /// colliders laid over what is painted: floors, ledges, a hazard pit, vines to climb and swing on, missiles flying across. The scene's own
    /// environment is hidden. The match is held back until FIGHT; Esc in game reloads the
    /// scene and comes back here. Creates itself on scene load, so the scene file needs no changes.
    /// </summary>
    public sealed class Arenas : MonoBehaviour
    {
        // Painting layout: 1600x893 inside padding (see Tools/prepare_backgrounds.py).
        private const float PaintingWidth = 1600f;
        private const float PadTop = 320f, PadBottom = 160f, PadSide = 200f;
        private const float PixelsPerUnit = 92f; // fighters stand about as tall as the painted lanterns
        private const float FullResPixelsPerUnit = PixelsPerUnit * 2752f / 1600f; // Arena/Ivy and Arena/Missile keep their paintings' full resolution
        private const float ClimbWidth = 0.9f;   // how far either side of a vine it can be taken hold of
        private const float HalfWidth = PaintingWidth * 0.5f / PixelsPerUnit;
        private const float FirstIcicle = 3f;      // seconds into each round
        private const float IcicleInterval = 5f;   // then every this many seconds
        private const float IcicleHeight = 9f; // just above the top of the view

        // Train arena (Tools/prepare_backgrounds.py train()). Its numbers are pixels of the 10100x8007
        // Tools/reference/train_guide.png canvas, which Arena/Train and Arena/TrainGuide are cut from.
        private const float TrainPixelsPerUnit = 140f;                  // the sheet shrunk so a boxcar stands about one and a half fighters tall
        private const float TrainCropLeft = 770f, TrainCropTop = 470f;  // TRAIN_CROP's corner
        private const float TrainTextureScale = 100f / 140f;            // TRAIN_SCALE: the textures are stored smaller than the sheet, to fit 4096 px
        private const float TrainCentre = 3492f, TrainRoof = 629f;      // world x = 0 and y = 0: the train's middle, the boxcar roofs
        private const float TrainLeft = 800f, TrainRight = 6150f;       // the cowcatcher and the flatcar's end: the camera and the bot stay between
        private const float TrainWheels = 1024f;                        // the wheels' lowest row
        private const float TrainDeckBottom = 940f;                     // the stand-on boxes reach down to here, under the decks
        // The two scrolling layers (2752x1536 paintings at full size): wheels on BackdropWheelRow, and the camera never
        // goes lower than BackdropFloor above their ragged bottom edge or higher than the far layer's top.
        private const float BackdropPixelsPerUnit = 100f, BackdropHeight = 1536f, BackdropWheelRow = 1450f, BackdropFloor = 40f;
        private const float FarSpeed = 0.8f, NearSpeed = 7f;            // how fast the layers slide right (units a second)
        private const float FarFollow = 0.9f, NearFollow = 0.6f;        // how much they follow the camera sideways (parallax)

        /// <summary>Red lines of train_guide.png as (x0, x1, top): roofs, the low gaps between the cars, the flatcar's deck and cargo.</summary>
        private static readonly Vector3[] TrainFloors =
        {
            new Vector3(1048f, 1148f, 492f),                                                             // chimney
            new Vector3(1135f, 1232f, 639f), new Vector3(1228f, 1306f, 566f), new Vector3(1302f, 1398f, 645f), // boiler, sand dome, boiler
            new Vector3(1396f, 1484f, 549f), new Vector3(1480f, 1574f, 639f),                            // steam dome, boiler
            new Vector3(1572f, 1900f, 579f),                                                             // cab roof
            new Vector3(1896f, 2009f, 862f),                                                             // gap behind the cab
            new Vector3(2005f, 2580f, 676f),                                                             // tender
            new Vector3(2575f, 2672f, 794f),                                                             // gap
            new Vector3(2668f, 3752f, 629f),                                                             // boxcar
            new Vector3(3748f, 3848f, 778f),                                                             // gap
            new Vector3(3844f, 4916f, 629f),                                                             // boxcar
            new Vector3(4912f, 4990f, 812f), new Vector3(4986f, 5140f, 851f),                            // step down, gap
            new Vector3(5136f, 5248f, 765f), new Vector3(5244f, 5392f, 686f), new Vector3(5388f, 5502f, 766f), // crates
            new Vector3(5498f, 5728f, 853f),                                                             // flatcar deck
            new Vector3(5724f, 5782f, 786f), new Vector3(5778f, 5994f, 734f),                            // small crate, barrels
            new Vector3(5990f, 6140f, 859f),                                                             // flatcar deck
        };

        /// <summary>The cowcatcher's slope up to the smokebox, the guide's one slanted line.</summary>
        private static readonly Vector2[] TrainSlope = { new Vector2(810f, 965f), new Vector2(967f, 583f), new Vector2(1060f, 585f) };

        /// <summary>
        /// One arena, measured in painting pixels (x right, y down from the top). GroundRow is the
        /// main floor and becomes world y = 0, where both fighters spawn.
        /// </summary>
        private sealed class Arena
        {
            public string Name;
            public string Blurb;
            public string Texture;
            public float GroundRow;
            public Vector3[] Floors = new Vector3[0];  // solid (x0, x1, top)
            public Vector3[] Ledges = new Vector3[0];  // one-way (x0, x1, top): jump up through, land on top
            public Vector3[] Climbs = new Vector3[0];  // vines to climb (x, top row, bottom row); see ClimbZone
            public Vector2 Ivy;                        // the swinging ivy's anchor; none when zero (see SwingingIvy)
            public Vector2 Spawns;                     // painting columns the player and the enemy start on; zero keeps the scene's (±3 units)
            public bool Laser;                         // a laser beam comes down on the floor now and then (see FallingLaser)
            public Vector2[] Missiles = new Vector2[0]; // missiles fly in across the arena from these painting spots, one at a time (see MissileHazard)
            public Vector2[] Glows = new Vector2[0];
            public Color GlowColor;
            public float GlowSize = 2.5f;
            public Color Motes;                        // ambient particles
            public Vector2 MotesVelocity;
            public float MotesRow = 400f;
            public float MotesRate = 10f;
            public float MotesLifetime = 6f;
            public bool Icicles;                       // stalactites drop from the ceiling
            public float[] Pieces = new float[0];      // volcano: centre x of each sinking floor piece
            /// <summary>Endless scroller: Texture repeats sideways forever, with Texture + "_road" as its ground layer.</summary>
            public bool Endless;
            /// <summary>Painting width in pixels (the endless battlefield is wider than the 1600 px arenas).</summary>
            public float Width = PaintingWidth;
            /// <summary>Endless: the painting columns the background tile spans (its mirror seams; keep them clear of tents and the like).</summary>
            public float TileLeft, TileRight = PaintingWidth;
            /// <summary>Endless: painting row where the road layer starts (Tools/prepare_backgrounds.py ROAD_CUT).</summary>
            public float RoadCut;
            /// <summary>Endless: the background is drawn this much smaller (pinned at the road's edge) so it looks further away.</summary>
            public float BackgroundScale = 1f;
            /// <summary>Fought on the train (see BuildTrain): Texture is only the menu picture, Spawns are train_guide.png columns.</summary>
            public bool Train;
        }

        /// <summary>How far the endless battlefield's floor reaches either way (hours of walking).</summary>
        public const float EndlessReach = 100000f;

        private static readonly Arena[] All =
        {
            new Arena
            {
                Name = "SUNSET SHRINE", Blurb = "Open ground, classic duel", Texture = "Bg_shrine", GroundRow = 712f,
                Floors = new[] { new Vector3(0f, 1600f, 712f) },
                Glows = new[] { new Vector2(148f, 572f), new Vector2(1452f, 572f) },
                GlowColor = new Color(1f, 0.65f, 0.3f, 0.55f), GlowSize = 2.2f,
                Motes = new Color(1f, 0.85f, 0.45f, 0.9f), MotesVelocity = new Vector2(0.1f, 0.12f), MotesRow = 520f, MotesRate = 5f, MotesLifetime = 9f,
            },
            new Arena
            {
                Name = "FLOATING ISLES", Blurb = "Islands over the void, falling is death", Texture = "Bg_isles", GroundRow = 695f,
                Floors = new[] { new Vector3(360f, 1245f, 695f), new Vector3(0f, 80f, 610f), new Vector3(1520f, 1600f, 610f) },
                Ledges = new[] { new Vector3(118f, 328f, 485f), new Vector3(1272f, 1482f, 485f) },
                Motes = new Color(1f, 0.85f, 1f, 0.9f), MotesVelocity = new Vector2(0f, 0.35f), MotesRow = 780f, MotesRate = 22f,
            },
            new Arena
            {
                Name = "DRAGON VOLCANO", Blurb = "Floor pieces sink into the lava", Texture = "Bg_volcano", GroundRow = 712f,
                // The painted floor is replaced by a lava sea (Tools/prepare_backgrounds.py) with pieces on it.
                // The painted ledges are painted out too: the whole fight is on the pieces.
                Pieces = new[] { 147f, 473f, 800f, 1127f, 1453f },
                Glows = new[] { new Vector2(300f, 840f), new Vector2(800f, 840f), new Vector2(1300f, 840f), new Vector2(800f, 215f) },
                GlowColor = new Color(1f, 0.45f, 0.1f, 0.45f), GlowSize = 6f,
                Motes = new Color(1f, 0.6f, 0.2f, 0.95f), MotesVelocity = new Vector2(0.1f, 1.3f), MotesRow = 830f, MotesRate = 30f, MotesLifetime = 4f,
            },
            new Arena
            {
                Name = "FROZEN CAVERN", Blurb = "Icicles crash down from the ceiling", Texture = "Bg_ice", GroundRow = 686f,
                Floors = new[] { new Vector3(0f, 1600f, 686f), new Vector3(0f, 75f, 605f), new Vector3(1525f, 1600f, 605f) },
                Icicles = true,
                Motes = new Color(0.9f, 0.97f, 1f, 0.9f), MotesVelocity = new Vector2(-0.25f, -0.7f), MotesRow = -120f, MotesRate = 35f, MotesLifetime = 14f,
            },
            new Arena
            {
                // Measured off Tools/reference/forest_guide.png. The side logs reach 32 px past their painted
                // ends so the ivy's full swing lands squarely on them; their vines hang under them.
                Name = "MISTY FOREST", Blurb = "Climb the vines, swing the ivy", Texture = "Bg_forest", GroundRow = 736f,
                Floors = new[] { new Vector3(409f, 1217f, 736f) },
                Ledges = new[] { new Vector3(19f, 334f, 484f), new Vector3(1277f, 1598f, 480f) },
                Climbs = new[] { new Vector3(208f, 470f, 881f), new Vector3(1404f, 466f, 879f) },
                Ivy = new Vector2(801f, 21f),
                Motes = new Color(0.75f, 1f, 0.6f, 0.8f), MotesVelocity = new Vector2(0.05f, 0.25f), MotesRow = 520f, MotesRate = 12f, MotesLifetime = 8f,
            },
            new Arena
            {
                // Measured off Tools/reference/space_guide.png: three lower decks over a drop, five
                // one-way walkways above, a truss ladder up the left edge. Fighters start a deck apart.
                Name = "ORBITAL DOCK", Blurb = "Decks over the drop, a ladder up the left", Texture = "Bg_space", GroundRow = 558f,
                Floors = new[] { new Vector3(69f, 430f, 558f), new Vector3(1192f, 1541f, 558f), new Vector3(550f, 1058f, 583f) },
                Ledges = new[] { new Vector3(43f, 217f, 289f), new Vector3(281f, 485f, 250f), new Vector3(559f, 1072f, 218f), new Vector3(1165f, 1364f, 241f), new Vector3(1430f, 1599f, 288f) },
                Climbs = new[] { new Vector3(64f, 275f, 572f) },
                Spawns = new Vector2(380f, 1240f),
                Motes = new Color(0.8f, 0.9f, 1f, 0.7f), MotesVelocity = new Vector2(-0.08f, 0.06f), MotesRow = 400f, MotesRate = 8f, MotesLifetime = 10f,
            },
            new Arena
            {
                // Measured off Tools/reference/neon_guide.png: one flat deck across the whole painting.
                Name = "NEON DISTRICT", Blurb = "A laser rakes the deck", Texture = "Bg_neon", GroundRow = 580f,
                Floors = new[] { new Vector3(0f, 1600f, 580f) },
                Laser = true,
                Motes = new Color(1f, 0.5f, 0.9f, 0.8f), MotesVelocity = new Vector2(0.05f, 0.2f), MotesRow = 450f, MotesRate = 10f, MotesLifetime = 7f,
            },
            new Arena
            {
                // Measured off Tools/reference/deserted_guide.png: stepped ground over the wrecks either
                // side, a scaffold in the middle (solid lower deck, one-way upper deck, a ladder between),
                // and missiles coming in at three heights from either edge. Fighters start on the wrecks' roofs.
                Name = "DESERTED", Blurb = "Missiles rake the base", Texture = "Bg_deserted", GroundRow = 575f,
                Floors = new[]
                {
                    new Vector3(0f, 126f, 642f), new Vector3(114f, 344f, 570f), new Vector3(328f, 401f, 586f), new Vector3(374f, 535f, 632f),
                    new Vector3(535f, 958f, 543f),
                    new Vector3(958f, 1134f, 609f), new Vector3(1134f, 1256f, 577f), new Vector3(1256f, 1377f, 615f), new Vector3(1372f, 1460f, 653f), new Vector3(1456f, 1600f, 691f),
                },
                Ledges = new[] { new Vector3(697f, 944f, 378f) },
                Climbs = new[] { new Vector3(819f, 364f, 543f) },
                Spawns = new Vector2(229f, 1195f),
                Missiles = new[] { new Vector2(19f, 338f), new Vector2(19f, 521f), new Vector2(19f, 618f), new Vector2(1578f, 331f), new Vector2(1578f, 499f), new Vector2(1578f, 643f) },
                Motes = new Color(0.85f, 0.7f, 0.45f, 0.7f), MotesVelocity = new Vector2(0.4f, 0.05f), MotesRow = 600f, MotesRate = 10f, MotesLifetime = 8f,
            },
            new Arena
            {
                // On the roofs of a train running left through the desert: the ground streams past behind it.
                // Fighters start on the two boxcars.
                Name = "DESERT TRAIN", Blurb = "Fight on the roofs of a running train", Texture = "TrainPreview", Train = true,
                Spawns = new Vector2(3210f, 4380f),
                Motes = new Color(0.85f, 0.7f, 0.5f, 0.6f), MotesVelocity = new Vector2(5f, 0.1f), MotesRate = 25f, MotesLifetime = 6f,
            },
        };

        /// <summary>Endless Survival's ground: a torched war camp, not offered as a duel arena.</summary>
        private static readonly Arena Battlefield = new Arena
        {
            Name = "BATTLEFIELD", Blurb = "Hold the line", Texture = "Bg_battlefield", GroundRow = 718f, Endless = true,
            Width = 2105f, TileLeft = 179f, TileRight = 1668f, // ultrawide; the seams sit clear of the tents
            RoadCut = 670f, BackgroundScale = 0.8f,
        };

        // Kept across Esc reloads so the menu remembers the last choice.
        private static readonly PlayerLoadout loadout = new PlayerLoadout();
        private static int selectedMap;
        private static int selectedSniperMap;

        private DuelManager duel;
        private GameObject environment;
        private Camera cam;
        private Material glowMaterial;
        private GameObject menu;
        private Sprite icicleSprite;
        private Sprite shadowSprite;
        private readonly List<Outline> weaponOutlines = new List<Outline>();
        private readonly List<Outline> mapOutlines = new List<Outline>();
        private readonly List<Outline> sniperMapOutlines = new List<Outline>();
        private readonly List<GameObject> arenaCards = new List<GameObject>();
        private readonly List<GameObject> sniperMapCards = new List<GameObject>();
        private Text mapRowTitle;
        private readonly List<Outline> modeOutlines = new List<Outline>();
        private enum Mode { Duel, StormTheCastle, Endless, Sniper }

        private Mode mode; // per scene load: Esc always comes back to Duel

        public static Arenas Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // Runs after the scene's Awake/OnEnable but before any Start, so the match can be held back.
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var duel = FindAnyObjectByType<DuelManager>();
            GameObject environment = GameObject.Find("Environment");
            if (duel == null || environment == null)
            {
                return;
            }

            duel.enabled = false;
            Instance = new GameObject("Arenas").AddComponent<Arenas>();
            Instance.duel = duel;
            Instance.environment = environment;
        }

        private void Start()
        {
            cam = Camera.main;
            foreach (SpriteRenderer sprite in environment.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sprite.name == "LanternGlow")
                {
                    glowMaterial = sprite.sharedMaterial;
                    break;
                }
            }

            // The saved scene still has the old controls hint (Space used to jump).
            foreach (Text text in FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                text.text = text.text.Replace("W / SPACE  jump    Q / E  roll", "W  jump    SPACE  kick    Q / E  roll    ESC  menu")
                    .Replace("HOLD SHIFT  thrust", "SHIFT  thrust / chop    F  special");
            }

            SetControls(false);
            BuildMenu();
            Cursor.visible = true; // the menu needs it; Fight hides it again
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (menu == null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                }

                return;
            }

            if (keyboard.enterKey.wasPressedThisFrame)
            {
                Fight();
            }
        }

        private int lastRound;

        private void LateUpdate()
        {
            // Every round starts on level floor pieces.
            if (duel.enabled && duel.Round != lastRound)
            {
                lastRound = duel.Round;
                foreach (SinkingPlatform piece in SinkingPlatform.All)
                {
                    piece.ResetHeight();
                }
            }
        }

        /// <summary>Skips the menu (used by the editor test bridge). A locked weapon logs and keeps the last pick.</summary>
        public void StartWith(WeaponType weapon, int map)
        {
            loadout.TrySelect(weapon);
            selectedMap = Mathf.Clamp(map, 0, All.Length - 1);
            Fight();
        }

        /// <summary>Skips the menu straight into Storm The Castle (used by the editor test bridge).</summary>
        public void StartSurvival()
        {
            SelectMode(Mode.StormTheCastle);
            Fight();
        }

        /// <summary>Skips the menu straight into Endless Survival (used by the editor test bridge).</summary>
        public void StartEndless()
        {
            SelectMode(Mode.Endless);
            Fight();
        }

        private void Fight()
        {
            if (menu == null)
            {
                return;
            }

            Destroy(menu);
            menu = null;
            Cursor.visible = false; // aim is the weapon's job (the sniper laser, the aim line); Esc reloads into the menu
            if (mode == Mode.Sniper)
            {
                StartSniper(); // its own map, loadout, bot and flow: none of the steps below apply
                return;
            }

            bool castleRun = mode == Mode.StormTheCastle;
            CastleLevel castle = castleRun ? BuildCastle() : null;
            Arena arena = castleRun ? null : mode == Mode.Endless ? Battlefield : All[selectedMap];
            if (arena != null)
            {
                Build(arena);
                if (arena.Train)
                {
                    duel.SetSpawns(TrainWorld(arena.Spawns.x, TrainRoof).x, TrainWorld(arena.Spawns.y, TrainRoof).x);
                }
                else if (arena.Spawns != Vector2.zero)
                {
                    duel.SetSpawns(World(arena.Spawns.x, 0f, arena).x, World(arena.Spawns.y, 0f, arena).x);
                }
            }

            Fighter player = duel.Player;
            Fighter enemy = duel.Enemy;
            if (player.sword != null) WeaponFactory.Prepare(loadout.Weapon, player)?.Equip(player); // the menu's weapon in every mode, the castle too
            if (enemy.sword != null) WeaponFactory.Prepare(WeaponType.Sword, enemy)?.Equip(enemy);
            foreach (AIDriver ai in FindObjectsByType<AIDriver>(FindObjectsSortMode.None))
            {
                if (arena != null && arena.Train) ai.SetBounds(TrainWorld(TrainLeft, 0f).x, TrainWorld(TrainRight, 0f).x);
                else ai.ArenaHalfWidth = HalfWidth;
            }

            if (castleRun && duel.GetComponent<StormTheCastleMode>() == null)
            {
                duel.gameObject.AddComponent<StormTheCastleMode>().Init(duel, castle);
            }
            else if (mode == Mode.Endless && duel.GetComponent<EndlessSurvivalMode>() == null)
            {
                duel.gameObject.AddComponent<EndlessSurvivalMode>().Init(duel, EndlessReach);
            }

            duel.enabled = true; // its Start kicks off the match (or just the HUD, in a run mode)
        }

        /// <summary>Sniper Mode: the graybox map in place of a painted arena, then the mode takes over (weapons, bot, round).</summary>
        private void StartSniper()
        {
            environment.SetActive(false);
            HideSceneSky();
            if (duel.GetComponent<SniperMode>() == null)
            {
                duel.gameObject.AddComponent<SniperMode>().Init(duel, selectedSniperMap);
            }

            duel.enabled = true; // its Start builds the HUD; SniperMode runs the round
        }

        /// <summary>Skips the menu straight into Sniper Mode (for an editor test bridge).</summary>
        public void StartSniperMode()
        {
            SelectMode(Mode.Sniper);
            Fight();
        }

        private static void SetControls(bool enabled)
        {
            foreach (Fighter fighter in FindObjectsByType<Fighter>(FindObjectsSortMode.None))
            {
                fighter.ControlsEnabled = enabled;
            }
        }

        // ================================================================ arenas

        private static Vector2 World(float px, float py, Arena arena) => new Vector2((px - PaintingWidth * 0.5f) / PixelsPerUnit, (arena.GroundRow - py) / PixelsPerUnit);

        private void Build(Arena arena)
        {
            environment.SetActive(false);
            var root = new GameObject(arena.Name).transform;
            if (arena.Endless)
            {
                BuildEndless(root, arena);
                return;
            }

            if (arena.Train)
            {
                BuildTrain(root, arena);
                return;
            }

            var texture = Resources.Load<Texture2D>("Arena/" + arena.Texture);
            var background = new GameObject("Background").AddComponent<SpriteRenderer>();
            background.transform.SetParent(root, false);
            float groundFromBottom = texture.height - PadTop - arena.GroundRow;
            background.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, groundFromBottom / texture.height), PixelsPerUnit);
            background.sortingOrder = -100;

            if (cam != null)
            {
                HideSceneSky();
                // Never show past the painting's padding, sideways or by zooming out.
                float halfView = HalfWidth + PadSide * 0.5f / PixelsPerUnit;
                cam.GetComponent<CameraRig>().SetBounds(halfView, halfView / Mathf.Max(cam.aspect, 0.5f));
            }

            foreach (Vector3 floor in arena.Floors)
            {
                Box(root, "Floor", World(floor.x, floor.z, arena), World(floor.y, floor.z, arena).x, 8f);
            }

            foreach (Vector3 ledge in arena.Ledges)
            {
                GameObject go = Box(root, "Ledge", World(ledge.x, ledge.z, arena), World(ledge.y, ledge.z, arena).x, 0.3f);
                go.GetComponent<BoxCollider2D>().usedByEffector = true;
                var effector = go.AddComponent<PlatformEffector2D>();
                effector.useOneWay = true;
                effector.surfaceArc = 160f;
            }

            foreach (Vector3 climb in arena.Climbs)
            {
                var go = new GameObject("Vine") { layer = 2 }; // Ignore Raycast: aim lines and AI ground checks see through it
                go.transform.SetParent(root, false);
                Vector2 top = World(climb.x, climb.y, arena), bottom = World(climb.x, climb.z, arena);
                go.transform.position = (top + bottom) * 0.5f;
                var box = go.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                box.size = new Vector2(ClimbWidth, top.y - bottom.y);
                go.AddComponent<ClimbZone>();
            }

            if (arena.Ivy != Vector2.zero)
            {
                SwingingIvy.Create(root, World(arena.Ivy.x, arena.Ivy.y, arena), FullResPixelsPerUnit);
            }

            // Invisible walls at the painting's edges.
            foreach (int side in new[] { -1, 1 })
            {
                var wall = new GameObject("Wall");
                wall.transform.SetParent(root, false);
                wall.transform.position = new Vector2(side * (HalfWidth + 0.5f), 5f);
                wall.AddComponent<BoxCollider2D>().size = new Vector2(1f, 30f);
            }

            foreach (Vector2 glow in arena.Glows)
            {
                CreateGlow(root, World(glow.x, glow.y, arena), Vector3.one * arena.GlowSize, arena.GlowColor, -90).gameObject.AddComponent<Flicker>();
            }

            Particles(root, World(PaintingWidth * 0.5f, arena.MotesRow, arena), arena);
            if (arena.Icicles)
            {
                StartCoroutine(IcicleRain(root));
            }

            if (arena.Pieces.Length > 0)
            {
                BuildLavaSea(root, arena);
            }

            if (arena.Laser)
            {
                FallingLaser.Create(root, -HalfWidth, HalfWidth, () => RoundLive);
            }

            if (arena.Missiles.Length > 0)
            {
                var lanes = new Vector2[arena.Missiles.Length];
                for (int i = 0; i < lanes.Length; i++)
                {
                    lanes[i] = World(arena.Missiles[i].x, arena.Missiles[i].y, arena);
                }

                MissileHazard.Create(root, lanes, HalfWidth, FullResPixelsPerUnit, () => RoundLive);
            }
        }

        /// <summary>
        /// Endless scroller: the painting repeats sideways forever in two layers (the road cut-out fixed
        /// to the ground, the whole painting behind it sliding slowly), one floor reaching
        /// <see cref="EndlessReach"/> either way, no walls, and a camera free to follow sideways.
        /// </summary>
        private void BuildEndless(Transform root, Arena arena)
        {
            HideSceneSky();
            // The background hangs from the road's top edge, so shrinking it never opens a gap above the road.
            float roadEdge = (arena.GroundRow - arena.RoadCut) / PixelsPerUnit;
            InfiniteBackdrop.Create(root, "Background", Painting(arena.Texture, arena, arena.TileLeft, arena.TileRight, arena.RoadCut), 0.9f, -101,
                arena.BackgroundScale, roadEdge);
            InfiniteBackdrop.Create(root, "Road", Painting(arena.Texture + "_road", arena, 0f, arena.Width), 0f, -100);
            Box(root, "Floor", new Vector2(-EndlessReach, 0f), EndlessReach, 8f);
            if (cam != null)
            {
                float halfView = HalfWidth + PadSide * 0.5f / PixelsPerUnit; // same zoom limit as the arenas
                cam.GetComponent<CameraRig>().SetBounds(EndlessReach, halfView / Mathf.Max(cam.aspect, 0.5f));
            }
        }

        private static Vector2 TrainWorld(float px, float py) => new Vector2((px - TrainCentre) / TrainPixelsPerUnit, (TrainRoof - py) / TrainPixelsPerUnit);

        /// <summary>
        /// The train arena: the train stands still at the middle of the world while two painted layers
        /// slide right behind it (see <see cref="Treadmill"/>), the far one slowly, the near one fast. The
        /// colliders follow the red lines of train_guide.png, which is laid exactly over the train and then
        /// switched off. The camera follows the fighters along the train but never shows past the layers'
        /// top or bottom edge.
        /// </summary>
        private void BuildTrain(Transform root, Arena arena)
        {
            HideSceneSky();
            float bottom = TrainWorld(0f, TrainWheels).y - (BackdropHeight - BackdropWheelRow) / BackdropPixelsPerUnit; // both layers' bottom edge
            Sprite far = Backdrop("TrainFar");
            Treadmill.Create(root, "FarLayer", far, -20, FarSpeed, FarFollow, bottom);
            Treadmill.Create(root, "NearLayer", Backdrop("TrainNear"), -10, NearSpeed, NearFollow, bottom);
            TrainSprite(root, "Train", "Train", 0);
            GameObject guide = TrainSprite(root, "CollisionGuide", "TrainGuide", 1);

            foreach (Vector3 floor in TrainFloors)
            {
                Box(root, "Floor", TrainWorld(floor.x, floor.z), TrainWorld(floor.y, 0f).x, (TrainDeckBottom - floor.z) / TrainPixelsPerUnit);
            }

            var slope = new GameObject("Cowcatcher");
            slope.transform.SetParent(root, false);
            slope.AddComponent<EdgeCollider2D>().points = System.Array.ConvertAll(TrainSlope, p => TrainWorld(p.x, p.y));
            // No walls at the ends: off the front or the back of the train is a fall to death. (A wall at the
            // front would trap a fighter in the notch under the cowcatcher's slope, which is too steep to climb.)

            guide.SetActive(false); // the colliders are in: the guide was only there to lay them over

            Particles(root, new Vector2(-8f, 2f), arena); // dust blowing back along the train
            if (cam != null)
            {
                float halfView = HalfWidth + PadSide * 0.5f / PixelsPerUnit; // same zoom limit as the arenas
                float maxSize = halfView / Mathf.Max(cam.aspect, 0.5f);
                var rig = cam.GetComponent<CameraRig>();
                rig.SetBounds(TrainWorld(TrainLeft, 0f).x, TrainWorld(TrainRight, 0f).x, maxSize);
                rig.MinY = bottom + BackdropFloor / BackdropPixelsPerUnit + maxSize;
                rig.MaxY = bottom + far.bounds.size.y - maxSize;
            }
        }

        /// <summary>A scrolling layer of the train arena, pivot at its bottom centre.</summary>
        private static Sprite Backdrop(string texture)
        {
            var image = Resources.Load<Texture2D>("Arena/" + texture);
            return Sprite.Create(image, new Rect(0f, 0f, image.width, image.height), new Vector2(0.5f, 0f), BackdropPixelsPerUnit);
        }

        /// <summary>Arena/Train or its guide (same size, same cut, same scale), pinned so both land on the same spot.</summary>
        private static GameObject TrainSprite(Transform root, string name, string texture, int order)
        {
            var image = Resources.Load<Texture2D>("Arena/" + texture);
            var sprite = new GameObject(name).AddComponent<SpriteRenderer>();
            sprite.transform.SetParent(root, false);
            sprite.sprite = Sprite.Create(image, new Rect(0f, 0f, image.width, image.height),
                new Vector2((TrainCentre - TrainCropLeft) * TrainTextureScale / image.width, 1f - (TrainRoof - TrainCropTop) * TrainTextureScale / image.height),
                TrainPixelsPerUnit * TrainTextureScale);
            sprite.sortingOrder = order;
            return sprite.gameObject;
        }

        /// <summary>Painting columns <paramref name="left"/>..<paramref name="right"/> (never the mirrored side padding, so tiles meet edge to edge), pivot on the ground row (or <paramref name="pivotRow"/>).</summary>
        private static Sprite Painting(string texture, Arena arena, float left, float right, float pivotRow = -1f)
        {
            var image = Resources.Load<Texture2D>("Arena/" + texture);
            float groundFromBottom = image.height - PadTop - (pivotRow >= 0f ? pivotRow : arena.GroundRow);
            return Sprite.Create(image, new Rect(PadSide + left, 0f, right - left, image.height),
                new Vector2(0.5f, groundFromBottom / image.height), PixelsPerUnit);
        }

        /// <summary>The Survival castle (graybox), in place of a painted arena.</summary>
        private CastleLevel BuildCastle()
        {
            environment.SetActive(false);
            HideSceneSky();
            return CastleLevel.Build(glowMaterial);
        }

        // The scene's sky and drifting petals belong to the old shrine; each arena brings its own motes.
        private void HideSceneSky()
        {
            if (cam == null)
            {
                return;
            }

            foreach (string child in new[] { "Sky", "Petals" })
            {
                Transform t = cam.transform.Find(child);
                if (t != null) t.gameObject.SetActive(false);
            }

            cam.backgroundColor = Color.black;
        }

        // Volcano layout, in painting rows (see Tools/prepare_backgrounds.py).
        // LavaRow sits well under the pieces' tops, so they stand tall out of the lava.
        private const float LavaRow = 822f, PieceTopRow = 704f, PieceHeight = 189f, PieceWidth = 260f;

        /// <summary>Sinking basalt pieces over a lava sea that covers the whole floor.</summary>
        private void BuildLavaSea(Transform root, Arena arena)
        {
            for (int i = 0; i < arena.Pieces.Length; i++)
            {
                var texture = Resources.Load<Texture2D>("Arena/BasaltPiece" + (i % 2));
                var go = new GameObject("Piece");
                go.transform.SetParent(root, false);
                go.transform.position = World(arena.Pieces[i], arena.GroundRow, arena);
                var sprite = go.AddComponent<SpriteRenderer>();
                float surfaceFromTop = arena.GroundRow - PieceTopRow;
                sprite.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 1f - surfaceFromTop / PieceHeight), PixelsPerUnit);
                sprite.sortingOrder = 3;
                float width = (PieceWidth - 8f) / PixelsPerUnit;
                var box = go.AddComponent<BoxCollider2D>();
                box.size = new Vector2(width, 2f);
                box.offset = new Vector2(0f, -1f);
                go.AddComponent<Rigidbody2D>();
                go.AddComponent<SinkingPlatform>().Init(width);
            }

            // The lava surface in front of the pieces, so a sinking piece goes under it.
            var lavaTexture = Resources.Load<Texture2D>("Arena/LavaSea");
            var lava = new GameObject("LavaSea") { layer = 2 }; // Ignore Raycast: aim lines and AI ground checks see through it
            lava.transform.SetParent(root, false);
            Vector2 surface = World(PaintingWidth * 0.5f, LavaRow, arena);
            lava.transform.position = surface;
            var lavaSprite = lava.AddComponent<SpriteRenderer>();
            lavaSprite.sprite = Sprite.Create(lavaTexture, new Rect(0f, 0f, lavaTexture.width, lavaTexture.height), new Vector2(0.5f, 1f), PixelsPerUnit);
            lavaSprite.sortingOrder = 31;
            var burn = lava.AddComponent<BoxCollider2D>();
            burn.isTrigger = true;
            burn.size = new Vector2(HalfWidth * 2f, 3f);
            burn.offset = new Vector2(0f, -1.6f); // top just under the wavy surface
            lava.AddComponent<LavaHazard>();

            // Something to land on inside the lava, so a fall burns rather than drops out of the world.
            Box(root, "LavaBed", new Vector2(-HalfWidth, surface.y - 1.1f), HalfWidth, 2f);
        }

        private bool RoundLive => duel.Player.ControlsEnabled && !duel.Player.IsDead && !duel.Enemy.IsDead;

        /// <summary>
        /// Each round, FirstIcicle seconds after FIGHT and then every IcicleInterval seconds, three
        /// icicles drop at random spots spread across the floor. The clock restarts every round.
        /// </summary>
        private IEnumerator IcicleRain(Transform root)
        {
            var spots = new List<float>();
            while (true)
            {
                yield return new WaitUntil(() => RoundLive);
                int round = duel.Round;
                float clock = 0f;
                float next = FirstIcicle;
                while (RoundLive && duel.Round == round)
                {
                    clock += Time.unscaledDeltaTime; // real seconds, not slowed by game speed
                    if (clock >= next)
                    {
                        next += IcicleInterval;
                        DropThree(root, spots);
                    }

                    yield return null;
                }
            }
        }

        private void DropThree(Transform root, List<float> spots)
        {
            spots.Clear();
            while (spots.Count < 3)
            {
                float x = Random.Range(-HalfWidth + 1f, HalfWidth - 1f);
                if (spots.TrueForAll(other => Mathf.Abs(other - x) > 2f))
                {
                    spots.Add(x);
                }
            }

            DropIcicles(root, spots);
        }

        /// <summary>Drops one icicle over each x, each with a shadow on the floor where it will land.</summary>
        public void DropIcicles(Transform root, IEnumerable<float> xs)
        {
            if (icicleSprite == null)
            {
                var texture = Resources.Load<Texture2D>("Arena/Icicle");
                icicleSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 1f), 160f);
                var circle = (Texture2D)Fx.Instance.DotMaterial.mainTexture;
                shadowSprite = Sprite.Create(circle, new Rect(0f, 0f, circle.width, circle.height), new Vector2(0.5f, 0.5f), circle.width);
            }

            foreach (float x in xs)
            {
                // A dark shadow reads on the bright snow, where an additive glow would vanish.
                var shadow = new GameObject("IcicleShadow").AddComponent<SpriteRenderer>();
                shadow.transform.SetParent(root, false);
                shadow.transform.position = new Vector2(x, 0.02f);
                shadow.transform.localScale = new Vector3(1.6f, 0.35f, 1f);
                shadow.sprite = shadowSprite;
                shadow.color = new Color(0.02f, 0.06f, 0.2f, 0.9f);
                shadow.sortingOrder = 31;
                Icicle.Drop(icicleSprite, new Vector2(x, IcicleHeight), shadow);
            }
        }

        /// <summary>Solid box from x0 to x1 whose top edge is at <paramref name="topLeft"/>.y, <paramref name="depth"/> deep.</summary>
        private static GameObject Box(Transform root, string name, Vector2 topLeft, float right, float depth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = new Vector2((topLeft.x + right) * 0.5f, topLeft.y);
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(right - topLeft.x, depth);
            box.offset = new Vector2(0f, -depth * 0.5f);
            return go;
        }

        /// <summary>Additive glow using the lantern glow material (a Flicker added afterwards reads its colour).</summary>
        private SpriteRenderer CreateGlow(Transform root, Vector2 position, Vector3 scale, Color color, int order)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            var sprite = go.AddComponent<SpriteRenderer>();
            if (glowMaterial != null && glowMaterial.mainTexture is Texture2D texture)
            {
                sprite.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
                sprite.sharedMaterial = glowMaterial;
            }

            sprite.sortingOrder = order;
            sprite.color = color;
            return sprite;
        }

        private static void Particles(Transform root, Vector2 position, Arena arena)
        {
            var go = new GameObject("Motes");
            go.transform.SetParent(root, false);
            go.transform.position = position;
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(arena.MotesLifetime * 0.6f, arena.MotesLifetime);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.11f);
            main.startColor = arena.Motes;
            main.maxParticles = 500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = arena.MotesRate;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(HalfWidth * 2.4f, 0.5f, 0.1f);
            ParticleSystem.VelocityOverLifetimeModule drift = system.velocityOverLifetime;
            drift.enabled = true;
            Vector2 v = arena.MotesVelocity;
            drift.x = new ParticleSystem.MinMaxCurve(v.x - 0.3f, v.x + 0.3f);
            drift.y = new ParticleSystem.MinMaxCurve(v.y * 0.6f, v.y * 1.4f);
            drift.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            // Plain sprite shader so the motes keep their exact colour (the FX dot material dims them).
            var material = new Material(Shader.Find("Sprites/Default"));
            if (Fx.Instance != null) material.mainTexture = Fx.Instance.DotMaterial.mainTexture;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = 35;
            system.Play();
        }

        // ================================================================ menu

        private void BuildMenu()
        {
            if (EventSystem.current == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            }

            Text sample = FindAnyObjectByType<Text>();
            Font font = sample != null ? sample.font : null;

            menu = new GameObject("MainMenu");
            var canvas = menu.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = menu.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            menu.AddComponent<GraphicRaycaster>();

            Image shade = UiImage("Shade", menu.transform, new Color(0.03f, 0.02f, 0.06f, 0.82f), Vector2.zero, Vector2.zero);
            shade.rectTransform.anchorMin = Vector2.zero;
            shade.rectTransform.anchorMax = Vector2.one;
            shade.rectTransform.offsetMin = shade.rectTransform.offsetMax = Vector2.zero;

            UiText(menu.transform, font, "STICK DUEL", 96, new Vector2(0f, 420f), new Color(1f, 0.85f, 0.4f));
            UiText(menu.transform, font, "CHOOSE YOUR WEAPON", 34, new Vector2(0f, 310f), Color.white);
            Sprite katana = duel.Player.sword.GetComponent<SpriteRenderer>().sprite;
            var weaponTypes = (WeaponType[])System.Enum.GetValues(typeof(WeaponType));
            // The row must fit a 16:10 screen too (about 1800 reference units wide), so the cards shrink with the count.
            float spacing = Mathf.Min(290f, 1700f / weaponTypes.Length);
            float cardWidth = spacing - 24f;
            for (int i = 0; i < weaponTypes.Length; i++)
            {
                WeaponType type = weaponTypes[i];
                bool playable = WeaponTypeInfo.IsPlayable(type);
                string name = WeaponTypeInfo.DisplayName(type);
                Color color = playable ? new Color(0.16f, 0.13f, 0.22f) : new Color(0.1f, 0.09f, 0.12f);
                Image card = Card(name, new Vector2((i - (weaponTypes.Length - 1) * 0.5f) * spacing, 165f), new Vector2(cardWidth, 210f), color, () => SelectWeapon(type));
                card.GetComponent<Button>().interactable = playable; // locked: coming soon
                if (playable)
                {
                    Image picture = UiImage("Art", card.transform, Color.white, new Vector2(0f, 34f), new Vector2(cardWidth - 20f, 100f));
                    picture.sprite = type == WeaponType.Spear ? SpearWeapon.Art : type == WeaponType.Axe ? AxeWeapon.Art : type == WeaponType.Mace ? MaceWeapon.Art : type == WeaponType.LaserSword ? LaserSwordWeapon.LaserBlade.Sprite : type == WeaponType.Bow ? BowWeapon.Art : type == WeaponType.SniperRifle ? SniperRifleWeapon.Art : type == WeaponType.CastleSword ? CastleSwordWeapon.Art : katana;
                    picture.preserveAspect = true;
                    picture.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 20f);
                }

                UiText(card.transform, font, name, cardWidth >= 250f ? 30 : 24, new Vector2(0f, -40f), playable ? Color.white : new Color(1f, 1f, 1f, 0.4f));
                UiText(card.transform, font, WeaponTypeInfo.Blurb(type), 16, new Vector2(0f, -78f), new Color(1f, 1f, 1f, playable ? 0.65f : 0.35f), cardWidth - 16f);
                weaponOutlines.Add(card.GetComponent<Outline>());
            }

            mapRowTitle = UiText(menu.transform, font, "CHOOSE YOUR ARENA", 34, new Vector2(0f, -10f), Color.white);
            // The row must fit a 16:10 screen like the weapon row (about 1700 reference units), so the cards shrink with the count.
            float arenaSpacing = Mathf.Min(440f, 1700f / All.Length);
            float arenaWidth = arenaSpacing - 30f;
            for (int i = 0; i < All.Length; i++)
            {
                int index = i;
                Arena arena = All[i];
                Image card = Card(arena.Name, new Vector2((i - (All.Length - 1) * 0.5f) * arenaSpacing, -195f), new Vector2(arenaWidth, 290f), new Color(0.16f, 0.13f, 0.22f), () => SelectMap(index));
                var texture = Resources.Load<Texture2D>("Arena/" + arena.Texture);
                Image picture = UiImage("Preview", card.transform, Color.white, new Vector2(0f, 40f), new Vector2(arenaWidth - 20f, (arenaWidth - 20f) * 218f / 390f));
                picture.sprite = arena.Train
                    ? Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f)) // already just the picture
                    : Sprite.Create(texture, new Rect(PadSide, PadBottom, PaintingWidth, texture.height - PadTop - PadBottom), new Vector2(0.5f, 0.5f));
                UiText(card.transform, font, arena.Name, arenaWidth >= 400f ? 30 : arenaWidth >= 300f ? 26 : 22, new Vector2(0f, -84f), Color.white);
                UiText(card.transform, font, arena.Blurb, arenaWidth >= 400f ? 19 : arenaWidth >= 300f ? 17 : 15, new Vector2(0f, -118f), new Color(1f, 1f, 1f, 0.7f), arenaWidth - 16f);
                mapOutlines.Add(card.GetComponent<Outline>());
                arenaCards.Add(card.gameObject);
            }

            // Sniper Mode plays its own maps: the same row shows them instead while that mode is picked.
            for (int i = 0; i < SniperMap.Count; i++)
            {
                int index = i;
                Image card = Card(SniperMap.Name(i), new Vector2((i - 1.5f) * 440f, -195f), new Vector2(410f, 290f), new Color(0.16f, 0.13f, 0.22f), () => SelectSniperMap(index));
                Image picture = UiImage("Preview", card.transform, Color.white, new Vector2(0f, 30f), new Vector2(390f, 218f));
                picture.sprite = SniperMap.Preview(i);
                picture.preserveAspect = true;
                UiText(card.transform, font, SniperMap.Name(i), 30, new Vector2(0f, -100f), Color.white);
                UiText(card.transform, font, SniperMap.Blurb(i), 19, new Vector2(0f, -128f), new Color(1f, 1f, 1f, 0.7f));
                sniperMapOutlines.Add(card.GetComponent<Outline>());
                sniperMapCards.Add(card.gameObject);
            }

            Image fight = Card("Fight", new Vector2(560f, -410f), new Vector2(300f, 86f), new Color(0.85f, 0.25f, 0.2f), Fight);
            fight.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.5f);
            UiText(fight.transform, font, "FIGHT!", 46, Vector2.zero, Color.white);
            UiText(menu.transform, font, "Enter to fight      Esc in game returns here", 22, new Vector2(0f, -490f), new Color(1f, 1f, 1f, 0.5f));

            // Modes left to centre, FIGHT on the right (cards are built in Mode order).
            (Mode mode, string title, string blurb)[] modes =
            {
                (Mode.Duel, "DUEL", "Best of five"),
                (Mode.StormTheCastle, "STORM THE CASTLE", "Climb the castle, take the crown"),
                (Mode.Endless, "ENDLESS SURVIVAL", "Hold out against the horde"),
                (Mode.Sniper, "SNIPER MODE", "Rifles only, walls stop bullets"),
            };
            for (int i = 0; i < modes.Length; i++)
            {
                Mode pick = modes[i].mode;
                // Four cards fit left of FIGHT (which starts at x = 410).
                Image card = Card(modes[i].title, new Vector2(-720f + i * 255f, -410f), new Vector2(240f, 86f), new Color(0.16f, 0.13f, 0.22f), () => SelectMode(pick));
                UiText(card.transform, font, modes[i].title, modes[i].title.Length > 12 ? 20 : modes[i].title.Length > 8 ? 24 : 34, new Vector2(0f, 12f), Color.white);
                UiText(card.transform, font, modes[i].blurb, 15, new Vector2(0f, -22f), new Color(1f, 1f, 1f, 0.65f), 225f);
                modeOutlines.Add(card.GetComponent<Outline>());
            }

            SelectWeapon(loadout.Weapon);
            SelectMap(selectedMap);
            SelectSniperMap(selectedSniperMap);
            SelectMode(mode);
        }

        private void SelectMode(Mode pick)
        {
            mode = pick;
            Highlight(modeOutlines, (int)pick);
            bool sniper = pick == Mode.Sniper;
            foreach (GameObject card in arenaCards) card.SetActive(!sniper);
            foreach (GameObject card in sniperMapCards) card.SetActive(sniper);
            if (mapRowTitle != null) mapRowTitle.text = sniper ? "CHOOSE THE FIRST MAP" : "CHOOSE YOUR ARENA";
        }

        private void SelectWeapon(WeaponType type)
        {
            loadout.TrySelect(type);
            Highlight(weaponOutlines, (int)loadout.Weapon); // cards are built in enum order
        }

        private void SelectMap(int index)
        {
            selectedMap = index;
            Highlight(mapOutlines, index);
        }

        /// <summary>Sniper Mode starts on this map and goes on through the rest in order.</summary>
        private void SelectSniperMap(int index)
        {
            selectedSniperMap = Mathf.Clamp(index, 0, SniperMap.Count - 1);
            Highlight(sniperMapOutlines, selectedSniperMap);
        }

        private static void Highlight(List<Outline> outlines, int index)
        {
            for (int i = 0; i < outlines.Count; i++)
            {
                outlines[i].effectColor = i == index ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 1f, 1f, 0.12f);
                outlines[i].effectDistance = i == index ? new Vector2(6f, -6f) : new Vector2(2f, -2f);
            }
        }

        private Image Card(string name, Vector2 position, Vector2 size, Color color, UnityEngine.Events.UnityAction onClick)
        {
            Image card = UiImage(name, menu.transform, color, position, size);
            card.raycastTarget = true;
            var button = card.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.85f, 0.85f, 0.85f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
            button.onClick.AddListener(onClick);
            card.gameObject.AddComponent<Outline>();
            return card;
        }

        private static Image UiImage(string name, Transform parent, Color color, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
            return image;
        }

        /// <summary>Centred label; with a <paramref name="width"/> it wraps inside it instead of overflowing.</summary>
        private static Text UiText(Transform parent, Font font, string value, int size, Vector2 position, Color color, float width = 0f)
        {
            var go = new GameObject(value, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.horizontalOverflow = width > 0f ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.rectTransform.anchoredPosition = position;
            if (width > 0f)
            {
                text.rectTransform.sizeDelta = new Vector2(width, text.rectTransform.sizeDelta.y);
            }

            return text;
        }
    }
}
