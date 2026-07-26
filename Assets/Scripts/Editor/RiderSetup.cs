// RiderSetup.cs
// Task 5: Imports the Quaternius Animated Human FBX as a humanoid, builds the Rider prefab,
// places it under the player Visual root in Main.unity, and wires RiderAnimator (Task 6 Step 2).
//
// Menu:   Snowdrift/Build Rider Prefab
// Batch:  -executeMethod Snowdrift.Editor.RiderSetup.BuildAll
//
// Idempotency: safe to run multiple times.  Overwrites the prefab content in-place.

using System.IO;
using System.Collections.Generic;
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
        internal const string PrefabPath     = "Assets/Prefabs/Rider.prefab";
        private const string PrefabDir      = "Assets/Prefabs";
        private const string ControllerPath = "Assets/Art/Animations/RiderController.controller";
        private const string ScenePath      = "Assets/Scenes/Main.unity";

        // Rider material paths
        private const string SuitMatPath = "Assets/Materials/RiderSuit.mat";
        private const string SkinMatPath = "Assets/Materials/RiderSkin.mat";

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
                File.Copy(fullSrc, fullDest, overwrite: true);
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

            // ── Task 7: Build ragdoll inside the prefab instance ──
            Step2b_BuildRagdoll(riderInstance, animator);

            // ── Rider materials: deep teal suit + light skin ──────────────────
            // Contrast fix: FBX embedded material is near-white (invisible on snow).
            // Apply a dark teal suit (#2E5266) to all body SkinnedMeshRenderers,
            // keeping head/face meshes with a skin tone for silhouette readability.
            EnsureDir("Assets/Materials");
            var suitMat = LoadOrCreateRiderMat(SuitMatPath, new Color(0.180f, 0.322f, 0.400f), 0.25f); // #2E5266
            var skinMat = LoadOrCreateRiderMat(SkinMatPath, new Color(0.894f, 0.722f, 0.596f), 0.05f); // warm skin
            Step2c_ApplyRiderMaterials(riderInstance, suitMat, skinMat);

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
        // Step 2b (Task 7): Build ragdoll inside the rider prefab instance.
        // Called from Step2_3_BuildAndSavePrefab before SaveAsPrefabAsset.
        // Idempotent: strips existing joints/rigidbodies/colliders first.
        // ─────────────────────────────────────────────────────────────────────
        static void Step2b_BuildRagdoll(GameObject riderInstance, Animator anim)
        {
            if (anim == null || !anim.isHuman || anim.avatar == null || !anim.avatar.isValid)
            {
                Debug.LogWarning("[RiderSetup] Ragdoll skipped: Animator is not humanoid or avatar invalid.");
                return;
            }

            int ragdollLayer = EnsureRagdollLayer();

            // ── Resolve bone transforms ──────────────────────────────────────
            // 11 bones: Hips, Spine, Head, UpperArm×2, LowerArm×2, UpperLeg×2, LowerLeg×2
            var boneMap = new Dictionary<HumanBodyBones, Transform>
            {
                { HumanBodyBones.Hips,           anim.GetBoneTransform(HumanBodyBones.Hips) },
                { HumanBodyBones.Spine,          anim.GetBoneTransform(HumanBodyBones.Spine) },
                { HumanBodyBones.Head,           anim.GetBoneTransform(HumanBodyBones.Head) },
                { HumanBodyBones.LeftUpperArm,   anim.GetBoneTransform(HumanBodyBones.LeftUpperArm) },
                { HumanBodyBones.RightUpperArm,  anim.GetBoneTransform(HumanBodyBones.RightUpperArm) },
                { HumanBodyBones.LeftLowerArm,   anim.GetBoneTransform(HumanBodyBones.LeftLowerArm) },
                { HumanBodyBones.RightLowerArm,  anim.GetBoneTransform(HumanBodyBones.RightLowerArm) },
                { HumanBodyBones.LeftUpperLeg,   anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg) },
                { HumanBodyBones.RightUpperLeg,  anim.GetBoneTransform(HumanBodyBones.RightUpperLeg) },
                { HumanBodyBones.LeftLowerLeg,   anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg) },
                { HumanBodyBones.RightLowerLeg,  anim.GetBoneTransform(HumanBodyBones.RightLowerLeg) },
            };

            // Check all bones resolved
            foreach (var kvp in boneMap)
            {
                if (kvp.Value == null)
                {
                    Debug.LogWarning($"[RiderSetup] Ragdoll: bone {kvp.Key} not found — skipping ragdoll build.");
                    return;
                }
            }

            // ── Strip existing ragdoll components (idempotency) ──────────────
            // Must remove joints first (they reference Rigidbodies), then Rigidbodies, then Colliders.
            foreach (var kvp in boneMap)
            {
                var t = kvp.Value;
                foreach (var j in t.GetComponents<CharacterJoint>())
                    Object.DestroyImmediate(j);
            }
            foreach (var kvp in boneMap)
            {
                var t = kvp.Value;
                foreach (var rb in t.GetComponents<Rigidbody>())
                    Object.DestroyImmediate(rb);
                foreach (var col in t.GetComponents<Collider>())
                    Object.DestroyImmediate(col);
            }

            // ── Mass table — human-plausible distribution ────────────────────
            // Legs heavier than arms (previously inverted) → lower centre of mass,
            // ragdoll settles feet/hips-down instead of top-heavy cartwheeling.
            var massMap = new Dictionary<HumanBodyBones, float>
            {
                { HumanBodyBones.Hips,          10f },
                { HumanBodyBones.Spine,         10f },
                { HumanBodyBones.Head,          5f },
                { HumanBodyBones.LeftUpperArm,  2f },
                { HumanBodyBones.RightUpperArm, 2f },
                { HumanBodyBones.LeftLowerArm,  1.5f },
                { HumanBodyBones.RightLowerArm, 1.5f },
                { HumanBodyBones.LeftUpperLeg,  6f },
                { HumanBodyBones.RightUpperLeg, 6f },
                { HumanBodyBones.LeftLowerLeg,  4f },
                { HumanBodyBones.RightLowerLeg, 4f },
            };

            // Shared snow-contact material: moderate friction so the ragdoll ploughs
            // and slows in snow instead of ice-sliding forever; near-zero bounce.
            var bonePhysMat = LoadOrCreateBonePhysicsMaterial();

            // ── Add Rigidbody + Collider to every bone ───────────────────────
            var rbMap = new Dictionary<HumanBodyBones, Rigidbody>();
            foreach (var kvp in boneMap)
            {
                var bone  = kvp.Key;
                var t     = kvp.Value;
                t.gameObject.layer = ragdollLayer;

                var rb = t.gameObject.AddComponent<Rigidbody>();
                rb.mass                    = massMap[bone];
                rb.isKinematic             = true;
                rb.maxDepenetrationVelocity = 2f;   // cap depenetration impulse to reduce explosive separation
                rb.solverIterations        = 12;    // more solver passes keeps joints stable under violence
                rb.solverVelocityIterations = 4;
                rb.linearDamping           = 0.15f; // snow drag — bleeds slide speed
                rb.angularDamping          = 0.8f;  // limbs settle instead of windmilling
                rbMap[bone]    = rb;

                // Collider sized plausibly to bone role
                AddBoneCollider(bone, t);
                foreach (var bc in t.GetComponents<Collider>())
                    bc.sharedMaterial = bonePhysMat;
            }

            // ── CharacterJoints on child bones (Hips gets none) ─────────────
            // Joint hierarchy: Hips(root) → Spine → Head
            //                               ↓                ↓
            //               UpperArm → LowerArm    UpperLeg → LowerLeg
            var jointParent = new Dictionary<HumanBodyBones, HumanBodyBones>
            {
                { HumanBodyBones.Spine,          HumanBodyBones.Hips },
                { HumanBodyBones.Head,           HumanBodyBones.Spine },
                { HumanBodyBones.LeftUpperArm,   HumanBodyBones.Spine },
                { HumanBodyBones.RightUpperArm,  HumanBodyBones.Spine },
                { HumanBodyBones.LeftLowerArm,   HumanBodyBones.LeftUpperArm },
                { HumanBodyBones.RightLowerArm,  HumanBodyBones.RightUpperArm },
                { HumanBodyBones.LeftUpperLeg,   HumanBodyBones.Hips },
                { HumanBodyBones.RightUpperLeg,  HumanBodyBones.Hips },
                { HumanBodyBones.LeftLowerLeg,   HumanBodyBones.LeftUpperLeg },
                { HumanBodyBones.RightLowerLeg,  HumanBodyBones.RightUpperLeg },
            };

            // Per-joint anatomical limits: (twist, swing1, swing2).
            // Elbows/knees behave hinge-like (one wide axis, others tight) — uniform
            // limits previously let knees bend sideways, reading as broken limbs.
            var jointLimits = new Dictionary<HumanBodyBones, (float twist, float swing1, float swing2)>
            {
                { HumanBodyBones.Spine,          (20f,  30f, 20f) },
                { HumanBodyBones.Head,           (40f,  45f, 25f) },
                { HumanBodyBones.LeftUpperArm,   (60f,  80f, 60f) },
                { HumanBodyBones.RightUpperArm,  (60f,  80f, 60f) },
                { HumanBodyBones.LeftLowerArm,   (10f, 100f, 10f) },
                { HumanBodyBones.RightLowerArm,  (10f, 100f, 10f) },
                { HumanBodyBones.LeftUpperLeg,   (30f,  70f, 40f) },
                { HumanBodyBones.RightUpperLeg,  (30f,  70f, 40f) },
                { HumanBodyBones.LeftLowerLeg,   (10f, 110f, 10f) },
                { HumanBodyBones.RightLowerLeg,  (10f, 110f, 10f) },
            };

            foreach (var kvp in jointParent)
            {
                var childBone  = kvp.Key;
                var parentBone = kvp.Value;
                var t          = boneMap[childBone];

                var joint = t.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = rbMap[parentBone];

                // Projection: snaps bone back to within projectionDistance of its anchor
                // when the solver cannot converge, preventing visible limb stretching.
                joint.enableProjection    = true;
                joint.projectionDistance  = 0.1f;   // max gap before projection kicks in (metres)
                joint.projectionAngle     = 20f;    // max angular drift before angular projection (degrees)

                var lim = jointLimits[childBone];
                var lowTwist  = new SoftJointLimit { limit = -lim.twist };
                var highTwist = new SoftJointLimit { limit =  lim.twist };
                var swing1    = new SoftJointLimit { limit =  lim.swing1 };
                var swing2    = new SoftJointLimit { limit =  lim.swing2 };
                joint.lowTwistLimit  = lowTwist;
                joint.highTwistLimit = highTwist;
                joint.swing1Limit    = swing1;
                joint.swing2Limit    = swing2;

                // Soft limit springs: cushioned stop at the limit instead of a hard
                // clamp — removes the "mannequin snap" look when limbs hit limits.
                var spring = new SoftJointLimitSpring { spring = 250f, damper = 25f };
                joint.twistLimitSpring = spring;
                joint.swingLimitSpring = spring;
            }

            Debug.Log($"[RiderSetup] Ragdoll built: {boneMap.Count} bones on layer '{LayerMask.LayerToName(ragdollLayer)}' ({ragdollLayer}).");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Rider material helpers
        // ─────────────────────────────────────────────────────────────────────

        /// Load or create the shared physics material for ragdoll bone colliders.
        static PhysicsMaterial LoadOrCreateBonePhysicsMaterial()
        {
            const string path = "Assets/Materials/RagdollBone.physicMaterial";
            var existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (existing != null) return existing;

            var pm = new PhysicsMaterial("RagdollBone")
            {
                dynamicFriction = 0.6f,
                staticFriction  = 0.7f,
                bounciness      = 0.05f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine   = PhysicsMaterialCombine.Minimum,
            };
            AssetDatabase.CreateAsset(pm, path);
            Debug.Log("[RiderSetup] Created ragdoll bone physics material at " + path);
            return pm;
        }

        /// Load existing mat or create a new URP/Lit one at the given path.
        static Material LoadOrCreateRiderMat(string path, Color color, float smoothness)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = color;
            mat.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(mat, path);
            Debug.Log($"[RiderSetup] Created rider material at {path}");
            return mat;
        }

        /// Assign suitMat to all SkinnedMeshRenderers on the rider.
        /// If the FBX has a separate head/face mesh (name contains "head" or "face"),
        /// assign skinMat to it instead for silhouette readability.
        static void Step2c_ApplyRiderMaterials(GameObject riderInstance, Material suitMat, Material skinMat)
        {
            if (suitMat == null)
            {
                Debug.LogWarning("[RiderSetup] suitMat is null — skipping material assignment.");
                return;
            }

            var smrs = riderInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs.Length == 0)
            {
                Debug.LogWarning("[RiderSetup] No SkinnedMeshRenderers found on rider — material not applied.");
                return;
            }

            int suitCount = 0, skinCount = 0;
            foreach (var smr in smrs)
            {
                string lname = smr.gameObject.name.ToLowerInvariant();
                bool isHead  = lname.Contains("head") || lname.Contains("face");
                Material target = (isHead && skinMat != null) ? skinMat : suitMat;

                // Replace every material slot with the chosen material
                var slots = new Material[smr.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = target;
                smr.sharedMaterials = slots;

                if (isHead) skinCount++; else suitCount++;
            }

            Debug.Log($"[RiderSetup] Rider materials applied: {suitCount} suit slot(s), {skinCount} skin slot(s).");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Add a plausibly-sized collider to a ragdoll bone transform.
        // ─────────────────────────────────────────────────────────────────────
        static void AddBoneCollider(HumanBodyBones bone, Transform t)
        {
            switch (bone)
            {
                case HumanBodyBones.Head:
                {
                    var s = t.gameObject.AddComponent<SphereCollider>();
                    s.radius = 0.11f;
                    s.center = Vector3.up * 0.07f;
                    break;
                }
                case HumanBodyBones.Hips:
                {
                    var b = t.gameObject.AddComponent<BoxCollider>();
                    b.size   = new Vector3(0.28f, 0.18f, 0.18f);
                    b.center = Vector3.zero;
                    break;
                }
                case HumanBodyBones.Spine:
                {
                    var c = t.gameObject.AddComponent<CapsuleCollider>();
                    c.direction = 1; // Y axis
                    c.height    = 0.30f;
                    c.radius    = 0.12f;
                    c.center    = new Vector3(0f, 0.15f, 0f);
                    break;
                }
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm:
                {
                    var c = t.gameObject.AddComponent<CapsuleCollider>();
                    c.direction = 0; // X axis (arm extends laterally)
                    c.height    = 0.26f;
                    c.radius    = 0.05f;
                    c.center    = new Vector3(bone == HumanBodyBones.LeftUpperArm ? -0.13f : 0.13f, 0f, 0f);
                    break;
                }
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm:
                {
                    var c = t.gameObject.AddComponent<CapsuleCollider>();
                    c.direction = 0;
                    c.height    = 0.24f;
                    c.radius    = 0.04f;
                    c.center    = new Vector3(bone == HumanBodyBones.LeftLowerArm ? -0.12f : 0.12f, 0f, 0f);
                    break;
                }
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg:
                {
                    var c = t.gameObject.AddComponent<CapsuleCollider>();
                    c.direction = 1; // Y axis (leg extends down)
                    c.height    = 0.38f;
                    c.radius    = 0.07f;
                    c.center    = new Vector3(0f, -0.19f, 0f);
                    break;
                }
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg:
                {
                    var c = t.gameObject.AddComponent<CapsuleCollider>();
                    c.direction = 1;
                    c.height    = 0.36f;
                    c.radius    = 0.05f;
                    c.center    = new Vector3(0f, -0.18f, 0f);
                    break;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Ensure the "Ragdoll" layer exists in TagManager.asset.
        // Writes to the first free slot >= 8.
        // Returns the layer index.
        // ─────────────────────────────────────────────────────────────────────
        static int EnsureRagdollLayer()
        {
            const string layerName = "Ragdoll";

            // Check if already exists
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0)
                return existing;

            var tagManagerPath = "ProjectSettings/TagManager.asset";
            var tagManagerSO   = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath(tagManagerPath)[0]);
            var layers = tagManagerSO.FindProperty("layers");

            // Find first free slot >= 8 (Unity built-in layers occupy 0-7)
            int freeSlot = -1;
            for (int i = 8; i < layers.arraySize; i++)
            {
                string val = layers.GetArrayElementAtIndex(i).stringValue;
                if (string.IsNullOrEmpty(val))
                {
                    freeSlot = i;
                    break;
                }
            }

            if (freeSlot < 0)
            {
                Debug.LogError("[RiderSetup] No free layer slot >= 8 found in TagManager. Ragdoll layer not created.");
                return 0; // fallback to Default — ragdoll still works, just no layer separation
            }

            layers.GetArrayElementAtIndex(freeSlot).stringValue = layerName;
            tagManagerSO.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            // Force Unity to reload the layer registry
            AssetDatabase.ImportAsset(tagManagerPath);

            int result = LayerMask.NameToLayer(layerName);
            if (result < 0)
            {
                // LayerMask not yet refreshed in this domain; return the slot index directly
                result = freeSlot;
            }
            Debug.Log($"[RiderSetup] Created layer '{layerName}' at index {result}.");
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step 4: Wire prefab into Main.unity + add RiderAnimator (Task 6 Step 2)
        // ─────────────────────────────────────────────────────────────────────
        internal static void Step4_WireScene()
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
            // Snowboard stance: rotate the whole rider ~-35° around Y so the body faces
            // slightly across the board (regular stance) instead of ski-straight downhill.
            // Combined with the leg-spread muscles in the pose clips, the feet land along
            // the board's long axis. TrickSystem owns Player-root rotation; this is a
            // fixed local offset on the visual only.
            riderGo.transform.localRotation = Quaternion.Euler(0f, -35f, 0f);

            // Seat offset measured at RUNTIME (Diag_Closeup_RiderAndBoard telemetry):
            // the pose clips carry muscle curves only (no RootT), so the humanoid
            // Animator pins the hips near the rider-transform origin and the feet hang
            // ~0.53 below it. Bind-pose SMR bounds are useless here — the Animator
            // overrides the pose at runtime. Lifting the rider transform moves the
            // animated hips 1:1 (verified: +0.131 localY -> +0.130 hips). With
            // localY = 0.211 the feet sat at -0.316 relative to the player root and
            // the board deck top is ~+0.10, so localY = 0.211 + (0.10 - (-0.316)) = 0.63.
            // localZ = 0.22: the Ride pose bends knees so feet trail ~0.25 behind the
            // hips — diag side capture showed feet over the tail. Shifting the rider
            // forward centres the feet over the board's midpoint.
            riderGo.transform.localPosition = new Vector3(0f, 0.63f, 0.22f);
            Debug.Log("[RiderSetup] Rider seated at localY=0.63, localZ=0.22, yaw=-35 (snowboard stance).");

            Debug.Log("[RiderSetup] Instantiated Rider prefab under Player/Visual.");

            // Remove placeholder capsule (BodyMesh) that ProjectSetup creates as a stand-in.
            // CrashHandler.visual points at the Visual root, not at BodyMesh, so this is safe.
            // BoardVisual is intentionally kept.
            var bodyMesh = visualTransform.Find("BodyMesh");
            if (bodyMesh != null)
            {
                Object.DestroyImmediate(bodyMesh.gameObject);
                Debug.Log("[RiderSetup] Removed placeholder BodyMesh from Player/Visual.");
            }

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
