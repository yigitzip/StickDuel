using System.Collections;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Scene-wide game feel: particles, one-shot sounds, hitstop, slow motion and camera shake.
    /// Particle systems are created in code so all tuning lives in one place.
    /// </summary>
    public sealed class Fx : MonoBehaviour
    {
        public static Fx Instance { get; private set; }

        [Header("Materials")]
        [SerializeField] private Material sparkMaterial;
        [SerializeField] private Material glowMaterial;
        [SerializeField] private Material dotMaterial;
        public Material DotMaterial => dotMaterial;
        [SerializeField] private Material petalMaterial;

        [Header("Audio")]
        [SerializeField] private AudioClip swoosh;
        [SerializeField] private AudioClip[] clangs;
        [SerializeField] private AudioClip hit;
        [SerializeField] private AudioClip knockout;
        [SerializeField] private AudioClip step;
        [SerializeField] private AudioClip beep;
        [SerializeField] private AudioClip gong;

        [SerializeField] private CameraRig cameraRig;
        [SerializeField] private Font calloutFont;

        private ParticleSystem sparks, flashes, ink, dust;
        private AudioSource[] voices;
        private int nextVoice;
        private float hitstopUntil;
        private float slowMotionUntil;
        private float slowMotionScale = 1f;
        private SpriteRenderer screenFlash;
        private float baseFixedDeltaTime;

        [Tooltip("Overall game speed; 1 = original, 0.8 = 20% slower.")]
        [SerializeField, Range(0.1f, 2f)] private float gameSpeed = 0.8f;
        [Tooltip("Off mutes every game sound, the background wind included; the clips stay assigned.")]
        [SerializeField] private bool soundEnabled = false;

        /// <summary>Time scale outside of hitstop (used for K.O. slow motion).</summary>
        public float BaseTimeScale { get; set; } = 1f;
        /// <summary>On = no hitstop, no slow motion and no floating words (the castle keeps flowing).</summary>
        public bool Calm { get; set; }

        private void Awake()
        {
            Instance = this;
            baseFixedDeltaTime = Time.fixedDeltaTime;
            AudioListener.volume = soundEnabled ? 1f : 0f;
            sparks = CreateSystem("Sparks", sparkMaterial, 1.2f, 40, true);
            CollideWithGround(sparks, 0.4f);
            flashes = CreateSystem("Flashes", glowMaterial, 0f, 39, false);
            ink = CreateSystem("Ink", dotMaterial, 2.2f, 38, false);
            CollideWithGround(ink, 0.05f);
            dust = CreateSystem("Dust", dotMaterial, -0.15f, 31, false);
            CreatePetals();

            voices = new AudioSource[12];
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                Time.timeScale = 1f;
                Time.fixedDeltaTime = baseFixedDeltaTime;
            }
        }

        private void Update()
        {
            float scale = BaseTimeScale * gameSpeed;
            if (Time.unscaledTime < slowMotionUntil)
            {
                scale = Mathf.Min(scale, slowMotionScale);
            }

            if (Time.unscaledTime < hitstopUntil)
            {
                scale = 0.04f;
            }

            Time.timeScale = scale;
            Time.fixedDeltaTime = baseFixedDeltaTime * Mathf.Max(scale, 0.05f);
        }

        // ------------------------------------------------------------------ events

        public void Hit(Vector2 point, Vector2 direction, Color color, float damage, bool headshot)
        {
            float k = Mathf.Clamp01(damage / 35f);
            Burst(ink, point, direction, 50f, 10 + (int)(k * 22), 2f, 7f + k * 6f, 0.05f, 0.16f, color, 0.7f);
            Burst(sparks, point, direction, 35f, 6 + (int)(k * 10), 6f, 14f, 0.06f, 0.12f, Color.white, 0.25f);
            Burst(flashes, point, Vector2.zero, 0f, 1, 0f, 0f, 1.2f + k, 1.6f + k, new Color(1f, 0.9f, 0.8f, 0.8f), 0.12f);
            Play(hit, 0.7f + k * 0.3f, Random.Range(0.9f, 1.1f) * (headshot ? 1.15f : 1f));
            Hitstop(0.04f + k * 0.07f);
            Shake(0.25f + k * 0.35f);
        }

        /// <summary>Ice breaking on the floor: a spray of shards, no hitstop.</summary>
        public void Shatter(Vector2 point, Color color)
        {
            Burst(sparks, point, Vector2.up, 70f, 16, 3f, 8f, 0.05f, 0.12f, color, 0.4f);
            Burst(dust, point, Vector2.up, 80f, 6, 0.4f, 1.4f, 0.1f, 0.22f, new Color(0.85f, 0.95f, 1f, 0.5f), 0.6f);
            Shake(0.12f);
        }

        public void Clash(Vector2 point, float speed)
        {
            float k = Mathf.Clamp01(speed / 20f);
            Burst(sparks, point, Vector2.up, 180f, 10 + (int)(k * 20), 4f, 9f + k * 8f, 0.05f, 0.11f, new Color(1f, 0.85f, 0.5f), 0.35f);
            Burst(flashes, point, Vector2.zero, 0f, 1, 0f, 0f, 1f + k, 1.3f + k, new Color(1f, 0.8f, 0.45f, 0.9f), 0.1f);
            if (clangs != null && clangs.Length > 0)
            {
                Play(clangs[Random.Range(0, clangs.Length)], 0.35f + k * 0.5f, Random.Range(0.92f, 1.12f));
            }

            if (k > 0.35f)
            {
                Hitstop(0.03f + k * 0.04f);
            }

            Shake(0.12f + k * 0.2f);
        }

        public void Swoosh(Vector2 point, float speed)
        {
            Play(swoosh, Mathf.Clamp01(speed / 25f) * 0.5f, Random.Range(0.85f, 1.15f) + speed * 0.01f);
        }

        public void Step(Vector2 point, float intensity)
        {
            Burst(dust, point, Vector2.up, 70f, 2, 0.3f, 0.9f, 0.08f, 0.18f, new Color(0.75f, 0.6f, 0.75f, 0.35f), 0.6f);
            Play(step, 0.12f * intensity, Random.Range(0.85f, 1.2f));
        }

        public void Land(Vector2 point)
        {
            Burst(dust, point, Vector2.up, 85f, 8, 0.5f, 2f, 0.12f, 0.25f, new Color(0.75f, 0.6f, 0.75f, 0.4f), 0.7f);
            Play(step, 0.3f, 0.7f);
        }

        public void Knockout()
        {
            Play(knockout, 0.9f, 1f);
            Shake(0.8f);
        }

        /// <summary>
        /// Spin-attack hit: freeze → slow-motion ramp, a glowing slash mark across the victim, red
        /// screen flash, camera punch-in, a red burst and a big "CRIT!" with the damage under it.
        /// </summary>
        public void Crit(Vector2 point, Vector2 direction, float damage)
        {
            Color red = new Color(1f, 0.18f, 0.15f);
            Burst(sparks, point, direction, 70f, 30, 7f, 20f, 0.07f, 0.16f, red, 0.45f);
            Burst(sparks, point, -direction, 50f, 12, 4f, 10f, 0.05f, 0.1f, Color.white, 0.3f);
            Burst(flashes, point, Vector2.zero, 0f, 1, 0f, 0f, 2.8f, 3.4f, new Color(1f, 0.25f, 0.2f, 0.9f), 0.18f);
            Hitstop(0.08f);
            SlowMotion(0.25f, 0.45f);
            Shake(0.75f);
            if (cameraRig != null)
            {
                cameraRig.Punch(0.7f);
            }

            StartCoroutine(SlashMark(point, direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right, red));
            StartCoroutine(ScreenFlash(new Color(1f, 0.1f, 0.08f, 0.32f), 0.25f));
            Callout(point + Vector2.up * 0.55f, "CRIT!", red, 1.7f);
            Callout(point + Vector2.up * 0.05f, Mathf.RoundToInt(damage).ToString(), Color.white, 1.1f);
        }

        /// <summary>Slows the game to <paramref name="scale"/> for <paramref name="seconds"/> of real time.</summary>
        public void SlowMotion(float scale, float seconds)
        {
            if (Calm) return;
            slowMotionScale = scale;
            slowMotionUntil = Mathf.Max(slowMotionUntil, Time.unscaledTime + seconds);
        }

        private IEnumerator SlashMark(Vector2 point, Vector2 direction, Color color)
        {
            var go = new GameObject("SlashMark");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = sparkMaterial;
            line.textureMode = LineTextureMode.Stretch;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.sortingOrder = 48;
            line.numCapVertices = 4;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));

            const float duration = 0.32f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = t / duration;
                float grow = 1f - (1f - Mathf.Min(1f, k * 4f)) * (1f - Mathf.Min(1f, k * 4f)); // fast ease-out
                float half = 1.7f * grow;
                line.SetPosition(0, point - direction * half);
                line.SetPosition(1, point + direction * half);
                line.widthMultiplier = Mathf.Lerp(0.45f, 0.05f, k);
                Color c = Color.Lerp(Color.white, color, Mathf.Min(1f, k * 3f));
                c.a = 1f - k * k;
                line.startColor = c;
                line.endColor = c;
                yield return null;
            }

            Destroy(go);
        }

        private IEnumerator ScreenFlash(Color color, float duration)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                yield break;
            }

            if (screenFlash == null)
            {
                var go = new GameObject("ScreenFlash");
                go.transform.SetParent(cam.transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, 1f);
                go.transform.localScale = new Vector3(400f, 400f, 1f); // 1 px sprite at 4 ppu → covers any zoom
                screenFlash = go.AddComponent<SpriteRenderer>();
                screenFlash.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
                screenFlash.sortingOrder = 100;
            }

            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                screenFlash.color = new Color(color.r, color.g, color.b, color.a * (1f - t / duration));
                yield return null;
            }

            screenFlash.color = Color.clear;
        }

        /// <summary>Short floating text in the world, e.g. "BACKSTAB!".</summary>
        public void Callout(Vector2 position, string text) => Callout(position, text, new Color(1f, 0.85f, 0.35f), 1f);

        public void Callout(Vector2 position, string text, Color color, float size)
        {
            if (Calm) return;
            StartCoroutine(CalloutRoutine(position, text, color, size));
        }

        private IEnumerator CalloutRoutine(Vector2 position, string text, Color color, float size)
        {
            Font font = calloutFont != null ? calloutFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Callout");
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.text = text;
            mesh.fontSize = 64;
            mesh.characterSize = 0.06f;
            mesh.anchor = TextAnchor.MiddleCenter;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.sortingOrder = 51;

            // Dark drop shadow so the text reads over sparks and sky.
            var shadowGo = new GameObject("Shadow");
            shadowGo.transform.SetParent(go.transform, false);
            shadowGo.transform.localPosition = new Vector3(0.05f, -0.05f, 0f);
            var shadow = shadowGo.AddComponent<TextMesh>();
            shadow.font = font;
            shadow.text = text;
            shadow.fontSize = mesh.fontSize;
            shadow.characterSize = mesh.characterSize;
            shadow.anchor = mesh.anchor;
            var shadowRenderer = shadowGo.GetComponent<MeshRenderer>();
            shadowRenderer.sharedMaterial = font.material;
            shadowRenderer.sortingOrder = 50;

            const float duration = 0.9f;
            float wobbleSeed = Random.value * 10f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = t / duration;
                // Pop: overshoot to 1.5×, settle to 1× (damped spring look), then drift up and fade.
                float pop = k < 0.25f ? 1f + 0.5f * Mathf.Sin(k / 0.25f * Mathf.PI) * (1f - k / 0.25f) + (1f - k / 0.25f) * 0.4f : 1f;
                float rise = 1f - (1f - k) * (1f - k);
                float wobble = Mathf.Sin(t * 40f + wobbleSeed) * 8f * Mathf.Max(0f, 1f - k * 4f);
                go.transform.SetPositionAndRotation(
                    new Vector3(position.x, position.y + 0.3f + rise * 0.7f, 0f),
                    Quaternion.Euler(0f, 0f, wobble));
                go.transform.localScale = Vector3.one * size * pop;
                float alpha = 1f - Mathf.Max(0f, (k - 0.6f) / 0.4f);
                Color flicker = k < 0.12f ? Color.white : color;
                mesh.color = new Color(flicker.r, flicker.g, flicker.b, alpha);
                shadow.color = new Color(0.05f, 0.02f, 0.08f, alpha * 0.85f);
                yield return null;
            }

            Destroy(go);
        }

        public void Beep(float pitch = 1f) => Play(beep, 0.5f, pitch);
        public void Gong() => Play(gong, 0.8f, 1f);

        public void Hitstop(float seconds)
        {
            if (Calm) return;
            hitstopUntil = Mathf.Max(hitstopUntil, Time.unscaledTime + seconds);
        }

        public void Shake(float amount)
        {
            if (cameraRig != null)
            {
                cameraRig.Shake(amount);
            }
        }

        // ------------------------------------------------------------------ helpers

        private void Play(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || !soundEnabled)
            {
                return;
            }

            AudioSource voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            voice.pitch = pitch;
            voice.PlayOneShot(clip, volume);
        }

        private static void Burst(ParticleSystem system, Vector2 position, Vector2 direction, float spreadDegrees, int count,
            float minSpeed, float maxSpeed, float minSize, float maxSize, Color color, float lifetime)
        {
            float baseAngle = direction.sqrMagnitude > 0.0001f ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg : 90f;
            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                float angle = (baseAngle + Random.Range(-spreadDegrees, spreadDegrees)) * Mathf.Deg2Rad;
                emit.position = new Vector3(position.x, position.y, 0f);
                emit.velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * Random.Range(minSpeed, maxSpeed);
                emit.startSize = Random.Range(minSize, maxSize);
                emit.startColor = color;
                emit.startLifetime = lifetime * Random.Range(0.6f, 1.2f);
                system.Emit(emit, 1);
            }
        }

        private ParticleSystem CreateSystem(string systemName, Material material, float gravity, int sortingOrder, bool stretch)
        {
            var go = new GameObject(systemName);
            go.transform.SetParent(transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            var colorOverLifetime = system.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = fade;

            var sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.08f;
            limit.limit = 0f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;
            if (stretch)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.035f;
                renderer.lengthScale = 1f;
            }

            system.Play();
            return system;
        }

        private static void CollideWithGround(ParticleSystem system, float bounce)
        {
            var collision = system.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision2D;
            collision.collidesWith = 1; // Default layer = ground
            collision.bounce = bounce;
            collision.dampen = 0.6f;
            collision.lifetimeLoss = 0f;
        }

        private void CreatePetals()
        {
            Camera cam = Camera.main;
            if (cam == null || petalMaterial == null)
            {
                return;
            }

            var go = new GameObject("Petals");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(4f, 8f, 10f);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = true;
            main.startLifetime = 9f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.85f, 0.9f), new Color(1f, 0.55f, 0.7f, 0.9f));
            main.gravityModifier = 0.02f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            main.prewarm = true;

            var emission = system.emission;
            emission.rateOverTime = 7f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(30f, 1f, 1f);

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-1.6f, -0.6f);
            velocity.y = new ParticleSystem.MinMaxCurve(-1.2f, -0.6f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.4f;

            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-2f, 2f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = petalMaterial;
            renderer.sortingOrder = 45;

            system.Play();
        }
    }
}
