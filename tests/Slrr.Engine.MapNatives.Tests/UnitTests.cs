using System.Numerics;
using Xunit;

namespace Slrr.Engine.MapNatives.Tests;

public sealed class UnitTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1.57f, 0f)]
    [InlineData(-2.923f, 0.3f)]
    [InlineData(0.4f, -0.2f)]
    public void YprFromDirectionInvertsForward(float yaw, float pitch)
    {
        var f = MathNatives.Forward(new Vector3(yaw, pitch, 0));
        var back = MathNatives.FromDirection(f);
        Assert.Equal(yaw, back.X, 1e-4f);
        Assert.Equal(pitch, back.Y, 1e-4f);
    }

    [Fact]
    public void ForwardMatchesTheHostRotateConvention()
    {
        // HostNatives.Rotate (Slrr.Game) applied to (0,0,1) with yaw 1.57 gives about (1, 0, 0)
        var f = MathNatives.Forward(new Vector3(MathF.PI / 2, 0, 0));
        Assert.Equal(1f, f.X, 1e-4f);
        Assert.Equal(0f, f.Z, 1e-4f);
    }

    [Fact]
    public void ToYprRecoversRoll()
    {
        var a = new Vector3(0.7f, -0.3f, 0.25f);
        var r = MathNatives.ToYpr(Quaternion.CreateFromYawPitchRoll(a.X, a.Y, a.Z));
        Assert.Equal(a.X, r.X, 1e-4f);
        Assert.Equal(a.Y, r.Y, 1e-4f);
        Assert.Equal(a.Z, r.Z, 1e-4f);
    }

    [Fact]
    public void ComposingYawsAdds()
    {
        var r = MathNatives.Compose(new Vector3(0.5f, 0, 0), new Vector3(0.25f, 0, 0));
        Assert.Equal(0.75f, r.X, 1e-4f);
    }

    [Fact]
    public void StartDirectionIsHorizontalUnit()
    {
        var d = GroundNatives.Direction(new Vector3(0, 5, 0), new Vector3(3, -2, 4));
        Assert.Equal(new Vector3(0.6f, 0, 0.8f), d);
        Assert.Equal(Vector3.UnitZ, GroundNatives.Direction(Vector3.One, Vector3.One));
    }

    [Fact]
    public void AnimationClockFollowsSpeedAndPause()
    {
        var s = new AnimationState { TargetId = 1, AnimationId = 2, Playing = true, Speed = 2f, Since = 10f };
        Assert.Equal(4f, s.PosAt(12f));
        s.BasePos = s.PosAt(12f);
        s.Since = 12f;
        s.Playing = false;
        Assert.Equal(4f, s.PosAt(20f));
    }
}
