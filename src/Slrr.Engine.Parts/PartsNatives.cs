using Slrr.Vm;

namespace Slrr.Engine.Parts;

/// <summary>
/// Natives of <c>java.game.parts.{Part, WheelRef, SfxTable, DynoData}</c>, <c>java.game.parts.bodypart.Chassis</c> and
/// <c>java.lang.RawEdit</c>, backed by one <see cref="PartsWorld"/>. Descriptors are the exe registrations
/// (docs/research/natives-redline.tsv, natives-slrr_gi-extra.tsv) plus two declared-only ones
/// (<c>Chassis.setBuck(IIFFFF)V</c>, <c>Chassis.getIndexedAddr(I)I</c>). Semantics: docs/engine/parts.md.
/// </summary>
public sealed class PartsNatives : INativeModule
{
    public const string Part = PartsWorld.PartClass;
    public const string Chassis = PartsWorld.ChassisClass;
    public const string WheelRef = PartsWorld.WheelRefClass;
    public const string SfxTable = PartsWorld.SfxTableClass;
    public const string DynoData = PartsWorld.DynoDataClass;
    public const string RawEdit = "java.lang.RawEdit";

    private const string V3 = "Ljava.lang.Vector3;";
    private const string YprD = "Ljava.lang.Ypr;";
    private const string Str = "Ljava.lang.String;";
    private const string ResRef = "Ljava.util.resource.ResourceRef;";
    private const string GameRefD = "Ljava.util.resource.GameRef;";

    public PartsNatives(PartsWorld? world = null) => World = world ?? new PartsWorld();

    public PartsWorld World { get; }

    /// <summary>Registered keys, for coverage reports.</summary>
    public List<NativeKey> Registered { get; } = [];

    public void Register(NativeRegistry r)
    {
        RegisterPart(r);
        RegisterChassis(r);
        RegisterWheel(r);
        RegisterSfxAndDyno(r);
        RegisterRawEdit(r);
    }

    private void Add(NativeRegistry r, string cls, string name, string desc, NativeMethod fn)
    {
        r.Register(cls, name, desc, fn);
        Registered.Add(new NativeKey(cls, name, desc));
    }

    private PartState P(NativeCall c) => World.Get(c.Self);

    private SlotState? SlotById(NativeCall c, int id) => P(c).Slot(id);

    // ---------------------------------------------------------------- Part

    private void RegisterPart(NativeRegistry r)
    {
        // Condition. Setters return the previous value (guess; no shipped script uses the result).
        Add(r, Part, "getWear", "()F", c => Value.Float(P(c).Wear));
        Add(r, Part, "setWear", "(F)F", c => Swap(P(c), c.Float(0), static p => p.Wear, static (p, v) => p.Wear = v));
        Add(r, Part, "getTear", "()F", c => Value.Float(P(c).Tear));
        Add(r, Part, "setTear", "(F)F", c => Swap(P(c), c.Float(0), static p => p.Tear, static (p, v) => p.Tear = v));
        Add(r, Part, "setMaxWear", "(F)V", c =>
        {
            P(c).MaxWear = c.Float(0);
            return Value.Void;
        });

        // Appearance (resource ids are opaque ints here).
        Add(r, Part, "getTexture", "()I", c => Value.Int(P(c).Texture));
        Add(r, Part, "setTexture", "(I)I", c => SwapI(P(c), c.Int(0), static p => p.Texture, static (p, v) => p.Texture = v));
        Add(r, Part, "getMesh", "()I", c => Value.Int(P(c).Mesh));
        Add(r, Part, "setMesh", "(I)I", c => SwapI(P(c), c.Int(0), static p => p.Mesh, static (p, v) => p.Mesh = v));
        Add(r, Part, "getRenderType", "()I", c => Value.Int(P(c).RenderType));
        Add(r, Part, "setRenderType", "(I)I", c => SwapI(P(c), c.Int(0), static p => p.RenderType, static (p, v) => p.RenderType = v));
        Add(r, Part, "flap", "(I)I", c =>
        {
            var p = P(c);
            if (c.Int(0) != 0)
            {
                p.Flap = p.Flap == 0 ? 1 : 0; // flap(1) toggles, flap(0) queries (M, Part.getFlap/toggleFlap)
            }

            return Value.Int(p.Flap);
        });
        Add(r, Part, "getLogo", "()I", c => Value.Int(ScriptObjects.GetInt(c.Self, "manufacturer"))); // script method shadows it
        Add(r, Part, "getMass", "()F", c => Value.Float(P(c).Definition?.Mass ?? 0f));
        Add(r, Part, "setSfxLoopParams", "(FF)I", c =>
        {
            P(c).SfxLoopParams = (c.Float(0), c.Float(1));
            return Value.Int(0);
        });

        // Car membership.
        Add(r, Part, "getCar", "()I", c =>
        {
            var p = P(c);
            return Value.Int(p.Car is { } car ? World.Host.IdOf(car.Object) : 0);
        });
        Add(r, Part, "getCarRef", "()Ljava.game.parts.Part;", c => Value.Obj(PartsWorld.CarOf(P(c))?.Object));
        Add(r, Part, "getWheelID", "()I", c => Value.Int(PartsWorld.WheelIdOf(P(c))));

        // Slots.
        Add(r, Part, "getSlots", "()I", c => Value.Int(P(c).DefinedSlotCount));
        Add(r, Part, "getSlotID", "(I)I", c =>
        {
            var p = P(c);
            var i = c.Int(0);
            return Value.Int(i == -1 ? p.MountSlot?.Id ?? 0 : p.SlotAt(i)?.Id ?? 0);
        });
        Add(r, Part, "getSlotIndex", "(I)I", c => Value.Int(SlotById(c, c.Int(0))?.Index ?? -1));
        Add(r, Part, "isSlotDisabled", "(I)I", c => Value.Bool(SlotById(c, c.Int(0))?.Disabled ?? false));
        Add(r, Part, "disableSlot", "(II)V", c =>
        {
            P(c).SlotOrCreate(c.Int(0)).Disabled = c.Int(1) != 0;
            return Value.Void;
        });
        Add(r, Part, "getSlotDamage", "(I)" + Str, c => c.Str(P(c).SlotAt(c.Int(0))?.Damage));
        Add(r, Part, "setSlotDamage", "(I" + Str + ")V", c =>
        {
            if (P(c).SlotAt(c.Int(0)) is { } s)
            {
                s.Damage = c.String(1);
            }

            return Value.Void;
        });
        Add(r, Part, "setSlotPos", "(I" + V3 + YprD + ")V", c =>
        {
            var s = P(c).SlotOrCreate(c.Int(0));
            if (ScriptObjects.ReadVector(c.Object(1)) is { } pos)
            {
                s.Position = pos;
            }

            if (ScriptObjects.ReadYpr(c.Object(2)) is { } ori)
            {
                s.Orientation = ori;
            }

            return Value.Void;
        });
        Add(r, Part, "partOnSlot", "(I)Ljava.game.parts.Part;", c => Value.Obj(SlotById(c, c.Int(0))?.Linked?.Object));
        Add(r, Part, "slotIDOnSlot", "(I)I", c => Value.Int(SlotById(c, c.Int(0))?.LinkedSlotId ?? 0));
        Add(r, Part, "install_OK", "(" + GameRefD + "I" + GameRefD + "I" + V3 + ")[I", c =>
        {
            var p = P(c);
            var carObj = c.Object(0) is { } co ? World.Host.PartBehind(co) : null;
            var car = carObj is null ? null : World.Get(carObj);
            if (car is null || World.FindInstallTarget(p, car, c.Int(1)) is not { } found)
            {
                return Value.Null;
            }

            if (c.Object(2) is { } outRef)
            {
                World.Host.Bind(outRef, found.Target.Object);
            }

            var arr = c.Vm.NewArray("I", found.Pair.Length);
            for (var i = 0; i < found.Pair.Length; i++)
            {
                arr.Items[i] = Value.Int(found.Pair[i]);
            }

            return Value.Obj(arr);
        });
    }

    private static Value Swap(PartState p, float v, Func<PartState, float> get, Action<PartState, float> set)
    {
        var old = get(p);
        set(p, v);
        return Value.Float(old);
    }

    private static Value SwapI(PartState p, int v, Func<PartState, int> get, Action<PartState, int> set)
    {
        var old = get(p);
        set(p, v);
        return Value.Int(old);
    }

    // ---------------------------------------------------------------- Chassis

    private ChassisState Ch(NativeCall c) => World.ChassisOf(P(c));

    private void RegisterChassis(NativeRegistry r)
    {
        Add(r, Chassis, "getWheels", "()I", c => Value.Int(Ch(c).Wheels.Count));
        Add(r, Chassis, "getWheel", "(I)Ljava.game.parts.WheelRef;", c => Value.Obj(World.WheelRef(c.Vm, P(c), c.Int(0))));
        Add(r, Chassis, "getWheelPos", "(I)" + V3, c =>
        {
            var ch = Ch(c);
            var i = c.Int(0);
            return i >= 0 && i < ch.Wheels.Count ? ScriptObjects.NewVector(c.Vm, ch.Wheels[i].Position) : Value.Null;
        });
        Add(r, Chassis, "forceUpdate", "()V", c =>
        {
            World.ForceUpdate(P(c));
            return Value.Void;
        });
        Add(r, Chassis, "getMass", "()F", c => Value.Float(Ch(c).Mass));
        Add(r, Chassis, "getCM", "()" + V3, c => ScriptObjects.NewVector(c.Vm, Ch(c).CenterOfMass));
        Add(r, Chassis, "getMin", "()" + V3, c => ScriptObjects.NewVector(c.Vm, Ch(c).Min));
        Add(r, Chassis, "getMax", "()" + V3, c => ScriptObjects.NewVector(c.Vm, Ch(c).Max));
        Add(r, Chassis, "getWheelDamage", "(I)" + Str, c => c.Str(Ch(c).WheelDamage.GetValueOrDefault(c.Int(0))));
        Add(r, Chassis, "setWheelDamage", "(I" + Str + ")V", c =>
        {
            Ch(c).WheelDamage[c.Int(0)] = c.String(1);
            return Value.Void;
        });
        Add(r, Chassis, "setCooling", "(FFF)V", c =>
        {
            Ch(c).Cooling = (c.Float(0), c.Float(1), c.Float(2));
            return Value.Void;
        });
        Add(r, Chassis, "getSfxTable", "(I)Ljava.game.parts.SfxTable;", c => Value.Obj(World.SfxTable(c.Vm, P(c), c.Int(0))));
        Add(r, Chassis, "setSfxExhaustMinVol", "(F)V", c =>
        {
            Ch(c).SfxExhaustMinVolume = c.Float(0);
            return Value.Void;
        });
        Add(r, Chassis, "setSteerWheelRadius", "(F)V", c =>
        {
            Ch(c).SteerWheelRadius = c.Float(0);
            return Value.Void;
        });
        Add(r, Chassis, "setSteerWheel", "(FF)V", c =>
        {
            var ch = Ch(c);
            ch.SteerWheelRadius = c.Float(0);
            ch.SteerWheelZOffset = c.Float(1);
            return Value.Void;
        });
        Add(r, Chassis, "setHornSFX", "(" + ResRef + "FI)V", c =>
        {
            Ch(c).Horns[c.Int(2)] = (c.Object(0), c.Float(1));
            return Value.Void;
        });
        Add(r, Chassis, "setNitroSFX", "(" + ResRef + "F)V", c =>
        {
            Ch(c).NitroSound = (c.Object(0), c.Float(1));
            return Value.Void;
        });
        Add(r, Chassis, "getMileage", "()F", c => Value.Float(Ch(c).Mileage));
        Add(r, Chassis, "setMileage", "(F)V", c =>
        {
            Ch(c).Mileage = c.Float(0);
            return Value.Void;
        });
        NativeMethod buck = c =>
        {
            Ch(c).Bucks.Add((c.Int(0), c.Int(1), c.Float(2), c.Float(3), c.Float(4), c.Float(5)));
            return c.Method.Return == "I" ? Value.Int(0) : Value.Void;
        };
        Add(r, Chassis, "setBuck", "(IIFFFF)I", buck); // exe registration
        Add(r, Chassis, "setBuck", "(IIFFFF)V", buck); // shipped declaration
        Add(r, Chassis, "setAckermann", "(F)V", c =>
        {
            Ch(c).Ackermann = c.Float(0);
            return Value.Void;
        });
        Add(r, Chassis, "setTorque", "(F)V", c =>
        {
            Ch(c).TorqueOverride = c.Float(0);
            return Value.Void;
        });
        Add(r, Chassis, "getTorque", "(FF)F", c =>
        {
            var dyno = ScriptObjects.GetObject(c.Self, "dynodata");
            return Value.Float(dyno is null ? 0f : World.Dyno.Torque(DynoParams.Read(dyno), c.Float(0), c.Float(1)));
        });
        Add(r, Chassis, "getIndexedData", "(I)F", c => Value.Float(BitConverter.Int32BitsToSingle(Ch(c).Telemetry.ReadBits(c.Int(0)))));
        Add(r, Chassis, "getIndexedAddr", "(I)I", c => Value.Int(World.TelemetryAddress(P(c), c.Int(0))));
    }

    // ---------------------------------------------------------------- WheelRef

    private WheelState W(NativeCall c) =>
        World.Wheel(c.Self) ?? throw new VmException($"{c.Method.Signature}: WheelRef {c.Self.Describe()} has no native wheel");

    private void Set(NativeRegistry r, string name, Action<WheelState, float> set) =>
        Add(r, WheelRef, name, "(F)V", c =>
        {
            var w = W(c);
            set(w, c.Float(0));
            w.Writes++;
            return Value.Void;
        });

    private void SetArray(NativeRegistry r, string name, int n, Func<WheelState, float[]> target) =>
        Add(r, WheelRef, name, "(" + new string('F', n) + ")V", c =>
        {
            var w = W(c);
            var a = target(w);
            for (var i = 0; i < n; i++)
            {
                a[i] = c.Float(i);
            }

            w.Writes++;
            return Value.Void;
        });

    private void RegisterWheel(NativeRegistry r)
    {
        Add(r, WheelRef, "getPos", "()" + V3, c => ScriptObjects.NewVector(c.Vm, W(c).Position));
        Add(r, WheelRef, "getYpr", "()" + YprD, c => ScriptObjects.NewYpr(c.Vm, W(c).Orientation));
        Add(r, WheelRef, "getDrive", "()F", c => Value.Float(W(c).Drive));
        Add(r, WheelRef, "getSteer", "()F", c => Value.Float(W(c).Steer));
        Add(r, WheelRef, "getRadius", "()F", c => Value.Float(W(c).Radius));
        Add(r, WheelRef, "getBrake", "()F", c => Value.Float(W(c).Brake));
        Add(r, WheelRef, "getHBrake", "()F", c => Value.Float(W(c).HandBrake));
        Add(r, WheelRef, "setPos", "(" + V3 + ")V", c =>
        {
            var w = W(c);
            if (ScriptObjects.ReadVector(c.Object(0)) is { } v)
            {
                w.Position = v;
            }

            w.Writes++;
            return Value.Void;
        });
        Add(r, WheelRef, "setYpr", "(" + YprD + ")V", c =>
        {
            var w = W(c);
            if (ScriptObjects.ReadYpr(c.Object(0)) is { } v)
            {
                w.Orientation = v;
            }

            w.Writes++;
            return Value.Void;
        });
        Set(r, "setDrive", static (w, v) => w.Drive = v);
        Set(r, "setSteer", static (w, v) => w.Steer = v);
        Set(r, "setRadius", static (w, v) => w.Radius = v);
        Set(r, "setWidth", static (w, v) => w.Width = v);
        Set(r, "setFriction", static (w, v) => w.Friction = v);
        Set(r, "setFrictn_x", static (w, v) => w.FrictionX = v);
        Set(r, "setSliction", static (w, v) => w.Sliction = v);
        Set(r, "setStiffness", static (w, v) => w.Stiffness = v);
        Set(r, "setRollRes", static (w, v) => w.RollResistance = v);
        Set(r, "setBearing", static (w, v) => w.Bearing = v);
        Set(r, "setMaxLoad", static (w, v) => w.MaxLoad = v);
        Set(r, "setLoadSmooth", static (w, v) => w.LoadSmooth = v);
        Set(r, "setForce", static (w, v) => w.Force = v);
        Set(r, "setDamping", static (w, v) => w.DampingBump = w.DampingRebound = v);
        Set(r, "setRestLen", static (w, v) => w.RestLength = v);
        Set(r, "setMinLen", static (w, v) => w.MinLength = v);
        Set(r, "setMaxLen", static (w, v) => w.MaxLength = v);
        Set(r, "setBrake", static (w, v) => w.Brake = v);
        Set(r, "setHBrake", static (w, v) => w.HandBrake = v);
        Add(r, WheelRef, "setDamping", "(FF)V", c =>
        {
            var w = W(c);
            w.DampingBump = c.Float(0);
            w.DampingRebound = c.Float(1);
            w.Writes++;
            return Value.Void;
        });
        Add(r, WheelRef, "setCPatch", "(FFF)V", c =>
        {
            var w = W(c);
            w.ContactPatch = (c.Float(0), c.Float(1), c.Float(2));
            w.Writes++;
            return Value.Void;
        });
        Add(r, WheelRef, "setPacejka", "(IF)V", c =>
        {
            var w = W(c);
            var i = c.Int(0);
            if (i >= 0 && i < WheelState.PacejkaCount)
            {
                w.Pacejka[i] = c.Float(1);
            }

            w.Writes++;
            return Value.Void;
        });
        Add(r, WheelRef, "setOppWheel", "(I)V", c =>
        {
            W(c).OppositeWheel = c.Int(0);
            return Value.Void;
        });
        SetArray(r, "setInstantCenter", 6, static w => w.InstantCenter);
        SetArray(r, "setArm", 7, static w => w.Arm);
        SetArray(r, "setHub", 10, static w => w.Hub);
    }

    // ---------------------------------------------------------------- SfxTable, DynoData

    private void RegisterSfxAndDyno(NativeRegistry r)
    {
        Add(r, SfxTable, "getItems", "()I", c => Value.Int(World.Sfx(c.Self).Items.Count));
        Add(r, SfxTable, "clear", "()V", c =>
        {
            var t = World.Sfx(c.Self);
            t.Items.Clear();
            t.Clears++;
            return Value.Void;
        });
        NativeMethod add = c =>
        {
            var t = World.Sfx(c.Self);
            t.Items.Add(new SfxItem(c.Object(0), c.Float(1), c.Float(2), c.Float(3), c.Float(4), c.Float(5)));
            // AddItemPtr returns a pointer to the new item (only commented-out script code uses it): opaque index here.
            return c.Method.Return == "I" ? Value.Int(t.Items.Count) : Value.Void;
        };
        Add(r, SfxTable, "addItem", "(" + ResRef + "FFFFF)V", add);
        Add(r, SfxTable, "AddItemPtr", "(" + ResRef + "FFFFF)I", add);

        Add(r, DynoData, "newNative", "()V", c =>
        {
            World.NewDyno(c.Self);
            return Value.Void;
        });
        Add(r, DynoData, "deleteNative", "()V", c =>
        {
            if (World.FindDyno(c.Self) is { } d)
            {
                d.Deleted = true;
            }

            return Value.Void;
        });
        Add(r, DynoData, "calcDyno", "(F)F", c =>
        {
            var curve = World.Dyno.Calculate(DynoParams.Read(c.Self), c.Float(0));
            if (curve is not null)
            {
                ScriptObjects.Set(c.Self, "torque", Value.Float(curve.Torque));
                ScriptObjects.Set(c.Self, "torque2", Value.Float(curve.Torque2));
                ScriptObjects.Set(c.Self, "maxRPM", Value.Float(curve.MaxRpm));
                ScriptObjects.Set(c.Self, "maxHP", Value.Float(curve.MaxHp));
                ScriptObjects.Set(c.Self, "maxTorque", Value.Float(curve.MaxTorque));
                ScriptObjects.Set(c.Self, "RPM_maxTorque", Value.Float(curve.RpmMaxTorque));
                ScriptObjects.Set(c.Self, "RPM_maxHP", Value.Float(curve.RpmMaxHp));
                if (curve.Displacement is { } displacement)
                {
                    ScriptObjects.Set(c.Self, "Displacement", Value.Float(displacement));
                }

                if (curve.Compression is { } compression)
                {
                    ScriptObjects.Set(c.Self, "Compression", Value.Float(compression));
                }
            }

            if (World.FindDyno(c.Self) is { } d)
            {
                d.Curve = curve;
            }

            return Value.Float(0); // returned value unused by the corpus
        });
        Add(r, DynoData, "getTorque", "(FF)F", c => Value.Float(World.Dyno.Torque(DynoParams.Read(c.Self), c.Float(0), c.Float(1))));
        Add(r, DynoData, "getHP", "(FF)F", c => Value.Float(World.Dyno.Horsepower(DynoParams.Read(c.Self), c.Float(0), c.Float(1))));
    }

    // ---------------------------------------------------------------- RawEdit (Slrr_GI only)

    private void RegisterRawEdit(NativeRegistry r)
    {
        Add(r, RawEdit, "getI", "(I)I", c => Value.Int(World.ReadRaw(c.Int(0))));
        Add(r, RawEdit, "getF", "(I)F", c => Value.Float(BitConverter.Int32BitsToSingle(World.ReadRaw(c.Int(0)))));
        Add(r, RawEdit, "setI", "(II)I", c => Value.Int(World.WriteRaw(c.Int(0), c.Int(1))));
        Add(r, RawEdit, "setF", "(IF)I", c => Value.Int(World.WriteRaw(c.Int(0), BitConverter.SingleToInt32Bits(c.Float(1)))));
        Add(r, RawEdit, "CentCur", "(I)I", c => Value.Int(World.CenterCursor?.Invoke(c.Int(0)) ?? 0));
    }
}
