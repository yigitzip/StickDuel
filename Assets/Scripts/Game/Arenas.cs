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
    /// colliders laid over what is painted: floors, ledges, a hazard pit. The scene's own
    /// environment is hidden. The match is held back until FIGHT; Esc in game reloads the
    /// scene and comes back here. Creates itself on scene load, so the scene file needs no changes.
    /// </summary>
    public sealed class Arenas : MonoBehaviour
    {
        // Painting layout: 1600x893 inside padding (see Tools/prepare_backgrounds.py).
        private const float PaintingWidth = 1600f;
        private const float PadTop = 320f, PadBottom = 160f, PadSide = 200f;
        private const float PixelsPerUnit = 92f; // fighters stand about as tall as the painted lanterns
        private const float HalfWidth = PaintingWidth * 0.5f / PixelsPerUnit;
        private const float FirstIcicle = 3f;      // seconds into each round
        private const float IcicleInterval = 5f;   // then every this many seconds
        private const float IcicleHeight = 9f; // just above the top of the view

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
            if (!castleRun)
            {
                Build(mode == Mode.Endless ? Battlefield : All[selectedMap]);
            }

            Fighter player = duel.Player;
            Fighter enemy = duel.Enemy;
            if (player.sword != null) WeaponFactory.Prepare(castleRun ? WeaponType.Sword : loadout.Weapon, player)?.Equip(player);
            if (enemy.sword != null) WeaponFactory.Prepare(WeaponType.Sword, enemy)?.Equip(enemy);
            foreach (AIDriver ai in FindObjectsByType<AIDriver>(FindObjectsSortMode.None))
            {
                ai.ArenaHalfWidth = HalfWidth;
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
                    picture.sprite = type == WeaponType.Spear ? SpearWeapon.Art : type == WeaponType.Axe ? AxeWeapon.Art : type == WeaponType.Mace ? MaceWeapon.Art : type == WeaponType.LaserSword ? LaserSwordWeapon.LaserBlade.Sprite : type == WeaponType.Bow ? BowWeapon.Art : type == WeaponType.SniperRifle ? SniperRifleWeapon.Art : katana;
                    picture.preserveAspect = true;
                    picture.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 20f);
                }

                UiText(card.transform, font, name, cardWidth >= 250f ? 30 : 24, new Vector2(0f, -40f), playable ? Color.white : new Color(1f, 1f, 1f, 0.4f));
                UiText(card.transform, font, WeaponTypeInfo.Blurb(type), 16, new Vector2(0f, -78f), new Color(1f, 1f, 1f, playable ? 0.65f : 0.35f), cardWidth - 16f);
                weaponOutlines.Add(card.GetComponent<Outline>());
            }

            mapRowTitle = UiText(menu.transform, font, "CHOOSE YOUR ARENA", 34, new Vector2(0f, -10f), Color.white);
            for (int i = 0; i < All.Length; i++)
            {
                int index = i;
                Arena arena = All[i];
                Image card = Card(arena.Name, new Vector2((i - 1.5f) * 440f, -195f), new Vector2(410f, 290f), new Color(0.16f, 0.13f, 0.22f), () => SelectMap(index));
                var texture = Resources.Load<Texture2D>("Arena/" + arena.Texture);
                Image picture = UiImage("Preview", card.transform, Color.white, new Vector2(0f, 30f), new Vector2(390f, 218f));
                picture.sprite = Sprite.Create(texture, new Rect(PadSide, PadBottom, PaintingWidth, texture.height - PadTop - PadBottom), new Vector2(0.5f, 0.5f));
                UiText(card.transform, font, arena.Name, 30, new Vector2(0f, -100f), Color.white);
                UiText(card.transform, font, arena.Blurb, 19, new Vector2(0f, -128f), new Color(1f, 1f, 1f, 0.7f));
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
