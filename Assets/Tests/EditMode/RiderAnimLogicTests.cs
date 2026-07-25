using NUnit.Framework;
using Snowdrift.Core;

public class RiderAnimLogicTests
{
    [Test]
    public void Grounded_NoInput_IsRideState()
    {
        var p = RiderAnimLogic.Map(new RiderAnimInput { Grounded = true });
        Assert.AreEqual(RiderPose.Ride, p.Pose);
        Assert.AreEqual(0f, p.Crouch);
    }

    [Test]
    public void Charging_SetsCrouchFraction()
    {
        var p = RiderAnimLogic.Map(new RiderAnimInput { Grounded = true, ChargeFraction = 0.6f });
        Assert.AreEqual(RiderPose.Ride, p.Pose);
        Assert.AreEqual(0.6f, p.Crouch, 1e-4f);
    }

    [Test]
    public void AirborneGrabLeft_IsGrabLPose()
    {
        var p = RiderAnimLogic.Map(new RiderAnimInput { Grounded = false, GrabLeft = true });
        Assert.AreEqual(RiderPose.GrabL, p.Pose);
    }

    [Test]
    public void GrabLeft_WinsTie_WhenBothHeld()
    {
        var p = RiderAnimLogic.Map(new RiderAnimInput { Grounded = false, GrabLeft = true, GrabRight = true });
        Assert.AreEqual(RiderPose.GrabL, p.Pose); // left wins ties, documented
    }

    [Test]
    public void AirborneNearLanding_IsBrace()
    {
        var p = RiderAnimLogic.Map(new RiderAnimInput { Grounded = false, TimeToLanding = 0.2f });
        Assert.AreEqual(RiderPose.Brace, p.Pose);
    }

    [Test]
    public void GrabOverridesBrace()
    {
        var p = RiderAnimLogic.Map(new RiderAnimInput { Grounded = false, GrabRight = true, TimeToLanding = 0.1f });
        Assert.AreEqual(RiderPose.GrabR, p.Pose);
    }

    [Test]
    public void AirborneDefault_IsAir()
    {
        var p = RiderAnimLogic.Map(new RiderAnimInput { Grounded = false, TimeToLanding = 5f });
        Assert.AreEqual(RiderPose.Air, p.Pose);
    }

    [Test]
    public void LeanFollowsSteerWithClamp()
    {
        Assert.AreEqual(20f, RiderAnimLogic.LeanAngle(1f, maxLeanDeg: 20f), 1e-4f);
        Assert.AreEqual(-10f, RiderAnimLogic.LeanAngle(-0.5f, maxLeanDeg: 20f), 1e-4f);
    }
}
