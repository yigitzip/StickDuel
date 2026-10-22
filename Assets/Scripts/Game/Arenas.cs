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
        }

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

        // Kept across Esc reloads so the menu remembers the last choice.
        private static int selectedWeapon;
        private static int selectedMap;

        private DuelManager duel;
        private GameObject environment;
        private Camera cam;
        private Material glowMaterial;
        private GameObject menu;
        private Sprite icicleSprite;
        private Sprite shadowSprite;
        private readonly List<Outline> weaponOutlines = new List<Outline>();
        private readonly List<Outline> mapOutlines = new List<Outline>();

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
                text.text = text.text.Replace("W / SPACE  jump    Q / E  roll", "W  jump    SPACE  kick    Q / E  roll    ESC  menu");
            }

            SetControls(false);
            BuildMenu();
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

        /// <summary>Skips the menu (used by the editor test bridge).</summary>
        public void StartWith(int weapon, int map)
        {
            selectedWeapon = Mathf.Clamp(weapon, 0, Weapon.All.Length - 1);
            selectedMap = Mathf.Clamp(map, 0, All.Length - 1);
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
            Build(All[selectedMap]);
            Fighter player = duel.Player;
            Fighter enemy = duel.Enemy;
            if (player.sword != null) player.sword.Equip(Weapon.All[selectedWeapon]);
            if (enemy.sword != null) enemy.sword.Equip(Weapon.All[Random.Range(0, Weapon.All.Length)]);
            foreach (AIDriver ai in FindObjectsByType<AIDriver>(FindObjectsSortMode.None))
            {
                ai.ArenaHalfWidth = HalfWidth;
            }

            duel.enabled = true; // its Start kicks off the match
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

            var texture = Resources.Load<Texture2D>("Arena/" + arena.Texture);
            var background = new GameObject("Background").AddComponent<SpriteRenderer>();
            background.transform.SetParent(root, false);
            float groundFromBottom = texture.height - PadTop - arena.GroundRow;
            background.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, groundFromBottom / texture.height), PixelsPerUnit);
            background.sortingOrder = -100;

            if (cam != null)
            {
                // The scene's sky and drifting petals belong to the old shrine; each arena brings its own motes.
                foreach (string child in new[] { "Sky", "Petals" })
                {
                    Transform t = cam.transform.Find(child);
                    if (t != null) t.gameObject.SetActive(false);
                }

                cam.backgroundColor = Color.black;
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
            for (int i = 0; i < Weapon.All.Length; i++)
            {
                int index = i;
                Weapon weapon = Weapon.All[i];
                Image card = Card(weapon.Name, new Vector2((i - 2) * 300f, 165f), new Vector2(270f, 210f), new Color(0.16f, 0.13f, 0.22f), () => SelectWeapon(index));
                Image picture = UiImage("Art", card.transform, Color.white, new Vector2(0f, 30f), new Vector2(230f, 110f));
                picture.sprite = weapon.Sprite != null ? weapon.Sprite : katana;
                picture.preserveAspect = true;
                picture.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 20f);
                UiText(card.transform, font, weapon.Name, 30, new Vector2(0f, -48f), Color.white);
                UiText(card.transform, font, weapon.Blurb, 19, new Vector2(0f, -80f), new Color(1f, 1f, 1f, 0.65f));
                weaponOutlines.Add(card.GetComponent<Outline>());
            }

            UiText(menu.transform, font, "CHOOSE YOUR ARENA", 34, new Vector2(0f, -10f), Color.white);
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
            }

            Image fight = Card("Fight", new Vector2(0f, -410f), new Vector2(360f, 86f), new Color(0.85f, 0.25f, 0.2f), Fight);
            fight.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.5f);
            UiText(fight.transform, font, "FIGHT!", 46, Vector2.zero, Color.white);
            UiText(menu.transform, font, "Enter to fight      Esc in game returns here", 22, new Vector2(0f, -490f), new Color(1f, 1f, 1f, 0.5f));

            SelectWeapon(selectedWeapon);
            SelectMap(selectedMap);
        }

        private void SelectWeapon(int index)
        {
            selectedWeapon = index;
            Highlight(weaponOutlines, index);
        }

        private void SelectMap(int index)
        {
            selectedMap = index;
            Highlight(mapOutlines, index);
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

        private static void UiText(Transform parent, Font font, string value, int size, Vector2 position, Color color)
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
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.rectTransform.anchoredPosition = position;
        }
    }
}
