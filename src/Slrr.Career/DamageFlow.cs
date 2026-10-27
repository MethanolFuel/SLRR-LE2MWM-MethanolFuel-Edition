using System.Globalization;
using System.Numerics;
using Slrr.Game;
using Slrr.Vm;

namespace Slrr.Career;

/// <summary>
/// Collision and damage end to end: the garage repair quote before (Mechanic "Repair all the car" → RepairAllDialog, the
/// script's own <c>repairCost()</c>), a crash in Valo City (physics impacts → <see cref="CollisionDamageBridge"/> →
/// EVENT_COLLISION to the car → City.handleEvent sets <c>collision</c>/<c>lastCollisionTime</c>; parts lose tear), back
/// to the garage and the repair quote after.
/// </summary>
public static class DamageFlow
{
    public static FlowResult Run(CareerSession s, CityOptions? o = null, float crashSeconds = 8f)
    {
        o ??= new CityOptions();
        var r = new FlowResult("damage");
        if (!CareerFlows.BootAndBuy(s, r))
        {
            return r;
        }

        var quote0 = RepairQuote(s, r, "before");
        var (n0, tear0) = CollisionDamageBridge.BodyTear(s.Host, s.Car);
        var cond0 = CollisionDamageBridge.Condition(s.Host, s.Car);
        r.Num("body-parts", $"{n0}");
        r.Num("body-tear-sum-before", $"{tear0:F3}");
        r.Num("condition-before", $"{cond0:F4}");
        if (!CityFlows.EnterCity(s, r, o))
        {
            return r;
        }

        var city = s.ActiveSection!;
        var records0 = s.Damage.Records.Count;
        // full throttle the wrong way down the street from the free-ride start (it ends in a wall, MEASURED below)
        var t = s.Race.PlayerTelemetry!;
        s.Driver.Place(t.Position, (o.PlayerYaw + MathF.PI) % (2 * MathF.PI));
        s.Host.RunFor(1f);
        s.Driver.Hold(StandInDriver.AxisThrottle, 1f);
        var maxKmh = 0f;
        var end = s.Host.Vm.Time + crashSeconds;
        while (s.Host.Vm.Time < end)
        {
            s.Host.RunFrame();
            maxKmh = Math.Max(maxKmh, s.Race.PlayerTelemetry?.SpeedKmh ?? 0);
        }

        s.Driver.Release();
        s.Host.RunFor(1.5f);
        var recs = s.Damage.Records.Skip(records0).ToList();
        r.Num("crash-max-kmh", $"{maxKmh:F1}");
        r.Num("collision-events", $"{recs.Count}");
        r.Num("strongest-impact-dv", $"{(recs.Count == 0 ? 0 : recs.Max(x => x.DeltaV)):F2}");
        r.Num("tear-removed", $"{recs.Sum(x => x.TearRemoved):F3}");
        var scriptCollision = Probe.Field(city, "collision").AsInt;
        var lastCollision = Probe.Field(city, "lastCollisionTime").AsFloat;
        r.Num("city.collision", $"{scriptCollision}");
        r.Num("city.lastCollisionTime", $"{lastCollision:F2}");
        if (recs.Count == 0)
        {
            r.Block(s, $"no impact in {crashSeconds} s (max {maxKmh:F0} km/h)");
            return r;
        }

        r.Mark(s, "impacts", string.Join("; ", recs.Take(5).Select(x => string.Create(CultureInfo.InvariantCulture,
            $"t {x.Time:F2} car #{x.CarId:X} other #{x.OtherId:X} dv {x.DeltaV:F2} m/s parts {x.PartsDamaged} tear -{x.TearRemoved:F3}"))));
        if (scriptCollision == 1)
        {
            r.Mark(s, "script-collision", string.Create(CultureInfo.InvariantCulture, $"City.collision = 1, lastCollisionTime {lastCollision:F2} s (EVENT_COLLISION handled by City.handleEvent)"));
        }
        else
        {
            r.Block(s, "City.handleEvent did not see EVENT_COLLISION (City.collision still 0)");
        }

        var (n1, tear1) = CollisionDamageBridge.BodyTear(s.Host, s.Car);
        var cond1 = CollisionDamageBridge.Condition(s.Host, s.Car);
        r.Num("body-tear-sum-after", $"{tear1:F3}");
        r.Num("condition-after", $"{cond1:F4}");
        r.Mark(s, "damage-in-script-state", string.Create(CultureInfo.InvariantCulture,
            $"body tear sum {tear0:F3} -> {tear1:F3} over {n1} parts, chassis getCondition() {cond0:F4} -> {cond1:F4}"));
        CityFlows.ReturnToGarage(s, r);
        var quote1 = RepairQuote(s, r, "after");
        if (quote0 is { } a && quote1 is { } b)
        {
            r.Num("repair-quote", $"${a} -> ${b}");
            if (b > a)
            {
                r.Mark(s, "repair-screen-shows-damage", $"RepairAllDialog ${a} -> ${b}");
                r.Completed = true;
            }
            else
            {
                r.Block(s, $"repair quote did not rise (${a} -> ${b})");
            }
        }

        return r;
    }

    /// <summary>Garage: "Install/Remove parts" (mechanic mode) → "Repair all the car" → the dialog's cost → NO.</summary>
    public static int? RepairQuote(CareerSession s, FlowResult r, string label)
    {
        if (s.Section != "Garage")
        {
            return null;
        }

        s.Click("Install/Remove parts");
        s.Host.RunFor(1f);
        if (!s.Click("Repair all the car"))
        {
            r.Block(s, "Mechanic 'Repair all the car' not found");
            return null;
        }

        s.WaitFor(() => s.DialogOnTop, 5, "repair dialog");
        var text = string.Join(" ", s.TopTexts()).Replace('\n', ' ');
        s.Step($"repair dialog ({label}): {text}");
        int? cost = null;
        var at = text.IndexOf('$');
        if (at >= 0)
        {
            var digits = new string(text[(at + 1)..].TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c))
            {
                cost = c;
            }
        }

        r.Mark(s, $"repair-quote-{label}", text);
        if (!s.Host.Input.Click("NO"))
        {
            s.Dismiss(1);
        }

        s.Host.RunFor(0.5f);
        s.Host.Input.TapAxis(35); // leave the mechanic mode
        s.Host.RunFor(1f);
        return cost;
    }
}
