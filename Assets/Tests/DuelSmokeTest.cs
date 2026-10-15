using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StickDuel.Tests
{
    /// <summary>
    /// Loads the arena, swaps the player for a second AI and lets them duel.
    /// Set STICKDUEL_SHOTS=/some/dir to also dump screenshots along the way.
    /// </summary>
    public sealed class DuelSmokeTest
    {
        [Test]
        public void SolveJointKeepsBoneLengths()
        {
            Vector2 end = new Vector2(0.3f, -0.8f);
            Vector2 joint = StickFigureRig.SolveJoint(Vector2.zero, ref end, 0.5f, 0.5f, 1f);
            Assert.AreEqual(0.5f, joint.magnitude, 1e-4f);
            Assert.AreEqual(0.5f, (end - joint).magnitude, 1e-4f);
            Assert.Greater(joint.x, end.x * 0.5f, "joint should bend towards +x for bendSide = 1");

            Vector2 far = new Vector2(5f, 0f);
            StickFigureRig.SolveJoint(Vector2.zero, ref far, 0.5f, 0.5f, 1f);
            Assert.LessOrEqual(far.magnitude, 1f, "unreachable targets are clamped to the limb length");
        }

        [UnityTest]
        public IEnumerator AiVersusAiPlaysRounds()
        {
            yield return SceneManager.LoadSceneAsync("DuelArena");
            var manager = Object.FindFirstObjectByType<DuelManager>();
            Assert.IsNotNull(manager);

            Fighter player = manager.Player;
            Object.Destroy(player.GetComponent<PlayerDriver>());
            player.gameObject.AddComponent<AIDriver>();

            float damage = 0f;
            player.Damaged += (_, amount) => damage += amount;
            manager.Enemy.Damaged += (_, amount) => damage += amount;

            string shotDir = System.Environment.GetEnvironmentVariable("STICKDUEL_SHOTS");
            if (!string.IsNullOrEmpty(shotDir))
            {
                Directory.CreateDirectory(shotDir);
                var canvas = Object.FindFirstObjectByType<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = Camera.main;
                canvas.planeDistance = 1f;
            }

            float start = Time.realtimeSinceStartup;
            int shots = 0;
            while (Time.realtimeSinceStartup - start < 45f && manager.Round < 3)
            {
                if (!string.IsNullOrEmpty(shotDir) && Time.realtimeSinceStartup - start > 0.8f + shots * 2.5f)
                {
                    Capture(Path.Combine(shotDir, $"shot_{shots:00}.png"));
                    shots++;
                }

                yield return null;
            }

            Debug.Log($"[StickDuel test] rounds={manager.Round} score={manager.PlayerScore}-{manager.EnemyScore} damage={damage:F0}");
            Assert.Greater(damage, 0f, "no sword hit landed");
            Assert.GreaterOrEqual(manager.Round, 2, "match never got past round 1");
        }

        private static void Capture(string path)
        {
            Camera cam = Camera.main;
            var target = new RenderTexture(1600, 900, 24);
            cam.targetTexture = target;
            cam.Render();
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.Destroy(image);
            target.Release();
        }
    }
}
