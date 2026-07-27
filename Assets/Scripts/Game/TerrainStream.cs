using UnityEngine;
using Snowdrift.Core;
using System.Collections.Generic;

namespace Snowdrift.Game
{
    public class TerrainStream : MonoBehaviour
    {
        public BoardController board;
        public Material snowMat, rockMat, kickerMat, lipMat;
        public int seed = 42;
        public float shiftEvery = 2000f;

        // ── Prop prefabs (optional — null arrays fall back to primitive obstacle path) ──
        // Assigned by EnvironmentSetup.WireScene() or ProjectSetup.BuildScene().
        public GameObject[] treePrefabs;
        public GameObject[] rockPrefabs;
        public GameObject   borderTreePrefab;

        public DistanceTracker Distance { get; } = new();
        public SlopeField Field { get; private set; }
        public float OriginZOffset => _ozOff; // slope-space z offset for external Respawn calls

        readonly Dictionary<int, TerrainChunk> _chunks = new();
        readonly List<int> _dead = new();
        float _ozOff, _oyOff;      // accumulated origin offsets (slope-space -> world)
        float _lastZ;              // player slope-z last fix
        public event System.Action<Vector3> OnOriginShifted; // arg = world-position delta applied to everything

        void Awake()
        {
            Field = new SlopeField(seed, 15f, 4f, 0.015f, 0.6f, 0.08f);
            GameState.I.OnRestart += Regenerate;
        }

        void OnDestroy()
        {
            if (GameState.I != null) GameState.I.OnRestart -= Regenerate;
        }

        void Start() => Regenerate();

        float PlayerSlopeZ => board.transform.position.z + _ozOff;

        void FixedUpdate()
        {
            float z = PlayerSlopeZ;
            Distance.Advance(Mathf.Max(0f, z - _lastZ));
            _lastZ = z;

            board.SpeedCapScale = DifficultyCurve.SpeedCapMult(Distance.TrueDistance);

            int cur = Mathf.FloorToInt(z / TerrainChunk.Length);
            for (int i = cur - 2; i <= cur + 4; i++) EnsureChunk(i);
            _dead.Clear();
            foreach (var kv in _chunks) if (kv.Key < cur - 2 || kv.Key > cur + 4) _dead.Add(kv.Key);
            foreach (var k in _dead) { Destroy(_chunks[k].gameObject); _chunks.Remove(k); }

            if (!board.Crashed && board.transform.position.z > shiftEvery) OriginShift();

            // Fall-off safety: if board falls more than 30m below expected ground, respawn it
            if (!board.Crashed)
            {
                float expectedY = Field.Height(board.transform.position.x, z) - _oyOff;
                if (board.transform.position.y < expectedY - 30f)
                {
                    var pos = CorridorSpawnNear(board.transform.position);
                    board.Respawn(pos, Field, _ozOff);
                }
            }
        }

        void EnsureChunk(int index)
        {
            if (index < -1 || _chunks.ContainsKey(index)) return;
            var go = new GameObject($"chunk_{index}");
            go.transform.SetParent(transform);
            var chunk = go.AddComponent<TerrainChunk>();
            int featureCount = DifficultyCurve.FeatureCount(index * TerrainChunk.Length);
            chunk.Build(Field, index * TerrainChunk.Length, _ozOff, _oyOff, snowMat, SpawnFeature, featureCount);
            // Spawn decoration-only border trees parented to this chunk (destroyed with it)
            SpawnBorderTrees(go.transform, index);
            _chunks.Add(index, chunk);
        }

        // Deterministic 0..1 hash from a float seed — stable across chunk rebuilds
        // (same slopeZ always yields the same yaw/scale variance).
        static float Hash01(float v)
        {
            float s = Mathf.Sin(v * 12.9898f + 78.233f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        GameObject SpawnFeature(Vector3 pos, float slopeZ, int kind)
        {
            GameObject go;
            if (kind == 2)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "kicker";
                go.transform.localScale = new Vector3(6f, 1.5f, 8f);
                go.transform.position = pos + Vector3.up * 0.2f;
                go.transform.rotation = Quaternion.Euler(-20f, 0f, 0f); // ramp facing downhill
                go.GetComponent<MeshRenderer>().sharedMaterial = kickerMat;
                go.layer = LayerMask.NameToLayer("Ground"); // rideable

                // Lip accent — visual only, no collider
                var lip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lip.name = "kicker_lip";
                // Destroy the BoxCollider so it contributes no physics
                var lipCol = lip.GetComponent<BoxCollider>();
                if (lipCol != null) Object.Destroy(lipCol);
                // 0.1m tall strip spanning full kicker width, along top-front edge (local space).
                // Parent scale is (6, 1.5, 8) so local coords map directly to cube mesh extents (-0.5..+0.5).
                lip.transform.SetParent(go.transform, false);
                lip.transform.localPosition = new Vector3(0f, 0.5f, -0.5f); // top-front edge of parent cube mesh
                lip.transform.localRotation = Quaternion.identity;
                // localScale is multiplied by parent scale (6,1.5,8): x=1→6m wide, y=0.067→0.1m tall, z=0.003→0.025m thin
                lip.transform.localScale = new Vector3(1f, 0.067f, 0.003f);
                if (lipMat != null)
                    lip.GetComponent<MeshRenderer>().sharedMaterial = lipMat;
            }
            else if (kind == 0)
            {
                // Rock obstacle — use prop prefab if available, else primitive fallback
                var pool = rockPrefabs;
                if (pool != null && pool.Length > 0 && pool[0] != null)
                {
                    // Spawn prefab at pos (no Y offset). The prefab's SphereCollider has center (0, 0.5, 0)
                    // so the world sphere center lands at pos.y + 0.5, matching the primitive fallback
                    // (which spawns at pos+0.5Y with collider center (0,0,0)).
                    int idx = System.Math.Abs(Mathf.RoundToInt(slopeZ * 7)) % pool.Length;
                    go = Object.Instantiate(pool[idx], pos + Vector3.down * 0.1f,
                        Quaternion.Euler(0f, Hash01(slopeZ * 2.3f) * 360f, 0f));
                    go.name = "rock_prop";
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = "rock";
                    go.transform.localScale = new Vector3(2.5f, 1.8f, 2.5f);
                    go.transform.position = pos + Vector3.up * 0.5f;
                    go.GetComponent<MeshRenderer>().sharedMaterial = rockMat;
                    go.tag = "Obstacle";
                    var trig = go.AddComponent<SphereCollider>();
                    trig.isTrigger = true; trig.radius = 2.5f;
                    go.AddComponent<NearMissZone>();
                }
            }
            else
            {
                // Tree obstacle (kind == 1) — use prop prefab if available, else primitive fallback
                var pool = treePrefabs;
                if (pool != null && pool.Length > 0 && pool[0] != null)
                {
                    int idx = System.Math.Abs(Mathf.RoundToInt(slopeZ * 13)) % pool.Length;
                    // Deterministic variance from slopeZ: random yaw + visual scale so
                    // identical prefabs don't read as copy-paste. Sink 0.2m hides the
                    // flat-shaded mesh deviating from Field.Height between vertices.
                    float h01 = Hash01(slopeZ * 1.7f);
                    go = Object.Instantiate(pool[idx], pos + Vector3.down * 0.2f,
                        Quaternion.Euler(0f, h01 * 360f, 0f));
                    go.name = "tree_prop";
                    var meshChild = go.transform.Find("Mesh");
                    if (meshChild != null)
                        meshChild.localScale *= 0.85f + Hash01(slopeZ * 3.1f) * 0.4f; // 0.85..1.25
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.name = "tree";
                    go.transform.localScale = new Vector3(1.2f, 4f, 1.2f);
                    go.transform.position = pos + Vector3.up * 2f;
                    go.GetComponent<MeshRenderer>().sharedMaterial = rockMat;
                    go.tag = "Obstacle";
                    var trig = go.AddComponent<SphereCollider>();
                    trig.isTrigger = true; trig.radius = 2.5f;
                    go.AddComponent<NearMissZone>();
                }
            }
            return go;
        }

        // Spawn non-collider border pines along the corridor edges of a chunk.
        // parentTransform: the chunk's Transform so border pines are destroyed with the chunk.
        void SpawnBorderTrees(Transform parentTransform, int chunkIndex)
        {
            if (borderTreePrefab == null) return;

            float slopeZStart = chunkIndex * TerrainChunk.Length;
            var rng = new System.Random(chunkIndex * 31337); // deterministic per chunk, independent of obstacle RNG
            int count = 10 + rng.Next(6); // 10–15 trees — dense forest band on both edges

            for (int i = 0; i < count; i++)
            {
                float slopeZ = slopeZStart + (float)rng.NextDouble() * TerrainChunk.Length;
                // x: corridorEdge + 2..26m on alternating sides (band hugs the playfield edge)
                float offsetX = 2f + (float)rng.NextDouble() * 24f; // 2..26
                float side    = rng.Next(2) == 0 ? -1f : 1f;
                float x       = side * (TerrainChunk.Width / 2f + offsetX);

                float worldY  = Field.Height(x, slopeZ) - _oyOff - 0.2f; // sink base into snow
                float worldZ  = slopeZ - _ozOff;

                var pos = new Vector3(x, worldY, worldZ);
                float yaw = (float)rng.NextDouble() * 360f;
                var go  = Object.Instantiate(borderTreePrefab, pos, Quaternion.Euler(0f, yaw, 0f));
                go.name = "border_pine";
                var meshChild = go.transform.Find("Mesh");
                if (meshChild != null)
                    meshChild.localScale *= 0.7f + (float)rng.NextDouble() * 0.6f; // 0.7..1.3
                go.transform.SetParent(parentTransform, true);
            }
        }

        void OriginShift()
        {
            float dz = board.transform.position.z;
            float dy = board.transform.position.y;
            _ozOff += dz; _oyOff += dy;
            var delta = new Vector3(0f, -dy, -dz); // applied to every world position
            foreach (Transform child in transform) child.position += delta;
            board.transform.position += delta;
            Distance.OnOriginShift();
            OnOriginShifted?.Invoke(delta); // CameraRig warps Cinemachine, GameFeel clears trail/particles
        }

        // Returns the world-space spawn position; callers use board.Respawn(pos, Field, _ozOff)
        // so slope-aligned rotation and initial velocity are computed there.
        public Vector3 CorridorSpawnNear(Vector3 worldPos)
        {
            float sz = worldPos.z + _ozOff + 5f;
            float x = Field.CorridorCenter(sz, TerrainChunk.Width / 2f);
            return new Vector3(x, Field.Height(x, sz) - _oyOff + 1f, sz - _ozOff);
        }

        void Regenerate()
        {
            foreach (var kv in _chunks) Destroy(kv.Value.gameObject);
            _chunks.Clear();
            _ozOff = 0f; _oyOff = 0f; _lastZ = 0f;
            Distance.Reset();
            var pos = CorridorSpawnNear(Vector3.zero);
            board.Respawn(pos, Field, _ozOff);
            for (int i = 0; i <= 4; i++) EnsureChunk(i);
        }
    }
}
