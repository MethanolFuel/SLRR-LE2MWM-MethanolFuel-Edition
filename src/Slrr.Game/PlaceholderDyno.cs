using Slrr.Engine.Parts;

namespace Slrr.Game;

/// <summary>
/// GUESSED stand-in for the engine model behind <c>DynoData.calcDyno</c> until Slrr.Physics supplies the real one
/// (set <see cref="GameHostOptions.Dyno"/>). Without any curve <c>block.dynodata.maxTorque</c> stays 0 and
/// <c>Vehicle.isDriveable()</c> answers "the engine is not in working condition." (MEASURED from Vehicle.class), which
/// blocks every drive/race entry (Garage CMD_TRACKSELECT, CMD_TESTTRACK, ROC). The model is deliberately simple and
/// NOT the original: displacement from cylinders/bore/stroke, brake mean effective pressure 1.0 MPa scaled by
/// (1 + turbo boost bar), a parabolic torque curve peaking at 60 % of the rev limit; an engine whose
/// <c>flags</c> has <c>flagsXX_inoperable</c> (0x80000000, DynoData constant) gives no torque.
/// </summary>
public sealed class PlaceholderDynoCalculator : IDynoCalculator
{
    private const float Bmep = 1.0e6f;

    public DynoCurve? Calculate(DynoParams dyno, float steps)
    {
        var limit = Limit(dyno);
        var rpmT = 0.6f * limit;
        var tMax = PeakTorque(dyno);
        var rpmP = 0.85f * limit;
        var hp = Torque(dyno, rpmP, 0f) * rpmP / 7121f;
        // Displacement / Compression: the geometric definitions the exe model uses (Slrr.Physics EngineCycleModel.UpdateGeometry);
        // without them Block.isDynoable sees Compression 0 < minCompression and the car is "not in working condition".
        var swept = MathF.PI / 4f * dyno["bore"] * dyno["bore"] * dyno["stroke"];
        var vmin = dyno["Vmin"];
        return new DynoCurve(tMax, tMax, limit, hp, tMax, rpmT, rpmP)
        {
            Displacement = swept * dyno["cylinders"],
            Compression = vmin > 0f ? (swept + vmin) / vmin : null,
        };
    }

    public float Torque(DynoParams dyno, float rpm, float boost)
    {
        var limit = Limit(dyno);
        if (rpm <= 0 || rpm > limit * 1.05f)
        {
            return 0f;
        }

        var x = (rpm - 0.6f * limit) / (0.6f * limit);
        return MathF.Max(0f, PeakTorque(dyno) * (1f - (0.5f * x * x)));
    }

    public float Horsepower(DynoParams dyno, float rpm, float boost) => Torque(dyno, rpm, boost) * rpm / 7121f;

    private static float Limit(DynoParams d) => d["RPM_limit"] > 1000f ? d["RPM_limit"] : 7000f;

    private static float PeakTorque(DynoParams d)
    {
        if ((unchecked((uint)d.Flags) & 0x80000000u) != 0)
        {
            return 0f;
        }

        var displacement = d["cylinders"] * MathF.PI / 4f * d["bore"] * d["bore"] * d["stroke"];
        return Bmep * (1f + MathF.Max(0f, d["P_turbo_max"])) * displacement / (4f * MathF.PI);
    }
}
