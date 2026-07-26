using UnityEngine;
using Unity.Cinemachine;
using Snowdrift.Core;

namespace Snowdrift.Game
{
    // The CinemachineCamera follows/looks at this component's transform ("camera target"),
    // which tracks the board's position but derives rotation from travel direction.
    //
    // Crash behaviour: on crash the rig switches its follow position to track the ragdoll
    // root (first Rigidbody under CrashHandler.visual/RiderVisual) so the camera stays on
    // the tumbling rider instead of the frozen board.  On respawn it snaps back to the board.
    public class CameraRig : MonoBehaviour
    {
        public BoardController board;
        public CrashHandler crashHandler;   // optional — wired at runtime; null-safe fallback
        public TerrainStream terrain;
        public CinemachineCamera vcam;
        public CinemachineImpulseSource landImpulse;
        public float fovMin = 60f, fovMax = 75f, speedForMaxFov = 28f;
        public float dutchMax = 4f;

        // Ragdoll follow state
        bool _crashed;
        Rigidbody _ragdollRoot; // pelvis / first Rigidbody under RiderVisual; resolved on first crash

        void Awake()
        {
            board.OnLand     += HandleLand;
            board.OnCrashed  += HandleCrash;
            board.OnRespawned += HandleRespawn;
            terrain.OnOriginShifted += HandleOriginShifted;
        }

        void OnDestroy()
        {
            if (board != null)
            {
                board.OnLand      -= HandleLand;
                board.OnCrashed   -= HandleCrash;
                board.OnRespawned -= HandleRespawn;
            }
            if (terrain != null) terrain.OnOriginShifted -= HandleOriginShifted;
        }

        void HandleLand(LandingGrade grade, float airtime)
        {
            landImpulse.GenerateImpulse(Mathf.Clamp01(airtime / 2f));
        }

        void HandleCrash()
        {
            _crashed = true;

            // Resolve ragdoll root lazily: first Rigidbody under RiderVisual (the pelvis/hips bone).
            // CrashHandler.visual is the "Visual" GO; RiderVisual is its child.
            if (_ragdollRoot == null && crashHandler != null && crashHandler.visual != null)
            {
                var riderVisual = crashHandler.visual.Find("RiderVisual");
                if (riderVisual != null)
                    _ragdollRoot = riderVisual.GetComponentInChildren<Rigidbody>();
            }
        }

        void HandleRespawn()
        {
            _crashed = false;
        }

        void HandleOriginShifted(Vector3 delta)
        {
            // Positions already moved by TerrainStream; tell Cinemachine so damping doesn't snap.
            // Only needed when following the board target (rig transform is shifted with the player root).
            // During a crash the ragdoll root is a child of RiderVisual which is under the player root,
            // so it is also shifted automatically — no extra warping needed.
            if (!_crashed)
                vcam.OnTargetObjectWarped(transform, delta);
        }

        void LateUpdate()
        {
            // ── Follow position ───────────────────────────────────────────────────
            // During crash: track ragdoll root if available; fall back to board position.
            if (_crashed && _ragdollRoot != null)
            {
                transform.position = _ragdollRoot.position;
            }
            else
            {
                transform.position = board.transform.position;
            }

            // ── Rotation / lens (always derived from board, unchanged during crash) ──
            var vel = board.Velocity; vel.y = 0f;
            float speed = board.Velocity.magnitude;
            // Blend look dir: board yaw below 3 m/s -> velocity dir above 8 m/s
            float t = Mathf.InverseLerp(3f, 8f, speed);
            var boardFwd = Vector3.ProjectOnPlane(board.transform.forward, Vector3.up).normalized;
            var dir = Vector3.Slerp(boardFwd, vel.sqrMagnitude > 0.01f ? vel.normalized : boardFwd, t);
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);

            // Smooth toward targets — board velocity zeroes instantly on crash, which
            // used to snap FOV to fovMin and Dutch to 0 in a single frame.
            float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
            float fovTarget = Mathf.Lerp(fovMin, fovMax, Mathf.Clamp01(speed / speedForMaxFov));
            vcam.Lens.FieldOfView = Mathf.Lerp(vcam.Lens.FieldOfView, fovTarget, k);
            float lean = board.Airborne ? 0f : Vector3.SignedAngle(boardFwd, dir, Vector3.up) / 45f;
            float dutchTarget = Mathf.Clamp(lean, -1f, 1f) * dutchMax;
            vcam.Lens.Dutch = Mathf.Lerp(vcam.Lens.Dutch, dutchTarget, k);
        }
    }
}
