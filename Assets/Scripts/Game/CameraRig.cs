using UnityEngine;
using Unity.Cinemachine;
using Snowdrift.Core;

namespace Snowdrift.Game
{
    // The CinemachineCamera follows/looks at this component's transform ("camera target"),
    // which tracks the board's position but derives rotation from travel direction.
    public class CameraRig : MonoBehaviour
    {
        public BoardController board;
        public TerrainStream terrain;
        public CinemachineCamera vcam;
        public CinemachineImpulseSource landImpulse;
        public float fovMin = 60f, fovMax = 75f, speedForMaxFov = 28f;
        public float dutchMax = 4f;

        void Awake()
        {
            board.OnLand += HandleLand;
            terrain.OnOriginShifted += HandleOriginShifted;
        }

        void OnDestroy()
        {
            if (board != null) board.OnLand -= HandleLand;
            if (terrain != null) terrain.OnOriginShifted -= HandleOriginShifted;
        }

        void HandleLand(LandingGrade grade, float airtime)
        {
            landImpulse.GenerateImpulse(Mathf.Clamp01(airtime / 2f));
        }

        void HandleOriginShifted(Vector3 delta)
        {
            vcam.OnTargetObjectWarped(transform, delta); // positions already moved; tell Cinemachine so damping doesn't snap
        }

        void LateUpdate()
        {
            transform.position = board.transform.position;

            var vel = board.Velocity; vel.y = 0f;
            float speed = board.Velocity.magnitude;
            // Blend look dir: board yaw below 3 m/s -> velocity dir above 8 m/s
            float t = Mathf.InverseLerp(3f, 8f, speed);
            var boardFwd = Vector3.ProjectOnPlane(board.transform.forward, Vector3.up).normalized;
            var dir = Vector3.Slerp(boardFwd, vel.sqrMagnitude > 0.01f ? vel.normalized : boardFwd, t);
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);

            vcam.Lens.FieldOfView = Mathf.Lerp(fovMin, fovMax, Mathf.Clamp01(speed / speedForMaxFov));
            float lean = board.Airborne ? 0f : Vector3.SignedAngle(boardFwd, dir, Vector3.up) / 45f;
            vcam.Lens.Dutch = Mathf.Clamp(lean, -1f, 1f) * dutchMax;
        }
    }
}
