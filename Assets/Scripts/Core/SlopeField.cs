using UnityEngine;

namespace Snowdrift.Core
{
    // Heightfield in continuous slope-space coords (x across, z downhill-distance).
    // Independent of floating-origin shifts: callers pass slope-space z, not world z.
    public class SlopeField
    {
        readonly float _tan, _a1, _f1, _a2, _f2, _ox, _oz;

        public SlopeField(int seed, float basePitchDeg, float octave1Amp, float octave1Freq,
                          float octave2Amp, float octave2Freq)
        {
            _tan = Mathf.Tan(basePitchDeg * Mathf.Deg2Rad);
            _a1 = octave1Amp; _f1 = octave1Freq; _a2 = octave2Amp; _f2 = octave2Freq;
            var rng = new System.Random(seed);
            _ox = (float)rng.NextDouble() * 1000f;
            _oz = (float)rng.NextDouble() * 1000f;
        }

        public float Height(float x, float z)
        {
            float baseY = -z * _tan;
            float n1 = (Mathf.PerlinNoise(_ox + x * _f1, _oz + z * _f1) - 0.5f) * 2f * _a1;
            float n2 = (Mathf.PerlinNoise(_ox + 77f + x * _f2, _oz + 77f + z * _f2) - 0.5f) * 2f * _a2;
            return baseY + n1 + n2;
        }

        // Corridor center x wanders with low-freq noise; guaranteed clear line.
        public float CorridorCenter(float z, float halfWidthOfSlope)
            => (Mathf.PerlinNoise(_ox + 333f, _oz + z * 0.008f) - 0.5f) * 2f * (halfWidthOfSlope * 0.6f);
    }
}
