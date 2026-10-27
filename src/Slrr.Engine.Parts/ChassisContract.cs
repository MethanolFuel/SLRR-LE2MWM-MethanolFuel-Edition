using Slrr.Vm;

namespace Slrr.Engine.Parts;

/// <summary>
/// The 23 Chassis script fields whose exact names <c>StreetLegal_Redline.exe</c> pushes to the VM field accessors
/// (0x4484c4..0x448cd6, plus <c>maxHP</c> 0x46af53 and <c>dynodata</c> 0x447d54; docs/engine-api/java.game.parts.md).
/// The names are measured (string xrefs); when the engine reads them and their units are not (open question P1 #7).
/// Snapshotted by <c>Chassis.forceUpdate()</c>; Phase 4 physics consumes this record, never the script object.
/// </summary>
public sealed record ChassisParams
{
    /// <summary>Field names in the order of their first code xref.</summary>
    public static readonly IReadOnlyList<string> FieldNames =
    [
        "suspend_update", "engine_inertia", "starter_torque", "engine_friction_fwd", "engine_friction_rev", "engine_rpm_idle",
        "engine_mass", "ratio", "gears", "rearend_ratio", "maxRPM", "RPM_limit", "ClutchF", "SFX_trans_fwd", "SFX_trans_rev",
        "SFX_ignition", "rpm_trans_fwd", "rpm_trans_rev", "sfx_starter_rpm", "tank_nitro", "consumption_nitro", "maxHP", "dynodata",
    ];

    public int SuspendUpdate { get; init; }

    public float EngineInertia { get; init; }

    public float StarterTorque { get; init; }

    public float EngineFrictionFwd { get; init; }

    public float EngineFrictionRev { get; init; }

    public float EngineRpmIdle { get; init; }

    public float EngineMass { get; init; }

    /// <summary><c>float[] ratio</c> (Chassis fallback: {0, 3.5, 2.7, 1.9, 1.3, 0, 0, -4}; reverse index guessed to be 7).</summary>
    public IReadOnlyList<float> Ratio { get; init; } = [];

    public int Gears { get; init; }

    public float RearendRatio { get; init; }

    public float MaxRpm { get; init; }

    public float RpmLimit { get; init; }

    public float ClutchF { get; init; }

    public int SfxTransFwd { get; init; }

    public int SfxTransRev { get; init; }

    public int SfxIgnition { get; init; }

    public float RpmTransFwd { get; init; }

    public float RpmTransRev { get; init; }

    public float SfxStarterRpm { get; init; }

    public float TankNitro { get; init; }

    public float ConsumptionNitro { get; init; }

    public float MaxHp { get; init; }

    /// <summary>Snapshot of the <c>dynodata</c> object (null when the field is null).</summary>
    public DynoParams? Dyno { get; init; }

    /// <summary>Fields Chassis.updatevariables copies that have no exe xref of their own (read for convenience).</summary>
    public float EngineTorque { get; init; }

    public float EngineTorque2 { get; init; }

    /// <summary>Field names of the contract that the script object does not declare (should be empty for shipped cars).</summary>
    public IReadOnlyList<string> Missing { get; init; } = [];

    public static ChassisParams Read(VmObject chassis)
    {
        var missing = FieldNames.Where(n => !ScriptObjects.HasField(chassis, n)).ToList();
        var ratio = ScriptObjects.GetObject(chassis, "ratio") as VmArray;
        var dyno = ScriptObjects.GetObject(chassis, "dynodata");
        float F(string n) => ScriptObjects.GetFloat(chassis, n);
        int I(string n) => ScriptObjects.GetInt(chassis, n);
        return new ChassisParams
        {
            SuspendUpdate = I("suspend_update"),
            EngineInertia = F("engine_inertia"),
            StarterTorque = F("starter_torque"),
            EngineFrictionFwd = F("engine_friction_fwd"),
            EngineFrictionRev = F("engine_friction_rev"),
            EngineRpmIdle = F("engine_rpm_idle"),
            EngineMass = F("engine_mass"),
            Ratio = ratio is null ? [] : ratio.Items.Select(v => v.Kind is VmKind.Float or VmKind.Int ? v.AsFloat : 0f).ToArray(),
            Gears = I("gears"),
            RearendRatio = F("rearend_ratio"),
            MaxRpm = F("maxRPM"),
            RpmLimit = F("RPM_limit"),
            ClutchF = F("ClutchF"),
            SfxTransFwd = I("SFX_trans_fwd"),
            SfxTransRev = I("SFX_trans_rev"),
            SfxIgnition = I("SFX_ignition"),
            RpmTransFwd = F("rpm_trans_fwd"),
            RpmTransRev = F("rpm_trans_rev"),
            SfxStarterRpm = F("sfx_starter_rpm"),
            TankNitro = F("tank_nitro"),
            ConsumptionNitro = F("consumption_nitro"),
            MaxHp = F("maxHP"),
            Dyno = dyno is null ? null : DynoParams.Read(dyno),
            EngineTorque = F("engine_torque"),
            EngineTorque2 = F("engine_torque2"),
            Missing = missing,
        };
    }
}

/// <summary>
/// The 39 DynoData field names the exe reads by name (0x46aaa5..0x46b039, RPM_limit/maxRPM shared with Chassis).
/// Names measured; semantics guessed from the script defaults and Chassis.java.
/// </summary>
public sealed record DynoParams
{
    public static readonly IReadOnlyList<string> FieldNames =
    [
        "cylinders", "bore", "stroke", "Vmin", "in_min", "out_min", "in_max", "out_max", "time_in_open", "time_in_close",
        "time_out_open", "time_out_close", "time_spark_min", "time_spark_inc", "time_spark_RPM0", "time_spark_RPM1", "time_burn",
        "RPM_limit", "rpm_turbo_mul", "rpm_turbo_opt", "rpm_turbo_range", "P_turbo_max", "P_turbo_waste", "nitro_H", "nitro_cooling",
        "mixture_ratio", "mixture_H", "max_fuel_consumption", "max_air_consumption", "torque", "torque2", "maxRPM", "Displacement",
        "Compression", "maxTorque", "maxHP", "RPM_maxTorque", "RPM_maxHP", "flags",
    ];

    /// <summary>Every contract field by name (floats; <c>flags</c> as its int value).</summary>
    public required IReadOnlyDictionary<string, float> Values { get; init; }

    public int Flags { get; init; }

    public float this[string name] => Values.TryGetValue(name, out var v) ? v : 0f;

    public IReadOnlyList<string> Missing { get; init; } = [];

    public static DynoParams Read(VmObject dyno)
    {
        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var n in FieldNames)
        {
            values[n] = ScriptObjects.GetFloat(dyno, n);
        }

        return new DynoParams
        {
            Values = values,
            Flags = ScriptObjects.GetInt(dyno, "flags"),
            Missing = FieldNames.Where(n => !ScriptObjects.HasField(dyno, n)).ToList(),
        };
    }
}
