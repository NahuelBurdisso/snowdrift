using NUnit.Framework;
using Snowdrift.Core;

public class DistanceTrackerTests
{
    [Test]
    public void TrueDistance_SurvivesOriginShift()
    {
        var d = new DistanceTracker();
        d.Advance(1500f);
        d.OnOriginShift();          // world moved back; true distance unaffected
        d.Advance(700f);
        Assert.AreEqual(2200f, d.TrueDistance, 1e-3f);
    }

    [Test]
    public void Reset_Zeroes()
    {
        var d = new DistanceTracker();
        d.Advance(100f); d.Reset();
        Assert.AreEqual(0f, d.TrueDistance);
    }
}
