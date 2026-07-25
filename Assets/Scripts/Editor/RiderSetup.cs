// RiderSetup.cs
// Task 5: Imports the Quaternius Animated Human FBX as a humanoid, builds the Rider prefab,
// places it under the player Visual root in Main.unity, and wires RiderAnimator (Task 6 Step 2).
//
// Menu:   Snowdrift/Build Rider Prefab
// Batch:  -executeMethod Snowdrift.Editor.RiderSetup.BuildAll
//
// Idempotency: safe to run multiple times.  Overwrites the prefab content in-place.

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Snowdrift.Game;

namespace Snowdrift.Editor
{
    public static class RiderSetup
    {
        // ─────────────────────────────────────────────────────────────────────
        // Paths
        // ─────────────────────────────────────────────────────────────────────
        private const string IncomingFbx    = "D:/proyects/snowdrift/_incoming/Quaternius_AnimatedHuman.fbx";
        private const string RiderFbxPath   = "Assets/Art/Rider/AnimatedHuman.fbx";
        private const string RiderDir       = "Assets/Art/Rider";
        private const string PrefabPath     = "Assets/Prefabs/Rider.prefab";
        private const string PrefabDir      = "Assets/Prefabs";
        private const string ControllerPath = "Assets/Art/Animations/RiderController.controller";
        private const string ScenePath      = "Assets/Scenes/Main.unity";

        // ─────────────────────────────────────────────────────────────────────
        // Menu entry
        // ─────────────────────────────────────────────────────────────────────
        [MenuItem("Snowdrift/Build Rider Prefab")]
        public static void BuildAll()
        {
            Debug.Log("[RiderSetup] BuildAll() start.");

            if (!Step0_EnsureFbxInProject())
            {
                Debug.LogError("[RiderSetup] BLOCKED: FBX not available. Aborting.");
                return;
            }

            Step1_ConfigureImporter();
            Step2_3_BuildAndSavePrefab();
            Step4_WireScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[RiderSetup] BuildAll() complete.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step 0: Copy FBX from _incoming if not already in Assets
        // ─────────────────────────────────────────────────────────────────────
        static bool Step0_EnsureFbxInProject()
        {
            EnsureDir(RiderDir);

            // Check if already imported
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RiderFbxPath) != null)
            {
                Debug.Log("[RiderSetup] FBX already in project at " + RiderFbxPath);
                return true;
            }

            // Not in project yet — copy from _incoming
            string fullDest = Path.GetFullPath(Path.Combine(Application.dataPath, "..", RiderFbxPath));
            if (!File.Exists(fullDest))
            {
                string fullSrc = Path.GetFullPath(IncomingFbx);
                if (!File.Exists(fullSrc))
                {
                    Debug.LogError("[RiderSetup] Source FBX not found at: " + fullSrc);
                    return false;
                }
                File.Copy(fullSrc, fullDest, overwrite: false);
                Debug.Log("[RiderSetup] Copied FBX to " + RiderFbxPath);
            }

            AssetDatabase.ImportAsset(RiderFbxPath, ImportAssetOptions.ForceSynchronousImport);

            // Verify import succeeded
            if (AssetDatabase.LoadAssetAtPath<GameObject>(RiderFbxPath) == null)
            {
                Debug.LogError("[RiderSetup] FBX import failed for " + RiderFbxPath);
                return false;
            }

            Debug.Log("[RiderSetup] FBX imported at " + RiderFbxPath);
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step 1: Set humanoid rig + bake root settings on the rider FBX
        //         (and on any Mixamo FBX under Assets/Art/Animations if present)
        // ─────────────────────────────────────────────────────────────────────
        static void Step1_ConfigureImporter()
        {
            ConfigureFbxImporter(RiderFbxPath);

            // Also apply to any Mixamo FBX in Animations folder (currently none, but future-proof)
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Art/Animations" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) &&
                    !path.EndsWith(".FBX", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                ConfigureFbxImporter(path);
            }

            // Re-run pose clips + controller after importer changes
            Debug.Log("[RiderSetup] Re-running PoseClipGenerator.GenerateAll() to refresh controller.");
            PoseClipGenerator.GenerateAll();
        }

        static void ConfigureFbxImporter(string assetPath)
        {
            var imp = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (imp == null)
            {
                Debug.LogWarning("[RiderSetup] No ModelImporter found at " + assetPath);
                return;
            }

            bool dirty = false;

            // Set humanoid rig
            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                dirty = true;
                Debug.Log("[RiderSetup] Set animationType=Human for " + assetPath);
            }

            // Copy defaultClipAnimations → clipAnimations with lockRoot settings
            // (needed on first import when clipAnimations is empty)
            var defaults = imp.defaultClipAnimations;
            if (defaults != null && defaults.Length > 0)
            {
                // Check if any clip is missing root locks
                bool needsUpdate = false;
                var clips = imp.clipAnimations;

                // If clipAnimations is empty, seed from defaults
                if (clips == null || clips.Length == 0)
                {
                    clips = defaults;
                    needsUpdate = true;
                }
                else
                {
                    foreach (var c in clips)
                    {
                        if (!c.lockRootRotation || !c.lockRootHeightY || !c.lockRootPositionXZ)
                        { needsUpdate = true; break; }
                    }
                }

                if (needsUpdate)
                {
                    foreach (var c in clips)
                    {
                        c.lockRootRotation    = true;
                        c.lockRootHeightY     = true;
                        c.lockRootPositionXZ  = true;
                    }
                    imp.clipAnimations = clips;
                    dirty = true;
                    Debug.Log("[RiderSetup] Applied lockRoot* to " + clips.Length + " clip(s) in " + assetPath);
                }
            }

            if (dirty)
            {
                imp.SaveAndReimport();
                Debug.Log("[RiderSetup] Reimported " + assetPath);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step 2+3: Instantiate rider, configure, save as prefab
        // ─────────────────────────────────────────────────────────────────────
        static void Step2_3_BuildAndSavePrefab()
        {
            EnsureDir(PrefabDir);

            // Load FBX root
            var fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(RiderFbxPath);
            if (fbxRoot == null)
            {
                Debug.LogError("[RiderSetup] Cannot load FBX asset at " + RiderFbxPath);
                return;
            }

            // Instantiate a temp copy in the scene to configure
            var riderInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxRoot);
            riderInstance.name = "RiderVisual";

            // Determine scale: check renderer bounds (Quaternius models are ~1.8m, usually fine)
            var renderers = riderInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length > 0)
            {
                // Compute combined bounds in world space
                Bounds bounds = renderers[0].bounds;
                foreach (var r in renderers)
                    bounds.Encapsulate(r.bounds);

                float height = bounds.size.y;
                Debug.Log($"[RiderSetup] Rider FBX bounds height = {height:F3}m");

                // Scale if wildly off (outside 1.0–2.5m range)
                if (height > 0.01f && (height < 1.0f || height > 2.5f))
                {
                    float targetHeight = 1.7f;
                    float scaleFactor = targetHeight / height;
                    riderInstance.transform.localScale = Vector3.one * scaleFactor;
                    Debug.Log($"[RiderSetup] Applied scale {scaleFactor:F4} to reach ~1.7m rider height.");
                }
            }

            // Position: feet at board deck level (y ≈ 0.08), face +Z
            riderInstance.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            riderInstance.transform.localRotation = Quaternion.identity;

            // Add Animator component
            var animator = riderInstance.GetComponent<Animator>();
            if (animator == null)
                animator = riderInstance.AddComponent<Animator>();

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller == null)
                Debug.LogWarning("[RiderSetup] RiderController.controller not found at " + ControllerPath + ". Animator will have no controller.");
            else
                Debug.Log("[RiderSetup] Assigned RiderController.controller to rider Animator.");

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            // Save as prefab (overwrite if exists)
            var prefab = PrefabUtility.SaveAsPrefabAsset(riderInstance, PrefabPath);
            if (prefab == null)
                Debug.LogError("[RiderSetup] Failed to save prefab at " + PrefabPath);
            else
                Debug.Log("[RiderSetup] Saved Rider.prefab at " + PrefabPath);

            // Destroy the temp instance
            Object.DestroyImmediate(riderInstance);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step 4: Wire prefab into Main.unity + add RiderAnimator (Task 6 Step 2)
        // ─────────────────────────────────────────────────────────────────────
        static void Step4_WireScene()
        {
            // Load the scene
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError("[RiderSetup] Could not open scene at " + ScenePath);
                return;
            }

            // Find Player root
            GameObject playerGo = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Player") { playerGo = root; break; }
            }
            if (playerGo == null)
            {
                Debug.LogError("[RiderSetup] 'Player' GameObject not found in scene. Run 'Snowdrift/Build Scene' first.");
                return;
            }

            // Find Visual child
            Transform visualTransform = playerGo.transform.Find("Visual");
            if (visualTransform == null)
            {
                Debug.LogError("[RiderSetup] 'Visual' child not found under Player. Run 'Snowdrift/Build Scene' first.");
                return;
            }

            // Remove any existing RiderVisual (idempotent)
            var existing = visualTransform.Find("RiderVisual");
            if (existing != null)
            {
                Debug.Log("[RiderSetup] Removing existing RiderVisual for idempotent re-run.");
                Object.DestroyImmediate(existing.gameObject);
            }

            // Load the prefab
            var riderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (riderPrefab == null)
            {
                Debug.LogError("[RiderSetup] Rider.prefab not found at " + PrefabPath + ". Run Step 2+3 first.");
                return;
            }

            // Instantiate prefab under Visual root
            var riderGo = (GameObject)PrefabUtility.InstantiatePrefab(riderPrefab, visualTransform);
            riderGo.name = "RiderVisual";
            riderGo.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            riderGo.transform.localRotation = Quaternion.identity;

            Debug.Log("[RiderSetup] Instantiated Rider prefab under Player/Visual.");

            // ── Task 6 Step 2: Wire RiderAnimator ──────────────────────────
            // Add RiderAnimator to the player root (so it can access BoardController).
            // Remove existing to avoid double-wiring on re-run.
            var existingRA = playerGo.GetComponent<RiderAnimator>();
            if (existingRA != null)
            {
                Debug.Log("[RiderSetup] Removing existing RiderAnimator for idempotent re-run.");
                Object.DestroyImmediate(existingRA);
            }

            var riderAnimator = playerGo.AddComponent<RiderAnimator>();

            // Use SerializedObject to assign private [SerializeField] fields
            var so = new SerializedObject(riderAnimator);

            // board → BoardController on player root
            var boardController = playerGo.GetComponent<BoardController>();
            if (boardController == null)
                Debug.LogWarning("[RiderSetup] BoardController not found on Player — board field will be null.");
            so.FindProperty("board").objectReferenceValue = boardController;

            // animator → Animator on the rider instance
            var riderAnimComponent = riderGo.GetComponent<Animator>();
            so.FindProperty("animator").objectReferenceValue = riderAnimComponent;

            // spineBone → via Animator.GetBoneTransform if humanoid avatar is valid
            Transform spineBone = null;
            if (riderAnimComponent != null && riderAnimComponent.isHuman && riderAnimComponent.avatar != null && riderAnimComponent.avatar.isValid)
            {
                spineBone = riderAnimComponent.GetBoneTransform(HumanBodyBones.Spine);
                if (spineBone == null)
                    Debug.LogWarning("[RiderSetup] GetBoneTransform(Spine) returned null despite valid humanoid avatar — leaving spineBone unassigned.");
                else
                    Debug.Log("[RiderSetup] Assigned spineBone: " + spineBone.name);
            }
            else
            {
                Debug.LogWarning("[RiderSetup] Rider Animator does not have a valid humanoid avatar — spineBone left unassigned. RiderAnimator handles null spine gracefully.");
            }
            so.FindProperty("spineBone").objectReferenceValue = spineBone;

            so.ApplyModifiedProperties();

            Debug.Log("[RiderSetup] RiderAnimator wired on Player.");

            // Save the scene
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[RiderSetup] Scene saved at " + ScenePath);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helper
        // ─────────────────────────────────────────────────────────────────────
        static void EnsureDir(string assetPath)
        {
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            if (!Directory.Exists(full))
            {
                Directory.CreateDirectory(full);
                Debug.Log("[RiderSetup] Created directory: " + assetPath);
            }
        }
    }
}
