using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StickDuel
{
    /// <summary>
    /// RUN!: the player runs right forever with nothing but a <see cref="ShieldWeapon"/>, past archer
    /// towers and sniper huts whose bots (<see cref="RunGunner"/>) shoot at them. Jump, roll (invulnerable)
    /// or turn the shield to the shots; the first death ends the run with the distance and the time,
    /// R runs again from where they fell.
    /// <para>
    /// Everything is laid out on the 2752x1536 canvas the RUN! paintings share (Tools/reference/run_*.png,
    /// marked up by run_guide.jpg; Tools/prepare_backgrounds.py run() makes the Arena/Run* sprites):
    /// the red line is the ground (world y = 0), the canvas repeats every <see cref="ChunkWidth"/> units,
    /// and in every repeat towers may stand on the two pink stretches and a hut on the blue one. The
    /// hut's roof (the guide's black lines) is solid: it can be run over and stops arrows and bullets.
    /// Depth: sky, hill, near grass and towers behind the fighters, the hut in front of them; every bot
    /// stands among the fighters (the sniper between the hut's back wall and its front) and shoots on
    /// the player's plane. The painted layers scroll by parallax (<see cref="Treadmill"/>); the ground,
    /// towers and huts are fixed in the world, which the player really runs through.
    /// </para>
    /// Drives DuelManager's HUD in place of its match flow (DuelManager.RunMatch = false). Added by
    /// Arenas; Esc reloads the scene, which removes it and everything it built.
    /// </summary>
    public sealed class RunMode : MonoBehaviour
    {
        // Canvas layout (keep in sync with Tools/prepare_backgrounds.py RUN_*), in canvas pixels, y down.
        private const float CanvasPixelsPerUnit = 130f;               // a fighter stands about as tall as the tower's lookout
        private const float StoredScale = 0.75f;                      // RUN_SCALE: the sprites are stored this much smaller
        private const float CanvasWidth = 2752f, CanvasHeight = 1536f;
        private const float GroundRow = 1262f;                        // the red line (it slants a little; this is its middle)
        private const float ChunkWidth = CanvasWidth / CanvasPixelsPerUnit;
        private static readonly Vector2[] TowerZones = { new Vector2(134f, 568f), new Vector2(1577f, 2004f) }; // pink: where a tower's middle may go
        private static readonly Vector2 HutZone = new Vector2(2116f, 2661f);                                   // blue: where a hut's middle may go
        private static readonly Rect TowerCrop = Rect.MinMaxRect(1527f, 23f, 2026f, 1168f);                  // RUN_TOWER_CROP
        private static readonly Rect HutCrop = Rect.MinMaxRect(2033f, 1013f, 2740f, 1560f);                  // RUN_HUT_CROP down to RUN_HUT_BOTTOM
        private const float TowerMiddle = 1776f, Lookout = 532f, LookoutHalfWidth = 205f;  // the archer stands on the lookout's floor
        private const float HutMiddle = 2384f;                                             // the roof's peak, above the opening's middle
        private static readonly Vector2[] Roof = { new Vector2(2043f, 1268f), new Vector2(2384f, 1013f), new Vector2(2716f, 1268f) }; // black lines, top edge (ends dipped into the ground)
        private const float SniperShoulderRow = 1330f;                                     // chest and rifle over the parapet, legs behind it

        private const float ViewSize = 5.6f, ViewBottom = -2f; // the camera never shows past the canvas' bottom or top
        private const float StartGap = 9f;                     // nothing stands this close ahead at the start
        private const float BehindLimit = 30f;                 // structures this far behind the player are cleared away

        [Tooltip("The runner's health (an arrow does up to 30, a sniper bullet about 34, 50 to the head).")]
        [SerializeField, Min(1f)] private float runnerHealth = 100f;
        [Tooltip("Chance a tower or hut spot is filled at the start, rising by RampPerMetre to MaxChance.")]
        [SerializeField, Range(0f, 1f)] private float startChance = 0.4f;
        [SerializeField, Min(0f)] private float rampPerMetre = 1f / 1500f;
        [SerializeField, Range(0f, 1f)] private float maxChance = 0.85f;

        private sealed class Post
        {
            public GameObject Root;
            public float Right;
            public Fighter Bot;
            public GameObject Sword;
        }

        private DuelManager duel;
        private Fighter player, template;
        private CameraRig cameraRig;
        private Transform world;
        private Collider2D ground;
        private Sprite tower, hut, hutWindow;
        private readonly List<Post> posts = new List<Post>();
        private float startX, nextChunk, startTime, distance, time;
        private bool running, over;

        public float Distance => distance;
        public float Seconds => time;
        public bool IsOver => over;
        public int Posts => posts.Count;

        /// <summary>Call right after adding the component, before DuelManager is enabled.</summary>
        public void Init(DuelManager duel)
        {
            this.duel = duel;
            duel.RunMatch = false;
            player = duel.Player;
            template = duel.Enemy; // only the mould the bots are cast from
            template.gameObject.SetActive(false);
            template.sword.gameObject.SetActive(false);

            WeaponFactory.Prepare(WeaponType.Shield, player)?.Equip(player);
            player.MaxHealth = runnerHealth;
            if (player.TryGetComponent(out PlayerDriver driver))
            {
                driver.AutoRun = true;
            }
            else
            {
                Debug.LogError("RunMode: the player has no PlayerDriver.", player);
            }

            BuildWorld();
            if (Fx.Instance != null) Fx.Instance.Calm = true; // no slow motion on every arrow
        }

        private void Start()
        {
            StartCoroutine(Run());
        }

        private void BuildWorld()
        {
            world = new GameObject("RunWorld").transform;
            float bottom = World(0f, CanvasHeight).y;
            Treadmill.Create(world, "Sky", Layer("RunSky"), -30, 0f, 0.9f, bottom);
            Treadmill.Create(world, "Hill", Layer("RunHill"), -25, 0f, 0.6f, bottom);
            Treadmill.Create(world, "Grass", Layer("RunGround"), -20, 0f, 0f, bottom);
            tower = Piece("RunTower", TowerCrop, TowerMiddle, TowerCrop.yMax);
            hut = Piece("RunHut", HutCrop, HutMiddle, GroundRow);
            hutWindow = Piece("RunHutWindow", HutCrop, HutMiddle, GroundRow);

            // One floor all the way: on Ignore Raycast so a sniper's shot from inside a hut goes up through
            // it (the rifle only stops at Default and Water); arrows still stick in it.
            var floor = new GameObject("Ground") { layer = 2 };
            floor.transform.SetParent(world, false);
            var box = floor.AddComponent<BoxCollider2D>();
            box.size = new Vector2(Arenas.EndlessReach * 2f, 8f);
            box.offset = new Vector2(0f, -4f);
            ground = box;

            Camera view = Camera.main;
            cameraRig = view != null ? view.GetComponent<CameraRig>() : null;
            if (cameraRig == null)
            {
                Debug.LogError("RunMode: no main camera with a CameraRig.", this);
                return;
            }

            cameraRig.SetBounds(-Arenas.EndlessReach, Arenas.EndlessReach, ViewSize);
            cameraRig.MinY = cameraRig.MaxY = ViewBottom + ViewSize;
            cameraRig.LeadX = ViewSize * Mathf.Max(view.aspect, 0.5f) * 0.45f; // the runner a quarter in from the left
            cameraRig.SetFixedFollow(player, view.orthographicSize / ViewSize);
            cameraRig.Targets = new[] { player };
        }

        /// <summary>Canvas pixel (x, row) of the repeat starting at world x = <paramref name="chunk"/>.</summary>
        private static Vector2 World(float x, float row, float chunk = 0f) => new Vector2(chunk + x / CanvasPixelsPerUnit, (GroundRow - row) / CanvasPixelsPerUnit);

        private static Texture2D Load(string name)
        {
            var image = Resources.Load<Texture2D>("Arena/" + name);
            if (image == null) Debug.LogError($"RunMode: Resources/Arena/{name}.png is missing (Tools/prepare_backgrounds.py run).");
            return image;
        }

        /// <summary>A scrolling layer, pivot at its bottom centre.</summary>
        private static Sprite Layer(string name)
        {
            Texture2D image = Load(name);
            return Sprite.Create(image, new Rect(0f, 0f, image.width, image.height), new Vector2(0.5f, 0f), CanvasPixelsPerUnit * StoredScale);
        }

        /// <summary>A tower or hut cut to <paramref name="crop"/>, pivot on canvas point (<paramref name="x"/>, <paramref name="row"/>).</summary>
        private static Sprite Piece(string name, Rect crop, float x, float row)
        {
            Texture2D image = Load(name);
            var pivot = new Vector2((x - crop.xMin) / crop.width, (crop.yMax - row) / crop.height);
            return Sprite.Create(image, new Rect(0f, 0f, image.width, image.height), pivot, CanvasPixelsPerUnit * StoredScale);
        }

        private IEnumerator Run()
        {
            running = false;
            yield return null; // DuelManager.Start runs first: it builds the HUD

            ClearPosts();
            over = false;
            player.opponent = null;
            startX = player.Body.position.x;
            player.ResetFighter(new Vector2(startX, Fighter.HipHeight + 0.05f));
            nextChunk = startX + StartGap - TowerZones[0].x / CanvasPixelsPerUnit;
            distance = time = 0f;
            duel.HideEnemyBars();
            duel.HideShurikenIcons();
            duel.SetHint("W  jump    Q / E  roll (no damage)    MOUSE  shield    R  run again    ESC  menu");
            UpdateLabel();

            player.ControlsEnabled = false;
            duel.Show("RUN!", "Jump (W), roll (Q / E) and hold the shield up to the arrows", Color.white);
            if (Fx.Instance != null) Fx.Instance.Beep();
            yield return new WaitForSecondsRealtime(1.6f);
            duel.Show("GO!", "", new Color(1f, 0.85f, 0.4f));
            if (Fx.Instance != null) Fx.Instance.Gong();
            player.ControlsEnabled = true;
            startTime = Time.time;
            running = true;
            yield return new WaitForSecondsRealtime(0.7f);
            duel.Hide();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (over && keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                Restart();
            }

            if (!running || over)
            {
                return;
            }

            float x = player.Body.position.x;
            distance = Mathf.Max(distance, x - startX);
            time = Time.time - startTime;
            if (player.IsDead)
            {
                over = true;
                StartCoroutine(Fell());
                return;
            }

            while (nextChunk < x + 40f)
            {
                Spawn(nextChunk);
                nextChunk += ChunkWidth;
            }

            for (int i = posts.Count - 1; i >= 0; i--)
            {
                if (posts[i].Right < x - BehindLimit)
                {
                    Clear(posts[i]);
                    posts.RemoveAt(i);
                }
            }

            UpdateLabel();
        }

        /// <summary>A whole new run, from where the last one ended (the camera stays put).</summary>
        public void Restart()
        {
            StopAllCoroutines();
            StartCoroutine(Run());
        }

        private IEnumerator Fell()
        {
            foreach (Post post in posts)
            {
                if (post.Bot != null) post.Bot.ControlsEnabled = false;
            }

            if (Fx.Instance != null) Fx.Instance.Knockout();
            yield return new WaitForSecondsRealtime(1f);
            duel.Show("YOU FELL", $"Ran {distance:0} m in {Clock(time)}      Press R to run again", new Color(1f, 0.4f, 0.4f));
        }

        /// <summary>One repeat of the canvas: each tower stretch and the hut stretch filled or not, by the current chance.</summary>
        private void Spawn(float chunk)
        {
            float chance = Mathf.Min(startChance + distance * rampPerMetre, maxChance);
            foreach (Vector2 zone in TowerZones)
            {
                if (Random.value < chance)
                {
                    Tower(chunk + Random.Range(zone.x, zone.y) / CanvasPixelsPerUnit);
                }
            }

            if (Random.value < chance * 0.8f)
            {
                Hut(chunk + Random.Range(HutZone.x, HutZone.y) / CanvasPixelsPerUnit);
            }
        }

        /// <summary>An archer tower whose middle is at world <paramref name="x"/>, the archer on its lookout.</summary>
        private void Tower(float x)
        {
            var root = new GameObject("ArcherTower").transform;
            root.SetParent(world, false);
            float shift = x - TowerMiddle / CanvasPixelsPerUnit; // canvas x → world x for this tower
            root.position = new Vector2(x, World(0f, TowerCrop.yMax).y);
            Art(root, "Tower", tower, -15);

            float floor = World(0f, Lookout).y;
            Perch(root, new Vector2(x, floor), LookoutHalfWidth * 2f / CanvasPixelsPerUnit);
            Fighter archer = Bot(WeaponType.Bow, new Vector2(x, floor + Fighter.HipHeight + 0.05f), 1f, out GameObject sword);
            posts.Add(new Post { Root = root.gameObject, Right = shift + TowerCrop.xMax / CanvasPixelsPerUnit, Bot = archer, Sword = sword });
        }

        /// <summary>A sniper hut whose middle is at world <paramref name="x"/>: the roof solid, the sniper behind the parapet.</summary>
        private void Hut(float x)
        {
            var root = new GameObject("SniperHut").transform;
            root.SetParent(world, false);
            root.position = new Vector2(x, 0f);
            Art(root, "Back", hutWindow, -10);
            Art(root, "Front", hut, 30); // over the fighters

            var roof = new GameObject("Roof");
            roof.transform.SetParent(root, false); // Default layer: arrows stick in it, the rifle stops at it
            var edge = roof.AddComponent<EdgeCollider2D>();
            edge.points = System.Array.ConvertAll(Roof, p => World(p.x - HutMiddle, p.y));

            float shoulder = World(0f, SniperShoulderRow).y;
            Vector2 hip = new Vector2(x, shoulder - Fighter.ShoulderHeight);
            Perch(root, hip + Vector2.down * (Fighter.HipHeight + 0.05f), 2f);
            Fighter sniper = Bot(WeaponType.SniperRifle, hip, 0.75f, out GameObject sword);
            foreach (Collider2D own in sniper.GetComponents<Collider2D>())
            {
                Physics2D.IgnoreCollision(own, ground); // it stands below the ground line, in the foreground
                Physics2D.IgnoreCollision(own, edge);
            }

            posts.Add(new Post { Root = root.gameObject, Right = x + (HutCrop.xMax - HutMiddle) / CanvasPixelsPerUnit, Bot = sniper, Sword = sword });
        }

        private static void Art(Transform root, string name, Sprite sprite, int order)
        {
            var art = new GameObject(name).AddComponent<SpriteRenderer>();
            art.transform.SetParent(root, false);
            art.sprite = sprite;
            art.sortingOrder = order;
        }

        /// <summary>Invisible floor under a bot (Ignore Raycast, like the ground).</summary>
        private static void Perch(Transform root, Vector2 top, float width)
        {
            var go = new GameObject("Perch") { layer = 2 };
            go.transform.SetParent(root, false);
            go.transform.position = top;
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(width, 0.3f);
            box.offset = new Vector2(0f, -0.15f);
        }

        /// <summary>
        /// A clone of the scene's enemy with <paramref name="weapon"/>, standing still at <paramref name="hip"/> and
        /// shooting at the player. <paramref name="sword"/> is its katana, hidden by the weapon: cleared away with the post.
        /// </summary>
        private Fighter Bot(WeaponType weapon, Vector2 hip, float damage, out GameObject sword)
        {
            Fighter bot = Bots.Clone(template, player);
            if (bot.TryGetComponent(out AIDriver ai)) ai.enabled = false;
            bot.usesVines = false;
            bot.MaxHealth = 30f; // a bounced arrow can bring it down
            bot.damageScale = damage;
            sword = bot.sword.gameObject;
            bot.gameObject.SetActive(true);
            sword.SetActive(true);
            WeaponFactory.Prepare(weapon, bot)?.Equip(bot); // after activation: the weapon poses itself off the bot's body
            bot.gameObject.AddComponent<RunGunner>();
            bot.ResetFighter(hip);
            bot.ControlsEnabled = true;
            return bot;
        }

        private static void Clear(Post post)
        {
            if (post.Bot != null)
            {
                if (post.Bot.heldWeapon != null) Destroy(post.Bot.heldWeapon.gameObject);
                Destroy(post.Bot.gameObject);
            }

            Destroy(post.Sword);
            Destroy(post.Root);
        }

        private void ClearPosts()
        {
            foreach (Post post in posts) Clear(post);
            posts.Clear();
        }

        private void UpdateLabel() => duel.ShowSurvivalLabel($"RUN!  {distance:0} m   {Clock(time)}");

        private static string Clock(float seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";
    }
}
