using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StickDuel
{
    /// <summary>
    /// Endless Survival: the battlefield (it scrolls sideways forever, the camera follows the
    /// player), the player's own weapon, and bots (clones of the scene's enemy, see
    /// <see cref="Bots.Clone"/>) dropping in just off screen on either side for as long as the player stands.
    /// The pressure never lets up: a hidden wave level rises every few kills, raising how many are
    /// on the field at once and how tough each new one is, and whoever is missing drops in at a
    /// steady pace. A bot left far behind off screen is brought back just outside the view on its
    /// side, so the fight never strings out. The player sees no waves: only enemies, and the clock
    /// and kill count in the HUD. Dying ends the run and shows how long it
    /// lasted and how many fell; R starts over. No rooms, no crown, no checkpoints. Drives
    /// DuelManager's HUD in place of its match flow (DuelManager.RunMatch = false). Added by
    /// Arenas; a scene reload removes it and every bot.
    /// </summary>
    public sealed class EndlessSurvivalMode : MonoBehaviour
    {
        private const float CorpseLifetime = 3f;
        private const float DropHeight = 3.5f;
        private const float OffScreen = 1.5f; // bots drop in at least this far past the edge of the view
        private const float Spread = 4f;      // ...plus up to this much more, so they arrive one by one, not in a clump
        private const float SpawnInterval = 1.2f;
        private const int KillsPerWave = 8;
        private const int MaxAlive = 4;
        private const float Leash = 3f; // past the edge of the view by more than this, a bot is brought back

        private DuelManager duel;
        private CameraRig cameraRig;
        private Fighter template;
        private PlayerDriver thrower;
        private float reach;
        private Vector2 home;
        private readonly List<Fighter> bots = new List<Fighter>();
        private int spawnSide = 1;
        private int kills;
        private float nextSpawnTime, startTime, survived;
        private bool running, over;

        /// <summary>Hidden difficulty level: 1 at the start, up one every <see cref="KillsPerWave"/> kills.</summary>
        public int Wave => 1 + kills / KillsPerWave;
        private int TargetAlive => Mathf.Min(1 + Wave, MaxAlive);
        public int Kills => kills;
        public int Alive => bots.Count;
        /// <summary>Seconds the current (or last) run lasted.</summary>
        public float Survived => over ? survived : running ? Time.time - startTime : 0f;
        public bool IsOver => over;

        /// <summary>Call right after adding the component, before DuelManager is enabled.</summary>
        /// <param name="reach">How far the floor goes either way (bots may roam all of it).</param>
        public void Init(DuelManager duel, float reach)
        {
            this.duel = duel;
            this.reach = reach;
            duel.RunMatch = false;
            thrower = duel.Player.GetComponent<PlayerDriver>();
            home = duel.Player.Body.position;

            // The scene's enemy is only the mould the bots are cast from.
            template = duel.Enemy;
            template.gameObject.SetActive(false);
            template.sword.gameObject.SetActive(false);

            cameraRig = Camera.main.GetComponent<CameraRig>();
            if (Fx.Instance != null) Fx.Instance.Calm = true; // no slow motion: the horde keeps coming
        }

        private void Start()
        {
            StartCoroutine(Run());
        }

        /// <summary>A whole new run.</summary>
        public void Restart()
        {
            StopAllCoroutines();
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            running = false;
            yield return null; // DuelManager.Start runs first: it builds the HUD

            Fighter player = duel.Player;
            ClearBots();
            kills = 0;
            over = false;
            player.opponent = null;
            player.ResetFighter(home);
            cameraRig.SetFixedFollow(player); // the field scrolls, so the view stays on the player
            if (thrower != null) thrower.RefillShuriken();
            duel.HideEnemyBars();
            UpdateTargets();
            UpdateLabel();

            player.ControlsEnabled = false;
            duel.Show("ENDLESS SURVIVAL", "They keep coming. How long can you last?", Color.white);
            if (Fx.Instance != null) Fx.Instance.Beep();
            yield return new WaitForSecondsRealtime(1.2f);
            duel.Show("FIGHT!", "", new Color(1f, 0.85f, 0.4f));
            if (Fx.Instance != null) Fx.Instance.Gong();
            player.ControlsEnabled = true;
            startTime = Time.time;
            nextSpawnTime = Time.time; // the first one drops in right away
            running = true;
            yield return new WaitForSecondsRealtime(0.7f);
            duel.Hide();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (over && keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                Restart(); // R only once the run has ended: while alive it would be a free heal
            }

            if (!running || over)
            {
                return;
            }

            Fighter player = duel.Player;
            if (player.IsDead)
            {
                over = true;
                survived = Time.time - startTime;
                StartCoroutine(Fell());
                return;
            }

            bool changed = false;
            for (int i = bots.Count - 1; i >= 0; i--)
            {
                if (bots[i].IsDead)
                {
                    kills++;
                    Destroy(bots[i].gameObject, CorpseLifetime);
                    Destroy(bots[i].sword.gameObject, CorpseLifetime);
                    bots.RemoveAt(i);
                    changed = true;
                }
            }

            if (bots.Count < TargetAlive && Time.time >= nextSpawnTime)
            {
                nextSpawnTime = Time.time + SpawnInterval;
                spawnSide = -spawnSide;
                bots.Add(Spawn(spawnSide));
                changed = true;
            }

            KeepClose(player);

            if (changed)
            {
                UpdateTargets();
            }

            UpdateLabel();
            player.opponent = Nearest(player);
        }

        /// <summary>Just past the edge of the view on <paramref name="side"/> of the player, up in the air.</summary>
        private Vector2 DropPoint(int side)
        {
            Camera view = Camera.main;
            float halfView = view.orthographicSize * view.aspect;
            float x = Mathf.Clamp(duel.Player.Body.position.x + side * (halfView + OffScreen + Random.Range(0f, Spread)), -reach + 2f, reach - 2f);
            return new Vector2(x, home.y + DropHeight);
        }

        /// <summary>A bot left far behind (the player ran off) drops back in just off screen on its side, health untouched.</summary>
        private void KeepClose(Fighter player)
        {
            Camera view = Camera.main;
            float limit = view.orthographicSize * view.aspect + Leash;
            foreach (Fighter bot in bots)
            {
                float dx = bot.Body.position.x - player.Body.position.x;
                if (Mathf.Abs(dx) > limit)
                {
                    bot.Body.position = DropPoint(dx > 0f ? 1 : -1);
                    bot.Body.linearVelocity = Vector2.zero;
                    if (bot.rig != null) bot.rig.Snap();
                }
            }
        }

        /// <summary>Drops one bot in just off screen on the <paramref name="side"/> of the player, tougher with every wave.</summary>
        private Fighter Spawn(int side)
        {
            Fighter bot = Bots.Clone(template, duel.Player);
            float grade = Mathf.Min(Wave - 1, 20); // difficulty tops out at wave 21
            bot.MaxHealth = 40f + grade * 5f;
            bot.damageScale = Mathf.Min(0.35f + grade * 0.025f, 0.8f);
            var ai = bot.GetComponent<AIDriver>();
            ai.enabled = true;
            ai.Skill = Mathf.Min(0.4f + grade * 0.03f, 0.95f);
            ai.SetBounds(-reach, reach);

            bot.gameObject.SetActive(true);
            bot.sword.gameObject.SetActive(true);
            bot.ResetFighter(DropPoint(side));
            bot.ControlsEnabled = true;
            return bot;
        }

        private IEnumerator Fell()
        {
            foreach (Fighter bot in bots) bot.ControlsEnabled = false;
            if (Fx.Instance != null) Fx.Instance.Knockout();
            yield return new WaitForSecondsRealtime(1f);
            duel.Show("YOU FELL", $"Survived {Clock(survived)}    {kills} kills      Press R to try again", new Color(1f, 0.4f, 0.4f));
        }

        private void ClearBots()
        {
            foreach (Fighter bot in bots)
            {
                Destroy(bot.sword.gameObject);
                Destroy(bot.gameObject);
            }

            bots.Clear();
        }

        private Fighter Nearest(Fighter player)
        {
            Fighter best = null;
            float bestDistance = float.MaxValue;
            foreach (Fighter bot in bots)
            {
                float d = Mathf.Abs(bot.Body.position.x - player.Body.position.x);
                if (d < bestDistance)
                {
                    best = bot;
                    bestDistance = d;
                }
            }

            return best;
        }

        private void UpdateTargets()
        {
            var targets = new Fighter[bots.Count + 1];
            targets[0] = duel.Player;
            bots.CopyTo(targets, 1);
            cameraRig.Targets = targets;
        }

        private void UpdateLabel() => duel.ShowSurvivalLabel($"TIME {Clock(Survived)}   KILLS {kills}");

        private static string Clock(float seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";
    }
}
