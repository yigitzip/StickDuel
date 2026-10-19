using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace StickDuel.EditorTools
{
    /// <summary>
    /// Rebuilds Assets/Scenes/DuelArena.unity from code: import settings, materials, arena,
    /// both fighters, FX and HUD. Menu: StickDuel/Rebuild Arena Scene.
    /// Batch: Unity -batchmode -quit -projectPath . -executeMethod StickDuel.EditorTools.StickDuelBuilder.Build
    /// </summary>
    public static class StickDuelBuilder
    {
        private const string ScenePath = "Assets/Scenes/DuelArena.unity";
        private const string SpriteDir = "Assets/Art/Sprites/";
        private const string AudioDir = "Assets/Art/Audio/";
        private const string MaterialDir = "Assets/Art/Materials/";
        private const float ArenaHalfWidth = 12f;

        private static readonly Color PlayerColor = new Color(0.55f, 0.88f, 1f);
        private static readonly Color EnemyColor = new Color(1f, 0.36f, 0.38f);

        private static Material lineMaterial, glowMaterial, sparkMaterial, dotMaterial, petalMaterial;
        private static Font font;

        [MenuItem("StickDuel/Rebuild Arena Scene")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EnsureLayers();
            ImportArt();
            CreateMaterials();
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/Fonts/RussoOne-Regular.ttf");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Camera cam = BuildCamera(out CameraRig cameraRig);
            BuildEnvironment(cam);

            Fighter player = BuildFighter("Player", "YOU", PlayerColor, new Vector2(-3f, Fighter.HipHeight + 0.05f));
            Fighter enemy = BuildFighter("Enemy", "RONIN", EnemyColor, new Vector2(3f, Fighter.HipHeight + 0.05f));
            player.opponent = enemy;
            enemy.opponent = player;
            Set(player.gameObject.AddComponent<PlayerDriver>(), "aimCamera", cam);
            enemy.gameObject.AddComponent<AIDriver>();
            Set(cameraRig, "targets", new Object[] { player, enemy });

            BuildFx(cameraRig);
            BuildHud(player, enemy);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.productName = "Stick Duel";
            AssetDatabase.SaveAssets();
            Debug.Log("[StickDuel] Arena scene rebuilt: " + ScenePath);
        }

        // ------------------------------------------------------------------ project setup

        private static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            string[] wanted = { Fighter.FighterLayer, Fighter.SwordLayer, Fighter.SwordHitLayer };
            for (int i = 0; i < wanted.Length; i++)
            {
                layers.GetArrayElementAtIndex(8 + i).stringValue = wanted[i];
            }

            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ImportArt()
        {
            AssetDatabase.Refresh();
            Vector2 center = new Vector2(0.5f, 0.5f);
            Vector2 bottom = new Vector2(0.5f, 0f);
            ConfigureSprite("Circle", 128, center);
            ConfigureSprite("Glow", 256, center);
            ConfigureSprite("Spark", 128, center);
            ConfigureSprite("Petal", 64, center);
            ConfigureSprite("Bar", 100, center, false, new Vector4(14, 14, 14, 14));
            ConfigureSprite("Sky", 100, center);
            ConfigureSprite("Sun", 100, center);
            ConfigureSprite("MountainsFar", 100, bottom);
            ConfigureSprite("MountainsMid", 100, bottom);
            ConfigureSprite("Hills", 100, bottom);
            ConfigureSprite("GroundTile", 100, new Vector2(0.5f, 1f), true);
            ConfigureSprite("GroundTop", 100, new Vector2(0.5f, 42f / 64f), true);
            ConfigureSprite("Cliff", 100, new Vector2(0f, 1f));
            ConfigureSprite("Torii", 100, bottom);
            ConfigureSprite("Lantern", 100, bottom);
            ConfigureSprite("Fog", 100, center, true);
            ConfigureSprite("Vignette", 100, center);
            ConfigureSprite("Katana", 213, new Vector2(48f / 320f, 0.5f));

            foreach (string clip in new[] { "Wind" })
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(AudioDir + clip + ".wav");
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureSprite(string name, float pixelsPerUnit, Vector2 pivot, bool tiled = false, Vector4 border = default)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(SpriteDir + name + ".png");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = tiled ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
            settings.wrapMode = tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            settings.spriteBorder = border;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static void CreateMaterials()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art/Materials"))
            {
                AssetDatabase.CreateFolder("Assets/Art", "Materials");
            }

            lineMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
            glowMaterial = MakeMaterial("Glow", "Legacy Shaders/Particles/Additive", "Glow");
            sparkMaterial = MakeMaterial("Spark", "Legacy Shaders/Particles/Additive", "Spark");
            dotMaterial = MakeMaterial("Dot", "Legacy Shaders/Particles/Alpha Blended", "Circle");
            petalMaterial = MakeMaterial("Petal", "Legacy Shaders/Particles/Alpha Blended", "Petal");
        }

        private static Material MakeMaterial(string name, string shaderName, string texture)
        {
            string path = MaterialDir + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shaderName));
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = Shader.Find(shaderName);
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(SpriteDir + texture + ".png");
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------ scene pieces

        private static Camera BuildCamera(out CameraRig rig)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 2.7f, -10f);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 4.6f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.04f, 0.14f);
            go.AddComponent<AudioListener>();
            rig = go.AddComponent<CameraRig>();

            var wind = go.AddComponent<AudioSource>();
            wind.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "Wind.wav");
            wind.loop = true;
            wind.volume = 0.22f;
            wind.playOnAwake = true;

            // Sky glued to the camera; sized for the widest zoom on ultrawide screens.
            SpriteRenderer sky = CreateSprite("Sky", "Sky", go.transform, new Vector3(0f, 0f, 20f), -100);
            sky.transform.localScale = new Vector3(55f, 1.45f, 1f);
            return cam;
        }

        private static void BuildEnvironment(Camera cam)
        {
            Transform root = new GameObject("Environment").transform;

            AddParallax(CreateSprite("Sun", "Sun", root, new Vector3(5.5f, 3.6f, 0f), -95), 0.9f);
            AddParallax(CreateSprite("MountainsFar", "MountainsFar", root, new Vector3(0f, -0.6f, 0f), -90, 1.15f), 0.8f);
            AddParallax(CreateTiled("FogFar", "Fog", root, new Vector3(0f, 1.6f, 0f), -86, new Vector2(70f, 2.56f), new Color(1f, 0.72f, 0.8f, 0.22f)), 0.75f, 0.25f, 10.24f);
            AddParallax(CreateSprite("MountainsMid", "MountainsMid", root, new Vector3(0f, -1.1f, 0f), -80), 0.6f);
            AddParallax(CreateTiled("FogMid", "Fog", root, new Vector3(0f, 0.6f, 0f), -76, new Vector2(70f, 2.56f), new Color(1f, 0.65f, 0.75f, 0.18f)), 0.5f, -0.35f, 10.24f);
            AddParallax(CreateSprite("Hills", "Hills", root, new Vector3(0f, -1.4f, 0f), -70), 0.4f);

            SpriteRenderer torii = CreateSprite("Torii", "Torii", root, new Vector3(0f, -0.02f, 0f), -60, 0.9f);
            AddParallax(torii, 0.08f);

            foreach (float x in new[] { -6.8f, 6.8f })
            {
                SpriteRenderer lantern = CreateSprite("Lantern", "Lantern", root, new Vector3(x, 0f, 0f), -55, 0.9f);
                SpriteRenderer glow = CreateSprite("LanternGlow", "Glow", lantern.transform, new Vector3(0f, 2.0f, 0f), -54, 3f);
                glow.sharedMaterial = glowMaterial;
                glow.color = new Color(1f, 0.62f, 0.3f, 0.55f);
                glow.gameObject.AddComponent<Flicker>();
            }

            // Ground: one collider, tiled stone + mossy lip, cliffs into the void on both sides.
            var ground = new GameObject("Ground");
            ground.transform.SetParent(root, false);
            var collider = ground.AddComponent<BoxCollider2D>();
            collider.size = new Vector2((ArenaHalfWidth + 1.6f) * 2f, 8f);
            collider.offset = new Vector2(0f, -4f);
            CreateTiled("Stone", "GroundTile", ground.transform, Vector3.zero, 4, new Vector2(ArenaHalfWidth * 2f, 8f), Color.white);
            CreateTiled("Lip", "GroundTop", ground.transform, Vector3.zero, 30, new Vector2((ArenaHalfWidth + 2.3f) * 2f, 0.64f), Color.white);
            foreach (int side in new[] { -1, 1 })
            {
                SpriteRenderer cliff = CreateSprite("Cliff", "Cliff", ground.transform, new Vector3(side * ArenaHalfWidth, 0f, 0f), 4);
                cliff.transform.localScale = new Vector3(side, 1.2f, 1f);
            }

            CreateTiled("ForegroundFog", "Fog", root, new Vector3(0f, -1.6f, 0f), 42, new Vector2(80f, 2.56f), new Color(0.8f, 0.6f, 0.85f, 0.25f))
                .gameObject.AddComponent<Parallax>();
        }

        private static Fighter BuildFighter(string objectName, string displayName, Color color, Vector2 position)
        {
            int fighterLayer = LayerMask.NameToLayer(Fighter.FighterLayer);
            var go = new GameObject(objectName) { layer = fighterLayer };
            go.transform.position = position;

            var body = go.AddComponent<Rigidbody2D>();
            body.mass = 3f;
            body.gravityScale = 2.6f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.sharedMaterial = NoFriction();

            var capsule = go.AddComponent<CapsuleCollider2D>();
            capsule.size = new Vector2(0.46f, 1.75f);
            capsule.offset = new Vector2(0f, -Fighter.HipHeight + 0.875f);

            var headGo = new GameObject("HeadHitbox") { layer = fighterLayer };
            headGo.transform.SetParent(go.transform, false);
            headGo.transform.localPosition = new Vector3(0f, 0.99f, 0f);
            var head = headGo.AddComponent<CircleCollider2D>();
            head.radius = 0.22f;

            var fighter = go.AddComponent<Fighter>();
            fighter.displayName = displayName;
            fighter.color = color;
            fighter.headCollider = head;

            var rig = go.AddComponent<StickFigureRig>();
            Set(rig, "fighter", fighter);
            Set(rig, "lineMaterial", lineMaterial);
            Set(rig, "headSprite", LoadSprite("Circle"));
            fighter.rig = rig;
            fighter.sword = BuildSword(objectName + " Katana", fighter, position);
            return fighter;
        }

        private static Sword BuildSword(string objectName, Fighter owner, Vector2 position)
        {
            var go = new GameObject(objectName) { layer = LayerMask.NameToLayer(Fighter.SwordLayer) };
            go.transform.position = position + new Vector2(0.6f, 0.6f);
            CreateSprite(null, "Katana", null, Vector3.zero, 14, 1f, go);

            var body = go.AddComponent<Rigidbody2D>();
            body.mass = 0.8f;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            // Blade from the guard (0.18) to the tip (1.25), in grip space.
            var blade = go.AddComponent<BoxCollider2D>();
            blade.offset = new Vector2(0.715f, 0f);
            blade.size = new Vector2(1.07f, 0.07f);

            var edge = new GameObject("Edge") { layer = LayerMask.NameToLayer(Fighter.SwordHitLayer) };
            edge.transform.SetParent(go.transform, false);
            var hit = edge.AddComponent<BoxCollider2D>();
            hit.isTrigger = true;
            hit.offset = blade.offset;
            hit.size = new Vector2(1.07f, 0.14f);

            var tip = new GameObject("Tip");
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = new Vector3(1.25f, 0f, 0f);
            var trail = tip.AddComponent<TrailRenderer>();
            trail.sharedMaterial = lineMaterial;
            trail.time = 0.12f;
            trail.minVertexDistance = 0.04f;
            trail.widthCurve = AnimationCurve.EaseInOut(0f, 0.16f, 1f, 0f);
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(owner.color, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
            trail.sortingOrder = 13;
            trail.emitting = false;

            var sword = go.AddComponent<Sword>();
            Set(sword, "owner", owner);
            Set(sword, "tip", tip.transform);
            Set(sword, "hitCollider", hit);
            Set(sword, "trail", trail);
            return sword;
        }

        private static void BuildFx(CameraRig cameraRig)
        {
            var fx = new GameObject("Fx").AddComponent<Fx>();
            Set(fx, "sparkMaterial", sparkMaterial);
            Set(fx, "glowMaterial", glowMaterial);
            Set(fx, "dotMaterial", dotMaterial);
            Set(fx, "petalMaterial", petalMaterial);
            Set(fx, "swoosh", LoadClip("Swoosh"));
            Set(fx, "clangs", new Object[] { LoadClip("Clang1"), LoadClip("Clang2") });
            Set(fx, "hit", LoadClip("Hit"));
            Set(fx, "knockout", LoadClip("KO"));
            Set(fx, "step", LoadClip("Step"));
            Set(fx, "beep", LoadClip("Beep"));
            Set(fx, "gong", LoadClip("Gong"));
            Set(fx, "cameraRig", cameraRig);
            Set(fx, "calloutFont", font);
        }

        private static void BuildHud(Fighter player, Fighter enemy)
        {
            var canvasGo = new GameObject("HUD");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Image vignette = CreateImage("Vignette", canvasGo.transform, LoadSprite("Vignette"), Color.white);
            Stretch(vignette.rectTransform);

            var manager = new GameObject("DuelManager").AddComponent<DuelManager>();
            Set(manager, "player", player);
            Set(manager, "enemy", enemy);
            BuildFighterPanel(canvasGo.transform, manager, false);
            BuildFighterPanel(canvasGo.transform, manager, true);

            Text center = CreateText("CenterText", canvasGo.transform, 150, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1600f, 220f));
            Text sub = CreateText("SubText", canvasGo.transform, 42, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(1600f, 80f));
            Text hint = CreateText("HintText", canvasGo.transform, 28, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1800f, 60f));
            hint.text = "A / D  move    W / SPACE  jump    Q / E  roll    MOUSE  swing    HOLD SHIFT  thrust    R  restart\nLEFT CLICK  throw sword (no sword: punch)    HOLD RIGHT CLICK  shuriken    walk over sword  pick up";
            hint.color = new Color(1f, 1f, 1f, 0.8f);
            center.gameObject.AddComponent<Outline>().effectDistance = new Vector2(4f, -4f);
            center.GetComponent<Outline>().effectColor = new Color(0.05f, 0.02f, 0.08f, 0.9f);
            sub.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
            hint.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

            Set(manager, "centerText", center);
            Set(manager, "subText", sub);
            Set(manager, "hintText", hint);
        }

        private static void BuildFighterPanel(Transform canvas, DuelManager manager, bool right)
        {
            float side = right ? -1f : 1f;
            Vector2 anchor = new Vector2(right ? 1f : 0f, 1f);
            string prefix = right ? "enemy" : "player";

            Text label = CreateText(prefix + "Name", canvas, 40, right ? TextAnchor.UpperRight : TextAnchor.UpperLeft, anchor, new Vector2(side * 360f, -52f), new Vector2(600f, 56f));
            label.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

            Image frame = CreateImage(prefix + "Bar", canvas, LoadSprite("Bar"), new Color(0.05f, 0.02f, 0.08f, 0.85f));
            frame.type = Image.Type.Sliced;
            Place(frame.rectTransform, anchor, new Vector2(side * 380f, -112f), new Vector2(640f, 38f));

            Image lag = CreateImage("Lag", frame.transform, LoadSprite("Bar"), new Color(1f, 0.95f, 0.85f, 0.85f));
            lag.type = Image.Type.Sliced;
            Image fill = CreateImage("Fill", frame.transform, LoadSprite("Bar"), right ? EnemyColor : PlayerColor);
            fill.type = Image.Type.Sliced;
            foreach (Image bar in new[] { lag, fill })
            {
                Stretch(bar.rectTransform);
                bar.rectTransform.offsetMin = new Vector2(5f, 5f);
                bar.rectTransform.offsetMax = new Vector2(-5f, -5f);
            }

            var pips = new Object[3];
            for (int i = 0; i < pips.Length; i++)
            {
                Image pip = CreateImage(prefix + "Pip" + i, canvas, LoadSprite("Circle"), Color.white);
                Place(pip.rectTransform, anchor, new Vector2(side * (75f + i * 38f), -158f), new Vector2(26f, 26f));
                pips[i] = pip;
            }

            Set(manager, prefix + "Fill", fill.rectTransform);
            Set(manager, prefix + "Lag", lag.rectTransform);
            Set(manager, prefix + "Pips", pips);
            Set(manager, prefix + "Name", label);
        }

        // ------------------------------------------------------------------ helpers

        private static SpriteRenderer CreateSprite(string name, string sprite, Transform parent, Vector3 localPosition, int order, float scale = 1f, GameObject existing = null)
        {
            GameObject go = existing != null ? existing : new GameObject(name);
            if (existing == null)
            {
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localPosition;
                go.transform.localScale = Vector3.one * scale;
            }

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = LoadSprite(sprite);
            renderer.sortingOrder = order;
            return renderer;
        }

        private static SpriteRenderer CreateTiled(string name, string sprite, Transform parent, Vector3 localPosition, int order, Vector2 size, Color color)
        {
            SpriteRenderer renderer = CreateSprite(name, sprite, parent, localPosition, order);
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.tileMode = SpriteTileMode.Continuous;
            renderer.size = size;
            renderer.color = color;
            return renderer;
        }

        private static void AddParallax(SpriteRenderer renderer, float factor, float drift = 0f, float wrap = 0f)
        {
            var parallax = renderer.gameObject.AddComponent<Parallax>();
            Set(parallax, "factor", factor);
            Set(parallax, "driftSpeed", drift);
            Set(parallax, "wrapWidth", wrap);
        }

        private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(string name, Transform parent, int size, TextAnchor alignment, Vector2 anchor, Vector2 position, Vector2 boxSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            Place(text.rectTransform, anchor, position, boxSize);
            return text;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static PhysicsMaterial2D NoFriction()
        {
            const string path = MaterialDir + "NoFriction.physicsMaterial2D";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path);
            if (material == null)
            {
                material = new PhysicsMaterial2D("NoFriction") { friction = 0f, bounciness = 0f };
                AssetDatabase.CreateAsset(material, path);
            }

            return material;
        }

        private static Sprite LoadSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + name + ".png");
        private static AudioClip LoadClip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + name + ".wav");

        /// <summary>Assigns a private [SerializeField] through SerializedObject.</summary>
        private static void Set(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                throw new System.ArgumentException($"{target.GetType().Name} has no serialized field '{field}'");
            }

            switch (value)
            {
                case Object[] array:
                    property.arraySize = array.Length;
                    for (int i = 0; i < array.Length; i++)
                    {
                        property.GetArrayElementAtIndex(i).objectReferenceValue = array[i];
                    }

                    break;
                case Object reference:
                    property.objectReferenceValue = reference;
                    break;
                case float number:
                    property.floatValue = number;
                    break;
                default:
                    throw new System.ArgumentException($"Unsupported value for '{field}'");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
