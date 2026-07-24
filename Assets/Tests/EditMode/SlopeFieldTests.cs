using NUnit.Framework;
using UnityEngine;
using Snowdrift.Core;

public class SlopeFieldTests
{
    static SlopeField F() => new SlopeField(seed: 42, basePitchDeg: 15f,
        octave1Amp: 4f, octave1Freq: 0.015f, octave2Amp: 0.6f, octave2Freq: 0.08f);

    [Test]
    public void Deterministic_SameInputSameOutput()
        => Assert.AreEqual(F().Height(10f, 250f), F().Height(10f, 250f), 1e-5f);

    [Test]
    public void SeamContinuity_ChunkBoundarySharesExactHeights()
    {
        var f = F();
        // Mirror TerrainChunk vertex parameterization: chunk N last row vs chunk N+1 first row.
        const float Length = 60f, Res = 2f;
        int nz = Mathf.RoundToInt(Length / Res);
        for (int chunk = 0; chunk < 5; chunk++)
        {
            float szEndOfN = chunk * Length + nz * Res;       // last row of chunk N
            float szStartOfN1 = (chunk + 1) * Length + 0 * Res; // first row of chunk N+1
            for (float x = -40f; x <= 40f; x += 2f)
                Assert.AreEqual(f.Height(x, szEndOfN), f.Height(x, szStartOfN1), 0f); // bitwise identical coords
        }
    }

    [Test]
    public void BaseSlope_DescendsAt15Degrees()
    {
        var flat = new SlopeField(42, 15f, 0f, 0.015f, 0f, 0.08f); // no noise
        float drop = flat.Height(0f, 0f) - flat.Height(0f, 100f);
        Assert.AreEqual(100f * Mathf.Tan(15f * Mathf.Deg2Rad), drop, 1e-3f);
    }

    [Test]
    public void Noise_StaysWithinAmplitudeBounds()
    {
        var f = F();
        var flat = new SlopeField(42, 15f, 0f, 0.015f, 0f, 0.08f);
        for (float z = 0; z < 500f; z += 7f)
        {
            float dev = Mathf.Abs(f.Height(3f, z) - flat.Height(3f, z));
            Assert.LessOrEqual(dev, 4f + 0.6f + 0.25f); // PerlinNoise can stray slightly outside [0,1]
        }
    }
}
