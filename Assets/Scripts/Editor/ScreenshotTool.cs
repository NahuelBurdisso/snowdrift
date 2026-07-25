// ScreenshotTool.cs
// Captures 3 batchmode screenshots of the Main.unity scene: rider close-up,
// slope overview (with props), and sky/horizon view.
//
// Menu:   Snowdrift/Capture Screenshots
// Batch:  -executeMethod Snowdrift.Editor.ScreenshotTool.Capture
//         (do NOT pass -nographics -- GPU rendering is required)
//
// Ground/terrain strategy:
//   TerrainStream generates chunks at runtime via MonoBehaviour.Start(), which does
//   not run in edit-mode batchmode rendering.  Instead this tool directly instantiates
//   a SlopeField (pure math, no MonoBehaviour) and calls the same Build() logic that
//   TerrainChunk uses to produce two chunks of real snowfield geometry beneath the
//   player.  Props are Y-positioned to sit on that terrain surface.  On failure (missing
//   material or assembly reference) the tool falls back to a flat ground plane.

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Snowdrift.Core;

namespace Snowdrift.Editor
{
    public static class ScreenshotTool
    {
        private const string ScenePath      = "Assets/Scenes/Main.unity";
        private const string OutputDir      = "Screenshots";   // project root relative
        private const int    Width          = 1920;
        private const int    Height         = 1080;

        // TerrainChunk constants mirrored here to avoid a runtime-assembly dependency.
        // Must stay in sync with TerrainChunk.cs.
        private const float ChunkLength = 60f;
        private const float ChunkWidth  = 80f;
        private const float ChunkRes    = 2f;

        // SlopeField seed + parameters must match TerrainStream.Awake() exactly.
        // TerrainStream: new SlopeField(seed:42, 15f, 4f, 0.015f, 0.6f, 0.08f)
        private const int   SlopeSeed     = 42;
        private const float SlopeBasePitch = 15f;
        private const float SlopeA1        = 4f;
        private const float SlopeF1        = 0.015f;
        private const float SlopeA2        = 0.6f;
        private const float SlopeF2        = 0.08f;

        // Prop prefab paths for temporary scene population.
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
        // Entry point
        // ─────────────────────────────────────────────────────────────────────
        [MenuItem("Snowdrift/Capture Screenshots")]
        public static void Capture()
        {
            Debug.Log("[ScreenshotTool] Capture() start.");

            // ── 1. Open Main.unity (read-only intent -- we will NOT SaveScene) ──
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
            // Player is placed at (0, 2, 0) by ProjectSetup.  If missing, default to origin.
            Vector3 playerPos = playerGo != null ? playerGo.transform.position : new Vector3(0f, 2f, 0f);
            Debug.Log($"[ScreenshotTool] Player position: {playerPos}");

            // ── 3. Locate scene camera for settings reference ──
            Camera sceneCamera = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                sceneCamera = root.GetComponentInChildren<Camera>(true);
                if (sceneCamera != null) break;
            }

            // ── 4. Build terrain geometry for this capture session ──
            // SlopeField is pure-math (no MonoBehaviour), safe to instantiate in editor.
            // We build 3 chunks covering slopeZ = 0..180 m, matching the corridor the player
            // starts in (slopeZ = 0 when _ozOff = 0, world Z = 0).
            var tempObjects = new System.Collections.Generic.List<GameObject>();
            var field = new SlopeField(SlopeSeed, SlopeBasePitch, SlopeA1, SlopeF1, SlopeA2, SlopeF2);
            var snowMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Snow.mat");

            bool terrainBuilt = false;
            if (snowMat != null)
            {
                terrainBuilt = BuildTerrainChunks(field, snowMat, tempObjects);
            }
            else
            {
                Debug.LogWarning("[ScreenshotTool] Assets/Materials/Snow.mat not found. Run 'Snowdrift/Build Scene' first.");
            }

            if (!terrainBuilt)
            {
                // Fallback: flat ground plane positioned at the slope's Y at Z=30 (midpoint of first chunk).
                SpawnFallbackGround(playerPos, snowMat, tempObjects);
            }

            // ── 5. Spawn temporary props sitting ON the terrain ──
            SpawnTempProps(playerPos, field, tempObjects);

            // ── 6. Reposition player Y onto the terrain surface ──
            // Player is at (0, 2, 0) from scene build; SlopeField Y at x=0, sz=0 gives the
            // actual terrain surface.  Place player 1m above it.
            if (playerGo != null)
            {
                float groundY = field.Height(0f, 0f);   // _ozOff=0, _oyOff=0 at start
                playerGo.transform.position = new Vector3(playerPos.x, groundY + 1f, playerPos.z);
                playerPos = playerGo.transform.position;
                Debug.Log($"[ScreenshotTool] Repositioned player to {playerPos} (groundY={groundY:F2})");
            }

            // ── 7. Ensure output directory ──
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputPath  = Path.Combine(projectRoot, OutputDir);
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            // ── 8. Create temporary camera ──
            var camGo = new GameObject("_ScreenshotCamera");
            var cam   = camGo.AddComponent<Camera>();

            if (sceneCamera != null)
            {
                cam.clearFlags    = sceneCamera.clearFlags;
                cam.backgroundColor = sceneCamera.backgroundColor;
                cam.farClipPlane  = sceneCamera.farClipPlane;
                cam.nearClipPlane = sceneCamera.nearClipPlane;
                cam.fieldOfView   = sceneCamera.fieldOfView;
                Debug.Log("[ScreenshotTool] Copied scene camera settings (clearFlags=" + cam.clearFlags + ", far=" + cam.farClipPlane + ").");
            }
            else
            {
                cam.clearFlags    = CameraClearFlags.Skybox;
                cam.farClipPlane  = 600f;
                cam.nearClipPlane = 0.3f;
                cam.fieldOfView   = 60f;
                Debug.Log("[ScreenshotTool] No scene camera found -- using skybox defaults.");
            }

            // URP requires UniversalAdditionalCameraData on any camera it renders.
            if (cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null)
                camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

            // ── 9. Warmup render ──
            // The first Camera.Render() in a batchmode/edit-mode session executes before
            // Unity has fully committed ambient SH probes, skybox state, and fog into the
            // GPU pipeline.  This causes the first render to use stale/default lighting
            // (often a flat orange ambient probe from a previous bake) regardless of the
            // current RenderSettings.  A dedicated warmup render into a throwaway RT
            // flushes that state so all subsequent shots receive correct lighting.
            WarmupRender(cam, playerPos);

            // ── 10. Capture shots ──
            bool allOk = true;

            // Rider close-up: behind player, looking forward (+Z) at slope level.
            allOk &= CaptureShot(cam, playerPos, "rider_closeup",
                offset:     new Vector3(0f, 1.6f, -3.5f),
                lookTarget: playerPos + new Vector3(0f, 1.2f, 0f),
                outputPath);

            // Slope overview: high above-behind, looking down-forward to show terrain context.
            allOk &= CaptureShot(cam, playerPos, "slope_view",
                offset:     new Vector3(0f, 8f, -14f),
                lookTarget: playerPos + new Vector3(0f, 0f, 12f),
                outputPath);

            // Sky/horizon: low camera looking toward horizon to show skybox gradient.
            allOk &= CaptureShot(cam, playerPos, "sky_view",
                offset:     new Vector3(0f, 2f, 0f),
                lookTarget: playerPos + new Vector3(0f, 2f + Mathf.Tan(20f * Mathf.Deg2Rad) * 50f, 50f),
                outputPath);

            // ── 11. Cleanup ──
            Object.DestroyImmediate(camGo);
            // Restore player position to avoid dirtying the scene.
            if (playerGo != null)
                playerGo.transform.position = new Vector3(playerPos.x, 2f, playerPos.z);
            foreach (var go in tempObjects)
                if (go != null) Object.DestroyImmediate(go);

            // Do NOT call EditorSceneManager.SaveScene -- scene must remain unmodified.

            if (allOk)
                Debug.Log("[ScreenshotTool] All screenshots written to: " + outputPath);
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
        // Warmup render: render a throwaway frame so the URP pipeline
        // initialises ambient SH, fog, and skybox state before the real captures.
        // Without this the first Camera.Render() in batchmode reuses stale GPU
        // state and ignores the current RenderSettings (causing the flat-orange
        // ambient seen in rider_closeup.png).
        // ─────────────────────────────────────────────────────────────────────
        static void WarmupRender(Camera cam, Vector3 referencePos)
        {
            cam.transform.position = referencePos + new Vector3(0f, 5f, -10f);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            var rt = new RenderTexture(256, 144, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            cam.targetTexture = rt;
            cam.Render();   // discard result -- this is a pipeline flush only
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);

            Debug.Log("[ScreenshotTool] Warmup render complete -- pipeline state initialised.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Build real terrain chunks using the same SlopeField + mesh generation
        // that TerrainChunk.Build() uses at runtime.  Produces 3 chunks covering
        // slopeZ 0..180m, centered on the player spawn corridor.
        //
        // originZOffset and originYOffset are both 0 (player just spawned, no shift).
        // Returns true if at least one chunk was successfully created.
        // ─────────────────────────────────────────────────────────────────────
        static bool BuildTerrainChunks(SlopeField field, Material snowMat,
                                       System.Collections.Generic.List<GameObject> tempObjects)
        {
            bool any = false;
            for (int ci = 0; ci <= 2; ci++)
            {
                float slopeZ = ci * ChunkLength;
                var go = BuildOneChunk(field, slopeZ, originZOffset: 0f, originYOffset: 0f, snowMat);
                if (go != null)
                {
                    tempObjects.Add(go);
                    any = true;
                }
            }
            if (any)
                Debug.Log("[ScreenshotTool] Built 3 terrain chunks from SlopeField (slopeZ 0..180m).");
            return any;
        }

        // Replicates TerrainChunk.Build() flat-shading logic without requiring a
        // TerrainChunk MonoBehaviour (which would call Start/Awake in edit mode).
        static GameObject BuildOneChunk(SlopeField field, float slopeZ,
                                        float originZOffset, float originYOffset, Material mat)
        {
            int nx = Mathf.RoundToInt(ChunkWidth  / ChunkRes);
            int nz = Mathf.RoundToInt(ChunkLength / ChunkRes);

            // Build shared vertex grid (slope-space coords -> world)
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            for (int iz = 0, i = 0; iz <= nz; iz++)
            for (int ix = 0; ix <= nx; ix++, i++)
            {
                float x  = ix * ChunkRes - ChunkWidth / 2f;
                float sz = slopeZ + iz * ChunkRes;
                // World Y: field.Height(x, sz) - originYOffset
                // World Z: sz - originZOffset
                verts[i] = new Vector3(x, field.Height(x, sz) - originYOffset, sz - originZOffset);
            }

            // Indexed tris (two per quad)
            var tris = new int[nx * nz * 6];
            for (int iz = 0, t = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                int a = iz * (nx + 1) + ix, b = a + 1, c = a + nx + 1, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }

            // Duplicate verts for flat shading (same as TerrainChunk)
            var flatVerts = new Vector3[tris.Length];
            var flatTris  = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++) { flatVerts[i] = verts[tris[i]]; flatTris[i] = i; }

            var mesh = new Mesh();
            mesh.name = $"ScreenshotTerrain_chunk{(int)(slopeZ / ChunkLength)}";
            mesh.vertices  = flatVerts;
            mesh.triangles = flatTris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject($"_TerrainChunk_{(int)(slopeZ / ChunkLength)}");
            go.AddComponent<MeshFilter>().sharedMesh  = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            // Assign Ground layer so the chunk reads correctly in any physics checks.
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0) go.layer = groundLayer;

            Debug.Log($"[ScreenshotTool] Built chunk slopeZ={slopeZ} ({flatVerts.Length} verts)");
            return go;
        }

        // Fallback: one large tilted plane when SlopeField mesh build fails.
        static void SpawnFallbackGround(Vector3 playerPos, Material mat,
                                        System.Collections.Generic.List<GameObject> tempObjects)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "_FallbackGround";
            // Tilt to approximate the 15-degree slope pitch.
            go.transform.rotation = Quaternion.Euler(-15f, 0f, 0f);
            go.transform.localScale = new Vector3(10f, 1f, 20f);
            // Centre it under the player at the slope's approximate Y.
            go.transform.position = new Vector3(0f, playerPos.y - 1f, playerPos.z + 50f);
            if (mat != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            tempObjects.Add(go);
            Debug.LogWarning("[ScreenshotTool] Using fallback flat ground plane -- terrain SlopeField build failed.");
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

            cam.transform.position = playerPos + offset;

            Vector3 dir = (lookTarget - cam.transform.position).normalized;
            if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
            cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            Debug.Log($"[ScreenshotTool] Rendering '{filename}' from {cam.transform.position} -> {lookTarget}");

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            rt.Create();

            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            byte[] png = tex.EncodeToPNG();
            File.WriteAllBytes(filePath, png);

            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);

            long size = new FileInfo(filePath).Length;
            Debug.Log($"[ScreenshotTool] Written: {filePath}  ({size / 1024} KB)");

            if (size < 10 * 1024)
            {
                Debug.LogError($"[ScreenshotTool] File suspiciously small ({size} bytes) -- likely a black/empty render.");
                return false;
            }
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Spawn props with Y grounded to the SlopeField surface.
        // slopeZ for each prop equals its world Z (since originZOffset == 0 at player spawn).
        // field.Height(x, sz) gives world Y before originYOffset (which is also 0 here).
        // ─────────────────────────────────────────────────────────────────────
        static void SpawnTempProps(Vector3 playerPos,
                                   SlopeField field,
                                   System.Collections.Generic.List<GameObject> tempObjects)
        {
            // (lateralOffset, forwardOffset) pairs relative to player world position.
            // Forward = +Z in world space (slope direction), lateral = X.
            var treePlacement = new (float dx, float dz)[]
            {
                (-6f,  8f),
                ( 7f, 14f),
                (-9f, 22f),
            };
            var rockPlacement = new (float dx, float dz)[]
            {
                ( 4f,  6f),
                (-3f, 18f),
            };

            int treeIdx = 0;
            foreach (var (dx, dz) in treePlacement)
            {
                float worldX  = playerPos.x + dx;
                float worldZ  = playerPos.z + dz;
                // slopeZ == worldZ when originZOffset == 0
                float groundY = field.Height(worldX, worldZ);
                var pos = new Vector3(worldX, groundY, worldZ);

                string prefabPath = TreePrefabPaths[treeIdx % TreePrefabPaths.Length];
                treeIdx++;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.position = pos;
                    tempObjects.Add(go);
                    Debug.Log($"[ScreenshotTool] Spawned tree at {pos} (groundY={groundY:F2})");
                }
                else
                {
                    // Primitive fallback tree -- capsule
                    var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.name = "_tree_fallback";
                    go.transform.localScale = new Vector3(1.2f, 4f, 1.2f);
                    go.transform.position = pos + Vector3.up * 2f;
                    var col = go.GetComponent<Collider>();
                    if (col != null) Object.DestroyImmediate(col);
                    tempObjects.Add(go);
                    Debug.LogWarning("[ScreenshotTool] Tree prefab not found: " + prefabPath + " -- spawning capsule primitive.");
                }
            }

            int rockIdx = 0;
            foreach (var (dx, dz) in rockPlacement)
            {
                float worldX  = playerPos.x + dx;
                float worldZ  = playerPos.z + dz;
                float groundY = field.Height(worldX, worldZ);
                var pos = new Vector3(worldX, groundY, worldZ);

                string prefabPath = RockPrefabPaths[rockIdx % RockPrefabPaths.Length];
                rockIdx++;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab != null)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.position = pos;
                    tempObjects.Add(go);
                    Debug.Log($"[ScreenshotTool] Spawned rock at {pos} (groundY={groundY:F2})");
                }
                else
                {
                    // Primitive fallback rock -- sphere
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = "_rock_fallback";
                    go.transform.localScale = new Vector3(2.5f, 1.8f, 2.5f);
                    go.transform.position = pos + Vector3.up * 0.5f;
                    var col = go.GetComponent<Collider>();
                    if (col != null) Object.DestroyImmediate(col);
                    tempObjects.Add(go);
                    Debug.LogWarning("[ScreenshotTool] Rock prefab not found: " + prefabPath + " -- spawning sphere primitive.");
                }
            }
        }
    }
}
