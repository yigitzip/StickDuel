using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StickDuel.EditorTools
{
    /// <summary>
    /// Lets an external tool drive the open editor for quick checks: write one command to
    /// Temp/bridge_in.txt, the result is appended to Temp/bridge_out.txt.
    /// Commands: refresh | reimport &lt;folder&gt; | play | stop | shot &lt;png path&gt; | start &lt;weapon&gt; &lt;map&gt; | autoplayer | slow &lt;scale&gt;
    /// | state | reload | menu | icicle | find &lt;name&gt; | stopmove | jumptest &lt;x&gt; [run dir] (put the player at x, mash jump while the first jump is still rising, then `jumps` reports how many fired: 1 = no mid-air re-jump)
    /// </summary>
    [InitializeOnLoad]
    public static class TestBridge
    {
        private const string InPath = "Temp/bridge_in.txt";
        private const string OutPath = "Temp/bridge_out.txt";

        private static double jumpUntil;
        private static int jumps;
        private static Fighter jumper;

        static TestBridge()
        {
            EditorApplication.update += Poll;
            EditorApplication.update += MashJump;
        }

        private static void MashJump()
        {
            if (EditorApplication.timeSinceStartup > jumpUntil || jumper == null || !EditorApplication.isPlaying)
            {
                return;
            }

            jumper.RequestJump();
        }

        private static void CountJump(Fighter fighter) => jumps++;

        private static void Poll()
        {
            if (!File.Exists(InPath))
            {
                return;
            }

            string command = File.ReadAllText(InPath).Trim();
            File.Delete(InPath);
            string result;
            try
            {
                result = Run(command);
            }
            catch (Exception e)
            {
                result = "ERROR " + e.Message;
            }

            File.AppendAllText(OutPath, $"{DateTime.Now:HH:mm:ss} {command} => {result}\n");
        }

        private static string Run(string command)
        {
            string[] parts = command.Split(' ');
            switch (parts[0])
            {
                case "refresh":
                    AssetDatabase.Refresh();
                    return "ok";
                case "reimport":
                    AssetDatabase.ImportAsset(parts[1], ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
                    return "ok";
                case "play":
                    EditorApplication.isPlaying = true;
                    return "ok";
                case "stop":
                    EditorApplication.isPlaying = false;
                    return "ok";
                case "shot":
                    ScreenCapture.CaptureScreenshot(parts[1]);
                    return "ok";
                case "start":
                    if (Arenas.Instance == null) return "no Arenas";
                    Arenas.Instance.StartWith(int.Parse(parts[1]), int.Parse(parts[2]));
                    return "ok";
                case "autoplayer":
                    // Let the AI drive the player too, to watch a full fight.
                    var duel = UnityEngine.Object.FindAnyObjectByType<DuelManager>();
                    duel.Player.GetComponent<PlayerDriver>().enabled = false;
                    duel.Player.gameObject.AddComponent<AIDriver>();
                    return "ok";
                case "slow":
                    Time.timeScale = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                    return "ok";
                case "jumptest":
                    var fight = UnityEngine.Object.FindAnyObjectByType<DuelManager>();
                    jumper = fight.Player;
                    jumper.GetComponent<PlayerDriver>().enabled = false;
                    var ai = jumper.GetComponent<AIDriver>();
                    if (ai != null) UnityEngine.Object.Destroy(ai);
                    fight.Enemy.ControlsEnabled = false;
                    jumper.ControlsEnabled = true;
                    // Optional run direction (-1/1) held through the jump: tests reaching a ledge with a running jump.
                    jumper.moveInput = parts.Length > 2 ? float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                    jumper.Body.position = new Vector2(float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), Fighter.HipHeight + 0.02f);
                    jumper.Body.linearVelocity = Vector2.zero;
                    jumper.Jumped -= CountJump;
                    jumper.Jumped += CountJump;
                    jumps = 0;
                    jumpUntil = EditorApplication.timeSinceStartup + 0.45; // still rising (apex ~0.59 s at game speed 0.8)
                    return "ok";
                case "stopmove":
                    if (jumper != null) jumper.moveInput = 0f;
                    return "ok";
                case "jumps":
                    return $"jumps {jumps} pos {(jumper != null ? jumper.Body.position : Vector2.zero)} grounded {(jumper != null && jumper.IsGrounded)}";
                case "reload":
                    // Same as Esc in game.
                    UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                    return "ok";
                case "icicle":
                    // Drop one icicle straight onto the player.
                    var d2 = UnityEngine.Object.FindAnyObjectByType<DuelManager>();
                    Arenas.Instance.DropIcicles(null, new[] { d2.Player.Body.position.x });
                    return $"player hp {d2.Player.Health:0}";
                case "icicles":
                    // How many icicles exist right now (to time the drops from outside).
                    return "icicles " + UnityEngine.Object.FindObjectsByType<Icicle>(FindObjectsSortMode.None).Length + " t " + Time.timeSinceLevelLoad.ToString("0.0");
                case "find":
                    // Describe live sprites with this name.
                    var found = new System.Text.StringBuilder();
                    foreach (var r in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                    {
                        if (r.name != parts[1]) continue;
                        found.Append($"[{r.transform.position} scale {r.transform.lossyScale} sprite {(r.sprite != null ? r.sprite.rect.size.ToString() : "null")} mat {(r.sharedMaterial != null ? r.sharedMaterial.shader.name : "null")} col {r.color} order {r.sortingOrder} vis {r.isVisible}] ");
                    }

                    return found.Length > 0 ? found.ToString() : "none";
                case "menu":
                    return GameObject.Find("MainMenu") != null ? "menu open" : "no menu";
                case "state":
                    var d = UnityEngine.Object.FindAnyObjectByType<DuelManager>();
                    return d == null ? "no duel" : $"round {d.Round} score {d.PlayerScore}-{d.EnemyScore} hp {d.Player.Health:0}/{d.Enemy.Health:0} pos {d.Player.Body.position} {d.Enemy.Body.position}";
                default:
                    return "unknown";
            }
        }
    }
}
