// EnvironmentSetup.cs
// Task 8: Copies Kenney prop FBX into Assets/Art/Props, creates URP-material prefabs for
// tree and rock obstacles, adds a border-tree (decoration-only) prefab, then wires the
// prefab arrays into the scene's TerrainStream.
//
// Menu:   Snowdrift/Build Environment
// Batch:  -executeMethod Snowdrift.Editor.EnvironmentSetup.BuildAll
//
// Idempotency: safe to re-run. Prefabs are SaveAsPrefabAsset (overwrite). Materials are
// only created if absent.  FBX copy skipped if already present.

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Snowdrift.Game;

namespace Snowdrift.Editor
{
    public static class EnvironmentSetup
    {
        // ─────────────────────────────────────────────────────────────────────
        // Paths
        // ─────────────────────────────────────────────────────────────────────
        private const string PropsArtDir    = "Assets/Art/Props";
        private const string PropsPrefabDir = "Assets/Prefabs/Props";
        private const string ScenePath     = "Assets/Scenes/Main.unity";

        // Incoming source paths (absolute)
        private const string InHoliday = "D:/proyects/snowdrift/_incoming/kenney_holiday-kit/Models/FBX format/";
        private const string InNature  = "D:/proyects/snowdrift/_incoming/kenney_nature-kit/Models/FBX format/";
        private const string InHolidayTex = "D:/proyects/snowdrift/_incoming/kenney_holiday-kit/Models/FBX format/Textures/colormap.png";

        // Tree FBX names (holiday kit)
        private static readonly string[] TreeFbxNames = { "tree-snow-a.fbx", "tree-snow-b.fbx", "tree-snow-c.fbx" };
        // Rock FBX names (nature kit)
        private static readonly string[] RockFbxNames = { "rock_largeA.fbx", "rock_largeB.fbx", "rock_largeC.fbx" };

        // Base obstacle dimensions from TerrainStream (kind==0=rock, kind==1=tree)
        //   Rock:  Sphere primitive, scale (2.5, 1.8, 2.5), pos +0.5 Y → effective height ~1.8m
        //   Tree:  Capsule primitive, scale (1.2, 4.0, 1.2), pos +2 Y  → effective height ~8m (capsule * 2 = 8m)
        // CapsuleCollider (primitive Capsule default): height=2, radius=0.5, then scaled
        //   Tree collider in world: height = scale.y * 2 = 4*2 = 8m, radius = scale.x * 0.5 = 0.6m
        //   Rock collider in world: SphereCollider(r=0.5) scaled → radius = 0.5 * 1.8 = 0.9m ... but
        //   actually primitives get colliders matching the mesh unit, scale is applied:
        //     Sphere: SphereCollider radius=0.5, world-radius ≈ scale.x/2 = 1.25m
        //     Capsule: CapsuleCollider height=2 radius=0.5, world-height = scale.y*2 = 8m, world-radius = scale.x*0.5 = 0.6m
        // We replicate the SAME collider shape+dimensions on the root of the prefab.
        private const float TreeColliderHeight = 8f;  // scale.y(4) * 2(capsule default)
        private const float TreeColliderRadius = 0.6f; // scale.x(1.2) * 0.5
        private const float RockColliderRadius = 1.25f; // scale.x(2.5) * 0.5

        // Target visual heights for scale-to-fit computation
        private const float TreeTargetHeight = 8f;  // match tree obstacle world height
        private const float RockTargetHeight = 1.8f; // match rock obstacle world height (scale.y)

        // Near-miss trigger radii — must match the WORLD radius produced by the primitive fallback path.
        // Primitives add SphereCollider(r=2.5) to the SCALED GO; Unity scales a SphereCollider by
        // max(scale.x, scale.y, scale.z).  Prefab roots are unscaled, so we bake the world value directly.
        //   Tree  primitive: scale (1.2, 4.0, 1.2) → max=4 → world r = 2.5 × 4  = 10 m
        //   Rock  primitive: scale (2.5, 1.8, 2.5) → max=2.5 → world r = 2.5 × 2.5 = 6.25 m
        private const float TreeNearMissTriggerRadius = 10f;
        private const float RockNearMissTriggerRadius = 6.25f;

        // ─────────────────────────────────────────────────────────────────────
        // Menu entry
        // ─────────────────────────────────────────────────────────────────────
        [MenuItem("Snowdrift/Build Environment")]
        public static void BuildAll()
        {
            Debug.Log("[EnvironmentSetup] BuildAll() start.");

            Step1_EnsureAssets();
            Step2_CreateMaterials();
            Step3_CreatePrefabs(out var treePrefabs, out var rockPrefabs, out var borderTreePrefab);
            WireScene(treePrefabs, rockPrefabs, borderTreePrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[EnvironmentSetup] BuildAll() complete.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step 1: Copy FBX + texture into Assets/Art/Props if not already there
        // ─────────────────────────────────────────────────────────────────────
        static void Step1_EnsureAssets()
        {
            EnsureDir(PropsArtDir);
            EnsureDir(PropsArtDir + "/Textures");

            // Holiday colormap (used by tree FBX)
            CopyIfMissing(InHolidayTex, PropsArtDir + "/Textures/colormap_holiday.png");

            foreach (var name in TreeFbxNames)
                CopyIfMissing(InHoliday + name, PropsArtDir + "/" + name);

            foreach (var name in RockFbxNames)
                CopyIfMissing(InNature + name, PropsArtDir + "/" + name);

            AssetDatabase.Refresh();

            // Convert any auto-created Standard materials on imported FBX to URP Lit
            ConvertFbxMaterialsToUrp(PropsArtDir);

            Debug.Log("[EnvironmentSetup] Step 1 done: FBX assets in " + PropsArtDir);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step 2: Create shared URP Lit materials for trees and rocks
        // ─────────────────────────────────────────────────────────────────────
        static void Step2_CreateMaterials()
        {
            EnsureDir("Assets/Materials");
            // Pine/tree: snowy white-green tint
            CreateUrpMat("Assets/Materials/PropPine.mat",  new Color(0.85f, 0.93f, 0.88f), 0.1f);
            // Rock: grey-brown
            CreateUrpMat("Assets/Materials/PropRock.mat",  new Color(0.40f, 0.38f, 0.36f), 0.05f);
            Debug.Log("[EnvironmentSetup] Step 2 done: prop materials ready.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Step 3: Create the 7 prefabs in Assets/Prefabs/Props/
        //   - 3 tree obstacle prefabs (tree-snow-a/b/c)
        //   - 3 rock obstacle prefabs (rock_largeA/B/C)
        //   - 1 border pine prefab (tree-snow-a, no collider/tag/trigger)
        // ─────────────────────────────────────────────────────────────────────
        static void Step3_CreatePrefabs(
            out GameObject[] treePrefabs,
            out GameObject[] rockPrefabs,
            out GameObject borderTreePrefab)
        {
            EnsureDir(PropsPrefabDir);

            var pineMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PropPine.mat");
            var rockMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PropRock.mat");

            treePrefabs = new GameObject[TreeFbxNames.Length];
            for (int i = 0; i < TreeFbxNames.Length; i++)
            {
                string fbxPath    = PropsArtDir + "/" + TreeFbxNames[i];
                string prefabName = TreeFbxNames[i].Replace(".fbx", "") + "_Obstacle.prefab";
                string prefabPath = PropsPrefabDir + "/" + prefabName;
                treePrefabs[i]    = BuildObstaclePrefab(fbxPath, prefabPath, isTree: true, pineMat);
            }

            rockPrefabs = new GameObject[RockFbxNames.Length];
            for (int i = 0; i < RockFbxNames.Length; i++)
            {
                string fbxPath    = PropsArtDir + "/" + RockFbxNames[i];
                string prefabName = RockFbxNames[i].Replace(".fbx", "") + "_Obstacle.prefab";
                string prefabPath = PropsPrefabDir + "/" + prefabName;
                rockPrefabs[i]    = BuildObstaclePrefab(fbxPath, prefabPath, isTree: false, rockMat);
            }

            // Border pine: tree-snow-a, decoration only
            borderTreePrefab = BuildBorderPinePrefab(
                PropsArtDir + "/tree-snow-a.fbx",
                PropsPrefabDir + "/BorderPine.prefab",
                pineMat);

            Debug.Log("[EnvironmentSetup] Step 3 done: 7 prefabs created in " + PropsPrefabDir);
        }

        // Build one obstacle prefab (tree or rock).
        // Root GO: has collider + tag "Obstacle" + SphereCollider trigger + NearMissZone.
        // Visual child: the FBX mesh, scaled to fit collider footprint, URP material applied.
        static GameObject BuildObstaclePrefab(string fbxPath, string prefabPath, bool isTree, Material mat)
        {
            var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxAsset == null)
            {
                Debug.LogError($"[EnvironmentSetup] FBX not found at {fbxPath} — skipping prefab {prefabPath}");
                return null;
            }

            // ── Root ──────────────────────────────────────────────────────────
            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            root.tag = "Obstacle";

            if (isTree)
            {
                // Capsule collider matching the primitive tree obstacle world span.
                // Primitive: Capsule GO scale (1.2, 4, 1.2), CapsuleCollider default h=2 r=0.5,
                //   spawned at pos + 2Y (this becomes the capsule center in world space).
                //   World h = 4*2 = 8m, world r = 1.2*0.5 = 0.6m.
                //   Capsule half-height = 4m → world span = (pos.y+2 - 4)..(pos.y+2 + 4)
                //                                         = pos.y - 2 .. pos.y + 6.
                // Prefab root is spawned at pos (no Y offset — TerrainStream tree-prefab branch uses +0Y).
                //   To reproduce the same span, center.y = 2 → half-height 4 →
                //   span (pos.y + 2 - 4)..(pos.y + 2 + 4) = pos.y - 2 .. pos.y + 6.  ✓
                var cc = root.AddComponent<CapsuleCollider>();
                cc.height = TreeColliderHeight;
                cc.radius = TreeColliderRadius;
                cc.center = new Vector3(0f, 2f, 0f);
            }
            else
            {
                // Sphere collider matching the primitive rock obstacle world span.
                // Primitive: Sphere GO scale (2.5, 1.8, 2.5), SphereCollider default r=0.5,
                //   spawned at pos + 0.5Y (this becomes sphere center in world).
                //   World r = 2.5*0.5 = 1.25m → span = (pos.y+0.5 - 1.25)..(pos.y+0.5 + 1.25)
                //                                     = pos.y - 0.75 .. pos.y + 1.75.
                // Prefab root is spawned at pos (no Y offset — TerrainStream rock-prefab branch
                //   removes the +0.5Y; see SpawnFeature kind==0).
                //   To reproduce the same span, center.y = 0.5 → r=1.25 →
                //   span (pos.y + 0.5 - 1.25)..(pos.y + 0.5 + 1.25) = pos.y - 0.75 .. pos.y + 1.75.  ✓
                var sc = root.AddComponent<SphereCollider>();
                sc.radius = RockColliderRadius;
                sc.center = new Vector3(0f, 0.5f, 0f);
            }

            // Near-miss trigger shell — world radii match the primitive fallback path.
            // Primitive adds SphereCollider(r=2.5) to the scaled GO; Unity scales by max(scale axis).
            //   Tree:  max(1.2,4,1.2)=4  → world r = 2.5×4   = 10 m
            //   Rock:  max(2.5,1.8,2.5)=2.5 → world r = 2.5×2.5 = 6.25 m
            // Prefab roots are unscaled, so we store the baked world value directly.
            var trig = root.AddComponent<SphereCollider>();
            trig.isTrigger = true;
            trig.radius    = isTree ? TreeNearMissTriggerRadius : RockNearMissTriggerRadius;
            if (isTree)
                trig.center = new Vector3(0f, 2f, 0f);
            else
                trig.center = new Vector3(0f, 0.5f, 0f);

            root.AddComponent<NearMissZone>();

            // ── Visual child ──────────────────────────────────────────────────
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            visual.name = "Mesh";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            // Scale visual to match obstacle height.
            // Compute mesh bounds first (at localScale 1,1,1).
            float meshHeight = GetInstantiatedBoundsHeight(visual);
            if (meshHeight > 0.001f)
            {
                float targetHeight = isTree ? TreeTargetHeight : RockTargetHeight;
                float scaleFactor  = targetHeight / meshHeight;
                visual.transform.localScale = Vector3.one * scaleFactor;
                Debug.Log($"[EnvironmentSetup] {Path.GetFileNameWithoutExtension(fbxPath)}: mesh height={meshHeight:F3}m → scale={scaleFactor:F4} (target={targetHeight}m)");
            }
            else
            {
                Debug.LogWarning($"[EnvironmentSetup] {fbxPath}: could not measure mesh bounds height. Using scale 1.");
            }

            // Apply URP material to all renderers on visual child
            ApplyMaterialToRenderers(visual, mat);

            // Remove all colliders from the visual subtree (physics belongs only on root)
            RemoveCollidersFromSubtree(visual);

            // ── Save prefab ───────────────────────────────────────────────────
            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);   // destroys root + all children (including visual) — do not destroy visual again

            Debug.Log($"[EnvironmentSetup] Saved prefab: {prefabPath}");
            return saved;
        }

        // Build the border pine prefab: mesh only, no collider, no tag, no trigger.
        static GameObject BuildBorderPinePrefab(string fbxPath, string prefabPath, Material mat)
        {
            var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxAsset == null)
            {
                Debug.LogError($"[EnvironmentSetup] FBX not found at {fbxPath} — skipping border pine prefab.");
                return null;
            }

            var root   = new GameObject("BorderPine");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            visual.name = "Mesh";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            // Scale to tree height (same as obstacle trees so they visually match corridor trees)
            float meshHeight = GetInstantiatedBoundsHeight(visual);
            if (meshHeight > 0.001f)
            {
                float scaleFactor = TreeTargetHeight / meshHeight;
                visual.transform.localScale = Vector3.one * scaleFactor;
            }

            ApplyMaterialToRenderers(visual, mat);
            RemoveCollidersFromSubtree(visual);

            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);   // destroys root + all children (including visual) — do not destroy visual again

            Debug.Log($"[EnvironmentSetup] Saved border pine prefab: {prefabPath}");
            return saved;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Wire prefab arrays into the TerrainStream in Main.unity.
        // Called from BuildAll and from ProjectSetup at end of BuildScene.
        // ─────────────────────────────────────────────────────────────────────
        internal static void WireScene(
            GameObject[] treePrefabs   = null,
            GameObject[] rockPrefabs   = null,
            GameObject   borderPrefab  = null)
        {
            // Load prefabs from disk if not supplied (allows ProjectSetup to call us without running Step3)
            if (treePrefabs == null || treePrefabs.Length == 0)
            {
                treePrefabs = new GameObject[]
                {
                    AssetDatabase.LoadAssetAtPath<GameObject>(PropsPrefabDir + "/tree-snow-a_Obstacle.prefab"),
                    AssetDatabase.LoadAssetAtPath<GameObject>(PropsPrefabDir + "/tree-snow-b_Obstacle.prefab"),
                    AssetDatabase.LoadAssetAtPath<GameObject>(PropsPrefabDir + "/tree-snow-c_Obstacle.prefab"),
                };
            }
            if (rockPrefabs == null || rockPrefabs.Length == 0)
            {
                rockPrefabs = new GameObject[]
                {
                    AssetDatabase.LoadAssetAtPath<GameObject>(PropsPrefabDir + "/rock_largeA_Obstacle.prefab"),
                    AssetDatabase.LoadAssetAtPath<GameObject>(PropsPrefabDir + "/rock_largeB_Obstacle.prefab"),
                    AssetDatabase.LoadAssetAtPath<GameObject>(PropsPrefabDir + "/rock_largeC_Obstacle.prefab"),
                };
            }
            if (borderPrefab == null)
                borderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PropsPrefabDir + "/BorderPine.prefab");

            // Check at least some are available
            bool hasTrees  = treePrefabs  != null && treePrefabs.Length  > 0 && treePrefabs[0]  != null;
            bool hasRocks  = rockPrefabs  != null && rockPrefabs.Length  > 0 && rockPrefabs[0]  != null;
            bool hasBorder = borderPrefab != null;

            if (!hasTrees && !hasRocks && !hasBorder)
            {
                Debug.LogWarning("[EnvironmentSetup] WireScene: no prop prefabs found — skipping scene wiring. Run 'Snowdrift/Build Environment' first.");
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Debug.LogError("[EnvironmentSetup] WireScene: cannot open scene at " + ScenePath);
                return;
            }

            TerrainStream terrainStream = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                terrainStream = root.GetComponentInChildren<TerrainStream>(true);
                if (terrainStream != null) break;
            }

            if (terrainStream == null)
            {
                Debug.LogWarning("[EnvironmentSetup] WireScene: TerrainStream not found in scene. Run 'Snowdrift/Build Scene' first.");
                EditorSceneManager.CloseScene(scene, false);
                return;
            }

            var so = new SerializedObject(terrainStream);

            if (hasTrees)
            {
                var treeProp = so.FindProperty("treePrefabs");
                if (treeProp != null)
                {
                    treeProp.arraySize = treePrefabs.Length;
                    for (int i = 0; i < treePrefabs.Length; i++)
                        treeProp.GetArrayElementAtIndex(i).objectReferenceValue = treePrefabs[i];
                    Debug.Log($"[EnvironmentSetup] WireScene: assigned {treePrefabs.Length} tree prefabs.");
                }
                else
                    Debug.LogWarning("[EnvironmentSetup] WireScene: 'treePrefabs' property not found on TerrainStream.");
            }

            if (hasRocks)
            {
                var rockProp = so.FindProperty("rockPrefabs");
                if (rockProp != null)
                {
                    rockProp.arraySize = rockPrefabs.Length;
                    for (int i = 0; i < rockPrefabs.Length; i++)
                        rockProp.GetArrayElementAtIndex(i).objectReferenceValue = rockPrefabs[i];
                    Debug.Log($"[EnvironmentSetup] WireScene: assigned {rockPrefabs.Length} rock prefabs.");
                }
                else
                    Debug.LogWarning("[EnvironmentSetup] WireScene: 'rockPrefabs' property not found on TerrainStream.");
            }

            if (hasBorder)
            {
                var borderProp = so.FindProperty("borderTreePrefab");
                if (borderProp != null)
                {
                    borderProp.objectReferenceValue = borderPrefab;
                    Debug.Log("[EnvironmentSetup] WireScene: assigned borderTreePrefab.");
                }
                else
                    Debug.LogWarning("[EnvironmentSetup] WireScene: 'borderTreePrefab' property not found on TerrainStream.");
            }

            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[EnvironmentSetup] WireScene: scene saved.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        static float GetInstantiatedBoundsHeight(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return 0f;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b.size.y;
        }

        static void ApplyMaterialToRenderers(GameObject go, Material mat)
        {
            if (mat == null) return;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                // Ensure URP shader (not Standard / magenta) on every sub-material slot
                var mats = mr.sharedMaterials;
                bool allUrp = true;
                foreach (var m in mats)
                    if (m == null || !IsUrpCompatible(m)) { allUrp = false; break; }

                if (!allUrp)
                {
                    // Replace every slot with our prop material
                    var replacement = new Material[mats.Length];
                    for (int i = 0; i < replacement.Length; i++) replacement[i] = mat;
                    mr.sharedMaterials = replacement;
                }
                // else keep existing URP materials (they are already fine)
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                bool allUrp = true;
                foreach (var m in mats)
                    if (m == null || !IsUrpCompatible(m)) { allUrp = false; break; }
                if (!allUrp)
                {
                    var replacement = new Material[mats.Length];
                    for (int i = 0; i < replacement.Length; i++) replacement[i] = mat;
                    smr.sharedMaterials = replacement;
                }
            }
        }

        static bool IsUrpCompatible(Material m)
        {
            if (m == null || m.shader == null) return false;
            var name = m.shader.name;
            return name.StartsWith("Universal Render Pipeline") || name.StartsWith("Shader Graphs/");
        }

        static void RemoveCollidersFromSubtree(GameObject go)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(col);
        }

        static void ConvertFbxMaterialsToUrp(string folder)
        {
            // Find any .mat files Unity auto-extracted under the folder and switch Standard→URP Lit
            var urpShader  = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null) return; // URP not available in this Unity session — skip

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat  = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;
                if (IsUrpCompatible(mat)) continue; // already fine

                // Preserve color + main texture before swapping shader
                var oldColor = mat.HasProperty("_Color") ? mat.color : Color.white;
                Texture oldTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;

                mat.shader = urpShader;
                if (mat.HasProperty("_BaseColor"))  mat.SetColor("_BaseColor",  oldColor);
                if (mat.HasProperty("_BaseMap") && oldTex != null)
                    mat.SetTexture("_BaseMap", oldTex);

                EditorUtility.SetDirty(mat);
                Debug.Log($"[EnvironmentSetup] Converted {path} → URP/Lit");
            }
        }

        static void CreateUrpMat(string path, Color color, float smoothness = 0.1f)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = color;
            mat.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(mat, path);
            Debug.Log($"[EnvironmentSetup] Created material {path}");
        }

        static void CopyIfMissing(string srcAbsolute, string dstAssetPath)
        {
            string dstFull = Path.GetFullPath(Path.Combine(Application.dataPath, "..", dstAssetPath));
            if (File.Exists(dstFull)) return;
            if (!File.Exists(srcAbsolute))
            {
                Debug.LogWarning($"[EnvironmentSetup] Source not found: {srcAbsolute}");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dstFull));
            File.Copy(srcAbsolute, dstFull, overwrite: false);
            Debug.Log($"[EnvironmentSetup] Copied {srcAbsolute} → {dstAssetPath}");
        }

        static void EnsureDir(string assetPath)
        {
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            if (!Directory.Exists(full))
            {
                Directory.CreateDirectory(full);
                Debug.Log("[EnvironmentSetup] Created directory: " + assetPath);
            }
        }
    }
}
