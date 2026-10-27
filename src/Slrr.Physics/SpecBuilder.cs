using System.Globalization;
using Slrr.Engine.Parts;
using Slrr.Engine.Parts.RawEdit;
using Slrr.Formats.Cfg;
using Slrr.Physics.Dyno;

namespace Slrr.Physics;

/// <summary>Knobs that are not in the data (all GUESSED) plus the RawEdit global multipliers.</summary>
public sealed class SpecOptions
{
    /// <summary>Simulated engine inertia = script <c>engine_inertia</c> * this. The exe multiplies by
    /// <c>engine_inertia_factor</c> (4 Redline / 5 GI) somewhere in the drivetrain step (0x4558de) but the units of the
    /// result were not traced; 0.025 turns the stock values (~7.6) into ~0.2 kg m^2 (GUESSED for plausibility).</summary>
    public double EngineInertiaScale { get; set; } = 0.025;

    /// <summary>Tyre multipliers of Slrr_GI (F_WheelFrictionMult 0.8, F_WheelStiffnessMult 0.7, F_WheelMaxLoadMult 0.8,
    /// F_WheelFriction_XMult 1, MEASURED file values); where the GI cave applies them is not traced. Null = Redline (none).</summary>
    public EngineSettings? Settings { get; set; }

    /// <summary>Centre of mass from the chassis bodies plus every installed part's bodies at their slot positions
    /// (GUESSED, off by default: the slot frames do not place the parts yet; on the SunStrip the engine block lands at
    /// z = -0.05 and the rims at the chassis origin, so the slot semantics are not understood).</summary>
    public bool PartsInCenterOfMass { get; set; }

    /// <summary>Body height used for the inertia box and frontal area when the cfg gives no usable bounds (GUESSED).</summary>
    public double BodyHeight { get; set; } = 1.3;
}

/// <summary>
/// Builds a <see cref="VehicleSpec"/> from the parts layer after <c>updatevariables()</c> + <c>forceUpdate()</c>:
/// the 23-field <see cref="ChassisParams"/> snapshot, every <see cref="WheelState"/>, the rollbars, the cfg wheel
/// lines and body masses, and the dyno tables of <see cref="ExeDynoCalculator"/>.
/// </summary>
public static class SpecBuilder
{
    public static VehicleSpec Build(PartState chassis, ExeDynoCalculator dyno, SpecOptions? options = null)
    {
        options ??= new SpecOptions();
        var cs = chassis.Chassis ?? throw new InvalidOperationException($"{chassis} is not a chassis");
        var spec = new VehicleSpec { Name = chassis.Object.Class.Name };
        var def = chassis.Definition;

        // Mass: chassis cfg bodies + the cfg bodies of every installed part (MEASURED data; summing = GUESSED).
        var parts = PartsWorld.Tree(chassis).ToList();
        var partsMass = parts.Where(p => p != chassis).Sum(p => (double)(p.Definition?.Mass ?? 0f));
        spec.Mass = cs.Mass + partsMass;
        spec.Notes.Add(FormattableString.Invariant($"mass {spec.Mass:F1} kg = chassis cfg bodies {cs.Mass:F1} + {parts.Count - 1} part cfg bodies {partsMass:F1} (sum GUESSED)"));
        var cm = cs.CenterOfMass;
        spec.CenterOfMass = new Vec3(cm.X, cm.Y, cm.Z);

        var maxSteer = CfgFloat(def?.Source, "maxsteer") ?? 0.6;
        var wheelCfg = def?.Wheels ?? [];
        var p = cs.Params;
        for (var i = 0; i < cs.Wheels.Count; i++)
        {
            var ws = cs.Wheels[i];
            var wc = i < wheelCfg.Count ? wheelCfg[i] : null;
            var w = new WheelSpec { Name = i switch { 0 => "FL", 1 => "FR", 2 => "RL", 3 => "RR", _ => $"W{i}" } };
            var pos = wc?.Position ?? ws.Position;
            w.Position = new Vec3(pos.X, pos.Y, pos.Z);
            w.Radius = ws.Radius > 0 ? ws.Radius : wc?.Radius > 0 ? wc.Radius : 0.3;
            w.Width = ws.Width > 0 && ws.Width < 1 ? ws.Width : 0.2;
            w.Mass = wc?.Mass > 0 ? wc.Mass : 10;
            w.Steers = wc is not null && wc.Flags.Length > 0 ? wc.Flags[0] != 0 : i < 2;
            w.MaxSteer = w.Steers ? maxSteer : 0;
            w.Drive = ws.Drive;
            w.BrakeTorque = ws.Brake;
            w.HandBrakeTorque = ws.HandBrake;
            if (ws.Force > 0 && ws.RestLength > 0)
            {
                w.SpringRate = ws.Force;
                w.RestLength = ws.RestLength;
                w.MinLength = ws.MinLength;
                w.MaxLength = ws.MaxLength > ws.MinLength ? ws.MaxLength : ws.RestLength;
            }

            if (ws.DampingBump > 0)
            {
                w.DampingBump = ws.DampingBump;
                w.DampingRebound = ws.DampingRebound > 0 ? ws.DampingRebound : ws.DampingBump * 0.7;
            }

            w.Tyre = TyreFrom(ws, options.Settings);
            spec.Wheels.Add(w);
        }

        var driveSum = spec.Wheels.Sum(w => w.Drive);
        if (driveSum > 0)
        {
            foreach (var w in spec.Wheels)
            {
                w.Drive /= driveSum; // per-axle fractions in the scripts (both rear wheels 1.0): normalised (GUESSED)
            }
        }
        else
        {
            spec.Notes.Add("no WheelRef.setDrive: rear wheels driven (fallback)");
            foreach (var w in spec.Wheels.Where(w => !w.Steers))
            {
                w.Drive = 1.0 / Math.Max(1, spec.Wheels.Count(x => !x.Steers));
            }
        }

        if (cs.CenterOfMassOverride is null && options.PartsInCenterOfMass
            && PartsCenterOfMass(chassis, out var withParts, out var placed, spec.Wheels.Select(w => w.Position).ToList()))
        {
            spec.Notes.Add(FormattableString.Invariant(
                $"centre of mass {withParts} from the chassis bodies and {placed} placed part bodies (chassis bodies alone: {spec.CenterOfMass}; GUESSED placement through the slot frames)"));
            spec.CenterOfMass = withParts;
        }

        foreach (var ((w1, w2), (force, damping)) in cs.Rollbars.OrderBy(r => r.Key.Wheel1).ThenBy(r => r.Key.Wheel2))
        {
            spec.Rollbars.Add(new RollbarSpec(w1, w2, force, damping));
        }

        if (def?.Source is { } cfgSource)
        {
            spec.Body.AddRange(ChassisShape.Read(cfgSource, spec.Notes));
        }

        // Body box: wheel extents (GUESSED proportions) for inertia and frontal area.
        if (spec.Wheels.Count > 0)
        {
            var track = spec.Wheels.Max(w => w.Position.X) - spec.Wheels.Min(w => w.Position.X);
            var wheelbase = spec.Wheels.Max(w => w.Position.Z) - spec.Wheels.Min(w => w.Position.Z);
            double width = track + 0.25, length = wheelbase * 1.65, height = options.BodyHeight;
            spec.Inertia = new Vec3(spec.Mass / 12 * (height * height + length * length), spec.Mass / 12 * (width * width + length * length), spec.Mass / 12 * (width * width + height * height));
            spec.FrontalArea = 0.85 * width * height;
        }

        // C_drag: Chassis.updatevariables computes fully_stripped_drag (0.333) minus every body part's drag_reduction,
        // which leaves ~0.015 on a complete stock car: whatever the exe does with it (field read at 0x448cd6, per-part
        // drag_own/drag_occluded too), it is not a plain Cd. Used as Cd only when it looks like one (GUESSED).
        var cDrag = ScriptObjects.HasField(chassis.Object, "C_drag") ? ScriptObjects.GetFloat(chassis.Object, "C_drag") : 0f;
        spec.DragCoefficient = cDrag is >= 0.1f and <= 1.5f ? cDrag : 0.32;
        spec.Notes.Add(FormattableString.Invariant($"script C_drag {cDrag:F3} -> Cd {spec.DragCoefficient:F3}{(cDrag is >= 0.1f and <= 1.5f ? "" : " (Chassis field default 0.32 used, GUESSED)")}"));
        if (options.Settings is { } s)
        {
            spec.Gravity = Math.Abs(s.Physics.Gravity);
        }

        if (p is not null)
        {
            var ratios = p.Ratio.Count >= 8 ? p.Ratio.Select(x => (double)x).ToArray() : spec.Drivetrain.Ratios;
            spec.Drivetrain = new DrivetrainSpec
            {
                Ratios = ratios,
                Gears = p.Gears > 0 ? p.Gears : ratios.Skip(1).Take(6).Count(r => r > 0),
                FinalDrive = p.RearendRatio > 0 ? p.RearendRatio : 3.5,
                ClutchCapacity = p.ClutchF > 0 ? p.ClutchF : 500,
            };
            spec.Engine = new EngineSpec
            {
                IdleRpm = p.EngineRpmIdle > 0 ? p.EngineRpmIdle : 800,
                RpmLimit = p.RpmLimit > 0 ? p.RpmLimit : p.MaxRpm,
                MaxRpm = p.MaxRpm,
                ScriptInertia = p.EngineInertia,
                Inertia = Math.Max(0.02, p.EngineInertia * options.EngineInertiaScale),
                FrictionFwd = p.EngineFrictionFwd,
                FrictionRev = p.EngineFrictionRev,
            };
            spec.ScriptMaxHp = p.MaxHp;
            if (p.Dyno is { } d && d["cylinders"] > 0)
            {
                var m = dyno.Model(d);
                spec.Engine.Torque = Sanitize(m.TorqueTables[0], spec, "torque table");
                spec.Engine.NitroTorque = m.TorqueTables[1] is { } nitro ? Sanitize(nitro, spec, "nitro table") : null;
                spec.Engine.TableStep = m.TableStep;
                spec.DynoMaxTorque = m.MaxTorque;
                spec.DynoRpmMaxTorque = m.RpmMaxTorque;
                spec.DynoRpmMaxHp = m.RpmMaxHp;
            }
            else
            {
                spec.Notes.Add("no dynodata: engine produces no torque");
            }
        }
        else
        {
            spec.Notes.Add("no forceUpdate snapshot: Chassis fallback drivetrain and no engine torque");
        }

        if (spec.Engine.RpmLimit <= 0)
        {
            spec.Engine.RpmLimit = 7000;
        }

        if (spec.Engine.MaxRpm <= 0)
        {
            spec.Engine.MaxRpm = spec.Engine.RpmLimit;
        }

        return spec;
    }

    /// <summary>
    /// Every part's frame in the chassis frame: a child's mount slot is put onto the parent's slot it links to
    /// (child = parentFrame * parentSlot * inverse(childMountSlot)), slot transforms = Ypr rotation then position
    /// (GUESSED convention; the same <c>CreateFromYawPitchRoll</c> the map objects use, docs/phase4/world.md). Chassis
    /// slots 101..104 (rims), 111..114 (brakes), 121..124 (suspension), 301..304 (shocks) and 311..314 (springs) hold
    /// positions near zero (MEASURED on the SunStrip: e.g. slot 101 at (-0.12, 0, 0), slot 111 at (-0.113, 0, 0)), so they
    /// are taken as wheel-local and rooted at wheel (id % 10 - 1)'s cfg centre (GUESSED, <paramref name="wheelCenters"/>).
    /// </summary>
    public static Dictionary<PartState, System.Numerics.Matrix4x4> PartFrames(PartState chassis, IReadOnlyList<Vec3>? wheelCenters = null)
    {
        var frames = new Dictionary<PartState, System.Numerics.Matrix4x4>(ReferenceEqualityComparer.Instance) { [chassis] = System.Numerics.Matrix4x4.Identity };
        var queue = new Queue<PartState>();
        queue.Enqueue(chassis);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            var pf = frames[parent];
            foreach (var (slot, child) in parent.Children)
            {
                if (frames.ContainsKey(child))
                {
                    continue;
                }

                var mount = child.MountSlot;
                var childSlot = mount is null ? System.Numerics.Matrix4x4.Identity : SlotMatrix(mount.Position, mount.Orientation);
                System.Numerics.Matrix4x4.Invert(childSlot, out var inv);
                var parentFrame = pf;
                if (parent == chassis && wheelCenters is not null && WheelOfSlot(slot.Id) is { } wi && wi < wheelCenters.Count)
                {
                    var c = wheelCenters[wi];
                    parentFrame = System.Numerics.Matrix4x4.CreateTranslation((float)c.X, (float)c.Y, (float)c.Z) * pf;
                }

                frames[child] = inv * SlotMatrix(slot.Position, slot.Orientation) * parentFrame;
                queue.Enqueue(child);
            }
        }

        return frames;
    }

    /// <summary>Wheel index of a wheel-local chassis slot id (101..104, 111..114, 121..124, 301..304, 311..314), else null.</summary>
    public static int? WheelOfSlot(int id) =>
        id is >= 101 and <= 104 or >= 111 and <= 114 or >= 121 and <= 124 or >= 301 and <= 304 or >= 311 and <= 314 ? id % 10 - 1 : null;

    private static System.Numerics.Matrix4x4 SlotMatrix(System.Numerics.Vector3 pos, Ypr o) =>
        System.Numerics.Matrix4x4.CreateFromYawPitchRoll(o.Y, o.P, o.R) * System.Numerics.Matrix4x4.CreateTranslation(pos);

    /// <summary>Mass-weighted centre of the chassis cfg bodies and every placed part's cfg bodies (chassis frame).</summary>
    public static bool PartsCenterOfMass(PartState chassis, out Vec3 center, out int placedBodies, IReadOnlyList<Vec3>? wheelCenters = null)
    {
        center = default;
        placedBodies = 0;
        var frames = PartFrames(chassis, wheelCenters);
        double m = 0, x = 0, y = 0, z = 0;
        foreach (var (part, frame) in frames)
        {
            foreach (var b in part.Definition?.Bodies ?? [])
            {
                if (!(b.Mass > 0))
                {
                    continue;
                }

                var p = System.Numerics.Vector3.Transform(b.Position, frame);
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                {
                    continue;
                }

                m += b.Mass;
                x += b.Mass * p.X;
                y += b.Mass * p.Y;
                z += b.Mass * p.Z;
                if (part != chassis)
                {
                    placedBodies++;
                }
            }
        }

        if (m <= 0 || placedBodies == 0)
        {
            return false;
        }

        center = new Vec3(x / m, y / m, z / m);
        return true;
    }

    /// <summary>The 17 tyre coefficients as the exe holds them (named setters share the array; see <see cref="TyreParams"/>).</summary>
    public static TyreParams TyreFrom(WheelState ws, EngineSettings? settings = null)
    {
        var t = new TyreParams { RollResistance = ws.RollResistance, Bearing = ws.Bearing };
        for (var i = 0; i < WheelState.PacejkaCount; i++)
        {
            t.P[i] = ws.Pacejka[i];
        }

        // The parts layer keeps the named setters apart and drops indices 15/16 (it bounds i to 0..14; the exe accepts
        // 0..16). Named setters run after setPacejka in Tyre.java and Wheel.java, so they win here.
        if (ws.Sliction != 0) t.P[0] = ws.Sliction;
        if (ws.Friction != 0) t.P[2] = ws.Friction;
        if (ws.Stiffness != 0) t.P[4] = ws.Stiffness;
        if (ws.MaxLoad != 0) t.P[11] = ws.MaxLoad;
        if (ws.FrictionX != 0) t.P[12] = ws.FrictionX;
        if (ws.LoadSmooth != 0 || ws.Writes > 0) t.P[14] = ws.LoadSmooth;
        if (t.P[0] == 0 && t.P[2] == 0 && t.P[4] == 0)
        {
            return TyreParams.WheelDefaults(); // corner never written by a script
        }

        if (settings is not null)
        {
            t.P[2] *= settings.Physics.WheelFrictionMult;
            t.P[4] *= settings.Physics.WheelStiffnessMult;
            t.P[11] *= settings.Physics.WheelMaxLoadMult;
            t.P[12] *= settings.Physics.WheelFrictionLateralMult;
        }

        return t;
    }

    private static float[] Sanitize(float[]? table, VehicleSpec spec, string what)
    {
        if (table is null)
        {
            return [];
        }

        var bad = table.Count(t => !float.IsFinite(t));
        if (bad > 0)
        {
            spec.Notes.Add($"{what}: {bad} non-finite entries (broken dyno inputs) replaced by 0");
        }

        return table.Select(t => float.IsFinite(t) ? t : 0f).ToArray();
    }

    private static double? CfgFloat(string? cfgPath, string keyword)
    {
        if (cfgPath is null || !File.Exists(cfgPath))
        {
            return null;
        }

        var line = CfgDocument.Load(cfgPath).Directives.FirstOrDefault(l => string.Equals(l.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
        return line is { Args.Count: > 0 } && double.TryParse(line.Args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
