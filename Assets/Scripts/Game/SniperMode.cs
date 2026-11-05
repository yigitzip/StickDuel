using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StickDuel
{
    /// <summary>
    /// Sniper Mode: the player and the scene's enemy, both with only a <see cref="SniperRifleWeapon"/>
    /// (whatever the menu says), through the <see cref="SniperMap"/> maps in order from the one
    /// picked in the menu. Shoot the bot and the next map loads; get shot (or fall) and the same map
    /// starts over; clear the last map to win, R plays again. The enemy is driven by
    /// <see cref="SniperBotController"/> (its sword <see cref="AIDriver"/> is switched off). The
    /// camera fills the screen with the painting and slides sideways with the player.
    /// Drives DuelManager's HUD in place of its match flow (DuelManager.RunMatch = false). Added by
    /// Arenas; Esc reloads the scene, which removes it, the map and both rifles.
    /// </summary>
    public sealed class SniperMode : MonoBehaviour
    {
        [Tooltip("Both fighters' health in this mode (rifle: 45 a body shot, x1.5 to the head).")]
        [SerializeField, Min(1f)] private float fighterHealth = 100f;

        private DuelManager duel;
        private Fighter player, enemy;
        private AIDriver swordAi;
        private SniperBotController bot;
        private SniperMap.Layout map;
        private int firstMap, stage;
        private bool over;

        /// <summary>Call right after adding the component, before DuelManager is enabled.</summary>
        public void Init(DuelManager duel, int startMap)
        {
            this.duel = duel;
            firstMap = stage = Mathf.Clamp(startMap, 0, SniperMap.Count - 1);
            duel.RunMatch = false;
            player = duel.Player;
            enemy = duel.Enemy;

            Arm(player);
            Arm(enemy);
            player.MaxHealth = enemy.MaxHealth = fighterHealth;

            if (player.TryGetComponent(out PlayerDriver driver))
            {
                driver.SniperOnly = true; // no shuriken, no punch
            }
            else
            {
                Debug.LogError("SniperMode: the player has no PlayerDriver.", player);
            }

            swordAi = enemy.GetComponent<AIDriver>();
            if (swordAi != null)
            {
                swordAi.enabled = false;
            }

            bot = enemy.TryGetComponent(out SniperBotController existing) ? existing : enemy.gameObject.AddComponent<SniperBotController>();
        }

        /// <summary>The rifle in place of the fighter's sword (or other weapon), through the usual factory.</summary>
        private static void Arm(Fighter fighter)
        {
            IWeaponBehaviour rifle = WeaponFactory.Prepare(WeaponType.SniperRifle, fighter);
            if (rifle == null)
            {
                Debug.LogError($"SniperMode: no sniper rifle for {fighter.name}.", fighter);
                return;
            }

            rifle.Equip(fighter);
        }

        private void Start()
        {
            StartCoroutine(Run());
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (over && keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                StopAllCoroutines();
                stage = firstMap;
                StartCoroutine(Run());
            }
        }

        private IEnumerator Run()
        {
            over = false;
            yield return null; // DuelManager.Start runs first: it builds the HUD and switches the enemy's AIDriver back on
            if (swordAi != null)
            {
                swordAi.enabled = false;
            }

            duel.HideShurikenIcons();
            while (true)
            {
                LoadMap(stage);
                duel.Show(map.Name, $"MAP {stage + 1}/{SniperMap.Count}   {SniperMap.Blurb(stage)}", Color.white);
                if (Fx.Instance != null) Fx.Instance.Beep();
                yield return new WaitForSecondsRealtime(1.6f);
                duel.Show("FIGHT!", "", new Color(1f, 0.85f, 0.4f));
                if (Fx.Instance != null) Fx.Instance.Gong();
                duel.SetControls(true);
                yield return new WaitForSecondsRealtime(0.7f);
                duel.Hide();

                yield return new WaitUntil(() => player.IsDead || enemy.IsDead);
                yield return new WaitForSeconds(0.12f); // simultaneous kills are a draw
                duel.SetControls(false);
                if (Fx.Instance != null) Fx.Instance.Knockout();

                if (enemy.IsDead && !player.IsDead)
                {
                    if (stage == SniperMap.Count - 1)
                    {
                        over = true;
                        duel.Show("ALL MAPS CLEARED", "Press R to play again", player.color);
                        yield break;
                    }

                    duel.Show("TARGET DOWN", "Next: " + SniperMap.Name(stage + 1), player.color);
                    yield return new WaitForSecondsRealtime(2.2f);
                    stage++;
                }
                else
                {
                    duel.Show(player.IsDead && enemy.IsDead ? "DOUBLE K.O." : "YOU WERE SHOT", "Same map again", enemy.color);
                    yield return new WaitForSecondsRealtime(2.2f);
                }
            }
        }

        /// <summary>Clears the last map, builds <paramref name="index"/> and puts both fighters on their spawns.</summary>
        private void LoadMap(int index)
        {
            if (map != null && map.Root != null)
            {
                Destroy(map.Root.gameObject);
            }

            map = SniperMap.Build(index);
            bot.Spots = map.Spots;
            duel.ShowSurvivalLabel($"SNIPER  {index + 1}/{SniperMap.Count}");
            duel.SetControls(false);
            player.ResetFighter(map.PlayerSpawn); // also puts a thrown rifle back in the hands
            enemy.ResetFighter(map.EnemySpawn);
            FrameMap();
        }

        /// <summary>
        /// The painting fills the screen top to bottom; the view follows the player sideways at a
        /// fixed zoom, clamped to the painting's edges, never up or down.
        /// </summary>
        private void FrameMap()
        {
            Camera view = Camera.main;
            CameraRig rig = view != null ? view.GetComponent<CameraRig>() : null;
            if (rig == null)
            {
                Debug.LogError("SniperMode: no main camera with a CameraRig.", this);
                return;
            }

            float height = map.Top - map.Bottom;
            float size = Mathf.Min(height * 0.5f, SniperMap.HalfWidth / Mathf.Max(view.aspect, 0.5f));
            float centre = (map.Top + map.Bottom) * 0.5f;
            rig.SetBounds(-SniperMap.HalfWidth, SniperMap.HalfWidth, size);
            rig.MinY = centre;
            rig.MaxY = centre;
            rig.SetFixedFollow(player, view.orthographicSize / size);
            rig.Targets = new[] { player, enemy };
        }
    }
}
