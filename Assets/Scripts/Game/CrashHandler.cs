using UnityEngine;
using System.Collections;

namespace Snowdrift.Game
{
    public class CrashHandler : MonoBehaviour
    {
        public BoardController board;
        public BoardTuning tuning;
        public Transform visual;          // player mesh root (child of board object)
        public TerrainStream terrain;

        Rigidbody _tumbleBody;
        CapsuleCollider _tumbleCol;
        Collider _boardCol;

        void Awake()
        {
            board.OnCrashed += HandleCrash;
            GameState.I.OnRestart += HandleRestart;
            _boardCol = board.GetComponent<Collider>();
        }

        void OnDestroy()
        {
            board.OnCrashed -= HandleCrash;
            if (GameState.I != null) GameState.I.OnRestart -= HandleRestart;
        }

        void HandleCrash()
        {
            if (_tumbleBody != null) return;
            // Detach visual, give it physics + random tumble
            visual.SetParent(null);
            _tumbleBody = visual.gameObject.AddComponent<Rigidbody>();
            _tumbleBody.linearVelocity = board.Velocity;
            _tumbleBody.AddTorque(Random.insideUnitSphere * 20f, ForceMode.VelocityChange);
            var col = visual.gameObject.AddComponent<CapsuleCollider>();
            col.height = 1.6f; col.radius = 0.35f; col.center = Vector3.up * 0.8f;
            _tumbleCol = col;
            if (_boardCol != null) Physics.IgnoreCollision(_boardCol, _tumbleCol, true);
            StartCoroutine(RespawnAfterDelay());
        }

        void HandleRestart()
        {
            StopAllCoroutines();
            if (_tumbleBody != null)
            {
                Destroy(_tumbleCol);
                Destroy(_tumbleBody);
                visual.SetParent(board.transform, false);
                visual.localPosition = Vector3.zero; visual.localRotation = Quaternion.identity;
            }
        }

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(tuning.respawnDelay);
            if (_tumbleBody == null) yield break;
            Destroy(_tumbleCol);
            Destroy(_tumbleBody);
            visual.SetParent(board.transform, false);
            visual.localPosition = Vector3.zero; visual.localRotation = Quaternion.identity;
            var (pos, rot) = terrain.CorridorSpawnNear(board.transform.position);
            board.Respawn(pos, rot);
        }
    }
}
