using NUnit.Framework;
using Snowdrift.Core;

public class ComboLogicTests
{
    [Test]
    public void CleanLandings_GrowMultiplierByHalf_CapAt5()
    {
        var c = new ComboLogic();
        Assert.AreEqual(1.0f, c.Multiplier);
        c.OnCleanLanding(trickScore: 100);
        Assert.AreEqual(1.5f, c.Multiplier);
        Assert.AreEqual(100, c.TotalScore); // banked at x1.0 before growth
        for (int i = 0; i < 20; i++) c.OnCleanLanding(0);
        Assert.AreEqual(5f, c.Multiplier);
    }

    [Test]
    public void SloppyOrCrash_ResetsMultiplier_KeepsBanked()
    {
        var c = new ComboLogic();
        c.OnCleanLanding(100);
        c.OnSloppyLanding();
        Assert.AreEqual(1.0f, c.Multiplier);
        Assert.AreEqual(100, c.TotalScore);
        c.OnCrash();
        Assert.AreEqual(1.0f, c.Multiplier);
    }

    [Test]
    public void PassiveTrickle_Adds()
    {
        var c = new ComboLogic();
        c.AddPassive(12.5f); c.AddPassive(12.5f);
        Assert.AreEqual(25, c.TotalScore);
    }

    // Reset() must clear the fractional passive accumulator so it is not carried
    // into the next run. AddPassive(0.5f) twice across a Reset() should contribute
    // 0 total (each 0.5 is sub-whole and Reset zeroes the accumulator between them).
    [Test]
    public void Reset_ClearsFractionalPassiveAccumulator()
    {
        var c = new ComboLogic();
        c.AddPassive(0.5f);  // _passive = 0.5, no whole point banked
        c.Reset();           // must zero _passive
        c.AddPassive(0.5f);  // _passive = 0.5 again, still no whole point
        Assert.AreEqual(0, c.TotalScore,
            "Fractional passive amount leaked across Reset()");
    }
}
