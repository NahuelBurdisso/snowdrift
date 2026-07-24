using UnityEngine;

namespace Snowdrift.Core
{
    public struct TrickResult
    {
        public int Spin180s;   // completed half-spins (nearest 180 bin)
        public int Flips;      // completed full flips
        public int SpinScore;  // 25n^2 + 75n
        public int FlipScore;  // 300 per flip
        public int GrabScore;
        public int Total => SpinScore + FlipScore + GrabScore;
    }

    public class TrickTracker
    {
        bool _airborne;
        float _yaw, _pitch, _grab;

        public void BeginAir() { _airborne = true; _yaw = 0f; _pitch = 0f; _grab = 0f; }

        public void AddRotation(float yawDeltaDeg, float pitchDeltaDeg)
        {
            if (!_airborne) return;
            _yaw += Mathf.Abs(yawDeltaDeg);
            _pitch += Mathf.Abs(pitchDeltaDeg);
        }

        public void TickGrab(float dt, float grabScorePerSec)
        {
            if (!_airborne) return;
            _grab += grabScorePerSec * dt;
        }

        public TrickResult EndAir()
        {
            _airborne = false;
            int n = Mathf.RoundToInt(_yaw / 180f);
            int flips = Mathf.RoundToInt(_pitch / 360f);
            bool rotating = _yaw >= 180f || _pitch >= 180f;
            return new TrickResult
            {
                Spin180s = n,
                Flips = flips,
                SpinScore = 25 * n * n + 75 * n,
                FlipScore = flips * 300,
                GrabScore = Mathf.RoundToInt(_grab * (rotating ? 2f : 1f)),
            };
        }
    }
}
