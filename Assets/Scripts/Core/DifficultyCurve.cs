using System;

namespace Snowdrift.Core
{
    /// <summary>
    /// Pure distance-based difficulty ramp (no UnityEngine dependency).
    /// Distance is meters of true downhill travel (DistanceTracker.TrueDistance
    /// or absolute slope-space z — equivalent for tuning purposes).
    /// </summary>
    public static class DifficultyCurve
    {
        public const float MaxSpeedCapMult = 1.35f;
        const float SpeedRampMeters = 1500f; // e-folding distance of the speed ramp

        public const int BaseFeatures = 3;
        public const int MaxFeatures = 12;
        const float FeatureRampMeters = 400f; // +1 obstacle per this many meters

        /// <summary>Speed-cap multiplier: 1.0 at spawn, asymptotic to 1.35 (~+30% at 3 km).</summary>
        public static float SpeedCapMult(float meters)
        {
            if (meters <= 0f) return 1f;
            return 1f + (MaxSpeedCapMult - 1f) * (1f - (float)Math.Exp(-meters / SpeedRampMeters));
        }

        /// <summary>Features per 60 m chunk: 3 at spawn, +1 per 400 m, capped at 12.</summary>
        public static int FeatureCount(float meters)
        {
            if (meters <= 0f) return BaseFeatures;
            return Math.Min(BaseFeatures + (int)(meters / FeatureRampMeters), MaxFeatures);
        }
    }
}
