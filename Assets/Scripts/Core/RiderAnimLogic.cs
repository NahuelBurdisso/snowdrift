namespace Snowdrift.Core
{
    public enum RiderPose { Ride, Air, GrabL, GrabR, Brace }

    public struct RiderAnimInput
    {
        public bool Grounded;
        public float ChargeFraction;   // 0..1
        public bool GrabLeft, GrabRight;
        public float TimeToLanding;    // seconds; pass float.PositiveInfinity when unknown
    }

    public struct RiderAnimParams
    {
        public RiderPose Pose;
        public float Crouch;           // 0..1
    }

    public static class RiderAnimLogic
    {
        // Matches auto-land assist window in BoardController.
        public const float BraceWindow = 0.25f;

        // Priority: Grounded > GrabL > GrabR > Brace > Air.
        // GrabL wins ties when both grab buttons are held (documented).
        // Callers must pass float.PositiveInfinity for TimeToLanding when unknown;
        // default(RiderAnimInput).TimeToLanding == 0 would otherwise trigger Brace.
        public static RiderAnimParams Map(in RiderAnimInput i)
        {
            if (i.Grounded)
                return new RiderAnimParams { Pose = RiderPose.Ride, Crouch = i.ChargeFraction };
            if (i.GrabLeft)  return new RiderAnimParams { Pose = RiderPose.GrabL };
            if (i.GrabRight) return new RiderAnimParams { Pose = RiderPose.GrabR };
            if (i.TimeToLanding <= BraceWindow) return new RiderAnimParams { Pose = RiderPose.Brace };
            return new RiderAnimParams { Pose = RiderPose.Air };
        }

        /// Maps steer (-1..1) to an additive spine lean in degrees.
        public static float LeanAngle(float steer, float maxLeanDeg)
            => UnityEngine.Mathf.Clamp(steer, -1f, 1f) * maxLeanDeg;
    }
}
