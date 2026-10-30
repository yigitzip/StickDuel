using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StickDuel
{
    /// <summary>
    /// Survival = storm the castle (CastleLevel) and take the crown. Walking into the next room
    /// spawns its guards (clones of the scene's enemy) in front of the player; the room's gate opens
    /// when they are all down. Dying sends you back to the last room you entered, at full health.
    /// No slow motion and no banners between fights: the game keeps flowing. Drives DuelManager's
    /// HUD in place of its match flow (DuelManager.RunMatch = false). Added by Arenas; a scene
    /// reload removes it, the castle and every guard.
    /// </summary>
    public sealed class SurvivalMode : MonoBehaviour
    {
        private const float CorpseLifetime = 3f;
        private const float CrownReach = 1.2f;
        private const float SpawnOutsideDistance = 8f;
        private const float SpawnSpacing = 1.6f;
        private const int MinimumGuardsPerRoom = 3;

        private DuelManager duel;
        private CastleLevel castle;
        private CameraRig cameraRig;
        private Fighter template;
        private PlayerDriver thrower;
        private readonly List<Fighter> guards = new List<Fighter>();
        private int cleared;         // rooms cleared so far (they are cleared in order)
        private int active = -1;     // room whose guards are out, -1 between rooms
        private int checkpoint = -1; // last room entered, -1 = still at the start
        private int spawnedInRoom;
        private int kills;
        private float startTime;
        private bool running;
        private bool over;
        private bool won;
        private Coroutine flow;

        public int Kills => kills;
        public int Cleared => cleared;
        public int Active => active;
        public int Guards => guards.Count;
        public bool IsOver => over;
        public bool Won => won;

        /// <summary>Call right after adding the component, before DuelManager is enabled.</summary>
        public void Init(DuelManager duel, CastleLevel castle)
        {
            this.duel = duel;
            this.castle = castle;
            duel.RunMatch = false;
            thrower = duel.Player.GetComponent<PlayerDriver>();

            // The scene's enemy is only the mould the guards are cast from.
            template = duel.Enemy;
            template.gameObject.SetActive(false);
            template.sword.gameObject.SetActive(false);

            // Castle look: a medieval arming sword in place of the katana, art only. The katana's
            // reach, hitbox and damage (Equip, just before) stay; guards clone the template's blade.
            Sprite castleSword = CastleSwordSprite();
            duel.Player.sword.GetComponent<SpriteRenderer>().sprite = castleSword;
            template.sword.GetComponent<SpriteRenderer>().sprite = castleSword;

            cameraRig = Camera.main.GetComponent<CameraRig>();
            cameraRig.SetBounds(0f, CastleLevel.Width, 5f);
            cameraRig.SetFixedFollow(duel.Player);
            if (Fx.Instance != null) Fx.Instance.Calm = true; // Fx lives in the scene: a reload resets it
        }

        /// <summary>
        /// Resources/Castle/Sword: pointing right, grip centre at x=80px on the blade axis (mid
        /// height), tip at x=503px. Scaled so the tip lands where the katana's does (Weapon.All[0]).
        /// </summary>
        private static Sprite CastleSwordSprite()
        {
            const float gripPixel = 80f, tipPixel = 503f;
            Weapon katana = Weapon.All[0];
            var texture = Resources.Load<Texture2D>("Castle/Sword");
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(gripPixel / texture.width, 0.5f), (tipPixel - gripPixel) / katana.Tip);
        }

        private void Start()
        {
            flow = StartCoroutine(Run());
        }

        /// <summary>A whole new run from the castle gate.</summary>
        public void Restart()
        {
            StopAllCoroutines();
            flow = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            running = false;
            yield return null; // DuelManager.Start runs first: it builds the HUD

            Fighter player = duel.Player;
            ClearGuards();

            castle.CrownObject.SetActive(false);
            cleared = 0;
            active = -1;
            checkpoint = -1;
            spawnedInRoom = 0;
            kills = 0;
            over = won = false;
            player.opponent = null;
            Respawn(castle.Start);
            duel.HideEnemyBars();
            UpdateLabel();

            player.ControlsEnabled = false;
            duel.Show("THE CROWN", "Fight your way to the top of the castle", Color.white);
            if (Fx.Instance != null) Fx.Instance.Beep();
            yield return new WaitForSecondsRealtime(1.2f);
            duel.Show("FIGHT!", "", new Color(1f, 0.85f, 0.4f));
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
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                // R only once the run has stopped: while alive it would be a free heal.
                if (won) Restart();
                else if (over) Retry();
            }

            if (!running || over)
            {
                return;
            }

            Fighter player = duel.Player;
            Vector2 feet = player.Body.position + Vector2.down * Fighter.HipHeight;
            cameraRig.MinY = CastleLevel.FloorOf(feet.y) * CastleLevel.FloorHeight + 2.9f; // the floor under your feet stays in view

            if (player.IsDead)
            {
                over = true;
                StartCoroutine(Fell());
                return;
            }

            bool changed = false;
            for (int i = guards.Count - 1; i >= 0; i--)
            {
                if (guards[i].IsDead)
                {
                    kills++;
                    Destroy(guards[i].gameObject, CorpseLifetime);
                    Destroy(guards[i].sword.gameObject, CorpseLifetime);
                    guards.RemoveAt(i);
                    changed = true;
                }
            }

            if (active >= 0 && guards.Count == 0)
            {
                CastleLevel.Room room = castle.Rooms[checkpoint];
                if (spawnedInRoom == Mathf.Max(MinimumGuardsPerRoom, room.Guards.Length))
                {
                    cleared++;
                    active = -1;
                    castle.CrownObject.SetActive(checkpoint == castle.Rooms.Length - 1);
                }
            }

            if (active >= 0)
            {
                changed |= SpawnPendingGuards(castle.Rooms[checkpoint], feet);
            }

            int nextRoom = checkpoint + 1;
            if (nextRoom < castle.Rooms.Length && castle.Rooms[nextRoom].HasPassedEntry(feet))
            {
                Enter(nextRoom);
                changed = true;
            }

            if (changed)
            {
                UpdateTargets();
                UpdateLabel();
            }

            player.opponent = Nearest(player);
            if (checkpoint == castle.Rooms.Length - 1 && guards.Count == 0
                && Vector2.Distance(player.Body.position, castle.Crown) < CrownReach)
            {
                Win();
            }
        }

        private void Enter(int index)
        {
            CastleLevel.Room room = castle.Rooms[index];
            Fighter player = duel.Player;
            if (checkpoint >= 0 && castle.Rooms[checkpoint].Floor != room.Floor)
            {
                player.RestoreHealth(); // a new floor: the climb up the tower is the breather
            }

            active = index;
            checkpoint = index;
            spawnedInRoom = 0;
            SpawnPendingGuards(room, player.Body.position + Vector2.down * Fighter.HipHeight);
        }

        private bool SpawnPendingGuards(CastleLevel.Room room, Vector2 feet)
        {
            bool spawned = false;
            int spawnCount = Mathf.Max(MinimumGuardsPerRoom, room.Guards.Length);
            while (spawnedInRoom < spawnCount)
            {
                float progress = spawnCount <= 1
                    ? 0f
                    : CastleLevel.RoomLength * spawnedInRoom / (float)spawnCount;
                if (progress > 0f && !room.HasPassedProgress(feet, progress))
                {
                    break;
                }

                int i = spawnedInRoom++;
                bool spawnAhead = i % 2 == 0;
                float offset = (i / 2) * SpawnSpacing;
                float x = spawnAhead
                    ? room.Entry + room.Direction * (CastleLevel.RoomLength + SpawnOutsideDistance + offset)
                    : feet.x - room.Direction * (SpawnOutsideDistance + offset);
                char tier = i < room.Guards.Length ? room.Guards[i] : 'n';
                guards.Add(Spawn(tier, new Vector2(x, room.FloorY + Fighter.HipHeight + 0.05f), room));
                spawned = true;
            }

            return spawned;
        }

        /// <summary>Clones the template enemy as a guard of <paramref name="room"/>.</summary>
        private Fighter Spawn(char tier, Vector2 position, CastleLevel.Room room)
        {
            // The template is inactive, so the clones are too: wire them up before their Awake runs.
            Fighter guard = Instantiate(template);
            Sword sword = Instantiate(template.sword);
            // Rig and blade trail build their pieces as children at Awake; drop the template's copies.
            KeepOnly(guard.transform, template.headCollider.name);
            KeepOnly(sword.transform, "Edge", "Tip");
            guard.sword = sword;
            sword.Owner = guard;

            guard.isGuard = true;
            guard.opponent = duel.Player;
            switch (tier)
            {
                case 'b': // the Guard Captain
                    guard.MaxHealth = 100f;
                    guard.damageScale = 0.55f;
                    guard.color = new Color(0.7f, 0.12f, 0.25f);
                    break;
                case 'e': // elite
                    guard.MaxHealth = 50f;
                    guard.damageScale = 0.45f;
                    guard.color = new Color(1f, 0.6f, 0.25f);
                    break;
                default: // a duel fighter has 150 health: one clean cut, two at most
                    guard.MaxHealth = 30f;
                    guard.damageScale = 0.4f;
                    break;
            }

            var ai = guard.GetComponent<AIDriver>();
            ai.enabled = true;
            ai.Skill = tier == 'b' ? 0.7f : tier == 'e' ? 0.6f : 0.5f;

            guard.gameObject.SetActive(true);
            sword.gameObject.SetActive(true);
            ai.SetBounds(0f, CastleLevel.Width); // guards can follow the player across former room boundaries
            guard.ResetFighter(position);
            guard.ControlsEnabled = true;
            return guard;
        }

        private static void KeepOnly(Transform parent, params string[] names)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (System.Array.IndexOf(names, child.name) < 0)
                {
                    DestroyImmediate(child);
                }
            }
        }

        private IEnumerator Fell()
        {
            foreach (Fighter guard in guards) guard.ControlsEnabled = false;
            if (Fx.Instance != null) Fx.Instance.Knockout();
            yield return new WaitForSecondsRealtime(1f);
            string room = checkpoint >= 0 ? castle.Rooms[checkpoint].Name : "CASTLE GATE";
            duel.Show("YOU FELL", "Press R to retry from the " + room, new Color(1f, 0.4f, 0.4f));
        }

        /// <summary>Back to the last room entered, at full health; its guards come back too.</summary>
        public void Retry()
        {
            StopAllCoroutines();
            ClearGuards();
            active = -1;
            spawnedInRoom = 0;
            over = false;
            duel.Hide();
            Respawn(checkpoint < 0 ? castle.Start : CheckpointOf(castle.Rooms[checkpoint]));
            UpdateLabel();
        }

        private static Vector2 CheckpointOf(CastleLevel.Room room) => new Vector2(room.Entry + room.Direction * 1.5f, room.FloorY + Fighter.HipHeight + 0.05f);

        private void Respawn(Vector2 position)
        {
            Fighter player = duel.Player;
            player.ResetFighter(position);
            player.ControlsEnabled = true;
            if (thrower != null) thrower.RefillShuriken();
            UpdateTargets();
        }

        private void Win()
        {
            over = won = true;
            castle.CrownObject.SetActive(false);
            duel.Player.ControlsEnabled = false;
            if (Fx.Instance != null) Fx.Instance.Gong();
            float seconds = Time.time - startTime;
            duel.Show("CROWN TAKEN", $"{(int)seconds / 60}:{(int)seconds % 60:00}    {kills} kills      Press R to play again", new Color(1f, 0.85f, 0.4f));
        }

        private void ClearGuards()
        {
            foreach (Fighter guard in guards)
            {
                Destroy(guard.sword.gameObject);
                Destroy(guard.gameObject);
            }

            guards.Clear();
        }

        private Fighter Nearest(Fighter player)
        {
            Fighter best = null;
            float bestDistance = float.MaxValue;
            foreach (Fighter guard in guards)
            {
                float d = Mathf.Abs(guard.Body.position.x - player.Body.position.x);
                if (d < bestDistance)
                {
                    best = guard;
                    bestDistance = d;
                }
            }

            return best;
        }

        private void UpdateTargets()
        {
            var targets = new Fighter[guards.Count + 1];
            targets[0] = duel.Player;
            guards.CopyTo(targets, 1);
            cameraRig.Targets = targets;
        }

        private void UpdateLabel()
        {
            CastleLevel.Room room = castle.Rooms[Mathf.Max(checkpoint, 0)];
            duel.ShowSurvivalLabel($"FLOOR {room.Floor + 1}  {room.Name}   KILLS {kills}");
        }
    }
}
