using Slrr.Engine.Resources;
using Slrr.Vm;

namespace Slrr.Game.Physics;

/// <summary>
/// The three <c>java.game.Vehicle</c> natives no other area registers (docs/phase4/game.md "Native coverage").
/// Pass it in <c>GameHostOptions.ExtraModules</c> next to the <see cref="GamePhysicsWorld"/> given as <c>Physics</c>.
/// </summary>
public sealed class PhysicsNatives(GamePhysicsWorld physics) : INativeModule
{
    public const string VehicleClass = "java.game.Vehicle";

    public void Register(NativeRegistry r)
    {
        // MEASURED unit: m²/s². CarInfo prints sqrt(maxTestTrackSpeedSq) * 2.24 as MPH (m/s -> mph) and
        // * 2.24 * 1.61 as KPH; TestTrack's "More speed!" thresholds 126.5 / 193.6 are 40.5 / 50.1 km/h in m/s².
        // An unbound or held car answers its last simulated speed (0 before the first step).
        r.Register(VehicleClass, "getSpeedSquare", "()F", c => Value.Float(Car(c)?.Telemetry?.SpeedSquare ?? 0f), replace: true);

        // Non-zero while the horn sounds (City reacts to it): the driver's AXIS_HORN or `sethorn 1` (GUESSED source).
        r.Register(VehicleClass, "getHorn", "()I", c => Value.Int(Car(c)?.Controls.Horn == true ? 1 : 0), replace: true);

        // Crime level for the police logic: no traffic-law model here (GUESSED 0 = never wanted).
        r.Register(VehicleClass, "hasCrime", "()F", _ => Value.Float(0f), replace: true);
    }

    private SimCar? Car(NativeCall c)
    {
        var id = ResourceEngine.IdOf(c.This);
        return physics.Scene.Car(id) ?? physics.Bind(id);
    }
}
