using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StickDuel
{
    /// <summary>Best-of match flow (ROUND n → FIGHT → K.O.), HUD bars/pips and restart.</summary>
    public sealed class DuelManager : MonoBehaviour
    {
        [SerializeField] private Fighter player;
        [SerializeField] private Fighter enemy;
        [SerializeField] private Vector2 playerSpawn = new Vector2(-3f, Fighter.HipHeight + 0.05f);
        [SerializeField] private Vector2 enemySpawn = new Vector2(3f, Fighter.HipHeight + 0.05f);
        [SerializeField, Min(1)] private int roundsToWin = 3;
        [Tooltip("Off = the enemy is a training dummy: it stands still holding a guard but can still be hit and K.O.'d.")]
        [SerializeField] private bool enemyAI = true;

        [Header("HUD")]
        [SerializeField] private RectTransform playerFill;
        [SerializeField] private RectTransform playerLag;
        [SerializeField] private RectTransform enemyFill;
        [SerializeField] private RectTransform enemyLag;
        [SerializeField] private Image[] playerPips;
        [SerializeField] private Image[] enemyPips;
        [SerializeField] private Text playerName;
        [SerializeField] private Text enemyName;
        [SerializeField] private Text centerText;
        [SerializeField] private Text subText;
        [SerializeField] private Text hintText;

        private int playerScore;
        private int enemyScore;
        private int round;
        private float playerLagValue = 1f;
        private float enemyLagValue = 1f;
        private float hintAlpha = 1f;
        private Coroutine flow;
        private RectTransform playerStamina;
        private RectTransform enemyStamina;
        private PlayerDriver playerThrower;
        private Image[] shurikenIcons;

        public int Round => round;
        public int PlayerScore => playerScore;
        public int EnemyScore => enemyScore;
        public Fighter Player => player;
        public Fighter Enemy => enemy;

        private void Start()
        {
            player.opponent = enemy;
            enemy.opponent = player;
            var ai = enemy.GetComponent<AIDriver>();
            if (ai != null)
            {
                ai.enabled = enemyAI;
            }

            playerName.text = player.displayName;
            enemyName.text = enemy.displayName;
            playerName.color = player.color;
            enemyName.color = enemy.color;
            playerStamina = CreateStaminaBar(playerFill, playerPips, false);
            enemyStamina = CreateStaminaBar(enemyFill, enemyPips, true);
            playerThrower = player.GetComponent<PlayerDriver>();
            if (playerThrower != null)
            {
                shurikenIcons = CreateShurikenIcons(playerFill, playerThrower.MaxShuriken);
            }

            flow = StartCoroutine(Match());
        }

        /// <summary>Builds a thin stamina bar under a health bar by cloning the health bar's frame.</summary>
        private static RectTransform CreateStaminaBar(RectTransform healthFill, Image[] pips, bool fromRight)
        {
            var frame = (RectTransform)healthFill.parent;
            var copy = (RectTransform)Instantiate(frame.gameObject, frame.parent).transform;
            copy.name = frame.name + "Stamina";
            Destroy(copy.Find("Lag").gameObject);

            float side = fromRight ? -1f : 1f;
            float width = frame.sizeDelta.x * 0.6f;
            float outerEdge = frame.anchoredPosition.x - side * frame.sizeDelta.x * 0.5f;
            copy.sizeDelta = new Vector2(width, 16f);
            copy.anchoredPosition = new Vector2(outerEdge + side * width * 0.5f, frame.anchoredPosition.y - 32f);

            var fill = (RectTransform)copy.Find("Fill");
            fill.offsetMin = new Vector2(3f, 3f);
            fill.offsetMax = new Vector2(-3f, -3f);
            fill.GetComponent<Image>().color = new Color(1f, 0.8f, 0.3f);

            foreach (Image pip in pips)
            {
                pip.rectTransform.anchoredPosition += new Vector2(0f, -18f);
            }

            return fill;
        }

        /// <summary>
        /// One shuriken per remaining throw, in the pips row next to the round pips. Each slot has a
        /// dim silhouette behind a radial fill, so an empty set visibly refills while reloading.
        /// </summary>
        private static Image[] CreateShurikenIcons(RectTransform healthFill, int count)
        {
            var frame = (RectTransform)healthFill.parent;
            float outerEdge = frame.anchoredPosition.x - frame.sizeDelta.x * 0.5f;
            var icons = new Image[count];
            for (int i = 0; i < count; i++)
            {
                var slot = new GameObject("Shuriken" + i, typeof(RectTransform));
                var rect = (RectTransform)slot.transform;
                rect.SetParent(frame.parent, false);
                rect.anchorMin = rect.anchorMax = frame.anchorMin;
                rect.sizeDelta = new Vector2(40f, 40f);
                rect.anchoredPosition = new Vector2(outerEdge + 205f + i * 46f, frame.anchoredPosition.y - 66f);

                Image back = slot.AddComponent<Image>();
                back.sprite = Shuriken.Sprite;
                back.color = new Color(0.1f, 0.06f, 0.14f, 0.6f);
                back.raycastTarget = false;

                var front = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
                front.rectTransform.SetParent(rect, false);
                front.rectTransform.anchorMin = Vector2.zero;
                front.rectTransform.anchorMax = Vector2.one;
                front.rectTransform.sizeDelta = Vector2.zero;
                front.sprite = Shuriken.Sprite;
                front.type = Image.Type.Filled;
                front.fillMethod = Image.FillMethod.Radial360;
                front.fillOrigin = (int)Image.Origin360.Top;
                front.fillClockwise = true;
                front.raycastTarget = false;
                icons[i] = front;
            }

            return icons;
        }

        private void UpdateShurikenIcons()
        {
            if (shurikenIcons == null)
            {
                return;
            }

            int count = playerThrower.ShurikenCount;
            float reload = playerThrower.ReloadProgress;
            for (int i = 0; i < shurikenIcons.Length; i++)
            {
                bool ready = i < count;
                Image icon = shurikenIcons[i];
                icon.fillAmount = ready ? 1f : reload;
                // Refilling icons stay greyed out until the set is back.
                icon.color = ready ? Color.white : new Color(0.55f, 0.55f, 0.6f, 0.8f);
            }
        }

        private void Update()
        {
            UpdateShurikenIcons();
            UpdateBar(playerFill, playerLag, player, ref playerLagValue, false);
            UpdateBar(enemyFill, enemyLag, enemy, ref enemyLagValue, true);
            SetBar(playerStamina, player.Stamina / player.MaxStamina, false);
            SetBar(enemyStamina, enemy.Stamina / enemy.MaxStamina, true);

            Transform center = centerText.transform;
            center.localScale = Vector3.Lerp(center.localScale, Vector3.one, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));

            if (round > 1)
            {
                hintAlpha = Mathf.MoveTowards(hintAlpha, 0f, Time.unscaledDeltaTime * 0.5f);
                hintText.color = new Color(1f, 1f, 1f, hintAlpha * 0.8f);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                Restart();
            }
        }

        public void Restart()
        {
            if (flow != null)
            {
                StopCoroutine(flow);
            }

            if (Fx.Instance != null)
            {
                Fx.Instance.BaseTimeScale = 1f;
            }

            flow = StartCoroutine(Match());
        }

        private IEnumerator Match()
        {
            playerScore = 0;
            enemyScore = 0;
            round = 0;
            UpdatePips();

            while (playerScore < roundsToWin && enemyScore < roundsToWin)
            {
                round++;
                yield return PlayRound();
            }

            bool won = playerScore > enemyScore;
            ResetFighters();
            SetControls(false);
            Show(won ? "VICTORY" : "DEFEAT", "Press R to duel again", won ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.4f, 0.4f));
            if (Fx.Instance != null)
            {
                Fx.Instance.Gong();
            }
        }

        private IEnumerator PlayRound()
        {
            ResetFighters();
            SetControls(false);

            Show($"ROUND {round}", round == 1 ? "First to " + roundsToWin : $"{playerScore} - {enemyScore}", Color.white);
            if (Fx.Instance != null) Fx.Instance.Beep();
            yield return new WaitForSecondsRealtime(1.2f);

            Show("FIGHT!", "", new Color(1f, 0.85f, 0.4f));
            if (Fx.Instance != null) Fx.Instance.Gong();
            SetControls(true);
            yield return new WaitForSecondsRealtime(0.7f);
            Hide();

            yield return new WaitUntil(() => player.IsDead || enemy.IsDead);
            yield return new WaitForSeconds(0.12f); // grace window: simultaneous kills are a draw

            if (Fx.Instance != null)
            {
                Fx.Instance.Knockout();
                Fx.Instance.BaseTimeScale = 0.3f;
            }

            if (player.IsDead && enemy.IsDead)
            {
                Show("DOUBLE K.O.", "No point awarded", Color.white);
            }
            else if (enemy.IsDead)
            {
                playerScore++;
                Show("K.O.", "You take the round", player.color);
            }
            else
            {
                enemyScore++;
                Show("K.O.", enemy.displayName + " takes the round", enemy.color);
            }

            UpdatePips();
            yield return new WaitForSecondsRealtime(2.4f);
            if (Fx.Instance != null)
            {
                Fx.Instance.BaseTimeScale = 1f;
            }

            Hide();
        }

        private void ResetFighters()
        {
            player.ResetFighter(playerSpawn);
            enemy.ResetFighter(enemySpawn);
            if (playerThrower != null)
            {
                playerThrower.RefillShuriken();
            }

            playerLagValue = enemyLagValue = 1f;
        }

        private void SetControls(bool enabled)
        {
            player.ControlsEnabled = enabled;
            enemy.ControlsEnabled = enabled;
        }

        private void Show(string title, string subtitle, Color color)
        {
            centerText.text = title;
            centerText.color = color;
            centerText.enabled = true;
            centerText.transform.localScale = Vector3.one * 1.5f;
            subText.text = subtitle;
            subText.enabled = !string.IsNullOrEmpty(subtitle);
        }

        private void Hide()
        {
            centerText.enabled = false;
            subText.enabled = false;
        }

        private void UpdatePips()
        {
            for (int i = 0; i < playerPips.Length; i++)
            {
                playerPips[i].color = i < playerScore ? player.color : new Color(1f, 1f, 1f, 0.18f);
            }

            for (int i = 0; i < enemyPips.Length; i++)
            {
                enemyPips[i].color = i < enemyScore ? enemy.color : new Color(1f, 1f, 1f, 0.18f);
            }
        }

        private static void UpdateBar(RectTransform fill, RectTransform lag, Fighter fighter, ref float lagValue, bool fromRight)
        {
            float value = fighter.Health / fighter.MaxHealth;
            lagValue = lagValue > value ? Mathf.MoveTowards(lagValue, value, Time.unscaledDeltaTime * 0.6f) : value;
            SetBar(fill, value, fromRight);
            SetBar(lag, lagValue, fromRight);
        }

        private static void SetBar(RectTransform bar, float value, bool fromRight)
        {
            bar.anchorMin = new Vector2(fromRight ? 1f - value : 0f, 0f);
            bar.anchorMax = new Vector2(fromRight ? 1f : value, 1f);
            bar.gameObject.SetActive(value > 0.001f);
        }
    }
}
