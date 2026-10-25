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
    /// | survival | castleshot &lt;png&gt; [ppu] | restart | kill enemy|player|guards | tp &lt;x&gt; &lt;y&gt; | walk &lt;dir&gt; | state | reload | menu | icicle | find &lt;name&gt; | stopmove | jumptest &lt;x&gt; [run dir] (put the player at x, mash jump while the first jump is still rising, then `jumps` reports how many fired: 1 = no mid-air re-jump)
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

        private static string CastleShot(string path, int pixelsPerUnit)
        {
            if (GameObject.Find("Castle") == null) return "no castle (start survival first)";
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (var f in UnityEngine.Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
            {
                hidden.Add(f.gameObject);
                if (f.sword != null) hidden.Add(f.sword.gameObject);
            }

            foreach (var go in hidden) go.SetActive(false);
            const float left = -1f, right = CastleLevel.Width + 1f, bottom = -1.5f, top = CastleLevel.FloorHeight * 3f + 0.6f;
            int width = Mathf.RoundToInt((right - left) * pixelsPerUnit), height = Mathf.RoundToInt((top - bottom) * pixelsPerUnit);
            var camGo = new GameObject("CastleShotCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = (top - bottom) * 0.5f;
            cam.aspect = (right - left) / (top - bottom);
            cam.transform.position = new Vector3((left + right) * 0.5f, (top + bottom) * 0.5f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var rt = new RenderTexture(width, height, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(path, image.EncodeToPNG());
            cam.targetTexture = null;
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(camGo);
            foreach (var go in hidden) go.SetActive(true);
            return $"saved {width}x{height}";
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
                case "survival":
                    if (Arenas.Instance == null) return "no Arenas";
                    Arenas.Instance.StartSurvival();
                    return "ok";
                case "restart":
                    // Same as R in game.
                    var run = UnityEngine.Object.FindAnyObjectByType<SurvivalMode>();
                    if (run != null) run.Restart();
                    else UnityEngine.Object.FindAnyObjectByType<DuelManager>().Restart();
                    return "ok";
                case "castleshot":
                    // Render the whole Survival castle (no fighters) to a PNG: castleshot <path> [pixels per unit]
                    return CastleShot(parts[1], parts.Length > 2 ? int.Parse(parts[2]) : 32);
                case "retry":
                    // Same as R after falling in Survival.
                    UnityEngine.Object.FindAnyObjectByType<SurvivalMode>().Retry();
                    return "ok";
                case "kill":
                    // kill player | enemy (duel) | guards (every live castle guard)
                    var victim = UnityEngine.Object.FindAnyObjectByType<DuelManager>();
                    if (parts[1] == "guards")
                    {
                        foreach (var g in UnityEngine.Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
                            if (g.isGuard && !g.IsDead) g.Die();
                    }
                    else (parts[1] == "player" ? victim.Player : victim.Enemy).Die();
                    return "ok";
                case "tp":
                    // Put the player at x y (feet height), e.g. next to a stair tower.
                    var mover = UnityEngine.Object.FindAnyObjectByType<DuelManager>().Player;
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    mover.Body.position = new Vector2(float.Parse(parts[1], inv), float.Parse(parts[2], inv) + Fighter.HipHeight + 0.05f);
                    mover.Body.linearVelocity = Vector2.zero;
                    return "ok";
                case "walk":
                    // Hold a move direction (-1, 0, 1) on the player, ignoring the keyboard.
                    var walker = UnityEngine.Object.FindAnyObjectByType<DuelManager>().Player;
                    walker.GetComponent<PlayerDriver>().enabled = false;
                    walker.moveInput = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
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
                    var sm = UnityEngine.Object.FindObjectsByType<SurvivalMode>(FindObjectsSortMode.None);
                    string survivalState = sm.Length == 0 ? "" : $" survival x{sm.Length} cleared {sm[0].Cleared} active {sm[0].Active} guards {sm[0].Guards} kills {sm[0].Kills} over {sm[0].IsOver} won {sm[0].Won} player {d.Player.Body.position} hp {d.Player.Health:0}";
                    return d == null ? "no duel" : survivalState + $" round {d.Round} score {d.PlayerScore}-{d.EnemyScore} hp {d.Player.Health:0}/{d.Enemy.Health:0} pos {d.Player.Body.position} {d.Enemy.Body.position}";
                default:
                    return "unknown";
            }
        }
    }
}
