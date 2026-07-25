// ScreenshotTool.cs
// Captures 3 batchmode screenshots of the Main.unity scene: rider close-up,
// slope overview (with props), and sky/horizon view.
//
// Menu:   Snowdrift/Capture Screenshots
// Batch:  -executeMethod Snowdrift.Editor.ScreenshotTool.Capture
//         (do NOT pass -nographics — GPU rendering is required)

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Snowdrift.Editor
{
    public static class ScreenshotTool
    {
        private const string ScenePath      = "Assets/Scenes/Main.unity";
        private const string OutputDir      = "Screenshots";   // project root relative
        private const int    Width          = 1920;
        private const int    Height         = 1080;

        // Prop prefab paths for temporary scene population when terrain is not generated.
        private static readonly string[] TreePrefabPaths = {
            "Assets/Prefabs/Props/tree-snow-a_Obstacle.prefab",
            "Assets/Prefabs/Props/tree-snow-b_Obstacle.prefab",
            "Assets/Prefabs/Props/tree-snow-c_Obstacle.prefab",
        };
        private static readonly string[] RockPrefabPaths = {
            "Assets/Prefabs/Props/rock_largeA_Obstacle.prefab",
            "Assets/Prefabs/Props/rock_largeB_Obstacle.prefab",
        };

        // ─────────────────────────────────────────────────────────────────────
        // Entry points
        // ─────────────────────────────────────────────────────────────────────
        [MenuItem("Snowdrift/Capture Screenshots")]
        public static void Capture()
        {
            Debug.Log("[ScreenshotTool] Capture() start.");

            // ── 1. Open Main.unity (read-only intent — we will NOT SaveScene) ──
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError("[ScreenshotTool] Cannot open scene at " + ScenePath);
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("[ScreenshotTool] Scene opened: " + scene.name);

            // ── 2. Locate player ──
            GameObject playerGo = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Player") { playerGo = root; break; }
            }
            Vector3 playerPos = playerGo != null ? playerGo.transform.position : Vector3.zero;
            Debug.Log($"[ScreenshotTool] Player position: {playerPos}");

            // ── 3. Locate scene camera for settings reference ──
            Camera sceneCamera = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                sceneCamera = root.GetComponentInChildren<Camera>(true);
                if (sceneCamera != null) break;
            }

            // ── 4. Spawn temporary props (terrain is not generated in editor) ──
            var tempObjects = new System.Collections.Generic.List<GameObject>();
            SpawnTempProps(playerPos, tempObjects);

            // ── 5. Ensure output directory ──
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputPath  = Path.Combine(projectRoot, OutputDir);
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            // ── 6. Create temporary camera ──
            var camGo = new GameObject("_ScreenshotCamera");
            var cam   = camGo.AddComponent<Camera>();

            // Copy settings from scene camera if available; otherwise set sensible defaults.
            if (sceneCamera != null)
            {
                cam.clearFlags   = sceneCamera.clearFlags;
                cam.backgroundColor = sceneCamera.backgroundColor;
                cam.farClipPlane = sceneCamera.farClipPlane;
                cam.nearClipPlane = sceneCamera.nearClipPlane;
                cam.fieldOfView  = sceneCamera.fieldOfView;
                Debug.Log("[ScreenshotTool] Copied scene camera settings (clearFlags=" + cam.clearFlags + ", far=" + cam.farClipPlane + ").");
            }
            else
            {
                cam.clearFlags   = CameraClearFlags.Skybox;
                cam.farClipPlane = 600f;
                cam.nearClipPlane = 0.3f;
                cam.fieldOfView  = 60f;
                Debug.Log("[ScreenshotTool] No scene camera found — using skybox defaults.");
            }

            // URP requires the camera to have a UniversalAdditionalCameraData component
            // to render correctly. Add it if missing.
            if (cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null)
                camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

            // ── 7. Capture shots ──
            bool allOk = true;
            allOk &= CaptureShot(cam, playerPos, "rider_closeup",
                offset: new Vector3(0f, 1.6f, -3.5f),
                lookTarget: playerPos + new Vector3(0f, 1.2f, 0f),
                outputPath);

            allOk &= CaptureShot(cam, playerPos, "slope_view",
                offset: new Vector3(0f, 8f, -14f),
                lookTarget: playerPos + new Vector3(0f, 0f, 12f),
                outputPath);

            allOk &= CaptureShot(cam, playerPos, "sky_view",
                offset: new Vector3(0f, 2f, 0f),
                lookTarget: playerPos + new Vector3(0f, 2f + Mathf.Tan(20f * Mathf.Deg2Rad) * 50f, 50f),
                outputPath);

            // ── 8. Cleanup ──
            Object.DestroyImmediate(camGo);
            foreach (var go in tempObjects)
                if (go != null) Object.DestroyImmediate(go);

            // Do NOT call EditorSceneManager.SaveScene — scene must remain unmodified.

            if (allOk)
            {
                Debug.Log("[ScreenshotTool] All screenshots written to: " + outputPath);
            }
            else
            {
                Debug.LogError("[ScreenshotTool] One or more screenshots failed.");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[ScreenshotTool] Capture() complete.");
            EditorApplication.Exit(0);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Render a single shot.
        // cameraPos = playerPos + offset (world space)
        // lookTarget = world-space point the camera looks at
        // ─────────────────────────────────────────────────────────────────────
        static bool CaptureShot(Camera cam, Vector3 playerPos, string filename,
                                 Vector3 offset, Vector3 lookTarget, string outputDir)
        {
            string filePath = Path.Combine(outputDir, filename + ".png");

            // Position the temp camera
            cam.transform.position = playerPos + offset;

            // Compute look direction — fall back to -Z if degenerate
            Vector3 dir = (lookTarget - cam.transform.position).normalized;
            if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
            cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            Debug.Log($"[ScreenshotTool] Rendering '{filename}' from {cam.transform.position} → {lookTarget}");

            // Allocate RenderTexture
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            rt.Create();

            cam.targetTexture = rt;

            // Force render — explicit call required in batchmode (no game loop tick)
            cam.Render();

            // Read pixels
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            // Encode & write
            byte[] png = tex.EncodeToPNG();
            File.WriteAllBytes(filePath, png);

            // Cleanup per-shot allocations
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);

            long size = new FileInfo(filePath).Length;
            Debug.Log($"[ScreenshotTool] Written: {filePath}  ({size / 1024} KB)");

            if (size < 10 * 1024)
            {
                Debug.LogError($"[ScreenshotTool] File suspiciously small ({size} bytes) — likely a black/empty render.");
                return false;
            }
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Spawn 2 trees + 2 rocks near the player so they appear in slope_view.
        // All instances are tracked in tempObjects; caller destroys them after capture.
        // ─────────────────────────────────────────────────────────────────────
        static void SpawnTempProps(Vector3 playerPos, System.Collections.Generic.List<GameObject> tempObjects)
        {
            // Tree positions: flanking the player to both sides, slightly ahead
            var treePositions = new Vector3[]
            {
                playerPos + new Vector3(-6f, 0f,  8f),
                playerPos + new Vector3( 7f, 0f, 14f),
                playerPos + new Vector3(-9f, 0f, 22f),
            };
            // Rock positions: scattered ahead
            var rockPositions = new Vector3[]
            {
                playerPos + new Vector3( 4f, 0f,  6f),
                playerPos + new Vector3(-3f, 0f, 18f),
            };

            int treeIdx = 0;
            foreach (var pos in treePositions)
            {
                string prefabPath = TreePrefabPaths[treeIdx % TreePrefabPaths.Length];
                treeIdx++;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning("[ScreenshotTool] Tree prefab not found: " + prefabPath);
                    continue;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.position = pos;
                tempObjects.Add(go);
                Debug.Log("[ScreenshotTool] Spawned temp tree at " + pos);
            }

            int rockIdx = 0;
            foreach (var pos in rockPositions)
            {
                string prefabPath = RockPrefabPaths[rockIdx % RockPrefabPaths.Length];
                rockIdx++;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning("[ScreenshotTool] Rock prefab not found: " + prefabPath);
                    continue;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.position = pos;
                tempObjects.Add(go);
                Debug.Log("[ScreenshotTool] Spawned temp rock at " + pos);
            }
        }
    }
}
