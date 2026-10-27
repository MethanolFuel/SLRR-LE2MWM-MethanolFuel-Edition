using Slrr.Physics.Loading;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.Physics.Tests;

/// <summary>Loads shipped cars through the resource + parts layers (vacuous without the game data).</summary>
public class CorpusTests(ITestOutputHelper output)
{
    [Fact]
    public void FindsTheCarGametypes()
    {
        if (GameRoot.Root is not { } root)
        {
            return;
        }

        var cars = CarLoader.FindCars(root);
        Assert.True(cars.Count >= 30, $"{cars.Count} car gametypes");
        Assert.Contains(cars, c => c.Name == "Baiern_DevilSport");
    }

    [Fact]
    public void DevilSportIsBuiltByItsOwnScriptsAndRunsTheDyno()
    {
        if (GameRoot.Root is not { } root)
        {
            return;
        }

        var car = CarLoader.Load(root, CarLoader.FindCar(root, "Baiern_DevilSport")!);
        foreach (var l in car.Log)
        {
            output.WriteLine(l);
        }

        Assert.True(car.Parts.Count >= 70, $"{car.Parts.Count} parts");
        Assert.Equal(4, car.Parts.Count(p => p.Object.Class.Name.Contains("tyres", StringComparison.Ordinal) && p.Parent is not null));
        Assert.Contains(car.Parts, p => p.Object.Class.Name.EndsWith("_block", StringComparison.Ordinal));
        if (!car.ScriptsCompleted)
        {
            // Known Slrr.Vm gap (docs/phase4/physics.md): `&&` on floats; everything else must have run.
            Assert.Contains(car.Log, l => l.Contains("non-integral operands (&&)", StringComparison.Ordinal));
        }

        var p = car.Chassis.Chassis!.Params!;
        Assert.Equal(6, p.Gears);
        Assert.True(p.MaxHp > 100, $"maxHP {p.MaxHp}"); // written by calcDyno through ExeDynoCalculator
        Assert.Equal(car.Dyno.Last!.MaxHp, p.MaxHp);
        Assert.All(car.Chassis.Chassis.Wheels, w => Assert.True(w.Brake > 0 && w.Force > 0 && w.Friction > 1));

        var spec = SpecBuilder.Build(car.Chassis, car.Dyno);
        output.WriteLine($"mass {spec.Mass} kg, table {spec.Engine.Torque.Length} entries");
        Assert.InRange(spec.Mass, 1000, 2500);
        Assert.Equal(0.5, spec.Wheels[2].Drive);
        Assert.Equal(0, spec.Wheels[0].Drive);
    }

    [Fact]
    public void DriveCommandRunsEveryScenario()
    {
        if (GameRoot.Root is not { } root)
        {
            return;
        }

        var o = new StringWriter();
        var e = new StringWriter();
        var rc = Slrr.Cli.CmdDrive.Run(["Baiern_DevilSport", "--root", root, "--spec"], o, e);
        output.WriteLine(o.ToString());
        output.WriteLine(e.ToString());
        Assert.Equal(0, rc);
        var text = o.ToString();
        foreach (var name in new[] { "0-100 km/h", "top speed", "100-0 km/h", "skidpad R40" })
        {
            Assert.Contains(name, text);
        }

        Assert.DoesNotContain("CHECK", text);
    }
}
