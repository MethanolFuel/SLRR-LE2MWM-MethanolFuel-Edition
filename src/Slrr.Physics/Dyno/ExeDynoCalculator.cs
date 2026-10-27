using Slrr.Engine.Parts;

namespace Slrr.Physics.Dyno;

/// <summary>
/// <see cref="IDynoCalculator"/> backed by <see cref="EngineCycleModel"/>: the field reader and driver of
/// <c>DynoData.calcDyno</c> (0x46aa20), <c>getTorque</c> (0x46b0f0) and <c>getHP</c> (0x46b210), MEASURED from the
/// disassembly. Plug it in with <c>new PartsWorld(dyno: new ExeDynoCalculator())</c> or <c>world.Dyno = ...</c>.
/// <para>The exe keeps the tables in the DynoData's native twin; the parts layer passes only a field snapshot, so the
/// last computed model is cached per distinct snapshot (<see cref="DynoParams.Values"/>). A lookup without a previous
/// calcDyno computes one with the Chassis step (maxRPM/250, GUESSED default).</para>
/// </summary>
public sealed class ExeDynoCalculator : IDynoCalculator
{
    private readonly Dictionary<string, EngineCycleModel> _cache = new(StringComparer.Ordinal);

    /// <summary>The model of the most recent <see cref="Calculate"/> (tables, maxima, displacement, compression).</summary>
    public EngineCycleModel? Last { get; private set; }

    public DynoCurve? Calculate(DynoParams dyno, float steps)
    {
        var m = Build(dyno, steps);
        _cache[Key(dyno)] = m;
        Last = m;
        var maxRpm = dyno["maxRPM"] > 0 ? dyno["maxRPM"] : m.MaxRpm;

        // With a native twin the exe does not touch `torque`/`torque2` (it writes 1.0 only for a temporary native,
        // 0x46ae11/0x46b033); they stay the script's multipliers.
        return new DynoCurve(dyno["torque"], dyno["torque2"], maxRpm, m.MaxHp, m.MaxTorque, m.RpmMaxTorque, m.RpmMaxHp);
    }

    public float Torque(DynoParams dyno, float rpm, float boost)
    {
        var m = Model(dyno);
        return EngineCycleModel.Lookup(m.TorqueTables[0], m.TableStep, rpm);
    }

    /// <summary>Net power in WATTS, as the exe (CarInfo multiplies the result by 0.001*1.341 to print hp).</summary>
    public float Horsepower(DynoParams dyno, float rpm, float boost)
    {
        var m = Model(dyno);
        return EngineCycleModel.Lookup(m.PowerTables[0], m.TableStep, rpm);
    }

    /// <summary>The cached model for a snapshot, computing it with the Chassis step (250 rpm) when needed.</summary>
    public EngineCycleModel Model(DynoParams dyno)
    {
        var key = Key(dyno);
        if (!_cache.TryGetValue(key, out var m))
        {
            var maxRpm = dyno["maxRPM"] > 0 ? dyno["maxRPM"] : 7000f;
            m = Build(dyno, MathF.Floor(maxRpm / 250f));
            _cache[key] = m;
        }

        return m;
    }

    /// <summary>The calcDyno routine 0x46aa20 on a fresh native (ctor 0x4b6670 runs at every call).</summary>
    public static EngineCycleModel Build(DynoParams d, float steps)
    {
        var m = new EngineCycleModel
        {
            Cylinders = d["cylinders"],
            BoreRadius = d["bore"] * 0.5f,
            CrankRadius = d["stroke"] * 0.5f,
            Vmin = d["Vmin"],
            InMin = d["in_min"],
            InMax = d["in_max"],
            OutMin = d["out_min"],
            OutMax = d["out_max"],
            InOpen = d["time_in_open"],
            InClose = d["time_in_close"],
            OutOpen = d["time_out_open"],
            OutClose = d["time_out_close"],
            TimeBurn = d["time_burn"],
            RpmLimit = d["RPM_limit"],
            SparkMin = d["time_spark_min"],
            SparkInc = d["time_spark_inc"],
            SparkRpm0 = d["time_spark_RPM0"],
            SparkRpm1 = d["time_spark_RPM1"],
            TurboMul = d["rpm_turbo_mul"],
            TurboOpt = d["rpm_turbo_opt"],
            TurboRange = d["rpm_turbo_range"],
            TurboDead = 0f,
            TurboMax = d["P_turbo_max"],
            MixtureH = d["mixture_H"],
            MixtureRatio = d["mixture_ratio"],
            MaxAirConsumption = d["max_air_consumption"],
            MaxFuelConsumption = d["max_fuel_consumption"],
        };
        m.InOpenBase = m.InOpen;
        m.OutOpenBase = m.OutOpen;
        m.InDuration = m.InClose - m.InOpen;
        m.OutDuration = m.OutClose - m.OutOpen;

        // P_turbo_waste is read only when the field exists, else = P_turbo_max (0x42a800 field test at 0x46aca8).
        m.TurboWaste = d.Values.ContainsKey("P_turbo_waste") && !d.Missing.Contains("P_turbo_waste") ? d["P_turbo_waste"] : m.TurboMax;

        // `intercooling` (optional field, 0x46ad37) is not a DynoData field of the shipped class: never present.
        m.IntakeTemperature = 293.1499938964844f;
        m.UpdateMixture();
        m.UpdateGeometry();
        if (d["maxRPM"] > 0)
        {
            m.MaxRpm = d["maxRPM"];
        }

        var stepSize = steps > 0 ? m.MaxRpm / steps : float.PositiveInfinity;
        if (!(stepSize > 1))
        {
            stepSize = 1;
        }

        var step = float.IsFinite(stepSize) ? (int)stepSize : int.MaxValue;
        m.BuildTable(step, 0);

        // nitro table (0x46af76..0x46b0ac): only when nitro_H or nitro_cooling exceed 1 by more than 1e-4.
        var nitroH = d["nitro_H"];
        var nitroCooling = d["nitro_cooling"];
        if (nitroH - 1f > 0.0001f || nitroCooling - 1f > 0.0001f)
        {
            var maxRpm = m.MaxRpm;
            m.IntakeTemperature = 293.1499938964844f * nitroCooling;
            m.MixtureH *= nitroH;
            m.UpdateMixture();
            m.UpdateGeometry();
            m.MaxRpm = maxRpm;
            var maxima = (m.MaxHp, m.MaxTorque, m.RpmMaxHp, m.RpmMaxTorque);
            m.BuildTable(step, 1);
            (m.MaxHp, m.MaxTorque, m.RpmMaxHp, m.RpmMaxTorque) = maxima; // the exe wrote the script maxima before this
        }

        return m;
    }

    private static string Key(DynoParams d) =>
        string.Join(";", DynoParams.FieldNames.Select(n => BitConverter.SingleToInt32Bits(d[n]).ToString(System.Globalization.CultureInfo.InvariantCulture)));
}
