using NUnit.Framework;
using Snowdrift.Core;

public class TrickTrackerTests
{
    [Test]
    public void Spin360_Scores250()
    {
        var t = new TrickTracker();
        t.BeginAir();
        t.AddRotation(yawDeltaDeg: 360f, pitchDeltaDeg: 0f);
        var r = t.EndAir();
        Assert.AreEqual(250, r.SpinScore); // 180=100, 360=250, 540=450 (25n^2+75n)
        Assert.AreEqual(2, r.Spin180s);
    }

    [Test]
    public void Spin_BinsToNearest180()
    {
        var t = new TrickTracker();
        t.BeginAir(); t.AddRotation(200f, 0f);
        Assert.AreEqual(1, t.EndAir().Spin180s); // 200 -> nearest 180
    }

    [Test]
    public void FullFlip_Scores300()
    {
        var t = new TrickTracker();
        t.BeginAir(); t.AddRotation(0f, 360f);
        Assert.AreEqual(300, t.EndAir().FlipScore);
    }

    [Test]
    public void Grab_TicksScoreOverTime()
    {
        var t = new TrickTracker();
        t.BeginAir(); t.TickGrab(dt: 1.0f, grabScorePerSec: 50f);
        Assert.AreEqual(50, t.EndAir().GrabScore);
    }

    [Test]
    public void GrabWithRotation_Multiplies()
    {
        var t = new TrickTracker();
        t.BeginAir();
        t.AddRotation(360f, 0f);
        t.TickGrab(1.0f, 50f);
        Assert.AreEqual(100, t.EndAir().GrabScore); // x2 when rotation held (>=180 accumulated)
    }

    [Test]
    public void RotationOnGround_Ignored()
    {
        var t = new TrickTracker();
        t.AddRotation(360f, 0f); // never BeginAir
        t.BeginAir();
        Assert.AreEqual(0, t.EndAir().SpinScore);
    }
}
