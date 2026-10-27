using System.Numerics;
using Slrr.Vm;

namespace Slrr.Engine.MapNatives;

/// <summary>
/// The <c>java.lang.Vector3</c> / <c>java.lang.Ypr</c> natives that <c>Track.enter</c> (makeCamExternal: <c>length()</c>,
/// <c>new Ypr(Vector3)</c>), City (<c>new Vector3(Ypr)</c>, <c>normalize</c>) and ROCTrack (<c>rotate</c>) call and no area
/// registers. Registered only when missing (the host already owns <c>Vector3.rotate(Ypr)</c>). Signatures MEASURED from
/// system/Scripts/lang/Vector3.class and Ypr.class; the angle convention is GUESSED but consistent with the host's
/// <c>Vector3.rotate(Ypr)</c> and with the MEASURED placed-object convention of Slrr.World (yaw about +Y, pitch about X,
/// roll about Z, <c>Quaternion.CreateFromYawPitchRoll</c>): yaw 0 faces +Z, <c>forward = (sin y cos p, -sin p, cos y cos p)</c>.
/// </summary>
public static class MathNatives
{
    private const string V = ScriptValues.Vector3Class;
    private const string Y = ScriptValues.YprClass;
    private const string LV = "L" + V + ";";
    private const string LY = "L" + Y + ";";

    public static void Register(Action<string, string, string, NativeMethod> addIfMissing)
    {
        addIfMissing(V, "<init>", $"({LY})", c =>
        {
            Write(c.Self, Forward(ScriptValues.ReadYpr(c.Object(0)) ?? Vector3.Zero));
            return Value.Void;
        });
        addIfMissing(V, "length", "()F", c => Value.Float(Read(c.Self).Length()));
        addIfMissing(V, "distance", $"({LV})F", c => Value.Float(Vector3.Distance(Read(c.Self), ScriptValues.ReadVector(c.Object(0)) ?? Vector3.Zero)));
        addIfMissing(V, "normalize", "()V", c =>
        {
            var v = Read(c.Self);
            if (v.LengthSquared() > 0)
            {
                Write(c.Self, Vector3.Normalize(v));
            }

            return Value.Void;
        });
        addIfMissing(V, "rotate", $"({LY}){LV}", c =>
        {
            var a = ScriptValues.ReadYpr(c.Object(0)) ?? Vector3.Zero;
            Write(c.Self, Vector3.Transform(Read(c.Self), Quaternion.CreateFromYawPitchRoll(a.X, a.Y, a.Z)));
            return Value.Obj(c.Self);
        });
        addIfMissing(V, "rotate", $"({LV}F){LV}", c =>
        {
            var axis = ScriptValues.ReadVector(c.Object(0)) ?? Vector3.UnitY;
            if (axis.LengthSquared() > 0)
            {
                Write(c.Self, Vector3.Transform(Read(c.Self), Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), c.Float(1))));
            }

            return Value.Obj(c.Self);
        });
        addIfMissing(Y, "<init>", $"({LV})", c =>
        {
            WriteYpr(c.Self, FromDirection(ScriptValues.ReadVector(c.Object(0)) ?? Vector3.UnitZ));
            return Value.Void;
        });
        addIfMissing(Y, "mul", $"({LY})V", c =>
        {
            var a = ScriptValues.ReadYpr(c.Self) ?? Vector3.Zero;
            var b = ScriptValues.ReadYpr(c.Object(0)) ?? Vector3.Zero;
            WriteYpr(c.Self, Compose(a, b));
            return Value.Void;
        });
    }

    /// <summary>Unit forward vector of (yaw, pitch, roll) = (0,0,1) rotated by the angles.</summary>
    public static Vector3 Forward(Vector3 ypr) => Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromYawPitchRoll(ypr.X, ypr.Y, ypr.Z));

    /// <summary>(yaw, pitch, 0) whose <see cref="Forward"/> is the direction of <paramref name="d"/>.</summary>
    public static Vector3 FromDirection(Vector3 d) =>
        new(MathF.Atan2(d.X, d.Z), MathF.Atan2(-d.Y, MathF.Sqrt((d.X * d.X) + (d.Z * d.Z))), 0);

    /// <summary>Rotation <paramref name="a"/> followed by <paramref name="b"/>, as (yaw, pitch, roll). Order GUESSED.</summary>
    public static Vector3 Compose(Vector3 a, Vector3 b) =>
        ToYpr(Quaternion.Concatenate(Quaternion.CreateFromYawPitchRoll(a.X, a.Y, a.Z), Quaternion.CreateFromYawPitchRoll(b.X, b.Y, b.Z)));

    /// <summary>(yaw, pitch, roll) of a rotation in the CreateFromYawPitchRoll convention.</summary>
    public static Vector3 ToYpr(Quaternion q)
    {
        var f = Vector3.Transform(Vector3.UnitZ, q);
        var yp = FromDirection(f);
        var undo = Quaternion.Inverse(Quaternion.CreateFromYawPitchRoll(yp.X, yp.Y, 0));
        var up = Vector3.Transform(Vector3.Transform(Vector3.UnitY, q), undo);
        return new Vector3(yp.X, yp.Y, MathF.Atan2(-up.X, up.Y));
    }

    private static Vector3 Read(VmObject v) => ScriptValues.ReadVector(v) ?? Vector3.Zero;

    private static void Write(VmObject v, Vector3 p)
    {
        ScriptValues.Set(v, "x", Value.Float(p.X));
        ScriptValues.Set(v, "y", Value.Float(p.Y));
        ScriptValues.Set(v, "z", Value.Float(p.Z));
    }

    private static void WriteYpr(VmObject v, Vector3 a)
    {
        ScriptValues.Set(v, "y", Value.Float(a.X));
        ScriptValues.Set(v, "p", Value.Float(a.Y));
        ScriptValues.Set(v, "r", Value.Float(a.Z));
    }
}
