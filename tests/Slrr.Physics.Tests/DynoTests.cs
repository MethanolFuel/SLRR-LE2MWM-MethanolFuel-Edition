using Slrr.Physics.Dyno;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.Physics.Tests;

public class DynoTests(ITestOutputHelper output)
{
    private static Dictionary<string, float> With(params (string Key, float Value)[] kv) => kv.ToDictionary(x => x.Key, x => x.Value);

    [Fact]
    public void DefaultDynoDataGivesAPlausibleCurve()
    {
        var calc = new ExeDynoCalculator();
        var d = DynoDataDefaults.Create();
        var curve = calc.Calculate(d, 28)!;
        var m = calc.Last!;
        output.WriteLine($"disp {m.Displacement * 1000:F3} L  CR {m.Compression:F2}  maxTorque {curve.MaxTorque:F1} @ {curve.RpmMaxTorque}  maxHP {curve.MaxHp:F1} @ {curve.RpmMaxHp}");
        for (var i = 0; i < m.TableCount; i++)
        {
            output.WriteLine($"{i * m.TableStep,6} rpm  T {m.TorqueTables[0]![i],8:F2}  P {m.PowerTables[0]![i] / 1000,8:F2} kW");
        }

        Assert.Equal(250, m.TableStep); // 7000 / 28
        Assert.Equal(28, m.TableCount); // (int)7000 / 250; the last point computed is 6750 rpm
        Assert.Equal(0f, m.TorqueTables[0]![0]);
        Assert.InRange(m.Displacement, 0.0019f, 0.00192f); // pi * 0.041^2 * 0.09 * 4 = 1.901 L
        Assert.InRange(m.Compression, 10.5f, 10.52f); // (Vs + Vmin) / Vmin with Vmin 5e-5
        Assert.Equal(1f, curve.Torque); // the script multiplier is left alone when the native exists
        Assert.InRange(curve.MaxTorque, 100f, 300f);
        Assert.InRange(curve.MaxHp, 50f, 200f);
        Assert.All(m.TorqueTables[0]!, t => Assert.True(float.IsFinite(t) && t >= 0));
        Assert.Null(m.TorqueTables[1]); // nitro_H = nitro_cooling = 1: no nitro table
    }

    [Fact]
    public void LookupInterpolatesLikeGetTorque()
    {
        float[] t = [0, 10, 30, 20];
        Assert.Equal(0f, EngineCycleModel.Lookup(t, 250, -1));
        Assert.Equal(0f, EngineCycleModel.Lookup(t, 250, 0));
        Assert.Equal(5f, EngineCycleModel.Lookup(t, 250, 125));
        Assert.Equal(10f, EngineCycleModel.Lookup(t, 250, 250));
        Assert.Equal(22.5f, EngineCycleModel.Lookup(t, 250, 687.5f));
        Assert.Equal(0f, EngineCycleModel.Lookup(t, 250, 750)); // past the last entry: 0 (the loop finds no i)
        var calc = new ExeDynoCalculator();
        var d = DynoDataDefaults.Create();
        calc.Calculate(d, 28);
        Assert.Equal(calc.Last!.TorqueTables[0]![12], calc.Torque(d, 3000, 0));
        Assert.Equal(calc.Last.PowerTables[0]![12], calc.Horsepower(d, 3000, 0)); // watts, as the exe
    }

    [Fact]
    public void BiggerBoreGivesMoreDisplacementAndTorque()
    {
        var small = ExeDynoCalculator.Build(DynoDataDefaults.Create(), 28);
        var big = ExeDynoCalculator.Build(DynoDataDefaults.Create(With(("bore", 0.092f))), 28);
        Assert.True(big.Displacement > small.Displacement);
        Assert.True(big.MaxTorque > small.MaxTorque);
        Assert.True(big.MaxHp > small.MaxHp);
    }

    [Fact]
    public void NitroBuildsTheSecondTableWithoutChangingTheReportedMaxima()
    {
        var plain = ExeDynoCalculator.Build(DynoDataDefaults.Create(), 28);
        var nitro = ExeDynoCalculator.Build(DynoDataDefaults.Create(With(("nitro_H", 1.3f))), 28);
        Assert.NotNull(nitro.TorqueTables[1]);
        Assert.Equal(plain.MaxHp, nitro.MaxHp); // written to the script before the nitro pass
        Assert.True(nitro.TorqueTables[1]!.Skip(4).Zip(nitro.TorqueTables[0]!.Skip(4)).All(x => x.First > x.Second));

        // nitro_H below 1 does not trigger the pass (the exe tests nitro_H - 1 > 1e-4, no abs)
        Assert.Null(ExeDynoCalculator.Build(DynoDataDefaults.Create(With(("nitro_H", 0.8f))), 28).TorqueTables[1]);
    }

    [Fact]
    public void TurboBoostRaisesTorqueInsideItsWindow()
    {
        var na = ExeDynoCalculator.Build(DynoDataDefaults.Create(), 28);
        // DynoData defaults: rpm_turbo_mul 16, opt 85000 -> the window is centred at 5312 rpm, range 20000/16 = 1250 rpm
        var turbo = ExeDynoCalculator.Build(DynoDataDefaults.Create(With(("P_turbo_max", 0.8f))), 28);
        Assert.Equal(0.8, turbo.TurboBoost(85000 / 16.0), 6);
        Assert.Equal(0.0, turbo.TurboBoost(1000), 6);
        var i = 5250 / 250;
        Assert.True(turbo.TorqueTables[0]![i] > na.TorqueTables[0]![i] * 1.3);
        Assert.Equal((double)na.TorqueTables[0]![4], turbo.TorqueTables[0]![4], 3); // 1000 rpm: outside the window
    }
}
