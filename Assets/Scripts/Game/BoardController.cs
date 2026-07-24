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
        public event Action<bool> OnAirborneChanged;

        Rigidbody _rb; GroundSensor _sensor;
        float _lean, _steerInput, _flipInput;
        bool _tuck, _drift, _grabL, _grabR, _chargeHeld;
        float _charge, _coyote, _buffer, _airTime;
        bool _airborne;
        public bool Airborne => _airborne;
        public bool GrabHeld => _grabL || _grabR;
        public float YawDegPerFix { get; private set; }
        public float PitchDegPerFix { get; private set; }
        public Vector3 Velocity => _rb.linearVelocity;
        public bool Crashed { get; private set; }
        float _yawVel, _pitchVel; // air rotation momentum

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
                else TryPop(_charge);
            }
            if (k.rKey.wasPressedThisFrame) GameState.I.Restart();
            if (k.escapeKey.wasPressedThisFrame) GameState.I.SetPaused(!GameState.I.Paused);
        }

        void FixedUpdate()
        {
            if (Crashed) return;
            _sensor.Sense(tuning.normalSmoothHz);
            float dt = Time.fixedDeltaTime;
            bool groundedNow = _sensor.Grounded && !_airborne || (_sensor.Grounded && _rb.linearVelocity.y <= 0.1f);

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
            var n = _sensor.SmoothedNormal;
            var vel = _rb.linearVelocity;

            // Charge while grounded and Space held
            _charge = _chargeHeld ? Mathf.Min(tuning.chargeTime, _charge + dt) : 0f;

            // Steering: lean with attack/release, rotate yaw
            _lean = CarveMath.StepLean(_lean, _steerInput, tuning.leanAttack, tuning.leanRelease, dt);
            transform.Rotate(0f, _lean * tuning.steerDegPerSec * dt, 0f, Space.World);

            // Gravity along slope
            vel += Vector3.ProjectOnPlane(Physics.gravity, n) * dt;

            // Grip (drift lowers it), then drag
            float grip = _drift ? tuning.driftGrip : tuning.carveGrip;
            var fwdOnPlane = Vector3.ProjectOnPlane(transform.forward, n).normalized;
            vel = CarveMath.ApplyGrip(vel, fwdOnPlane, grip, tuning.gripRedirect, dt);
            vel = CarveMath.ApplyDrag(vel, _tuck ? tuning.tuckSpeedCap : tuning.softSpeedCap, tuning.dragK, dt);

            // Stick: project onto plane + stick accel, unless crest angle exceeds release threshold
            float crest = Vector3.Angle(Vector3.ProjectOnPlane(vel, n), vel);
            if (crest < tuning.stickReleaseAngle)
            {
                vel = Vector3.ProjectOnPlane(vel, n);
                vel -= n * tuning.stickForce * dt * 0.1f;
            }

            _rb.linearVelocity = vel;

            // Align board to smoothed normal, keep yaw
            var up = n;
            var fwd = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            if (fwd.sqrMagnitude > 1e-4f)
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, Quaternion.LookRotation(fwd, up), tuning.groundAlignRate * dt));
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
            if (_sensor.PredictLanding(vel, tuning.autoLandWindow, out var n)
                && Vector3.Angle(transform.up, n) <= tuning.autoLandMaxAngle)
            {
                var fwd = Vector3.ProjectOnPlane(transform.forward, n).normalized;
                if (fwd.sqrMagnitude > 1e-4f)
                    _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, Quaternion.LookRotation(fwd, n), tuning.autoLandSlerpRate * dt));
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
            _airborne = true; _airTime = 0f; _yawVel = 0f; _pitchVel = 0f;
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
            OnCrashed?.Invoke();               // CrashHandler reads Velocity for the tumble first
            _rb.linearVelocity = Vector3.zero; // then root freezes until respawn
        }

        public void Respawn(Vector3 pos, Quaternion rot)
        {
            Crashed = false; _airborne = false;
            transform.SetPositionAndRotation(pos, rot);
            _rb.linearVelocity = Vector3.zero;
            _lean = 0f; _yawVel = 0f; _pitchVel = 0f;
            _charge = 0f; _buffer = 0f; _coyote = 0f; _chargeHeld = false;
            _tuck = false; _drift = false; _grabL = false; _grabR = false; _airTime = 0f;
            OnAirborneChanged?.Invoke(false);
        }

        void OnCollisionEnter(Collision c)
        {
            if (Crashed) return;
            if (c.gameObject.CompareTag("Obstacle") && c.relativeVelocity.magnitude > tuning.obstacleCrashSpeed)
                Crash(); // below threshold: physics bounce happens naturally
        }
    }
}
