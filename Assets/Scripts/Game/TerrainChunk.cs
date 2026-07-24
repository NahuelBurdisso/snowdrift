using UnityEngine;
using Snowdrift.Core;

namespace Snowdrift.Game
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class TerrainChunk : MonoBehaviour
    {
        public const float Length = 60f, Width = 80f, Res = 2f;

        // slopeZ = slope-space z of chunk start; originOffset = current world offset from origin shifts.
        public void Build(SlopeField field, float slopeZ, float originZOffset, float originYOffset,
                          Material mat, System.Func<Vector3, float, int, GameObject> spawnFeature, int difficulty)
        {
            int nx = Mathf.RoundToInt(Width / Res), nz = Mathf.RoundToInt(Length / Res);
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            for (int iz = 0, i = 0; iz <= nz; iz++)
            for (int ix = 0; ix <= nx; ix++, i++)
            {
                float x = ix * Res - Width / 2f;
                float sz = slopeZ + iz * Res;                       // slope-space (continuous, shift-independent)
                verts[i] = new Vector3(x, field.Height(x, sz) - originYOffset, sz - originZOffset);
            }
            // Flat shading: duplicate verts per triangle
            var tris = new int[nx * nz * 6];
            for (int iz = 0, t = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                int a = iz * (nx + 1) + ix, b = a + 1, c = a + nx + 1, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
            var flatVerts = new Vector3[tris.Length];
            var flatTris = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++) { flatVerts[i] = verts[tris[i]]; flatTris[i] = i; }

            var mesh = new Mesh { vertices = flatVerts, triangles = flatTris };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshCollider>().sharedMesh = mesh;
            GetComponent<MeshRenderer>().sharedMaterial = mat;
            gameObject.layer = LayerMask.NameToLayer("Ground");

            SpawnFeatures(field, slopeZ, originZOffset, originYOffset, spawnFeature, difficulty);
        }

        void SpawnFeatures(SlopeField field, float slopeZ, float ozOff, float oyOff,
                           System.Func<Vector3, float, int, GameObject> spawn, int difficulty)
        {
            var rng = new System.Random(Mathf.RoundToInt(slopeZ / Length));
            int features = Mathf.Min(2 + difficulty, 10);
            for (int i = 0; i < features; i++)
            {
                float sz = slopeZ + (float)rng.NextDouble() * Length;
                float corridor = field.CorridorCenter(sz, Width / 2f);
                int kind = rng.Next(3); // 0 rock, 1 tree, 2 kicker
                float x;
                if (kind == 2)
                {
                    // Kickers ride the clear line — features you WANT to hit (spec).
                    x = corridor + ((float)rng.NextDouble() - 0.5f) * 6f;
                }
                else
                {
                    // Obstacles stay outside the 10m clear corridor.
                    float side = rng.Next(2) == 0 ? -1f : 1f;
                    x = corridor + side * (7f + (float)rng.NextDouble() * (Width / 2f - 9f));
                }
                x = Mathf.Clamp(x, -Width / 2f + 2f, Width / 2f - 2f);
                var pos = new Vector3(x, field.Height(x, sz) - oyOff, sz - ozOff);
                var go = spawn(pos, sz, kind);
                go.transform.SetParent(transform, true);
            }
        }
    }
}
