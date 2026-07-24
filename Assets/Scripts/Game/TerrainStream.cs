using UnityEngine;
using Snowdrift.Core;
using System.Collections.Generic;

namespace Snowdrift.Game
{
    public class TerrainStream : MonoBehaviour
    {
        public BoardController board;
        public Material snowMat, rockMat, kickerMat;
        public int seed = 42;
        public float shiftEvery = 2000f;

        public DistanceTracker Distance { get; } = new();
        public SlopeField Field { get; private set; }

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
                    var (pos, rot) = CorridorSpawnNear(board.transform.position);
                    board.Respawn(pos, rot);
                }
            }
        }

        void EnsureChunk(int index)
        {
            if (index < -1 || _chunks.ContainsKey(index)) return;
            var go = new GameObject($"chunk_{index}");
            go.transform.SetParent(transform);
            var chunk = go.AddComponent<TerrainChunk>();
            int difficulty = Mathf.FloorToInt(index * TerrainChunk.Length / 500f);
            chunk.Build(Field, index * TerrainChunk.Length, _ozOff, _oyOff, snowMat, SpawnFeature, difficulty);
            _chunks.Add(index, chunk);
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
            }
            else
            {
                go = GameObject.CreatePrimitive(kind == 0 ? PrimitiveType.Sphere : PrimitiveType.Capsule);
                go.name = kind == 0 ? "rock" : "tree";
                go.transform.localScale = kind == 0 ? new Vector3(2.5f, 1.8f, 2.5f) : new Vector3(1.2f, 4f, 1.2f);
                go.transform.position = pos + Vector3.up * (kind == 0 ? 0.5f : 2f);
                go.GetComponent<MeshRenderer>().sharedMaterial = rockMat;
                go.tag = "Obstacle";
                // Near-miss trigger shell + detector
                var trig = go.AddComponent<SphereCollider>();
                trig.isTrigger = true; trig.radius = 2.5f;
                go.AddComponent<NearMissZone>();
            }
            return go;
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

        public (Vector3, Quaternion) CorridorSpawnNear(Vector3 worldPos)
        {
            float sz = worldPos.z + _ozOff + 5f;
            float x = Field.CorridorCenter(sz, TerrainChunk.Width / 2f);
            var pos = new Vector3(x, Field.Height(x, sz) - _oyOff + 1f, sz - _ozOff);
            return (pos, Quaternion.identity);
        }

        void Regenerate()
        {
            foreach (var kv in _chunks) Destroy(kv.Value.gameObject);
            _chunks.Clear();
            _ozOff = 0f; _oyOff = 0f; _lastZ = 0f;
            Distance.Reset();
            var (pos, rot) = CorridorSpawnNear(Vector3.zero);
            board.Respawn(pos, rot);
            for (int i = 0; i <= 4; i++) EnsureChunk(i);
        }
    }
}
