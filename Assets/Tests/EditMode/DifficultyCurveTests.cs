using NUnit.Framework;
using Snowdrift.Core;

namespace Snowdrift.Tests
{
    public class DifficultyCurveTests
    {
        [Test]
        public void SpeedCapMult_IsOneAtSpawnAndForNegative()
        {
            Assert.AreEqual(1f, DifficultyCurve.SpeedCapMult(0f), 1e-5f);
            Assert.AreEqual(1f, DifficultyCurve.SpeedCapMult(-50f), 1e-5f);
        }

        [Test]
        public void SpeedCapMult_MonotonicAndBounded()
        {
            float prev = 1f;
            for (float m = 0f; m <= 20000f; m += 250f)
            {
                float v = DifficultyCurve.SpeedCapMult(m);
                Assert.GreaterOrEqual(v, prev, $"not monotonic at {m}m");
                Assert.LessOrEqual(v, DifficultyCurve.MaxSpeedCapMult, $"exceeds cap at {m}m");
                prev = v;
            }
            // Far out, essentially at the asymptote
            Assert.AreEqual(DifficultyCurve.MaxSpeedCapMult, DifficultyCurve.SpeedCapMult(100000f), 1e-3f);
        }

        [Test]
        public void FeatureCount_RampsFromBaseToCap()
        {
            Assert.AreEqual(DifficultyCurve.BaseFeatures, DifficultyCurve.FeatureCount(0f));
            Assert.AreEqual(DifficultyCurve.BaseFeatures, DifficultyCurve.FeatureCount(399f));
            Assert.AreEqual(DifficultyCurve.BaseFeatures + 1, DifficultyCurve.FeatureCount(400f));
            Assert.AreEqual(DifficultyCurve.MaxFeatures, DifficultyCurve.FeatureCount(50000f));
        }

        [Test]
        public void FeatureCount_NeverExceedsCapOrDropsBelowBase()
        {
            for (float m = -100f; m <= 10000f; m += 100f)
            {
                int c = DifficultyCurve.FeatureCount(m);
                Assert.GreaterOrEqual(c, DifficultyCurve.BaseFeatures);
                Assert.LessOrEqual(c, DifficultyCurve.MaxFeatures);
            }
        }
    }
}
