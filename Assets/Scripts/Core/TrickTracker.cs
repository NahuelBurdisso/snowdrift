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

    public static class TrickDescriber
    {
        /// <summary>
        /// Returns a human-readable label for a <see cref="TrickResult"/>,
        /// e.g. "360 + Flip + Grab". Returns an empty string when all scores are zero.
        /// </summary>
        public static string Describe(TrickResult r)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (r.Spin180s > 0) parts.Add($"{r.Spin180s * 180}");
            if (r.Flips > 0) parts.Add(r.Flips == 1 ? "Flip" : $"{r.Flips}x Flip");
            if (r.GrabScore > 0) parts.Add("Grab");
            return string.Join(" + ", parts);
        }
    }

    public class TrickTracker
    {
        bool _airborne;
        float _yaw, _pitch, _grab;

        public void BeginAir() { _airborne = true; _yaw = 0f; _pitch = 0f; _grab = 0f; }

        public void AddRotation(float yawDeltaDeg, float pitchDeltaDeg)
        {
            if (!_airborne) return;
            _yaw += yawDeltaDeg;
            _pitch += pitchDeltaDeg;
        }

        public void TickGrab(float dt, float grabScorePerSec)
        {
            if (!_airborne) return;
            _grab += grabScorePerSec * dt;
        }

        public TrickResult EndAir()
        {
            if (!_airborne) return default(TrickResult);
            _airborne = false;
            int n = Mathf.RoundToInt(Mathf.Abs(_yaw) / 180f);
            int flips = Mathf.RoundToInt(Mathf.Abs(_pitch) / 360f);
            bool rotating = n > 0 || flips > 0;
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
