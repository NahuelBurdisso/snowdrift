using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Unity.Cinemachine;
using Snowdrift.Game;

namespace Snowdrift.Editor
{
    public static class ProjectSetup
    {
        [MenuItem("Snowdrift/Build Scene")]
        public static void BuildScene()
        {
            Debug.Log("[ProjectSetup] Starting BuildScene...");

            Step1_URP();
            Step2_TagsAndLayers();
            Step3_BoardTuningAsset();
            Step4_Materials();
            Step5_BuildMainScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ProjectSetup] BuildScene complete.");
        }

        // ─────────────────────────────────────────────────────────────
        // Step 1: URP asset + renderer data
        // ─────────────────────────────────────────────────────────────
        static void Step1_URP()
        {
            const string rendererPath = "Assets/Settings/SnowdriftRenderer.asset";
            const string urpPath = "Assets/Settings/SnowdriftURP.asset";

            EnsureDir("Assets/Settings");

            // Renderer data
            UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, rendererPath);
                Debug.Log("[ProjectSetup] Created renderer data at " + rendererPath);
            }

            // URP pipeline asset
            UniversalRenderPipelineAsset urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(urpPath);
            if (urpAsset == null)
            {
                urpAsset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urpAsset, urpPath);
                Debug.Log("[ProjectSetup] Created URP asset at " + urpPath);
            }

            // Assign to graphics + quality settings
            if (GraphicsSettings.defaultRenderPipeline != urpAsset)
            {
                GraphicsSettings.defaultRenderPipeline = urpAsset;
                EditorUtility.SetDirty(GraphicsSettings.defaultRenderPipeline);
                Debug.Log("[ProjectSetup] Assigned URP asset to GraphicsSettings.");
            }

            for (int i = 0; i < QualitySettings.count; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline != urpAsset)
                {
                    QualitySettings.renderPipeline = urpAsset;
                }
            }
            QualitySettings.SetQualityLevel(0, false);
            Debug.Log("[ProjectSetup] URP assigned to all quality levels.");
        }

        // ─────────────────────────────────────────────────────────────
        // Step 2: Verify layer "Ground" (index 3) and tag "Obstacle"
        // ─────────────────────────────────────────────────────────────
        static void Step2_TagsAndLayers()
        {
            var tagManagerPath = "ProjectSettings/TagManager.asset";
            var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(tagManagerPath)[0]);

            // Tags
            var tags = so.FindProperty("tags");
            bool hasObstacle = false;
            for (int i = 0; i < tags.arraySize; i++)
                if (tags.GetArrayElementAtIndex(i).stringValue == "Obstacle") { hasObstacle = true; break; }
            if (!hasObstacle)
            {
                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = "Obstacle";
                Debug.Log("[ProjectSetup] Added tag 'Obstacle'.");
            }

            // Layer "Ground" at index 3 (user layer array is offset: index 0 = Unity layer 8, but
            // the serialized array "layers" has indices 0-31 where 0=Default, 1=TransparentFX, etc.)
            var layers = so.FindProperty("layers");
            // Layer index 3 in TagManager means Unity layer 3.
            // The "layers" array element at arrayIndex 3 is Unity layer 3.
            bool hasGround = false;
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == "Ground") { hasGround = true; break; }
            }
            if (!hasGround && layers.arraySize > 3)
            {
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(3).stringValue))
                {
                    layers.GetArrayElementAtIndex(3).stringValue = "Ground";
                    Debug.Log("[ProjectSetup] Added layer 'Ground' at index 3.");
                }
                else
                {
                    Debug.LogWarning("[ProjectSetup] Layer index 3 is not empty and 'Ground' not found; skipping.");
                }
            }

            so.ApplyModifiedProperties();
        }

        // ─────────────────────────────────────────────────────────────
        // Step 3: BoardTuning.asset with defaults
        // ─────────────────────────────────────────────────────────────
        static void Step3_BoardTuningAsset()
        {
            const string path = "Assets/Settings/BoardTuning.asset";
            if (AssetDatabase.LoadAssetAtPath<BoardTuning>(path) != null)
            {
                Debug.Log("[ProjectSetup] BoardTuning.asset already exists.");
                return;
            }
            var tuning = ScriptableObject.CreateInstance<BoardTuning>();
            // defaults are already set in the class fields — just create it
            EnsureDir("Assets/Settings");
            AssetDatabase.CreateAsset(tuning, path);
            Debug.Log("[ProjectSetup] Created BoardTuning.asset.");
        }

        // ─────────────────────────────────────────────────────────────
        // Step 4: Materials
        // ─────────────────────────────────────────────────────────────
        static void Step4_Materials()
        {
            EnsureDir("Assets/Materials");
            CreateUrpMat("Assets/Materials/Snow.mat",   new Color(0.949f, 0.961f, 0.969f)); // #F2F5F7
            CreateUrpMat("Assets/Materials/Rock.mat",   new Color(0.357f, 0.404f, 0.439f)); // #5B6770
            CreateUrpMat("Assets/Materials/Kicker.mat", new Color(0.910f, 0.706f, 0.298f)); // #E8B44C
            CreateUrpMat("Assets/Materials/Player.mat", new Color(0.851f, 0.314f, 0.251f)); // #D95040
        }

        static void CreateUrpMat(string path, Color color)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = color;
            mat.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(mat, path);
            Debug.Log($"[ProjectSetup] Created material {path}");
        }

        // ─────────────────────────────────────────────────────────────
        // Step 5: Build Main.unity scene
        // ─────────────────────────────────────────────────────────────
        static void Step5_BuildMainScene()
        {
            const string scenePath = "Assets/Scenes/Main.unity";
            EnsureDir("Assets/Scenes");

            // Create or open scene
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // Load asset refs
            var tuningAsset = AssetDatabase.LoadAssetAtPath<BoardTuning>("Assets/Settings/BoardTuning.asset");
            var snowMat   = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Snow.mat");
            var rockMat   = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Rock.mat");
            var kickerMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Kicker.mat");
            var playerMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Player.mat");

            // Audio clips (optional)
            var windClip  = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/wind.wav");
            var carveClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/carve.wav");
            var thumpClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/thump.wav");

            // ── Directional light ──
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.96f, 0.88f); // warm golden-white
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f); // steeper winter sun

            // ── Main Camera ──
            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox; // use gradient sky
            camera.farClipPlane = 600f;
            cameraGo.AddComponent<AudioListener>();
            // CinemachineBrain required for Cinemachine to drive this camera
            cameraGo.AddComponent<CinemachineBrain>();
            // ImpulseListener on the camera GO
            var impulseListener = cameraGo.AddComponent<CinemachineImpulseListener>();
            impulseListener.ChannelMask = 1;

            // ── Ambient snow (local space — floats with camera for floating-origin safety) ──
            var ambientSnowGo = new GameObject("AmbientSnow");
            ambientSnowGo.transform.SetParent(cameraGo.transform, false);
            ambientSnowGo.transform.localPosition = Vector3.zero;
            var snowPs = ambientSnowGo.AddComponent<ParticleSystem>();
            {
                var main = snowPs.main;
                main.loop = true;
                main.simulationSpace = ParticleSystemSimulationSpace.Local; // CRITICAL: floating-origin safe
                main.maxParticles = 200;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.0f);
                main.startSpeed = 0f; // driven by velocityOverLifetime
                main.startSize = 0.05f;
                main.startColor = Color.white;
                var em = snowPs.emission;
                em.rateOverTime = 40f; // ~200 particles / ~5s lifetime = steady 40/s
                var shape = snowPs.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(30f, 20f, 30f);
                var vel = snowPs.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                // Constant downward drift in local space
                vel.x = new ParticleSystem.MinMaxCurve(0f);
                vel.y = new ParticleSystem.MinMaxCurve(-1.5f);
                vel.z = new ParticleSystem.MinMaxCurve(0f);
            }

            // ── GameState ──
            var gameStateGo = new GameObject("GameState");
            gameStateGo.AddComponent<GameState>();

            // ── Player rig ──
            var playerGo = new GameObject("Player");
            playerGo.transform.position = new Vector3(0f, 2f, 0f);

            // Rigidbody
            var rb = playerGo.AddComponent<Rigidbody>();
            rb.mass = 80f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.constraints = RigidbodyConstraints.None;

            // CapsuleCollider on player root
            var col = playerGo.AddComponent<CapsuleCollider>();
            col.height = 1.8f;
            col.radius = 0.35f;
            col.center = new Vector3(0f, 0.9f, 0f);

            // Visual root (CrashHandler.visual points here)
            var visualGo = new GameObject("Visual");
            visualGo.transform.SetParent(playerGo.transform, false);

            // Capsule visual (body)
            var capsuleVisualGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsuleVisualGo.name = "BodyMesh";
            capsuleVisualGo.transform.SetParent(visualGo.transform, false);
            capsuleVisualGo.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            capsuleVisualGo.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            capsuleVisualGo.GetComponent<MeshRenderer>().sharedMaterial = playerMat;
            // Remove collider from visual child (no colliders/rigidbody on visual children)
            var capCol = capsuleVisualGo.GetComponent<CapsuleCollider>();
            if (capCol != null) Object.DestroyImmediate(capCol);

            // Board (flattened cube)
            var boardVisualGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boardVisualGo.name = "BoardMesh";
            boardVisualGo.transform.SetParent(visualGo.transform, false);
            boardVisualGo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            boardVisualGo.transform.localScale = new Vector3(0.4f, 0.08f, 1.4f);
            boardVisualGo.GetComponent<MeshRenderer>().sharedMaterial = snowMat;
            var boardCol = boardVisualGo.GetComponent<BoxCollider>();
            if (boardCol != null) Object.DestroyImmediate(boardCol);

            // GroundSensor
            var groundSensor = playerGo.AddComponent<GroundSensor>();
            int groundLayerIndex = LayerMask.NameToLayer("Ground");
            if (groundLayerIndex < 0) groundLayerIndex = 3;
            groundSensor.groundMask = 1 << groundLayerIndex;

            // BoardController
            var boardController = playerGo.AddComponent<BoardController>();
            boardController.tuning = tuningAsset;

            // CrashHandler
            var crashHandler = playerGo.AddComponent<CrashHandler>();
            crashHandler.board = boardController;
            crashHandler.tuning = tuningAsset;
            crashHandler.visual = visualGo.transform;
            // terrain wired below after TerrainStream created

            // TrickSystem
            var trickSystem = playerGo.AddComponent<TrickSystem>();
            trickSystem.board = boardController;
            trickSystem.tuning = tuningAsset;

            // ComboScore
            var comboScore = playerGo.AddComponent<ComboScore>();
            comboScore.board = boardController;
            comboScore.tricks = trickSystem;
            comboScore.tuning = tuningAsset;
            // terrain wired below

            // ── Shared particle material (URP Particles/Unlit — prevents magenta in URP) ──
            var particleMat = LoadOrCreateParticleMat();

            // Assign to AmbientSnow renderer
            ambientSnowGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMat;

            // ── GameFeel: particle systems + audio ──
            // Carve spray (world space)
            var carveSprayGo = new GameObject("CarveSpray");
            carveSprayGo.transform.SetParent(playerGo.transform, false);
            var carvePs = carveSprayGo.AddComponent<ParticleSystem>();
            {
                var main = carvePs.main;
                main.loop = true;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = 0.5f;
                main.startSpeed = 2f;
                main.startSize = 0.1f;
                main.startColor = new Color(0.9f, 0.95f, 1f, 0.8f);
                var em = carvePs.emission;
                em.rateOverTime = 0f;
                var shape = carvePs.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 25f;
            }
            carveSprayGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMat;

            // Land burst (world space)
            var landBurstGo = new GameObject("LandBurst");
            landBurstGo.transform.SetParent(playerGo.transform, false);
            var landPs = landBurstGo.AddComponent<ParticleSystem>();
            {
                var main = landPs.main;
                main.loop = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = 0.6f;
                main.startSpeed = 3f;
                main.startSize = 0.15f;
                main.startColor = new Color(0.9f, 0.95f, 1f, 0.9f);
                var em = landPs.emission;
                em.rateOverTime = 0f;
                var burst = new ParticleSystem.Burst(0f, 30);
                em.SetBursts(new ParticleSystem.Burst[] { burst });
            }
            landBurstGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMat;

            // Speed streaks (local space, near camera)
            var speedStreaksGo = new GameObject("SpeedStreaks");
            speedStreaksGo.transform.SetParent(playerGo.transform, false);
            speedStreaksGo.transform.localPosition = new Vector3(0f, 1f, 0f);
            var streakPs = speedStreaksGo.AddComponent<ParticleSystem>();
            {
                var main = streakPs.main;
                main.loop = true;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startLifetime = 0.15f;
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);
                main.startColor = new Color(0.8f, 0.9f, 1f, 0.4f);
                var em = streakPs.emission;
                em.rateOverTime = 0f;
                var shape = streakPs.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.5f;
            }
            speedStreaksGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMat;

            // TrailRenderer
            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(playerGo.transform, false);
            trailGo.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.time = 0.4f;
            trail.startWidth = 0.3f;
            trail.endWidth = 0f;
            trail.material = snowMat;
            trail.emitting = false;

            // AudioSources
            var windLoopGo = new GameObject("WindLoop");
            windLoopGo.transform.SetParent(playerGo.transform, false);
            var windSource = windLoopGo.AddComponent<AudioSource>();
            windSource.loop = true;
            windSource.spatialBlend = 0f;
            windSource.volume = 0.3f;
            windSource.playOnAwake = true;
            if (windClip != null) { windSource.clip = windClip; windSource.Play(); }

            var carveLoopGo = new GameObject("CarveLoop");
            carveLoopGo.transform.SetParent(playerGo.transform, false);
            var carveSource = carveLoopGo.AddComponent<AudioSource>();
            carveSource.loop = true;
            carveSource.spatialBlend = 0f;
            carveSource.volume = 0f;
            carveSource.playOnAwake = false;
            if (carveClip != null) { carveSource.clip = carveClip; }

            var oneShotsGo = new GameObject("OneShots");
            oneShotsGo.transform.SetParent(playerGo.transform, false);
            var oneShotsSource = oneShotsGo.AddComponent<AudioSource>();
            oneShotsSource.loop = false;
            oneShotsSource.spatialBlend = 0f;
            oneShotsSource.playOnAwake = false;

            // ── Global Volume ──
            var volumeGo = new GameObject("GlobalVolume");
            var vol = volumeGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.2f);
            // Save profile as asset
            const string volProfilePath = "Assets/Settings/GameFeel.VolumeProfile.asset";
            var existingProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(volProfilePath);
            if (existingProfile == null)
            {
                AssetDatabase.CreateAsset(profile, volProfilePath);
            }
            else
            {
                profile = existingProfile;
            }
            vol.sharedProfile = profile;

            // ── GameFeel component (on Player) ──
            var gameFeel = playerGo.AddComponent<GameFeel>();
            gameFeel.board = boardController;
            gameFeel.tricks = trickSystem;
            gameFeel.tuning = tuningAsset;
            // terrain wired below
            gameFeel.carveSpray = carvePs;
            gameFeel.landBurst = landPs;
            gameFeel.speedStreaks = streakPs;
            gameFeel.trail = trail;
            gameFeel.postVolume = vol;
            gameFeel.windLoop = windSource;
            gameFeel.carveLoop = carveSource;
            gameFeel.oneShots = oneShotsSource;
            if (thumpClip != null) gameFeel.landThump = thumpClip;

            // ── TerrainStream ──
            var terrainGo = new GameObject("TerrainStream");
            var terrainStream = terrainGo.AddComponent<TerrainStream>();
            terrainStream.board = boardController;
            terrainStream.snowMat = snowMat;
            terrainStream.rockMat = rockMat;
            terrainStream.kickerMat = kickerMat;

            // Wire terrain refs now that it exists
            crashHandler.terrain = terrainStream;
            comboScore.terrain = terrainStream;
            gameFeel.terrain = terrainStream;

            // ── CameraRig ──
            var cameraRigGo = new GameObject("CameraRig");
            cameraRigGo.transform.position = new Vector3(0f, 2.5f, -6f);
            var cameraRig = cameraRigGo.AddComponent<CameraRig>();
            cameraRig.board = boardController;
            cameraRig.terrain = terrainStream;

            // ImpulseSource on CameraRig
            var landImpulse = cameraRigGo.AddComponent<CinemachineImpulseSource>();
            cameraRig.landImpulse = landImpulse;

            // ── CinemachineCamera ──
            var vcamGo = new GameObject("CinemachineCamera");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            // Follow + LookAt the CameraRig transform
            vcam.Follow = cameraRigGo.transform;
            vcam.LookAt = cameraRigGo.transform;

            // Add CinemachineFollow component (Body)
            var cmFollow = vcamGo.AddComponent<CinemachineFollow>();
            cmFollow.FollowOffset = new Vector3(0f, 2.5f, -6f);
            // Damping via TrackerSettings
            var ts = cmFollow.TrackerSettings;
            ts.PositionDamping = new Vector3(0.5f, 0.5f, 0.5f);
            ts.RotationDamping = new Vector3(0.3f, 0.3f, 0.3f);
            cmFollow.TrackerSettings = ts;

            // Add CinemachineRotationComposer (Aim)
            var cmAim = vcamGo.AddComponent<CinemachineRotationComposer>();
            cmAim.Damping = new Vector2(0.2f, 0.2f);

            // Wire vcam back to CameraRig
            cameraRig.vcam = vcam;

            // ── HUD Canvas ──
            var canvasGo = new GameObject("HUD");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Score text (top-left)
            var scoreTxt = CreateUIText(canvasGo, "ScoreText", "0", font, 36,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -10f), new Vector2(200f, 50f));

            // Multiplier text (top-left, below score)
            var multTxt = CreateUIText(canvasGo, "MultText", "x1.0", font, 24,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -65f), new Vector2(200f, 40f));

            // Distance text (top-right)
            var distTxt = CreateUIText(canvasGo, "DistText", "0 m", font, 28,
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-10f, -10f), new Vector2(200f, 45f));
            distTxt.alignment = TextAnchor.UpperRight;

            // Trick popup (center-bottom)
            var trickTxt = CreateUIText(canvasGo, "TrickText", "", font, 32,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 60f), new Vector2(400f, 50f));
            trickTxt.alignment = TextAnchor.MiddleCenter;
            trickTxt.enabled = false;

            // Pause text (centered)
            var pauseTxt = CreateUIText(canvasGo, "PauseText", "PAUSED", font, 40,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f), new Vector2(600f, 200f));
            pauseTxt.alignment = TextAnchor.MiddleCenter;
            pauseTxt.enabled = false;

            // Speed bar (bottom thin bar, Image)
            var speedBarGo = new GameObject("SpeedBar");
            speedBarGo.transform.SetParent(canvasGo.transform, false);
            var speedBarImg = speedBarGo.AddComponent<Image>();
            speedBarImg.color = new Color(0.2f, 0.7f, 1f, 0.7f);
            speedBarImg.type = Image.Type.Filled;
            speedBarImg.fillMethod = Image.FillMethod.Horizontal;
            speedBarImg.fillAmount = 0f;
            var speedBarRect = speedBarGo.GetComponent<RectTransform>();
            speedBarRect.anchorMin = new Vector2(0f, 0f);
            speedBarRect.anchorMax = new Vector2(1f, 0f);
            speedBarRect.pivot = new Vector2(0.5f, 0f);
            speedBarRect.anchoredPosition = new Vector2(0f, 5f);
            speedBarRect.sizeDelta = new Vector2(0f, 8f);

            // EventSystem
            var eventSysGo = new GameObject("EventSystem");
            eventSysGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSysGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // ── HUD component ──
            var hud = canvasGo.AddComponent<HUD>();
            hud.score = comboScore;
            hud.tricks = trickSystem;
            hud.terrain = terrainStream;
            hud.board = boardController;
            hud.tuning = tuningAsset;
            hud.scoreText = scoreTxt;
            hud.multText = multTxt;
            hud.distText = distTxt;
            hud.trickText = trickTxt;
            hud.pauseText = pauseTxt;
            hud.speedBar = speedBarImg;

            // ── Skybox material ──
            const string skyMatPath = "Assets/Materials/SkyGradient.mat";
            EnsureDir("Assets/Materials");
            var skyShader = Shader.Find("Snowdrift/GradientSky");
            if (skyShader == null)
                Debug.LogWarning("[ProjectSetup] Shader 'Snowdrift/GradientSky' not found — falling back to Skybox/Procedural. Import the custom sky shader to fix this.");
            Material skyMat = AssetDatabase.LoadAssetAtPath<Material>(skyMatPath);
            if (skyMat == null)
            {
                skyMat = new Material(skyShader != null ? skyShader : Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(skyMat, skyMatPath);
                Debug.Log("[ProjectSetup] Created SkyGradient.mat");
            }
            else if (skyShader != null && skyMat.shader != skyShader)
            {
                skyMat.shader = skyShader;
                EditorUtility.SetDirty(skyMat);
            }
            RenderSettings.skybox = skyMat;

            // ── Fog ──
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 220f;
            RenderSettings.fogColor = new Color(0.95f, 0.85f, 0.80f);

            // ── Ambient lighting (trilight) ──
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = new Color(0.55f, 0.70f, 0.90f); // sky — cool blue
            RenderSettings.ambientEquatorColor = new Color(0.90f, 0.88f, 0.82f); // horizon — warm
            RenderSettings.ambientGroundColor  = new Color(0.55f, 0.62f, 0.75f); // ground/shadow — cool

            // ── ColorAdjustments on existing post volume profile ──
            if (profile != null)
            {
                ColorAdjustments colorAdj;
                if (!profile.TryGet<ColorAdjustments>(out colorAdj))
                    colorAdj = profile.Add<ColorAdjustments>(true);
                colorAdj.contrast.Override(10f);
                colorAdj.saturation.Override(8f);
                EditorUtility.SetDirty(profile);
                Debug.Log("[ProjectSetup] Added ColorAdjustments (contrast +10, saturation +8) to volume profile.");
            }

            // ── Add scene to build settings ──
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
            AddSceneToBuildSettings(scenePath);

            Debug.Log("[ProjectSetup] Main.unity built and saved.");
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Loads Assets/Materials/ParticleSnow.mat or creates it with URP Particles/Unlit.
        /// Prevents magenta particles in URP by ensuring every ParticleSystemRenderer has a material.
        /// </summary>
        static Material LoadOrCreateParticleMat()
        {
            const string path = "Assets/Materials/ParticleSnow.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            EnsureDir("Assets/Materials");
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("[ProjectSetup] Shader 'Universal Render Pipeline/Particles/Unlit' not found — falling back to 'Universal Render Pipeline/Unlit'.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (shader == null)
                shader = Shader.Find("Standard");

            mat = new Material(shader);
            mat.color = Color.white;
            // Enable alpha blending (Surface = Transparent) if the property exists on this variant
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f); // 0 = Opaque, 1 = Transparent
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            AssetDatabase.CreateAsset(mat, path);
            Debug.Log("[ProjectSetup] Created ParticleSnow.mat at " + path);
            return mat;
        }

        static Text CreateUIText(GameObject parent, string name, string defaultText,
            Font font, int fontSize, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var txt = go.AddComponent<Text>();
            txt.text = defaultText;
            if (font != null) txt.font = font;
            txt.fontSize = fontSize;
            txt.color = Color.white;
            txt.raycastTarget = false;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = anchorMin; // pivot matches anchor for corner-anchored elements
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;
            return txt;
        }

        static void EnsureDir(string assetPath)
        {
            string full = Path.Combine(Application.dataPath, "..", assetPath);
            if (!Directory.Exists(full))
                Directory.CreateDirectory(full);
        }

        static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes)
                if (s.path == scenePath) return; // already present

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes)
            {
                new EditorBuildSettingsScene(scenePath, true)
            };
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[ProjectSetup] Added " + scenePath + " to EditorBuildSettings.");
        }
    }
}
