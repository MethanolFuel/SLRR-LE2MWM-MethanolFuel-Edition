using System.Numerics;
using Slrr.Vm;

namespace Slrr.Engine.Parts;

/// <summary>One <c>SfxTable.addItem(sound, a, b, c, d, e)</c> row (float roles guessed: rpm, rpmMin, rpmMax, volMin, volMax).</summary>
public readonly record struct SfxItem(VmObject? Sound, float A, float B, float C, float D, float E);

/// <summary>Native RPM-to-sound table behind a <c>java.game.parts.SfxTable</c> (Chassis.getSfxTable(channel)).</summary>
public sealed class SfxTableState
{
    internal SfxTableState(int channel) => Channel = channel;

    public int Channel { get; }

    public VmObject? Ref { get; internal set; }

    public List<SfxItem> Items { get; } = [];

    public int Clears { get; internal set; }
}

/// <summary>Native side of a <c>java.game.parts.DynoData</c> (newNative/deleteNative pair).</summary>
public sealed class DynoState
{
    internal DynoState(VmObject obj, int handle)
    {
        Object = obj;
        Handle = handle;
    }

    public VmObject Object { get; }

    public int Handle { get; }

    public bool Deleted { get; internal set; }

    /// <summary>Result of the last calcDyno (null before the first call).</summary>
    public DynoCurve? Curve { get; internal set; }
}

/// <summary>
/// Read-mostly vehicle telemetry addressed by <c>Chassis.getIndexedData(index)</c> / <c>getIndexedAddr(index)</c>
/// (Slrr_GI extras). Index = byte offset into the native vehicle struct; names from Chassis.java (M). The cells hold
/// raw 32-bit patterns because GearNumber is an int that getIndexedData reinterprets as float ("strange", source comment).
/// </summary>
public sealed class ChassisTelemetry
{
    public const int Mileage = 0x20FC;
    public const int Rpm = 0x1F4C;
    public const int GearStrength = 0x1DD8;
    public const int CarRuntime = 0x1E2C;
    public const int ExhaustActivity = 0x1E28;
    public const int CurrentSteering1 = 0x1DAC;
    public const int CurrentSteering2 = 0x1DB0;
    public const int CurrentSteering3 = 0x1F10;
    public const int Handbrake = 0x1DC0;
    public const int BrakeStrength = 0x1DB8;
    public const int ThrottleStrength = 0x1DB4;
    public const int ClutchStrength = 0x1DBC;
    public const int NitroStrength = 0x1DC4;
    public const int NosLevel = 0x1EF8;
    public const int EngineWear = 0x1E38;
    public const int HandbrakeLed = 0x3CC;
    public const int OverDriveLed = 0x3DC;
    public const int AsrLed = 0x3BC;
    public const int AbsLed = 0x3AC;
    public const int GearNumber = 0x1DF8;
    public const int Flags = 0x70;
    public const int SpeedLimit = 0x2104;

    /// <summary>The 22 named offsets (rawedit-chassis-offsets.tsv); <see cref="IsInt"/> marks the int cells.</summary>
    public static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [Mileage] = "Mileage", [Rpm] = "RPM", [GearStrength] = "GearStrength", [CarRuntime] = "CarRuntime",
        [ExhaustActivity] = "ExhaustActivity", [CurrentSteering1] = "CurrentSteering1", [CurrentSteering2] = "CurrentSteering2",
        [CurrentSteering3] = "CurrentSteering3", [Handbrake] = "Handbreak", [BrakeStrength] = "BreakStrength",
        [ThrottleStrength] = "ThrottleStrength", [ClutchStrength] = "ClutchStrength", [NitroStrength] = "NitroStrength",
        [NosLevel] = "NOSLevel", [EngineWear] = "EngineWear", [HandbrakeLed] = "HandbreakLED", [OverDriveLed] = "OverDriveLED",
        [AsrLed] = "ASRLED", [AbsLed] = "ABSLED", [GearNumber] = "GearNumber", [Flags] = "Flags", [SpeedLimit] = "SpeedLimit",
    };

    private readonly Dictionary<int, int> _cells = [];

    public static bool IsInt(int offset) => offset is GearNumber or Flags;

    /// <summary>Script-writable cells (rawedit.md rule 4: everything else is read-only for scripts).</summary>
    public static bool IsScriptWritable(int offset) => offset is GearNumber or Flags or SpeedLimit;

    public int ReadBits(int offset) => _cells.GetValueOrDefault(offset);

    public void WriteBits(int offset, int bits) => _cells[offset] = bits;

    public float GetFloat(int offset) => BitConverter.Int32BitsToSingle(ReadBits(offset));

    public void SetFloat(int offset, float v) => WriteBits(offset, BitConverter.SingleToInt32Bits(v));

    public int GetInt(int offset) => ReadBits(offset);

    public void SetInt(int offset, int v) => WriteBits(offset, v);
}

/// <summary>Chassis-only native state (the car root part).</summary>
public sealed class ChassisState
{
    internal ChassisState(PartState part, int serial)
    {
        Part = part;
        Serial = serial;
    }

    public PartState Part { get; }

    internal int Serial { get; }

    public List<WheelState> Wheels { get; } = [];

    public Dictionary<int, SfxTableState> SfxTables { get; } = [];

    public Dictionary<int, string?> WheelDamage { get; } = [];

    public ChassisTelemetry Telemetry { get; } = new();

    /// <summary>Odometer (getMileage/setMileage). Stored in the telemetry Mileage cell as well (same quantity, M).</summary>
    public float Mileage
    {
        get => Telemetry.GetFloat(ChassisTelemetry.Mileage);
        set => Telemetry.SetFloat(ChassisTelemetry.Mileage, value);
    }

    public float SfxExhaustMinVolume { get; set; }

    public (float Min, float Max, float Speed) Cooling { get; set; } = (10f, 50f, 0.01f);

    public float SteerWheelRadius { get; set; }

    public float SteerWheelZOffset { get; set; }

    /// <summary>setHornSFX(sfx, pitch, index): index 1..3 per car model script.</summary>
    public Dictionary<int, (VmObject? Sound, float Pitch)> Horns { get; } = [];

    public (VmObject? Sound, float Pitch)? NitroSound { get; set; }

    public List<(int PartId, int BuckId, float Freq, float Prob, float RpmDep, float Amp)> Bucks { get; } = [];

    public float Ackermann { get; set; }

    /// <summary>Anti-roll bars from the <c>add_rollbar w1 w2 force damping</c> command (Swaybar.updatevariables).</summary>
    public Dictionary<(int Wheel1, int Wheel2), (float Force, float Damping)> Rollbars { get; } = [];

    /// <summary>setTorque(F) (registered, never declared): stored only.</summary>
    public float TorqueOverride { get; set; }

    /// <summary>Overrides for the cfg-derived mass/CM/bounds (null = derive from the cfg bodies).</summary>
    public float? MassOverride { get; set; }

    public Vector3? CenterOfMassOverride { get; set; }

    public Vector3? MinOverride { get; set; }

    public Vector3? MaxOverride { get; set; }

    /// <summary>Snapshot taken by the last effective <c>forceUpdate()</c>.</summary>
    public ChassisParams? Params { get; internal set; }

    /// <summary>forceUpdate() calls that took a snapshot / that were skipped because suspend_update != 0.</summary>
    public int Updates { get; internal set; }

    public int SuspendedUpdates { get; internal set; }

    public float Mass => MassOverride ?? Part.Definition?.Mass ?? 0f;

    /// <summary>Mass-weighted mean of the cfg body positions (guess: the exe sums its physics bodies).</summary>
    public Vector3 CenterOfMass
    {
        get
        {
            if (CenterOfMassOverride is { } c)
            {
                return c;
            }

            var bodies = Part.Definition?.Bodies ?? [];
            var m = bodies.Sum(b => b.Mass);
            return m <= 0 ? Vector3.Zero : bodies.Aggregate(Vector3.Zero, (acc, b) => acc + (b.Position * b.Mass)) / m;
        }
    }

    /// <summary>Bounding box of the body centres (guess; the exe uses mesh bounds, which need the geometry layer).</summary>
    public Vector3 Min => MinOverride ?? Bounds(Vector3.Min);

    public Vector3 Max => MaxOverride ?? Bounds(Vector3.Max);

    private Vector3 Bounds(Func<Vector3, Vector3, Vector3> f)
    {
        var bodies = Part.Definition?.Bodies ?? [];
        return bodies.Count == 0 ? Vector3.Zero : bodies.Select(b => b.Position).Aggregate(f);
    }
}
