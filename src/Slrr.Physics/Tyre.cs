namespace Slrr.Physics;

/// <summary>
/// The tyre coefficients of one wheel: the 17-float array at wheel+0x1E8 that <c>WheelRef.setPacejka(i, v)</c>
/// writes (MEASURED: native 0x441210 accepts i = 0..16, <c>cmp eax,0x11; jae</c>). The named WheelRef setters write
/// into the same array (MEASURED from the setter natives): setSliction → [0], setFriction → [2] (also +0x64/+0x68),
/// setStiffness → [4], setMaxLoad → [11], setFrictn_x → [12], setLoadSmooth → [14]. setRollRes (+0x74) and
/// setBearing (+0xF0) are separate fields.
/// </summary>
public sealed class TyreParams
{
    public const int Count = 17;

    public double[] P { get; } = new double[Count];

    public double RollResistance { get; set; }

    public double Bearing { get; set; }

    /// <summary>Script defaults of <c>Wheel.updatevariables</c> for a wheel without a tyre (parts/scripts Wheel.java).</summary>
    public static TyreParams WheelDefaults()
    {
        var t = new TyreParams { RollResistance = 0.001, Bearing = 20 };
        // setPacejka(0..14) = 1.4, 0, 1.49, 0, 15.2, 0, -0, 0, -1, 0, 0, 8000, 1, 0.015, 0.4, then the named setters:
        // sliction 1 -> [0], friction 1 -> [2], stiffness 100 -> [4], maxload 1000 -> [11], frictn_x 1 -> [12], loadsmooth 0 -> [14].
        double[] p = [1.0, 0, 1.0, 0, 100, 0, -0.0, 0, -1.0, 0, 0, 1000, 1.0, 0.015, 0.0, 0, 0];
        p.CopyTo(t.P, 0);
        return t;
    }

    public TyreParams Clone()
    {
        var t = new TyreParams { RollResistance = RollResistance, Bearing = Bearing };
        P.CopyTo(t.P, 0);
        return t;
    }
}

/// <summary>Force curves of the tyre.</summary>
public static class TyreModel
{
    /// <summary>
    /// MEASURED port of 0x43ba80 (<c>ecx</c> = coefficient array, args <c>x</c>, <c>Fz</c>), a Pacejka-89-like magic
    /// formula without vertical shift, clamped at 0:
    /// <code>
    /// D  = p1*Fz + p2                  (peak friction coefficient; p2 = friction)
    /// C  = p0                          (shape; p0 = sliction)
    /// B  = (p3*Fz + p4) * exp(-p5*Fz) / (C*D)   (p4 = stiffness: initial slope dF/dx = p4*Fz)
    /// E  = p6*Fz^2 + p7*Fz + p8
    /// x' = x + p9*Fz + p10
    /// F  = D*Fz*sin(C*atan(B*x' + E*(atan(B*x') - B*x')))     ; 0 if C*D == 0; max(F, 0)
    /// </code>
    /// 0x43bb30 is the same without the clamp. What the exe passes as <c>x</c> was not traced: here it is the
    /// combined slip magnitude (GUESSED).
    /// </summary>
    public static double MagicFormula(ReadOnlySpan<double> p, double x, double fz, bool clampPositive = true)
    {
        var d = fz * p[1] + p[2];
        var cd = d * p[0];
        if (cd == 0)
        {
            return 0;
        }

        var b = (fz * p[3] + p[4]) * Math.Exp(-(fz * p[5])) / cd;
        var bx = b * (fz * p[9] + p[10] + x);
        var e = fz * fz * p[6] + fz * p[7] + p[8];
        var inner = (Math.Atan(bx) - bx) * e + bx;
        var f = Math.Sin(Math.Atan(inner) * p[0]) * (d * fz);
        return clampPositive && f < 0 ? 0 : f;
    }

    /// <summary>
    /// MEASURED (0x4562a4..0x456302): the load fed to the formula,
    /// <c>Fs = ls*maxL*(2/pi)*atan(pi*L/(2*maxL)) + (1-ls)*min(L, maxL)</c> with ls = p14, maxL = p11. The exe then
    /// multiplies by a further factor ((v+1)/2 of a quantity not identified) which is NOT reproduced.
    /// </summary>
    public static double SmoothedLoad(ReadOnlySpan<double> p, double load)
    {
        var maxL = p[11];
        if (!(maxL > 0))
        {
            return load;
        }

        var ls = p[14];
        var clamped = load < maxL ? load : maxL;
        return ls * maxL * (2.0 / Math.PI) * Math.Atan(load * (Math.PI / 2.0) / maxL) + (1.0 - ls) * clamped;
    }

    /// <summary>MEASURED (0x456313..0x456343): if p16 &gt; 0, stiffness p4 is scaled by (p15 + p16) / (Fs + p16).</summary>
    public static double LoadSensitiveStiffness(ReadOnlySpan<double> p, double smoothedLoad) =>
        p[16] > 0 ? p[4] * (p[15] + p[16]) / (smoothedLoad + p[16]) : p[4];

    /// <summary>MEASURED (setFriction 0x440f90, setBrake 0x441580, engine torque 0x4486fb): damage d scales a value by
    /// <c>1 - 0.6*d^2</c> while d &lt; 1, else by 0.1.</summary>
    public static double DamageFactor(double d) => d < 1 ? 1 - d * d * 0.6 : 0.1;
}
