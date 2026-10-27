using System.Globalization;
using Slrr.Engine.Parts.RawEdit;
using Slrr.Formats.Cfg;
using Slrr.Vm;
using Xunit;

namespace Slrr.Engine.Parts.Tests;

public class RawEditAndNativeTests
{
    private const string CarClass = "cars/racers/Baiern_data/scripts/Baiern_DevilSport.class";
    private const string CarCfg = "cars/racers/Baiern_data/scripts/_main_DevilSport.cfg";

    private static string P(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    // ---------------------------------------------------------------- no game data needed

    [Fact]
    public void AddressMapMatchesTheResearchTable()
    {
        var rows = File.ReadAllLines(Path.Combine(GameRoot.Repo, "docs", "research", "rawedit-addresses.tsv")).Skip(1)
            .Select(l => l.Split('\t')).ToList();
        Assert.Equal(69, rows.Count);
        Assert.Equal(69, RawAddressMap.Entries.Count);
        var gi = EngineSettings.MethanolFuel();
        var vanilla = EngineSettings.Vanilla();
        foreach (var r in rows)
        {
            var e = RawAddressMap.Find(uint.Parse(r[2].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            Assert.NotNull(e);
            Assert.Equal(r[0], e!.Name);
            Assert.Equal(r[1] == "float" ? RawCellType.Float : RawCellType.Int, e.Type);
            if (double.TryParse(r[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var giDefault))
            {
                Assert.Equal(giDefault, e.Get(gi), 4);
            }

            if (r[7].Length > 0 && double.TryParse(r[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var redline))
            {
                Assert.Equal(redline, e.Get(vanilla), 4); // config-backed globals take the Redline file value
            }
        }
    }

    [Fact]
    public void RawCellsKeepTheExeBitSemantics()
    {
        var world = new PartsWorld();
        const int gravity = 0x006186e4;
        Assert.Equal(-9.8067f, BitConverter.Int32BitsToSingle(world.ReadRaw(gravity)), 0.0001f);
        Assert.Equal(gravity, world.WriteRaw(gravity, BitConverter.SingleToInt32Bits(-3.7f))); // setF returns the address
        Assert.Equal(-3.7f, world.Settings.Physics.Gravity, 0.0001f);

        // Derived cell: -BodyDeformationMult, writes ignored.
        const int minus = 0x006185c0;
        world.WriteRaw(minus, BitConverter.SingleToInt32Bits(5f));
        Assert.Equal(-0.8f, BitConverter.Int32BitsToSingle(world.ReadRaw(minus)), 0.0001f);

        // Read-only host counters ignore script writes; bool cells read 0/1.
        world.WriteRaw(0x00765f60, 7);
        Assert.Equal(0, world.Settings.ResourceBudget.InstancesInUse);
        world.WriteRaw(0x006185d0, 0);
        Assert.False(world.Settings.Damage.DoorsOpenOnCollision);

        // Unknown addresses: logged once, read 0.
        Assert.Equal(0, world.ReadRaw(0x00401000));
        world.WriteRaw(0x00401000, 1);
        Assert.Single(world.Diagnostics);
    }

    [Fact]
    public void CfgDefinitionReadsSlotsRulesWheelsAndBodies()
    {
        const string text = """
            body  0 0 1  0 0 0  100.0 mesh.scx
            body  0 0 -1 0 0 0  300.0 mesh.scx
            type 10.000 sphere 0.315
            spring 0.290 0.100 0.050
            wheel -0.709 -0.590 -1.480 0 0 0 1 0 1 0
            slot 0 0 0 0 0 0 101 ; FL wheel
            attach 0x00040200 1
            slottype 101
            slotdmgmode 0x00000004
            slot 1 2 3 0.5 0 0 2
            compatible 0x00041000 1
            category 3
            stockpart 0x00040999
            eof
            slot 9 9 9 0 0 0 77
            """;
        var def = PartDefinition.FromCfg(CfgDocument.Parse(System.Text.Encoding.Latin1.GetBytes(text), "test.cfg"), 0x1234);
        Assert.Equal([101, 2], def.Slots.Select(s => s.Id));
        Assert.Equal(new SlotRef(0x00040200, 1), def.Slots[0].Attach.Single());
        Assert.Equal(101, def.Slots[0].SlotType);
        Assert.Equal(4, def.Slots[0].DamageMode);
        Assert.Equal(new SlotRef(0x00041000, 1), def.Slots[1].Compatible.Single());
        Assert.Equal(3f, def.Slots[1].Position.Z);
        Assert.Equal(3, def.Category);
        Assert.Equal([0x00040999], def.StockParts);
        Assert.Equal(400f, def.Mass);
        var w = Assert.Single(def.Wheels);
        Assert.Equal(0.315f, w.Radius);
        Assert.Equal([1f, 0f, 1f, 0f], w.Flags);
        Assert.Equal([0.29f, 0.1f, 0.05f], w.Spring);
    }

    [Fact]
    public void ContractFieldListsHaveTheDocumentedSizes()
    {
        Assert.Equal(23, ChassisParams.FieldNames.Count);
        Assert.Equal(39, DynoParams.FieldNames.Count);
        Assert.Equal(22, ChassisTelemetry.Names.Count);
    }

    [Fact]
    public void ModuleRegistersEveryPartsAreaNativeOfTheRanking()
    {
        var module = new PartsNatives();
        var r = new NativeRegistry().Add(module);
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(GameRoot.Repo, "docs", "research", "native-usage-order.json")));
        var area = json.RootElement.EnumerateArray()
            .Select(x => new NativeKey(x.GetProperty("class").GetString()!, x.GetProperty("method").GetString()!, x.GetProperty("descriptor").GetString()!))
            .Where(PartsSurvey.IsPartsNative).ToList();
        Assert.Equal(104, area.Count);
        Assert.All(area, k => Assert.True(r.Contains(k.ClassName, k.Name, k.Descriptor), k.ToString()));
        Assert.Equal(104, module.Registered.Count);
    }

    // ---------------------------------------------------------------- shipped scripts

    [Fact]
    public void ChassisWheelScriptWritesSteerThroughRawEdit()
    {
        if (Harness.Create() is not { } h)
        {
            return;
        }

        var car = h.Create(h.ClassOf(P(CarClass)), Harness.Cfg(P(CarCfg), 1), 1);
        ScriptObjects.Set(car.Object, "wheels", Value.Int(4));
        ScriptObjects.Set(car.Object, "FrontMaxSteer", Value.Float(0.61f));
        ScriptObjects.Set(car.Object, "RearMaxSteer", Value.Float(0.05f));
        h.Call(car, "UpdateOnlyWheels"); // RawEdit.setF(whl.ptr + 0xc8, FrontMaxSteer | RearMaxSteer)
        var wheels = car.Chassis!.Wheels;
        Assert.Equal(0.61f, wheels[0].Steer);
        Assert.Equal(0.61f, wheels[1].Steer);
        Assert.Equal(0.05f, wheels[3].Steer);
        Assert.Equal(0.61f, h.Call(wheels[0].Ref!, "getSteer").AsFloat);
        Assert.Empty(h.World.Diagnostics);

        // The HeadlightController feature flag File.exists("system\\Scripts\\lang\\RawEdit.class") must stay true.
        Assert.True(File.Exists(Path.Combine(GameRoot.Root!, "system", "Scripts", "lang", "RawEdit.class")));
    }

    [Fact]
    public void IndexedTelemetryIsReachableByIndexAndByAddress()
    {
        if (Harness.Create() is not { } h)
        {
            return;
        }

        var car = h.Create(h.ClassOf(P(CarClass)), null, 1);
        car.Chassis!.Telemetry.SetFloat(ChassisTelemetry.Rpm, 3500f);
        var rpmIndex = ScriptObjects.GetInt(car.Object, "IndexedData_RPM");
        Assert.Equal(ChassisTelemetry.Rpm, rpmIndex);
        Assert.Equal(3500f, h.Call(car, "getIndexedData", Value.Int(rpmIndex)).AsFloat);

        var raw = h.Vm.LoadClass("java.lang.RawEdit");
        Value Static(string name, string desc, params Value[] a) => h.Vm.Invoke(raw.Methods.Single(m => m.Name == name && m.Descriptor == desc), null, a);
        var gearAddr = h.Call(car, "getIndexedAddr", Value.Int(ChassisTelemetry.GearNumber)).AsInt;
        Static("setI", "(II)I", Value.Int(gearAddr), Value.Int(4)); // the "democall in garage" of Chassis.java
        Assert.Equal(4, car.Chassis.Telemetry.GetInt(ChassisTelemetry.GearNumber));
        Assert.Equal(4, Static("getI", "(I)I", Value.Int(gearAddr)).AsInt);
        var rpmAddr = h.Call(car, "getIndexedAddr", Value.Int(rpmIndex)).AsInt;
        Static("setF", "(IF)I", Value.Int(rpmAddr), Value.Float(1f)); // RPM is read-only for scripts
        Assert.Equal(3500f, Static("getF", "(I)F", Value.Int(rpmAddr)).AsFloat);
        Assert.Equal(-9.8067f, Static("getF", "(I)F", Value.Int(0x006186e4)).AsFloat, 0.0001f);
    }

    [Fact]
    public void DynoDataRoutesThroughTheCalculator()
    {
        if (Harness.Create() is not { } h)
        {
            return;
        }

        h.World.Dyno = new FixedDyno();
        var dyno = h.Vm.Construct(h.Vm.LoadClass(PartsWorld.DynoDataClass));
        var state = h.World.FindDyno(dyno);
        Assert.NotNull(state);
        Assert.Equal(state!.Handle, ScriptObjects.GetInt(dyno, "ptr"));
        h.Call(dyno, "calcDyno", Value.Float(28));
        Assert.Equal(123f, ScriptObjects.GetFloat(dyno, "maxHP"));
        Assert.Equal(7000f, ScriptObjects.GetFloat(dyno, "maxRPM"));
        Assert.Equal(2f * 7000f, h.Call(dyno, "getTorque", Value.Float(2), Value.Float(0)).AsFloat); // RPM_limit default 7000
        var copy = h.Call(dyno, "clone").AsObject!;
        Assert.NotNull(h.World.FindDyno(copy)); // clone() runs new DynoData() -> newNative
    }

    [Fact]
    public void GetInfoKeysAnswerFromTheTwin()
    {
        if (Harness.Create() is not { } h)
        {
            return;
        }

        var car = h.Create(h.ClassOf(P(CarClass)), Harness.Cfg(P(CarCfg), 0x41234), 0x41234);
        Assert.Equal(0x41234, h.Call(car, "getInfo", Value.Int(PartsWorld.GiiType)).AsInt); // Part.getInfo(I) -> GameRef.getInfo(II)
        Assert.Equal(PartsWorld.GirCatPart, h.Call(car, "getInfo", Value.Int(PartsWorld.GiiCategory)).AsInt);
    }

    private sealed class FixedDyno : IDynoCalculator
    {
        public DynoCurve? Calculate(DynoParams dyno, float steps) => new(1, 1, dyno["maxRPM"], 123, 200, 4000, 6000);

        public float Torque(DynoParams dyno, float rpm, float boost) => rpm * dyno["RPM_limit"];

        public float Horsepower(DynoParams dyno, float rpm, float boost) => 0;
    }
}
