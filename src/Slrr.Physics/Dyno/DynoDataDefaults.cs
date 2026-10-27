using Slrr.Engine.Parts;

namespace Slrr.Physics.Dyno;

/// <summary>
/// The field initializers of <c>parts/scripts/DynoData.class</c> (MEASURED from the class file): what a fresh
/// <c>new DynoData()</c> holds before any engine part fills it.
/// </summary>
public static class DynoDataDefaults
{
    public static IReadOnlyDictionary<string, float> Values { get; } = new Dictionary<string, float>(StringComparer.Ordinal)
    {
        ["cylinders"] = 4f, ["bore"] = 0.082f, ["stroke"] = 0.09f, ["Vmin"] = 5e-05f,
        ["in_min"] = 0.005f, ["out_min"] = 0.005f, ["in_max"] = 0.8f, ["out_max"] = 0.8f,
        ["time_in_open"] = 0.001f, ["time_in_close"] = 0.25f, ["time_out_open"] = 0.75f, ["time_out_close"] = 0.99f,
        ["time_spark_min"] = 0.48f, ["time_spark_inc"] = -0.05f, ["time_spark_RPM0"] = 2000f, ["time_spark_RPM1"] = 5000f,
        ["time_burn"] = 0.003f, ["RPM_limit"] = 7000f, ["rpm_turbo_mul"] = 16f, ["rpm_turbo_opt"] = 85000f,
        ["rpm_turbo_range"] = 20000f, ["P_turbo_max"] = 0f, ["P_turbo_waste"] = 10f, ["nitro_H"] = 1f, ["nitro_cooling"] = 1f,
        ["mixture_ratio"] = 14f, ["mixture_H"] = 1_000_000f, ["max_fuel_consumption"] = 0f, ["max_air_consumption"] = 0f,
        ["torque"] = 1f, ["torque2"] = 1f, ["maxRPM"] = 7000f, ["Displacement"] = 0f, ["Compression"] = 0f,
        ["maxTorque"] = 0f, ["maxHP"] = 0f, ["RPM_maxTorque"] = 0f, ["RPM_maxHP"] = 0f, ["flags"] = 0f,
    };

    /// <summary>A snapshot with the defaults, optionally overridden.</summary>
    public static DynoParams Create(IReadOnlyDictionary<string, float>? overrides = null)
    {
        var v = new Dictionary<string, float>(Values, StringComparer.Ordinal);
        if (overrides is not null)
        {
            foreach (var (k, x) in overrides)
            {
                v[k] = x;
            }
        }

        return new DynoParams { Values = v };
    }
}
