// BoardMeshGenerator.cs
// Procedurally generates a low-poly snowboard mesh and saves it as a reusable asset.
// Menu: Snowdrift/Generate Board Mesh
// Batch: -executeMethod Snowdrift.Editor.BoardMeshGenerator.Generate
//
// Geometry overview:
//   - 9 cross-section slices along Z (board length axis, centred at origin)
//   - Each slice is a closed rectangular cross-section (top, bottom, left side, right side)
//   - Width tapers at nose/tail; nose and tail curve upward
//   - Duplicated verts per face quad for flat/low-poly shading
//   - Top + bottom + left + right faces form a watertight shape

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Snowdrift.Editor
{
    public static class BoardMeshGenerator
    {
        // ── Asset paths ───────────────────────────────────────────────
        private const string MeshPath = "Assets/Art/Board.mesh";
        private const string MatPath  = "Assets/Materials/Snowboard.mat";

        // ── Board dimensions ──────────────────────────────────────────
        private const float BoardLength    = 1.5f;   // total Z extent
        private const float WaistHalfWidth = 0.125f; // half of 0.25 m waist
        private const float NoseHalfWidth  = 0.140f; // half of 0.28 m at widest (nose/tail)
        private const float BoardThickness = 0.03f;  // Y thickness
        private const float NoseLift       = 0.10f;  // max upward curve at tip
        private const float TailLift       = 0.06f;  // max upward curve at tail

        // Number of cross-section slices (including both end caps)
        private const int SliceCount = 9;

        // ── Menu entry point ──────────────────────────────────────────
        [MenuItem("Snowdrift/Generate Board Mesh")]
        public static void GenerateFromMenu()
        {
            Generate();
        }

        // ── Batch / public entry point ─────────────────────────────────
        public static void Generate()
        {
            Debug.Log("[BoardMeshGenerator] Generate() start.");

            EnsureDir("Assets/Art");
            EnsureDir("Assets/Materials");

            // ── Build geometry ──────────────────────────────────────────
            Mesh mesh = BuildMesh();

            // ── Save mesh (idempotent) ──────────────────────────────────
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existingMesh != null)
            {
                // Overwrite geometry on the SAME asset to preserve GUID refs
                existingMesh.Clear();
                CopyMeshData(mesh, existingMesh);
                EditorUtility.SetDirty(existingMesh);
                Debug.Log("[BoardMeshGenerator] Overwrote existing Board.mesh (GUID preserved).");
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, MeshPath);
                Debug.Log("[BoardMeshGenerator] Created Board.mesh at " + MeshPath);
            }

            // ── Save material (idempotent load-or-create) ───────────────
            LoadOrCreateSnowboardMat();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ── Report ──────────────────────────────────────────────────
            var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            int vertCount = savedMesh != null ? savedMesh.vertexCount : mesh.vertexCount;
            int triCount  = savedMesh != null ? savedMesh.triangles.Length / 3 : mesh.triangles.Length / 3;
            Debug.Log($"[BoardMeshGenerator] Done. Mesh: {MeshPath} | verts={vertCount} tris={triCount}");
            Debug.Log($"[BoardMeshGenerator] Material: {MatPath}");
        }

        // ─────────────────────────────────────────────────────────────
        // Mesh construction
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the snowboard Mesh procedurally.
        ///
        /// Coordinate convention:
        ///   Z = length axis (nose = +Z end, tail = -Z end)
        ///   X = width axis
        ///   Y = up (thickness)
        ///
        /// Each slice i has:
        ///   z[i]     = position along length
        ///   hw[i]    = half-width at that slice
        ///   yLift[i] = upward Y offset (rocker)
        ///
        /// We build four face strips (top, bottom, left, right), each as a
        /// sequence of quads between adjacent slices. Each quad uses four
        /// freshly-duplicated vertices so normals are face-flat (low-poly look).
        /// End caps (nose and tail) are filled with a fan of tris.
        /// </summary>
        private static Mesh BuildMesh()
        {
            // ── Slice profile ─────────────────────────────────────────
            float[] zPos   = new float[SliceCount];
            float[] hw     = new float[SliceCount]; // half-width
            float[] yLift  = new float[SliceCount]; // upward rocker
            float   yFloor = 0f;                    // bottom Y before lift (local: 0..thickness)

            for (int i = 0; i < SliceCount; i++)
            {
                float t = (float)i / (SliceCount - 1);           // 0 at tail, 1 at nose
                float zCentered = (t - 0.5f) * BoardLength;      // -0.75 .. +0.75
                zPos[i] = zCentered;

                // Width: use a smooth cosine taper.
                // Max width near tips (t≈0 and t≈1), min at waist (t=0.5).
                // cosine of (t*pi - pi/2) gives 0 at t=0.5 and ±1 at extremes.
                float widthBlend = Mathf.Pow(Mathf.Abs(Mathf.Cos(t * Mathf.PI)), 0.5f);
                hw[i] = Mathf.Lerp(WaistHalfWidth, NoseHalfWidth, widthBlend);

                // Rocker / upward lift at nose and tail.
                // Tail: last 2 slices rise to TailLift (t near 0).
                // Nose: last 2 slices rise to NoseLift (t near 1).
                float tNose = Mathf.Clamp01((t - 0.75f) / 0.25f);   // 0..1 in last 25% (nose)
                float tTail = Mathf.Clamp01((0.25f - t) / 0.25f);   // 0..1 in first 25% (tail)
                float noseRocker = NoseLift * tNose * tNose;          // quadratic ease-in
                float tailRocker = TailLift * tTail * tTail;
                yLift[i] = noseRocker + tailRocker;
            }

            var verts   = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs     = new List<Vector2>();
            var tris    = new List<int>();

            // Helper: add a quad (two tris) from 4 corner positions, computing face normal.
            // Winding: counter-clockwise when viewed from outside.
            //   p0──p1
            //   │  ╲ │
            //   p3──p2
            // Tri 0: p0,p1,p2   Tri 1: p0,p2,p3
            void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                int baseIdx = verts.Count;
                verts.Add(p0); normals.Add(n); uvs.Add(new Vector2(0f, 1f));
                verts.Add(p1); normals.Add(n); uvs.Add(new Vector2(1f, 1f));
                verts.Add(p2); normals.Add(n); uvs.Add(new Vector2(1f, 0f));
                verts.Add(p3); normals.Add(n); uvs.Add(new Vector2(0f, 0f));
                tris.Add(baseIdx + 0); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                tris.Add(baseIdx + 0); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
            }

            // Helper: add a triangle with an explicit face normal.
            void AddTri(Vector3 p0, Vector3 p1, Vector3 p2)
            {
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                int baseIdx = verts.Count;
                verts.Add(p0); normals.Add(n); uvs.Add(new Vector2(0f, 0f));
                verts.Add(p1); normals.Add(n); uvs.Add(new Vector2(0.5f, 1f));
                verts.Add(p2); normals.Add(n); uvs.Add(new Vector2(1f, 0f));
                tris.Add(baseIdx + 0); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
            }

            // Per-slice corner helpers
            // Board cross-section at slice i:
            //   top-left  (-hw, y+thick, z)    top-right  (+hw, y+thick, z)
            //   bot-left  (-hw, y,       z)    bot-right  (+hw, y,       z)
            Vector3 TL(int i) => new Vector3(-hw[i], yFloor + yLift[i] + BoardThickness, zPos[i]);
            Vector3 TR(int i) => new Vector3( hw[i], yFloor + yLift[i] + BoardThickness, zPos[i]);
            Vector3 BL(int i) => new Vector3(-hw[i], yFloor + yLift[i],                  zPos[i]);
            Vector3 BR(int i) => new Vector3( hw[i], yFloor + yLift[i],                  zPos[i]);

            // ── Top face (normal +Y) ──────────────────────────────────
            for (int i = 0; i < SliceCount - 1; i++)
            {
                // Quad looking up: tail-left, tail-right, nose-right, nose-left
                AddQuad(TL(i), TR(i), TR(i + 1), TL(i + 1));
            }

            // ── Bottom face (normal -Y) ──────────────────────────────
            for (int i = 0; i < SliceCount - 1; i++)
            {
                // Reversed winding so normal points down
                AddQuad(BR(i), BL(i), BL(i + 1), BR(i + 1));
            }

            // ── Left side face (normal -X) ────────────────────────────
            for (int i = 0; i < SliceCount - 1; i++)
            {
                AddQuad(BL(i), TL(i), TL(i + 1), BL(i + 1));
            }

            // ── Right side face (normal +X) ───────────────────────────
            for (int i = 0; i < SliceCount - 1; i++)
            {
                AddQuad(TR(i), BR(i), BR(i + 1), TR(i + 1));
            }

            // ── Tail cap (index 0, normal -Z) ────────────────────────
            // Fan from centre of the cap face
            {
                int ci = 0;
                Vector3 centre = new Vector3(0f,
                    yFloor + yLift[ci] + BoardThickness * 0.5f,
                    zPos[ci]);
                // Two tris: top-left→centre, bot-left→centre, etc.
                AddTri(TR(ci), TL(ci), centre);
                AddTri(BL(ci), BR(ci), centre);
                AddTri(TL(ci), BL(ci), centre);
                AddTri(BR(ci), TR(ci), centre);
            }

            // ── Nose cap (last index, normal +Z) ─────────────────────
            {
                int ci = SliceCount - 1;
                Vector3 centre = new Vector3(0f,
                    yFloor + yLift[ci] + BoardThickness * 0.5f,
                    zPos[ci]);
                AddTri(TL(ci), TR(ci), centre);
                AddTri(BR(ci), BL(ci), centre);
                AddTri(BL(ci), TL(ci), centre);
                AddTri(TR(ci), BR(ci), centre);
            }

            // ── Assemble Mesh ─────────────────────────────────────────
            var mesh = new Mesh();
            mesh.name = "Snowboard";
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        // ─────────────────────────────────────────────────────────────
        // Idempotent: copy all data from src onto dst (same asset object)
        // ─────────────────────────────────────────────────────────────
        private static void CopyMeshData(Mesh src, Mesh dst)
        {
            dst.name      = src.name;
            dst.vertices  = src.vertices;
            dst.normals   = src.normals;
            dst.uv        = src.uv;
            dst.triangles = src.triangles;
            dst.RecalculateBounds();
        }

        // ─────────────────────────────────────────────────────────────
        // Material: load or create URP/Lit Snowboard.mat
        // Matches ProjectSetup.CreateUrpMat pattern but load-or-create
        // so second run leaves the asset untouched.
        // ─────────────────────────────────────────────────────────────
        private static Material LoadOrCreateSnowboardMat()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat != null)
            {
                Debug.Log("[BoardMeshGenerator] Snowboard.mat already exists — skipping creation.");
                return mat;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("[BoardMeshGenerator] URP/Lit shader not found — falling back to Standard.");
                shader = Shader.Find("Standard");
            }

            mat = new Material(shader);
            // Vivid accent: #E64D26 ≈ (0.90, 0.30, 0.15)
            mat.color = new Color(0.90f, 0.30f, 0.15f);
            mat.SetFloat("_Smoothness", 0.4f);

            AssetDatabase.CreateAsset(mat, MatPath);
            Debug.Log("[BoardMeshGenerator] Created Snowboard.mat at " + MatPath);
            return mat;
        }

        // ─────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────
        private static void EnsureDir(string assetPath)
        {
            string full = Path.Combine(Application.dataPath, "..", assetPath);
            if (!Directory.Exists(full))
                Directory.CreateDirectory(full);
        }
    }
}
