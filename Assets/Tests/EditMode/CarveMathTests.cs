using NUnit.Framework;
using UnityEngine;
using Snowdrift.Core;

public class CarveMathTests
{
    [Test]
    public void Grip_NeverIncreasesSpeed()
    {
        var vel = new Vector3(5f, 0f, 10f);
        var fwd = Vector3.forward;
        var result = CarveMath.ApplyGrip(vel, fwd, grip: 10f, redirect: 0.9f, dt: 0.02f);
        Assert.LessOrEqual(result.magnitude, vel.magnitude + 1e-4f);
    }

    [Test]
    public void Grip_ReducesSidewaysComponent()
    {
        var vel = new Vector3(5f, 0f, 10f);
        var result = CarveMath.ApplyGrip(vel, Vector3.forward, 10f, 0.5f, 0.02f);
        Assert.Less(Mathf.Abs(result.x), 5f);
        Assert.GreaterOrEqual(result.z, 10f); // redirected into forward
    }

    [Test]
    public void Grip_ZeroGrip_LeavesVelocityUntouched()
    {
        var vel = new Vector3(5f, 0f, 10f);
        var result = CarveMath.ApplyGrip(vel, Vector3.forward, 0f, 0.9f, 0.02f);
        Assert.AreEqual(vel, result);
    }

    [Test]
    public void Drag_ReducesSpeedQuadratically()
    {
        var slow = CarveMath.ApplyDrag(new Vector3(0,0,10f), softCap: 28f, dragK: 0.02f, dt: 0.02f);
        var fast = CarveMath.ApplyDrag(new Vector3(0,0,30f), softCap: 28f, dragK: 0.02f, dt: 0.02f);
        Assert.AreEqual(10f, slow.magnitude, 0.01f);   // below cap: negligible
        Assert.Less(fast.magnitude, 30f);              // above cap: braked
    }

    [Test]
    public void SteerLean_AttackFasterThanRelease()
    {
        float up = CarveMath.StepLean(current: 0f, target: 1f, attack: 12f, release: 5f, dt: 0.05f);
        float down = 1f - CarveMath.StepLean(current: 1f, target: 0f, attack: 12f, release: 5f, dt: 0.05f);
        Assert.Greater(up, down);
    }

    [Test]
    public void SteerLean_ReversalUsesAttackRate()
    {
        // current=1, target=-1: sign reversal -> attack rate (12). Expected: 1 - 12*0.05 = 0.4
        float result = CarveMath.StepLean(current: 1f, target: -1f, attack: 12f, release: 5f, dt: 0.05f);
        Assert.AreEqual(0.4f, result, 1e-4f);
    }

    // When boardForward is Vector3.zero, ApplyGrip treats the entire velocity as
    // "sideways" and damps it all. This documents the current behaviour: result must
    // not contain NaN and must have magnitude <= original.
    [Test]
    public void Grip_ZeroBoardForward_NoNaN_SpeedNotIncreased()
    {
        var vel = new Vector3(3f, 0f, 7f);
        var result = CarveMath.ApplyGrip(vel, Vector3.zero, grip: 10f, redirect: 0.9f, dt: 0.02f);
        Assert.IsFalse(float.IsNaN(result.x) || float.IsNaN(result.y) || float.IsNaN(result.z),
            "Result contains NaN when boardForward is zero");
        Assert.LessOrEqual(result.magnitude, vel.magnitude + 1e-4f,
            "Speed increased when boardForward is zero");
    }
}
