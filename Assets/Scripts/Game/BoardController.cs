using UnityEngine;
using UnityEngine.InputSystem;
using Snowdrift.Core;
using System;

namespace Snowdrift.Game
{
    [RequireComponent(typeof(Rigidbody), typeof(GroundSensor))]
    public class BoardController : MonoBehaviour
    {
        public BoardTuning tuning;
        public event Action OnJump;
        public event Action<LandingGrade, float> OnLand;  // grade, airtime
        public event Action OnCrashed;
        public event Action OnRespawned;
        public event Action<bool> OnAirborneChanged;

        Rigidbody _rb; GroundSensor _sensor;
        float _lean, _steerInput, _flipInput;
        bool _tuck, _drift, _grabL, _grabR, _chargeHeld, _jumpRequest;
        float _charge, _coyote, _buffer, _airTime;
        bool _airborne;
        public bool Airborne => _airborne;
        public bool GrabHeld => _grabL || _grabR;
        public bool GrabLeftHeld => _grabL;
        public bool GrabRightHeld => _grabR;
        /// <summary>0..1 fraction of max jump charge; 0 when not charging.</summary>
        public float ChargeFraction => _chargeHeld ? Mathf.Clamp01(_charge / tuning.chargeTime) : 0f;
        /// <summary>True inside the auto-land assist window (PredictLanding hit within autoLandMaxAngle this FixedUpdate).</summary>
        public bool NearLanding { get; private set; }
        public float SteerInput => _steerInput;
        public float YawDegPerFix { get; private set; }
        public float PitchDegPerFix { get; private set; }
        public Vector3 Velocity => _rb.linearVelocity;
        public bool Crashed { get; private set; }
        /// <summary>Distance-based difficulty multiplier applied to soft/tuck speed caps (set by TerrainStream).</summary>
        public float SpeedCapScale = 1f;
        float _yawVel, _pitchVel; // air rotation momentum
        float _stuckTime;         // seconds spent grounded at near-zero speed (wedged watchdog)

        const float StuckSpeedThreshold = 0.5f; // m/s — below this we may be wedged on geometry
        const float StuckCrashAfter     = 1.5f; // s — wedged this long => crash out to respawn

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _sensor = GetComponent<GroundSensor>();
            _rb.useGravity = false; // we integrate gravity ourselves
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.constraints = RigidbodyConstraints.FreezeRotation; // rotation driven by us
            // NOTE: no OnRestart subscription here — TerrainStream.Regenerate owns restart and
            // calls board.Respawn with the corridor spawn (avoids undefined event-order races).
        }

        void Update() // input sampling (Input System)
        {
            var k = Keyboard.current; if (k == null) return;

            // Flow gating: outside Riding only start/quit are live (single input owner).
            var gs = GameState.I;
            if (gs != null && gs.Phase != RunPhase.Riding)
            {
                if (k.spaceKey.wasPressedThisFrame || k.rKey.wasPressedThisFrame) gs.StartRun();
                if (k.qKey.wasPressedThisFrame) Application.Quit();
                return;
            }

            _steerInput = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f)
                        + (k.rightArrowKey.isPressed ? 1f : 0f) - (k.leftArrowKey.isPressed ? 1f : 0f);
            _flipInput = (k.sKey.isPressed ? 1f : 0f) - (k.wKey.isPressed ? 1f : 0f);
            _steerInput = Mathf.Clamp(_steerInput, -1f, 1f);
            _tuck = k.wKey.isPressed; _drift = k.leftShiftKey.isPressed;
            _grabL = k.jKey.isPressed; _grabR = k.kKey.isPressed;

            if (k.spaceKey.wasPressedThisFrame) _chargeHeld = true;
            if (k.spaceKey.wasReleasedThisFrame)
            {
                _chargeHeld = false;
                if (_airborne) _buffer = tuning.jumpBuffer; // buffered = min pop on touchdown
                else _jumpRequest = true; // consumed in FixedUpdate; survives press+release within one fixed step
            }
            if (k.rKey.wasPressedThisFrame) GameState.I.Restart();
            if (k.escapeKey.wasPressedThisFrame) GameState.I.SetPaused(!GameState.I.Paused);
            if (GameState.I.Paused && k.qKey.wasPressedThisFrame) Application.Quit(); // all game input lives here
        }

        void FixedUpdate()
        {
            if (Crashed) return;
            _sensor.Sense(tuning.normalSmoothHz);
            float dt = Time.fixedDeltaTime;
            // Second clause guards terrain-crest overshoots: only allow landing when vertical
            // velocity is no longer upward (<=0f prevents premature land while still rising).
            bool groundedNow = (_sensor.Grounded && !_airborne) || (_sensor.Grounded && _rb.linearVelocity.y <= 0f);

            if (_airborne && groundedNow) Land();
            else if (!_airborne && !_sensor.Grounded)
            {
                _coyote = tuning.coyoteTime;
                BecomeAirborne();
            }

            if (Crashed) return;
            if (!_airborne) GroundMove(dt);
            else AirMove(dt);
        }

        void GroundMove(float dt)
        {
            NearLanding = false;
            var n = _sensor.SmoothedNormal;
            var vel = _rb.linearVelocity;

            // Consume tap/release request BEFORE charge reset — _chargeHeld is already
            // false on release, so resetting first would wipe the accumulated charge.
            if (_jumpRequest) { _jumpRequest = false; TryPop(_charge); _charge = 0f; return; }
            // Charge while grounded and Space held
            _charge = _chargeHeld ? Mathf.Min(tuning.chargeTime, _charge + dt) : 0f;

            // Steering: lean with attack/release, rotate yaw
            _lean = CarveMath.StepLean(_lean, _steerInput, tuning.leanAttack, tuning.leanRelease, dt);
            transform.Rotate(0f, _lean * tuning.steerDegPerSec * dt, 0f, Space.World);

            // Gravity along slope
            vel += Vector3.ProjectOnPlane(Physics.gravity, n) * dt;

            // Grip (drift lowers it), then drag
            float grip = _drift ? tuning.driftGrip : tuning.carveGrip;
            var fwdProjected = Vector3.ProjectOnPlane(transform.forward, n);
            // Guard: if board forward is nearly parallel to surface normal the projection
            // collapses to zero — fall back to world forward to avoid damping all velocity.
            var fwdOnPlane = fwdProjected.sqrMagnitude >= 1e-4f ? fwdProjected.normalized : transform.forward;
            vel = CarveMath.ApplyGrip(vel, fwdOnPlane, grip, tuning.gripRedirect, dt);
            vel = CarveMath.ApplyDrag(vel, (_tuck ? tuning.tuckSpeedCap : tuning.softSpeedCap) * SpeedCapScale, tuning.dragK, dt);

            // Stick: project onto plane + stick accel, unless crest angle exceeds release threshold
            float crest = Vector3.Angle(Vector3.ProjectOnPlane(vel, n), vel);
            if (crest < tuning.stickReleaseAngle)
            {
                vel = Vector3.ProjectOnPlane(vel, n);
                vel -= n * tuning.stickForce * dt * 0.1f;
            }

            _rb.linearVelocity = vel;

            // Wedged watchdog: a rider pinned against a kicker side / prop below the
            // crash speed threshold used to freeze indefinitely (contact kills all
            // velocity every step). Sustained near-zero speed on the ground is never
            // valid on a slope that always descends — crash out so respawn recovers.
            if (vel.sqrMagnitude < StuckSpeedThreshold * StuckSpeedThreshold)
            {
                _stuckTime += dt;
                if (_stuckTime >= StuckCrashAfter) { _stuckTime = 0f; Crash(); return; }
            }
            else _stuckTime = 0f;

            // Align board to smoothed normal, keep yaw
            var up = n;
            // Guard BEFORE normalizing: a tiny projection normalizes to a noisy unit
            // vector (jittery rotation target); catching it pre-normalize skips the frame.
            var alignFwd = Vector3.ProjectOnPlane(transform.forward, up);
            if (alignFwd.sqrMagnitude >= 1e-4f)
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, Quaternion.LookRotation(alignFwd.normalized, up), tuning.groundAlignRate * dt));
            CountRotation();
        }

        void AirMove(float dt)
        {
            _airTime += dt;
            _coyote -= dt;
            if (_coyote > 0f && !_chargeHeld && _charge > 0f) { TryPop(_charge); _coyote = 0f; }

            var vel = _rb.linearVelocity + Physics.gravity * dt;
            _rb.linearVelocity = vel;

            // Air rotation: momentum with high authority
            float k = 1f - Mathf.Exp(-tuning.airRotAccel * dt);
            _yawVel = Mathf.Lerp(_yawVel, _steerInput * tuning.spinDegPerSec, k);
            _pitchVel = Mathf.Lerp(_pitchVel, _flipInput * tuning.flipDegPerSec, k);

            // Auto-land assist near touchdown
            bool predictHit = _sensor.PredictLanding(vel, tuning.autoLandWindow, out var n);
            NearLanding = predictHit && Vector3.Angle(transform.up, n) <= tuning.autoLandMaxAngle;
            if (NearLanding)
            {
                var landFwd = Vector3.ProjectOnPlane(transform.forward, n);
                if (landFwd.sqrMagnitude >= 1e-4f)
                    _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, Quaternion.LookRotation(landFwd.normalized, n), tuning.autoLandSlerpRate * dt));
                float decay = Mathf.Exp(-tuning.autoLandDecayRate * dt); _yawVel *= decay; _pitchVel *= decay;
            }
            transform.Rotate(_pitchVel * dt, 0f, 0f, Space.Self); // pitch (flip)
            transform.Rotate(0f, _yawVel * dt, 0f, Space.World);  // yaw (spin)
            CountRotation();
            if (_buffer > 0f) _buffer -= dt;
        }

        void CountRotation()
        {
            YawDegPerFix = _airborne ? _yawVel * Time.fixedDeltaTime : 0f;
            PitchDegPerFix = _airborne ? _pitchVel * Time.fixedDeltaTime : 0f;
        }

        void TryPop(float charge)
        {
            float t = Mathf.Clamp01(charge / tuning.chargeTime);
            float pop = Mathf.Lerp(tuning.jumpMin, tuning.jumpMax, t);
            _rb.linearVelocity += _sensor.SmoothedNormal * pop;
            _charge = 0f; _buffer = 0f; // consume buffer: prevents coyote-pop + buffered-pop double jump
            BecomeAirborne();
            OnJump?.Invoke();
        }

        void BecomeAirborne()
        {
            if (_airborne) return;
            _airborne = true; _airTime = 0f; _yawVel = 0f; _pitchVel = 0f; _stuckTime = 0f;
            OnAirborneChanged?.Invoke(true);
        }

        void Land()
        {
            _airborne = false;
            var n = _sensor.SmoothedNormal;
            var projVel = Vector3.ProjectOnPlane(_rb.linearVelocity, n);
            float yawOff = projVel.sqrMagnitude < 0.25f
                ? 0f
                : Vector3.SignedAngle(projVel, Vector3.ProjectOnPlane(transform.forward, n), n);
            float flipOff = Vector3.Angle(transform.up, n);
            var grade = LandingJudge.Judge(yawOff, flipOff, tuning.cleanDeg, tuning.sloppyDeg);

            if (grade == LandingGrade.Sloppy)
                _rb.linearVelocity *= (1f - tuning.sloppySpeedLoss);
            if (grade == LandingGrade.Crash) { Crash(); return; }

            OnAirborneChanged?.Invoke(false);
            OnLand?.Invoke(grade, _airTime);

            // Buffered jump: min-strength pop right after touchdown
            if (_buffer > 0f) { _buffer = 0f; TryPop(0f); }
        }

        public void Crash()
        {
            Crashed = true;
            NearLanding = false;
            if (GameState.I != null) GameState.I.NotifyCrash(); // Riding -> Crashed (null in offline test scenes)
            OnCrashed?.Invoke();               // CrashHandler reads Velocity for the tumble first
            _rb.linearVelocity = Vector3.zero; // then root freezes until respawn
        }

        public void Respawn(Vector3 pos, Quaternion rot)
        {
            Crashed = false; _airborne = false;
            transform.SetPositionAndRotation(pos, rot);
            _rb.linearVelocity = Vector3.zero;
            _lean = 0f; _yawVel = 0f; _pitchVel = 0f;
            _charge = 0f; _buffer = 0f; _coyote = 0f; _chargeHeld = false; _jumpRequest = false; _stuckTime = 0f;
            _tuck = false; _drift = false; _grabL = false; _grabR = false; _airTime = 0f; NearLanding = false;
            OnAirborneChanged?.Invoke(false);
            OnRespawned?.Invoke();
        }

        /// <summary>
        /// Slope-aware respawn: aligns board to surface normal at pos and gives a small
        /// downhill kick so the run resumes smoothly instead of dead-stopping.
        /// ozOff is TerrainStream._ozOff (slope-space z offset) so we can convert world z
        /// back to slope-space for the SlopeField height query.
        /// </summary>
        public void Respawn(Vector3 pos, SlopeField field, float ozOff)
        {
            // Finite-difference gradient to get surface normal at spawn point.
            // Uses the same field + ozOff convention as TerrainStream.
            const float d = 0.5f;
            float sx = pos.x;
            float sz = pos.z + ozOff; // world z -> slope-space z
            float hC  = field.Height(sx,     sz);
            float hPx = field.Height(sx + d, sz);
            float hPz = field.Height(sx,     sz + d);
            // dH/dx and dH/dz give the tangent vectors; cross product yields the normal.
            var tangentX = new Vector3(d,  hPx - hC, 0f).normalized;
            var tangentZ = new Vector3(0f, hPz - hC,  d).normalized;
            var normal   = Vector3.Cross(tangentZ, tangentX).normalized; // points away from slope surface (upward)
            if (normal.y < 0f) normal = -normal;                        // ensure upward-facing

            // Downhill direction: project world -Z (the slope runs in +Z) onto the surface plane.
            var downhill = Vector3.ProjectOnPlane(Vector3.forward, normal).normalized;

            // Build rotation: board up = normal, board forward = downhill.
            Quaternion slopeRot = Quaternion.LookRotation(downhill, normal);

            // Apply state reset then override rotation and give initial velocity.
            Respawn(pos, slopeRot);
            const float kSpawnSpeed = 5f; // m/s — enough to feel live, not enough to startle
            _rb.linearVelocity = downhill * kSpawnSpeed;
        }

        void OnCollisionEnter(Collision c)
        {
            if (Crashed) return;
            if (c.gameObject.CompareTag("Obstacle") && c.relativeVelocity.magnitude > tuning.obstacleCrashSpeed)
                Crash(); // below threshold: physics bounce happens naturally
        }
    }
}
