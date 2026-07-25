using UnityEngine;
using Snowdrift.Core;

namespace Snowdrift.Game
{
    /// Applies RiderAnimLogic output to the Animator + additive spine lean.
    /// NEVER touches root rotation (TrickSystem owns it — spec hard rule).
    public class RiderAnimator : MonoBehaviour
    {
        [SerializeField] BoardController board;
        [SerializeField] Animator animator;          // may be null -> warn once, hold Ride
        [SerializeField] Transform spineBone;        // assigned by RiderSetup
        [SerializeField] float maxLeanDeg = 20f;
        [SerializeField] float leanSmooth = 12f;

        static readonly int PoseParam = Animator.StringToHash("Pose");
        static readonly int CrouchParam = Animator.StringToHash("Crouch");
        static readonly int LandTrigger = Animator.StringToHash("Land");
        static readonly int PopTrigger = Animator.StringToHash("Pop");

        float _lean;
        bool _warned;

        void OnEnable()
        {
            if (board == null)
            {
                Debug.LogWarning("RiderAnimator: no BoardController assigned, disabling");
                enabled = false;
                return;
            }
            board.OnLand += HandleLand;
            board.OnCrashed += HandleCrash;
            board.OnJump += HandleJump;
        }
        void OnDisable()
        {
            if (board == null) return;
            board.OnLand -= HandleLand;
            board.OnCrashed -= HandleCrash;
            board.OnJump -= HandleJump;
        }

        void Update()
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                if (!_warned) { Debug.LogWarning("RiderAnimator: no Animator, holding Ride pose"); _warned = true; }
                return; // spec: never block gameplay
            }
            var p = RiderAnimLogic.Map(new RiderAnimInput
            {
                Grounded = !board.Airborne,
                ChargeFraction = board.ChargeFraction,
                GrabLeft = board.GrabLeftHeld,
                GrabRight = board.GrabRightHeld,
                TimeToLanding = board.NearLanding ? 0f : float.PositiveInfinity // bool from auto-land assist; no time value exists in base game
            });
            animator.SetInteger(PoseParam, (int)p.Pose);
            animator.SetFloat(CrouchParam, p.Crouch);
        }

        void LateUpdate()
        {
            // Animator must be writing the pose each frame or the additive lean would compound.
            if (animator == null || !animator.isActiveAndEnabled) return;
            if (spineBone == null) return;
            float target = RiderAnimLogic.LeanAngle(board.SteerInput, maxLeanDeg);
            _lean = Mathf.Lerp(_lean, target, 1f - Mathf.Exp(-leanSmooth * Time.deltaTime));
            spineBone.localRotation *= Quaternion.AngleAxis(_lean, Vector3.forward); // additive after Animator
        }

        void HandleLand(LandingGrade grade, float airtime)
        {
            // Note: Crash grade never reaches OnLand subscribers (base game routes crash before invoking OnLand);
            // guard kept for cheap safety only.
            if (animator != null && grade != LandingGrade.Crash) animator.SetTrigger(LandTrigger);
        }
        void HandleJump() { if (animator != null) animator.SetTrigger(PopTrigger); }
        void HandleCrash() { /* CrashHandler swaps to ragdoll; Animator disabled there */ }
    }
}
