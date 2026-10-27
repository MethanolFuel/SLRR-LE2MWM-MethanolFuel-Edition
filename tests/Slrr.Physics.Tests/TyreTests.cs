using Xunit;

namespace Slrr.Physics.Tests;

public class TyreTests
{
    [Fact]
    public void MagicFormulaMatchesTheDisassembledExpression()
    {
        double[] p = [1.4, 0.0001, 1.49, 0.002, 15.2, 0.00001, 1e-9, 1e-5, -1.0, 0.0002, 0.01, 8000, 1, 0.015, 0.4, 0, 0];
        const double x = 0.07, fz = 3500;

        // 0x43ba80 step by step: D = Fz*p1 + p2; C*D; B = (Fz*p3 + p4)*exp(-Fz*p5)/(C*D); Bx = B*(Fz*p9 + p10 + x);
        // E = Fz^2*p6 + Fz*p7 + p8; F = sin(atan((atan(Bx) - Bx)*E + Bx)*C) * D * Fz
        var d = fz * p[1] + p[2];
        var b = (fz * p[3] + p[4]) * Math.Exp(-fz * p[5]) / (d * p[0]);
        var bx = b * (fz * p[9] + p[10] + x);
        var e = fz * fz * p[6] + fz * p[7] + p[8];
        var expected = Math.Sin(Math.Atan((Math.Atan(bx) - bx) * e + bx) * p[0]) * d * fz;
        Assert.Equal(expected, TyreModel.MagicFormula(p, x, fz), 9);
    }

    [Fact]
    public void MagicFormulaShape()
    {
        var p = TyreParams.WheelDefaults().P;
        Assert.Equal(0, TyreModel.MagicFormula(p, 0, 4000));
        Assert.Equal(0, TyreModel.MagicFormula(p, -0.2, 4000)); // 0x43ba80 clamps negative results to 0
        Assert.True(TyreModel.MagicFormula(p, -0.2, 4000, clampPositive: false) < 0); // 0x43bb30 does not

        // initial slope dF/dx = p4 * Fz (BCD = p4)
        var slope = TyreModel.MagicFormula(p, 1e-6, 4000) / 1e-6;
        Assert.Equal(p[4] * 4000, slope, 0);

        // rises monotonically to the peak, never exceeds D*Fz
        var prev = 0.0;
        for (var x = 0.001; x < 0.01; x += 0.001)
        {
            var f = TyreModel.MagicFormula(p, x, 4000);
            Assert.True(f > prev);
            prev = f;
        }

        for (var x = 0.0; x < 5; x += 0.01)
        {
            Assert.True(TyreModel.MagicFormula(p, x, 4000) <= p[2] * 4000 + 1e-9);
        }

        // zero friction (D = 0) gives no force (the C*D == 0 early exit)
        var q = (double[])p.Clone();
        q[2] = 0;
        Assert.Equal(0, TyreModel.MagicFormula(q, 0.1, 4000));
    }

    [Fact]
    public void LoadSmoothingAndStiffnessScaling()
    {
        double[] p = new double[17];
        p[11] = 30000;
        Assert.Equal(4000, TyreModel.SmoothedLoad(p, 4000), 9); // ls = 0: min(L, maxL)
        Assert.Equal(30000, TyreModel.SmoothedLoad(p, 50000), 9);
        p[14] = 1;
        Assert.Equal(30000 * 2 / Math.PI * Math.Atan(4000 * Math.PI / 2 / 30000), TyreModel.SmoothedLoad(p, 4000), 9);

        p[4] = 20;
        Assert.Equal(20, TyreModel.LoadSensitiveStiffness(p, 4000)); // p16 = 0: off
        p[15] = 2000;
        p[16] = 1000;
        Assert.Equal(20 * 3000 / 5000.0, TyreModel.LoadSensitiveStiffness(p, 4000), 9);
    }

    [Fact]
    public void DamageFactorIsTheExeCurve()
    {
        Assert.Equal(1.0, TyreModel.DamageFactor(0));
        Assert.Equal(1 - 0.6 * 0.25, TyreModel.DamageFactor(0.5), 12);
        Assert.Equal(0.1, TyreModel.DamageFactor(1));
        Assert.Equal(0.1, TyreModel.DamageFactor(3));
    }

    [Fact]
    public void FlatGroundRaycast()
    {
        var g = new FlatGround(0.5, 0.9, 0.02);
        Assert.True(g.Raycast(new Vec3(1, 2, 3), new Vec3(0, -1, 0), 10, out var hit));
        Assert.Equal(1.5, hit.Distance, 12);
        Assert.Equal(new Vec3(1, 0.5, 3), hit.Point);
        Assert.Equal(0.9, hit.Friction);
        Assert.False(g.Raycast(new Vec3(0, 2, 0), new Vec3(0, -1, 0), 1, out _)); // too short
        Assert.False(g.Raycast(new Vec3(0, 2, 0), new Vec3(0, 1, 0), 10, out _)); // pointing up
    }
}
