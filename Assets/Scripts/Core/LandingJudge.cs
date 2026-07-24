using UnityEngine;

namespace Snowdrift.Core
{
    public enum LandingGrade { Clean, Sloppy, Crash }

    public static class LandingJudge
    {
        // Yaw judged mod 180 (switch valid). Flip judged as given. Worst axis wins.
        public static LandingGrade Judge(float yawOffsetDeg, float flipOffsetDeg, float cleanDeg, float sloppyDeg)
        {
            float yaw = Mathf.Abs(Mathf.DeltaAngle(0f, yawOffsetDeg)); // 0..180
            yaw = Mathf.Min(yaw, 180f - yaw);                          // distance to nearest multiple of 180
            float flip = Mathf.Abs(Mathf.DeltaAngle(0f, flipOffsetDeg)); // fold into [0,180]
            float worst = Mathf.Max(yaw, flip);
            if (worst <= cleanDeg) return LandingGrade.Clean;
            if (worst <= sloppyDeg) return LandingGrade.Sloppy;
            return LandingGrade.Crash;
        }
    }
}
