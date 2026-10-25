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

        private DuelManager duel;
        private CastleLevel castle;
        private CameraRig cameraRig;
        private Fighter template;
        private PlayerDriver thrower;
        private readonly List<Fighter> guards = new List<Fighter>();
        private int cleared;         // rooms cleared so far (they are cleared in order)
        private int active = -1;     // room whose guards are out, -1 between rooms
        private int checkpoint = -1; // last room entered, -1 = still at the start
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

            cameraRig = Camera.main.GetComponent<CameraRig>();
            cameraRig.SetBounds(0f, CastleLevel.Width, 5f);
            if (Fx.Instance != null) Fx.Instance.Calm = true; // Fx lives in the scene: a reload resets it
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
            foreach (CastleLevel.Room room in castle.Rooms)
            {
                if (room.Gate != null) room.Gate.SetActive(true);
            }

            castle.CrownObject.SetActive(false);
            cleared = 0;
            active = -1;
            checkpoint = -1;
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
                CastleLevel.Room room = castle.Rooms[active];
                if (room.Gate != null) room.Gate.SetActive(false);
                cleared++;
                active = -1;
                castle.CrownObject.SetActive(cleared == castle.Rooms.Length);
            }

            if (active < 0 && cleared < castle.Rooms.Length && castle.Rooms[cleared].Contains(feet))
            {
                Enter(cleared);
                changed = true;
            }

            if (changed)
            {
                UpdateTargets();
                UpdateLabel();
            }

            player.opponent = Nearest(player);
            if (cleared == castle.Rooms.Length && Vector2.Distance(player.Body.position, castle.Crown) < CrownReach)
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
            for (int i = 0; i < room.Guards.Length; i++)
            {
                float x = Mathf.Clamp(player.Body.position.x + room.Direction * (6f + i * 1.6f), room.Left + 1f, room.Right - 1f);
                guards.Add(Spawn(room.Guards[i], new Vector2(x, room.FloorY + Fighter.HipHeight + 0.05f), room));
            }
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
            ai.SetBounds(room.Left, room.Right); // after Awake, which sets the scene's arena bounds
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
