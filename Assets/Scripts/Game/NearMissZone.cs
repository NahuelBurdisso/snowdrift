using UnityEngine;

namespace Snowdrift.Game
{
    public class NearMissZone : MonoBehaviour
    {
        public static event System.Action Passed;
        bool _counted;

        void OnTriggerExit(Collider other)
        {
            if (_counted || other.attachedRigidbody == null) return;
            var board = other.attachedRigidbody.GetComponent<BoardController>();
            if (board != null && !board.Crashed) { _counted = true; Passed?.Invoke(); }
        }
    }
}
