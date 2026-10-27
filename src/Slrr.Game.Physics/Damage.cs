using Slrr.Physics;

namespace Slrr.Game.Physics;

/// <summary>
/// Turns impact energy into damage d (0 = intact .. 1 = wrecked). The EFFECT of d is MEASURED (the exe scales tyre
/// friction, brake torque and engine torque by 1 - 0.6 d^2, 0.1 from d = 1: setFriction 0x440fcc, setBrake 0x4415ae,
/// engine 0x4486fb; <see cref="TyreModel.DamageFactor"/>). How the exe turns a collision into d was NOT traced: the
/// accumulation below is GUESSED. Config's damage knobs exist (script defaults external_damage 0.25, internal_damage 1.0,
/// player_damage_multiplier 0.8, deformation 1.6; docs/engine-api/java.util.md) but their use in the exe is unknown, so
/// only <see cref="InternalDamage"/> (default 1.0, the Config value) scales the result here.
/// </summary>
public sealed class DamageModel
{
    /// <summary>Closing energy per kg below which an impact does no damage [J/kg] (2 J/kg = 2 m/s head-on, GUESSED).</summary>
    public double ThresholdPerKg { get; set; } = 2.0;

    /// <summary>Closing energy per kg that adds d = 1 [J/kg] (400 J/kg = 28 m/s, ~100 km/h into a wall, GUESSED).</summary>
    public double FullDamagePerKg { get; set; } = 400.0;

    /// <summary>Config.internal_damage (script default 1.0).</summary>
    public double InternalDamage { get; set; } = 1.0;

    /// <summary>Impacts within this distance of a wheel centre also damage that corner, twice as fast (GUESSED) [m].</summary>
    public double WheelReach { get; set; } = 1.2;

    /// <summary>Damage added by an impact of <paramref name="energy"/> joules on <paramref name="car"/>; applies it.</summary>
    public double Apply(SimCar car, Vec3 point, double energy)
    {
        if (!(energy > 0) || car.Mass <= 0)
        {
            return 0;
        }

        var perKg = energy / car.Mass;
        if (perKg <= ThresholdPerKg)
        {
            return 0;
        }

        var added = (perKg - ThresholdPerKg) / FullDamagePerKg * InternalDamage * car.DamageMultiplier;
        if (!(added > 0))
        {
            return 0;
        }

        car.AddDamage(point, added, WheelReach);
        return added;
    }
}
