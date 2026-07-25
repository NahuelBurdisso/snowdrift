using NUnit.Framework;
using UnityEngine;
using Snowdrift.Core;

public class LandingJudgeTests
{
    static LandingGrade Judge(float yawOff, float flipOff) =>
        LandingJudge.Judge(yawOffsetDeg: yawOff, flipOffsetDeg: flipOff, cleanDeg: 25f, sloppyDeg: 60f);

    [Test] public void PerfectLanding_Clean() => Assert.AreEqual(LandingGrade.Clean, Judge(0f, 0f));
    [Test] public void Switch180_Clean() => Assert.AreEqual(LandingGrade.Clean, Judge(180f, 0f)); // mod 180
    [Test] public void Yaw200_Clean() => Assert.AreEqual(LandingGrade.Clean, Judge(200f, 0f));   // 20 off nearest 180
    [Test] public void Yaw40Off_Sloppy() => Assert.AreEqual(LandingGrade.Sloppy, Judge(40f, 0f));
    [Test] public void Flip70Off_Crash() => Assert.AreEqual(LandingGrade.Crash, Judge(0f, 70f));
    [Test] public void WorstAxisWins() => Assert.AreEqual(LandingGrade.Sloppy, Judge(10f, 40f));
    [Test] public void Yaw90_MaxDeviation_Crash() => Assert.AreEqual(LandingGrade.Crash, Judge(90f, 0f));
    [Test] public void Flip350_Clean() => Assert.AreEqual(LandingGrade.Clean, Judge(0f, 350f)); // 10° off full rotation

    // Boundary: exactly cleanDeg (25°). Implementation uses <= so this must be Clean.
    [Test]
    public void ExactlyCleanDeg_IsClean()
    {
        // worst == cleanDeg == 25f; condition is (worst <= cleanDeg) → Clean
        Assert.AreEqual(LandingGrade.Clean, Judge(25f, 0f),
            "Yaw exactly at cleanDeg boundary should be Clean (<=)");
        Assert.AreEqual(LandingGrade.Clean, Judge(0f, 25f),
            "Flip exactly at cleanDeg boundary should be Clean (<=)");
    }
}
