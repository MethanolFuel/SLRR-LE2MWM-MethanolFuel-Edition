using Slrr.Vm;
using Xunit;
using Xunit.Abstractions;

namespace Slrr.Engine.Parts.Tests;

/// <summary>Drives real shipped part scripts headless through the parts natives (corpus tests; vacuous without game data).</summary>
public class ScriptDrivenTests(ITestOutputHelper output)
{
    private const string CarClass = @"cars\racers\Baiern_data\scripts\Baiern_DevilSport.class";
    private const string CarCfg = @"cars\racers\Baiern_data\scripts\_main_DevilSport.cfg";
    private const string ShockClass = @"parts\MrSir_Running_Gear\scripts\shocks\KSport_GT_Pro_shock.class";
    private const string ShockCfg = @"parts\MrSir_Running_Gear\scripts\shocks\KSport_GT_Pro_shock.cfg";
    private const string DiscClass = @"parts\CMS_Brakes\scripts\brake_disk_300_172_1_L.class";
    private const string DiscCfg = @"parts\CMS_Brakes\scripts\brake_disk_300_172_1_L.cfg";
    private const string SwayClass = @"parts\running_gear\scripts\sways\swaybar_Baiern_DS_front.class";
    private const string SwayCfg = @"parts\swaybars\scripts\swaybar_F.cfg";
    private const string BlockClass = @"parts\engines\Einvagen_Duhen_Ishima_Focer\scripts\Einvagen_110_GT_block.class";
    private const string BlockCfg = @"parts\engines\Einvagen_Duhen_Ishima_Focer\scripts\Einvagen_110_GT_block.cfg";

    // Type ids chosen to match the car cfg's `attach` lines (real ids come from the resource layer).
    private const int CarType = 0x00041234;
    private const int ShockType = 0x00041000;
    private const int BrakeType = 0x00040024;
    private const int SwayType = 0x00044000;
    private const int BlockType = 0x000700DF;

    private static string P(string windowsPath) => windowsPath.Replace('\\', Path.DirectorySeparatorChar);

    private static PartState Part(Harness h, string cls, string cfg, int type) => h.Create(h.ClassOf(P(cls)), Harness.Cfg(P(cfg), type), type);

    private static void Near(float expected, float actual) =>
        Assert.True(Math.Abs(expected - actual) < 1e-2f, $"expected {expected}, got {actual}");

    private void Dump(Harness h)
    {
        foreach (var d in h.World.Diagnostics)
        {
            output.WriteLine("diag: " + d);
        }

        foreach (var e in h.Events)
        {
            output.WriteLine($"event {e.Event}: {e.Text}");
        }
    }

    [Fact]
    public void CarFromCfgHasWheelsMassAndSlots()
    {
        if (Harness.Create() is not { } h)
        {
            return;
        }

        var car = Part(h, CarClass, CarCfg, CarType);
        var ch = car.Chassis!;
        Assert.Equal(4, ch.Wheels.Count);
        Near(803.5f, ch.Mass); // cfg comment "# total mass: 803.500 kg" = sum of the body lines
        Near(-0.709f, ch.Wheels[0].Position.X);
        Near(0.315f, ch.Wheels[0].Radius);
        Assert.Equal(4, h.Call(car, "getWheels").AsInt);
        Near(803.5f, h.Call(car, "getMass").AsFloat);
        Assert.Equal(car.DefinedSlotCount, h.Call(car, "getSlots").AsInt);
        Assert.Equal(503, h.Call(car, "getSlotID", Value.Int(2)).AsInt);
        Assert.Same(car.Object, h.Call(car, "getCarRef").AsObject);
        var wheel = h.Call(car, "getWheel", Value.Int(1)).AsObject!;
        Assert.Same(wheel, h.Call(car, "getWheel", Value.Int(1)).AsObject); // one WheelRef per corner
        Near(0.709f, ScriptObjects.ReadVector(h.Call(wheel, "getPos").AsObject)!.Value.X);
        Assert.True(h.Call(car, "getWheel", Value.Int(4)).IsNull);
    }

    [Fact]
    public void ShockInstalledByCommandPushesItsConstantsIntoTheWheel()
    {
        if (Harness.Create() is not { } h)
        {
            return;
        }

        var car = Part(h, CarClass, CarCfg, CarType);
        var shock = Part(h, ShockClass, ShockCfg, ShockType);
        Assert.Equal(-1, h.Call(shock, "getWheelID").AsInt); // loose
        Assert.Equal(0, h.Call(shock, "getCar").AsInt);

        h.InstallByCommand(shock, car, car, 304); // RR shock absorber
        Dump(h);
        Assert.Same(car, shock.Parent);
        Assert.Equal(304, h.Call(shock, "slotIDOnSlot", Value.Int(1)).AsInt);
        Assert.Same(shock.Object, h.Call(car, "partOnSlot", Value.Int(304)).AsObject);
        Assert.Same(car.Object, h.Call(shock, "partOnSlot", Value.Int(1)).AsObject);
        Assert.Equal(3, h.Call(shock, "getWheelID").AsInt);
        Assert.Equal(h.World.Host.IdOf(car.Object), h.Call(shock, "getCar").AsInt);
        Assert.Equal(1, h.Call(car, "isComplex").AsInt); // parent.addpart(child) callback: Part.attachedParts

        h.Call(shock, "updatevariables");
        var w = car.Chassis!.Wheels[3];
        output.WriteLine($"RR wheel: writes {w.Writes} damping {w.DampingBump}/{w.DampingRebound} render {shock.RenderType}");
        Assert.True(w.Writes > 0);
        Assert.True(w.DampingBump > 0);
        Assert.NotEqual(0, shock.RenderType);
        Assert.Equal(0, car.Chassis.Wheels[0].Writes);
    }

    [Fact]
    public void CmsBrakeDiscMatchesTheCarCfgAndFeedsTheWheel()
    {
        if (Harness.Create() is not { } h)
        {
            return;
        }

        var car = Part(h, CarClass, CarCfg, CarType);
        var disc = Part(h, DiscClass, DiscCfg, BrakeType);
        // parentSlot 0: the engine picks the slot. The car's slot 111 says `attach 0x00040024 1`.
        Assert.True(h.World.Install(h.Vm, disc, car));
        Assert.Equal(111, disc.MountSlot!.LinkedSlotId);
        Assert.Equal(0, h.Call(disc, "getWheelID").AsInt);
        h.Call(disc, "updatevariables");
        Dump(h);
        var w = car.Chassis!.Wheels[0];
        output.WriteLine($"FL wheel: brake {w.Brake} hbrake {w.HandBrake} writes {w.Writes}");
        Assert.True(w.Writes > 0);

        // CMS Disc.load/handleMessage move slot 2 with setSlotPos.
        h.Call(disc, "setSlotPos", Value.Int(2), Value.Null, Value.Null);
        Assert.NotNull(disc.Slot(2));
    }

    [Fact]
    public void SwaybarSendsItsRollbarToTheChassis()
    {
        if (Harness.Create() is not { } h)
        {
            return;
        }

        var car = Part(h, CarClass, CarCfg, CarType);
        var bar = Part(h, SwayClass, SwayCfg, SwayType);
        var name = h.Vm.GetString(ScriptObjects.GetObject(bar.Object, "name"))!;
        output.WriteLine($"name '{name}' (class {bar.Object.Class.Source})");
        var force = ScriptObjects.GetFloat(bar.Object, "force");
        var damping = ScriptObjects.GetFloat(bar.Object, "damping");
        Assert.StartsWith("Baiern DevilSport", name);
        Assert.Contains("N/m anti-roll bar", name);
        // The DevilSport cfg has no slot 701 and the bar's `compatible` list names other cars' stock bars: link explicitly.
        Assert.False(h.World.Install(h.Vm, bar, car, 701));
        Assert.True(h.World.ForceLink(h.Vm, bar, 1, car, 701));
        h.Call(bar, "updatevariables");
        Dump(h);
        Assert.Equal((force, damping), car.Chassis!.Rollbars[(0, 1)]);
    }

    [Fact]
    public void ChassisUpdateWithEngineRunsTheDynoAndSnapshotsTheFieldContract()
    {
        if (Harness.Create() is not { } h)
        {
            return;
        }

        var car = Part(h, CarClass, CarCfg, CarType);
        var block = Part(h, BlockClass, BlockCfg, BlockType);
        Assert.False(h.World.Install(h.Vm, block, car, 401)); // slot 401 says `attach 0x000700DF 99`; the block has no slot 99
        Assert.True(h.World.ForceLink(h.Vm, block, 46, car, 401));
        Assert.True(block.MaxWear > 0, "Block constructor calls setMaxWear(kmToMaxWear(...))");
        h.Call(car, "updatevariables");
        h.Call(car, "forceUpdate");
        Dump(h);
        var p = car.Chassis!.Params!;
        output.WriteLine($"params: maxRPM {p.MaxRpm} RPM_limit {p.RpmLimit} gears {p.Gears} ratio [{string.Join(", ", p.Ratio)}] dyno {(p.Dyno is null ? "null" : "set")}");
        Assert.Empty(p.Missing);
        Assert.Equal(1, car.Chassis.Updates);
        Assert.NotNull(p.Dyno);
        Assert.Empty(p.Dyno!.Missing);
        Assert.Contains(car.Chassis.SfxTables.Values, t => t.Items.Count > 0);
    }

    [Fact]
    public void SuspendUpdateSkipsForceUpdate()
    {
        if (Harness.Create() is not { } h)
        {
            return;
        }

        var car = Part(h, CarClass, CarCfg, CarType);
        ScriptObjects.Set(car.Object, "suspend_update", Value.Int(1));
        h.Call(car, "forceUpdate");
        Assert.Equal(1, car.Chassis!.SuspendedUpdates);
        Assert.Null(car.Chassis.Params);
    }

    [Fact]
    public void ConditionPriceAndRepairRunThroughTheScripts()
    {
        if (Harness.Create() is not { } h)
        {
            return;
        }

        var car = Part(h, CarClass, CarCfg, CarType);
        var shock = Part(h, ShockClass, ShockCfg, ShockType);
        h.InstallByCommand(shock, car, car, 301);
        h.Call(shock, "setWear", Value.Float(0.5f));
        h.Call(shock, "setTear", Value.Float(0.8f));
        Near(0.4f, h.Call(shock, "getConditionNoAttach").AsFloat);
        Near(0.4f, h.Call(car, "getCondition").AsFloat); // car 1.0 * shock 0.4 via attachedParts
        h.Call(shock, "repair");
        Near(0.9f, shock.Wear); // repair_max_wear
        Near(0.9f, shock.Tear);
        Assert.Equal(1f, shock.Root == car ? 1f : 0f);

        h.Call(shock, "command", h.Str("remove 0 0"));
        Assert.Null(shock.Parent);
        Assert.True(h.Call(car, "partOnSlot", Value.Int(301)).IsNull);
        Assert.Equal(0, h.Call(car, "isComplex").AsInt); // rempart removed it from attachedParts
    }

    [Fact]
    public void InstallOkFindsTheSlotAndBindsTheTarget()
    {
        if (Harness.Create() is not { } h)
        {
            return;
        }

        var car = Part(h, CarClass, CarCfg, CarType);
        var shock = Part(h, ShockClass, ShockCfg, ShockType);
        var outRef = h.Vm.NewObject(h.Vm.LoadClass("java.util.resource.GameRef"));
        var pos = ScriptObjects.NewVector(h.Vm, default);
        var r = h.Call(shock, "install_OK", Value.Obj(car.Object), Value.Int(0), Value.Obj(outRef), Value.Int(0), pos).AsObject as VmArray;
        Assert.NotNull(r);
        Assert.Equal([301, 1], r!.Items.Select(x => x.AsInt));
        Assert.Same(car.Object, ((DefaultPartHost)h.World.Host).BoundTarget(outRef));
    }
}
