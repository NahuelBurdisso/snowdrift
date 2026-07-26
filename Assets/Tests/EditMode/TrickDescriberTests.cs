using NUnit.Framework;
using Snowdrift.Core;

public class TrickDescriberTests
{
    [Test]
    public void Spin360_Labels360()
    {
        var r = new TrickResult { Spin180s = 2, SpinScore = 250 };
        Assert.AreEqual("360", TrickDescriber.Describe(r));
    }

    [Test]
    public void Spin180_Labels180()
    {
        var r = new TrickResult { Spin180s = 1, SpinScore = 100 };
        Assert.AreEqual("180", TrickDescriber.Describe(r));
    }

    [Test]
    public void SingleFlip_LabelsFlip()
    {
        var r = new TrickResult { Flips = 1, FlipScore = 300 };
        Assert.AreEqual("Flip", TrickDescriber.Describe(r));
    }

    [Test]
    public void TwoFlips_Labels2xFlip()
    {
        var r = new TrickResult { Flips = 2, FlipScore = 600 };
        Assert.AreEqual("2x Flip", TrickDescriber.Describe(r));
    }

    [Test]
    public void GrabOnly_LabelsGrab()
    {
        var r = new TrickResult { GrabScore = 50 };
        Assert.AreEqual("Grab", TrickDescriber.Describe(r));
    }

    [Test]
    public void Spin360_Flip_Grab_LabelsAllParts()
    {
        var r = new TrickResult { Spin180s = 2, SpinScore = 250, Flips = 1, FlipScore = 300, GrabScore = 100 };
        Assert.AreEqual("360 + Flip + Grab", TrickDescriber.Describe(r));
    }

    [Test]
    public void EmptyResult_ReturnsEmptyString()
    {
        var r = new TrickResult();
        Assert.AreEqual("", TrickDescriber.Describe(r));
    }
}
