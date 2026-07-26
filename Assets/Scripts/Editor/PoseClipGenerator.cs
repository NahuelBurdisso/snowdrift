// PoseClipGenerator.cs
// Generates 1-frame Humanoid pose AnimationClips and an AnimatorController for the rider.
// Re-run GenerateAll() after importing real Mixamo FBX clips — the controller builder will
// pick them up automatically and replace the fallback pose clips.
//
// NOTE: BuildRiderController deletes and recreates RiderController.controller when it already
// exists. Prefab wiring breaks but is restored when the prefab is wired in Task 5 (which runs
// after this). This is intentional — rebuilding in-place is complex and not worth it here.

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Snowdrift.Editor
{
    public static class PoseClipGenerator
    {
        // ─────────────────────────────────────────────────────────────────────────
        // Paths
        // ─────────────────────────────────────────────────────────────────────────
        private const string AnimDir        = "Assets/Art/Animations";
        private const string ControllerPath = "Assets/Art/Animations/RiderController.controller";

        // ─────────────────────────────────────────────────────────────────────────
        // RiderPose int values — MUST match Snowdrift.Core.RiderPose enum order:
        //   Ride=0, Air=1, GrabL=2, GrabR=3, Brace=4
        // ─────────────────────────────────────────────────────────────────────────
        private const int PoseRide  = 0;
        private const int PoseAir   = 1;
        private const int PoseGrabL = 2;
        private const int PoseGrabR = 3;
        private const int PoseBrace = 4;

        // ─────────────────────────────────────────────────────────────────────────
        // Pose definitions
        // ─────────────────────────────────────────────────────────────────────────
        private struct PoseCurve
        {
            public string Muscle;
            public float  Value;
            public PoseCurve(string muscle, float value) { Muscle = muscle; Value = value; }
        }

        private struct PoseDef
        {
            public string      Name;
            public PoseCurve[] Curves;
            public PoseDef(string name, PoseCurve[] curves) { Name = name; Curves = curves; }
        }

        private static readonly PoseDef[] Poses = new[]
        {
            new PoseDef("Ride", new[]
            {
                new PoseCurve("Spine Front-Back",             -0.15f),
                new PoseCurve("Left Upper Leg Front-Back",    -0.3f),
                new PoseCurve("Right Upper Leg Front-Back",   -0.3f),
                new PoseCurve("Left Lower Leg Stretch",       -0.3f),
                new PoseCurve("Right Lower Leg Stretch",      -0.3f),
                // Snowboard stance: spread legs sideways so feet straddle the deck
                new PoseCurve("Left Upper Leg In-Out",         0.35f),
                new PoseCurve("Right Upper Leg In-Out",        0.35f),
            }),
            new PoseDef("GrabL", new[]
            {
                new PoseCurve("Left Arm Down-Up",             -0.9f),
                new PoseCurve("Spine Left-Right",              0.3f),
                new PoseCurve("Left Upper Leg Front-Back",    -0.8f),
                new PoseCurve("Right Upper Leg Front-Back",   -0.8f),
                new PoseCurve("Left Lower Leg Stretch",       -0.8f),
                new PoseCurve("Right Lower Leg Stretch",      -0.8f),
                new PoseCurve("Left Upper Leg In-Out",         0.35f),
                new PoseCurve("Right Upper Leg In-Out",        0.35f),
            }),
            new PoseDef("GrabR", new[]
            {
                new PoseCurve("Right Arm Down-Up",            -0.9f),
                new PoseCurve("Spine Left-Right",             -0.3f),
                new PoseCurve("Left Upper Leg Front-Back",    -0.8f),
                new PoseCurve("Right Upper Leg Front-Back",   -0.8f),
                new PoseCurve("Left Lower Leg Stretch",       -0.8f),
                new PoseCurve("Right Lower Leg Stretch",      -0.8f),
                new PoseCurve("Left Upper Leg In-Out",         0.35f),
                new PoseCurve("Right Upper Leg In-Out",        0.35f),
            }),
            new PoseDef("Brace", new[]
            {
                new PoseCurve("Left Upper Leg Front-Back",    -0.6f),
                new PoseCurve("Right Upper Leg Front-Back",   -0.6f),
                new PoseCurve("Left Lower Leg Stretch",       -0.6f),
                new PoseCurve("Right Lower Leg Stretch",      -0.6f),
                new PoseCurve("Left Arm Down-Up",              0.4f),
                new PoseCurve("Right Arm Down-Up",             0.4f),
                new PoseCurve("Left Upper Leg In-Out",         0.35f),
                new PoseCurve("Right Upper Leg In-Out",        0.35f),
            }),
        };

        // ─────────────────────────────────────────────────────────────────────────
        // Menu items
        // ─────────────────────────────────────────────────────────────────────────
        [MenuItem("Snowdrift/Generate Pose Clips")]
        public static void GeneratePoseClips()
        {
            EnsureDir(AnimDir);

            // Cache all valid muscle names for validation
            string[] validMuscles = HumanTrait.MuscleName;

            foreach (var pose in Poses)
            {
                string assetPath = $"{AnimDir}/Pose_{pose.Name}.anim";

                // Idempotent: load existing clip to overwrite curves; create fresh if absent
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                bool isNew = clip == null;
                if (isNew)
                {
                    clip = new AnimationClip();
                    clip.name = $"Pose_{pose.Name}";
                }

                // Humanoid clips must NOT be legacy
                clip.legacy = false;
                // 1-frame clip: length effectively 0 — no need to set frameRate explicitly

                // Clear existing curves on an existing clip so we don't accumulate stale ones
                if (!isNew)
                    AnimationUtility.SetEditorCurves(clip,
                        AnimationUtility.GetCurveBindings(clip),
                        new AnimationCurve[AnimationUtility.GetCurveBindings(clip).Length]);

                var invalidMuscles = new System.Collections.Generic.List<string>();
                foreach (var pc in pose.Curves)
                {
                    if (!ValidateMuscle(pc.Muscle, validMuscles))
                    {
                        invalidMuscles.Add(pc.Muscle);
                        continue; // error already logged inside ValidateMuscle
                    }

                    var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), pc.Muscle);
                    // Single key at t=0 — AnimationCurve.Constant works but leaves a redundant
                    // second key at t=1; use a single-key curve instead to keep clips minimal.
                    var curve = new AnimationCurve(new Keyframe(0f, pc.Value));
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }

                if (invalidMuscles.Count > 0)
                {
                    Debug.LogError($"[PoseClipGenerator] Pose '{pose.Name}' has {invalidMuscles.Count} invalid muscle name(s): {string.Join(", ", invalidMuscles.Select(m => $"\"{m}\""))} — skipping save of this clip to avoid partial output.");
                    continue; // don't write a partial clip; move on to the next pose
                }

                if (isNew)
                {
                    AssetDatabase.CreateAsset(clip, assetPath);
                    Debug.Log($"[PoseClipGenerator] Created {assetPath}");
                }
                else
                {
                    EditorUtility.SetDirty(clip);
                    Debug.Log($"[PoseClipGenerator] Updated curves on existing {assetPath}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[PoseClipGenerator] GeneratePoseClips complete.");
        }

        [MenuItem("Snowdrift/Build Rider Controller")]
        public static void BuildRiderController()
        {
            // Drift guard: int constants above MUST match Snowdrift.Core.RiderPose enum values.
            // Snowdrift.Editor.asmdef references Snowdrift.Core, so this assert compiles against
            // the live enum — any reorder will be caught at tool invocation time, not silently.
            Debug.Assert(
                (int)Snowdrift.Core.RiderPose.Ride  == 0 &&
                (int)Snowdrift.Core.RiderPose.Air   == 1 &&
                (int)Snowdrift.Core.RiderPose.GrabL == 2 &&
                (int)Snowdrift.Core.RiderPose.GrabR == 3 &&
                (int)Snowdrift.Core.RiderPose.Brace == 4,
                "RiderPose enum order drifted from controller Pose int mapping");

            EnsureDir(AnimDir);

            // ── Load generated pose clips ──────────────────────────────────────
            var clipRide  = LoadPoseClip("Ride");
            var clipGrabL = LoadPoseClip("GrabL");
            var clipGrabR = LoadPoseClip("GrabR");
            var clipBrace = LoadPoseClip("Brace");

            // ── Try to load Mixamo clips from FBX sub-assets ───────────────────
            var mixamoCrouch  = FindMixamoClip("crouch");
            var mixamoFalling = FindMixamoClip("falling");
            var mixamoLanding = FindMixamoClip("landing", "hard");
            var mixamoJump    = FindMixamoClip("jump");

            // ── Idempotent: delete existing controller and recreate ────────────
            // (See file header note — rebuilding in-place is complex; references
            //  are re-wired in Task 5 after real Mixamo clips are imported anyway.)
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
                Debug.Log("[PoseClipGenerator] Deleted existing RiderController.controller for rebuild.");
            }

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            // ── Parameters ────────────────────────────────────────────────────
            ctrl.AddParameter("Pose",  AnimatorControllerParameterType.Int);
            ctrl.AddParameter("Crouch", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Land",  AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Pop",   AnimatorControllerParameterType.Trigger);

            // ─────────────────────────────────────────────────────────────────
            // Layer 0: "Base"  (already created by CreateAnimatorControllerAtPath)
            // ─────────────────────────────────────────────────────────────────
            var baseLayer = ctrl.layers[0];
            baseLayer.name = "Base";
            baseLayer.defaultWeight = 1f;
            var baseSM = baseLayer.stateMachine;

            // ── Ride state: 1D BlendTree on Crouch ────────────────────────────
            AnimatorState rideState;
            {
                BlendTree tree;
                rideState = ctrl.CreateBlendTreeInController("Ride", out tree, 0);
                tree.blendType      = BlendTreeType.Simple1D;
                tree.blendParameter = "Crouch";

                Motion crouchMotion = mixamoCrouch != null ? (Motion)mixamoCrouch : (Motion)clipBrace;
                if (mixamoCrouch == null)
                    Debug.LogWarning("[PoseClipGenerator] Mixamo 'Crouch Idle' clip missing — falling back to Pose_Brace for Ride blend tree (max Crouch). Re-run after import.");

                tree.AddChild(clipRide,     0f);
                tree.AddChild(crouchMotion, 1f);
            }

            // ── Air state ─────────────────────────────────────────────────────
            Motion airMotion = mixamoFalling != null ? (Motion)mixamoFalling : (Motion)clipRide;
            if (mixamoFalling == null)
                Debug.LogWarning("[PoseClipGenerator] Mixamo 'Falling Idle' clip missing — falling back to Pose_Ride for Air state. Re-run after import.");
            var airState = baseSM.AddState("Air");
            airState.motion = airMotion;

            // ── GrabL, GrabR, Brace states ────────────────────────────────────
            var grabLState = baseSM.AddState("GrabL");
            grabLState.motion = clipGrabL;

            var grabRState = baseSM.AddState("GrabR");
            grabRState.motion = clipGrabR;

            var braceState = baseSM.AddState("Brace");
            braceState.motion = clipBrace;

            // ── AnyState → each state (Pose int equals) ───────────────────────
            // canTransitionToSelf=false is CRITICAL — default=true would restart
            // the state every frame whenever the condition is still true, freezing clips.
            AddAnyStateTransition(baseSM, rideState,  "Pose", PoseRide,  0.1f);
            AddAnyStateTransition(baseSM, airState,   "Pose", PoseAir,   0.1f);
            AddAnyStateTransition(baseSM, grabLState, "Pose", PoseGrabL, 0.1f);
            AddAnyStateTransition(baseSM, grabRState, "Pose", PoseGrabR, 0.1f);
            AddAnyStateTransition(baseSM, braceState, "Pose", PoseBrace, 0.1f);

            // Write layer 0 back (name + weight don't persist unless re-assigned)
            var layers = ctrl.layers;
            layers[0] = baseLayer;
            ctrl.layers = layers;

            // ─────────────────────────────────────────────────────────────────
            // Layer 1: "Oneshots"
            // ─────────────────────────────────────────────────────────────────
            // Must create state machine manually and AddObjectToAsset, then pass
            // it in an AnimatorControllerLayer struct to AddLayer — the string
            // overload of AddLayer doesn't support weight or blending settings.
            var oneshotSM = new AnimatorStateMachine();
            oneshotSM.name = "Oneshots";
            oneshotSM.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(oneshotSM, ctrl);

            // Empty (default) state
            var emptyState = oneshotSM.AddState("Empty");
            emptyState.motion = null; // no motion — pass-through

            // Land state
            Motion landMotion = mixamoLanding != null ? (Motion)mixamoLanding : (Motion)clipBrace;
            if (mixamoLanding == null)
                Debug.LogWarning("[PoseClipGenerator] Mixamo 'Hard Landing' clip missing — falling back to Pose_Brace for Land oneshot. Re-run after import.");
            var landState = oneshotSM.AddState("Land");
            landState.motion = landMotion;
            if (landMotion != null) SetNotLoop(landMotion);

            // Pop state
            Motion popMotion = mixamoJump != null ? (Motion)mixamoJump : (Motion)clipRide;
            if (mixamoJump == null)
                Debug.LogWarning("[PoseClipGenerator] Mixamo 'Jumping Up' clip missing — falling back to Pose_Ride for Pop oneshot. Re-run after import.");
            var popState = oneshotSM.AddState("Pop");
            popState.motion = popMotion;
            if (popMotion != null) SetNotLoop(popMotion);

            // Transitions
            // Empty → Land  (trigger Land, no exitTime, duration 0.05)
            {
                var t = emptyState.AddTransition(landState);
                t.hasExitTime = false;
                t.duration    = 0.05f;
                t.AddCondition(AnimatorConditionMode.If, 0, "Land");
            }
            // Land → Empty  (exitTime 0.9, hasExitTime=true, duration 0.1)
            {
                var t = landState.AddTransition(emptyState);
                t.hasExitTime = true;
                t.exitTime    = 0.9f;
                t.duration    = 0.1f;
            }
            // Empty → Pop  (trigger Pop, no exitTime, duration 0.05)
            {
                var t = emptyState.AddTransition(popState);
                t.hasExitTime = false;
                t.duration    = 0.05f;
                t.AddCondition(AnimatorConditionMode.If, 0, "Pop");
            }
            // Pop → Empty  (exitTime 0.4, hasExitTime=true, duration 0.1)
            {
                var t = popState.AddTransition(emptyState);
                t.hasExitTime = true;
                t.exitTime    = 0.4f;
                t.duration    = 0.1f;
            }

            oneshotSM.defaultState = emptyState;

            // Build the layer struct and add it
            var oneshotLayer = new AnimatorControllerLayer
            {
                name           = "Oneshots",
                defaultWeight  = 1f,
                blendingMode   = AnimatorLayerBlendingMode.Override,
                stateMachine   = oneshotSM,
                avatarMask     = null,
            };
            ctrl.AddLayer(oneshotLayer);

            // ── Save ──────────────────────────────────────────────────────────
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[PoseClipGenerator] RiderController.controller saved at {ControllerPath}");
        }

        /// <summary>
        /// Batch-mode entry point. Call both steps in sequence.
        /// Usage: -executeMethod Snowdrift.Editor.PoseClipGenerator.GenerateAll
        /// </summary>
        public static void GenerateAll()
        {
            Debug.Log("[PoseClipGenerator] GenerateAll() start.");
            GeneratePoseClips();
            BuildRiderController();
            Debug.Log("[PoseClipGenerator] GenerateAll() complete.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Validates that muscleName is a known HumanTrait muscle name.
        /// Logs an error with closest candidates if not found.
        /// Returns true when valid.
        /// </summary>
        private static bool ValidateMuscle(string muscleName, string[] validMuscles)
        {
            foreach (var m in validMuscles)
                if (m == muscleName) return true;

            // Find closest candidates (simple substring match, case-insensitive)
            var lower = muscleName.ToLowerInvariant();
            var candidates = validMuscles
                .Where(m => m.ToLowerInvariant().Contains(lower) ||
                            lower.Contains(m.ToLowerInvariant().Split(' ')[0]))
                .Take(5)
                .ToArray();

            string hint = candidates.Length > 0
                ? "Closest candidates: " + string.Join(", ", candidates.Select(c => $"\"{c}\""))
                : "No close candidates found. Check HumanTrait.MuscleName for full list.";

            Debug.LogError($"[PoseClipGenerator] Unknown muscle name \"{muscleName}\". {hint}");
            return false;
        }

        /// <summary>
        /// Loads a generated pose clip from AnimDir. Logs an error if missing.
        /// </summary>
        private static AnimationClip LoadPoseClip(string poseName)
        {
            string path = $"{AnimDir}/Pose_{poseName}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
                Debug.LogError($"[PoseClipGenerator] Pose clip not found at {path}. Run 'Snowdrift/Generate Pose Clips' first.");
            return clip;
        }

        /// <summary>
        /// Searches all FBX files under Assets/Art/Animations for AnimationClip sub-assets
        /// whose name contains any of the given substrings (case-insensitive).
        /// Returns the first match, or null with a warning if none found.
        /// </summary>
        private static AnimationClip FindMixamoClip(params string[] substrings)
        {
            // Find all FBX files in the animations folder
            string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { AnimDir });
            foreach (string guid in guids)
            {
                string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!fbxPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) &&
                    !fbxPath.EndsWith(".FBX", StringComparison.OrdinalIgnoreCase))
                    continue;

                var subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
                foreach (var asset in subAssets)
                {
                    if (!(asset is AnimationClip clip)) continue;
                    string lower = clip.name.ToLowerInvariant();
                    // Require ALL substrings to match (case-insensitive).
                    // e.g. FindMixamoClip("landing","hard") only matches a clip
                    // whose name contains both "landing" AND "hard".
                    bool matched = substrings.All(s => lower.Contains(s.ToLowerInvariant()));
                    if (matched)
                        return clip;
                }
            }
            return null; // caller logs warning
        }

        /// <summary>
        /// Adds an AnyState → target transition on Pose int equals poseValue.
        /// Sets canTransitionToSelf=false (critical — prevents state restart every frame).
        /// </summary>
        private static void AddAnyStateTransition(
            AnimatorStateMachine sm,
            AnimatorState target,
            string intParam,
            int intValue,
            float duration)
        {
            var t = sm.AddAnyStateTransition(target);
            t.canTransitionToSelf = false; // CRITICAL — default true restarts state every frame
            t.hasExitTime         = false;
            t.duration            = duration;
            t.AddCondition(AnimatorConditionMode.Equals, intValue, intParam);
        }

        /// <summary>
        /// Marks an AnimationClip as non-looping (for oneshot states).
        /// Only applies to AnimationClip assets (not BlendTree motions).
        /// </summary>
        private static void SetNotLoop(Motion motion)
        {
            if (motion is AnimationClip clip)
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
            }
        }

        /// <summary>Ensures the given asset-relative directory path exists on disk.</summary>
        private static void EnsureDir(string assetPath)
        {
            string full = Path.Combine(Application.dataPath, "..", assetPath);
            if (!Directory.Exists(full))
            {
                Directory.CreateDirectory(full);
                Debug.Log($"[PoseClipGenerator] Created directory {assetPath}");
            }
        }
    }
}
