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

        void Awake() => board.OnCrashed += HandleCrash;

        void HandleCrash()
        {
            if (_tumbleBody != null) return;
            // Detach visual, give it physics + random tumble
            visual.SetParent(null);
            _tumbleBody = visual.gameObject.AddComponent<Rigidbody>();
            _tumbleBody.linearVelocity = board.Velocity;
            _tumbleBody.AddTorque(Random.insideUnitSphere * 20f, ForceMode.VelocityChange);
            StartCoroutine(RespawnAfterDelay());
        }

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(tuning.respawnDelay);
            Destroy(_tumbleBody);
            visual.SetParent(board.transform, false);
            visual.localPosition = Vector3.zero; visual.localRotation = Quaternion.identity;
            var (pos, rot) = terrain.CorridorSpawnNear(board.transform.position);
            board.Respawn(pos, rot);
        }
    }
}
