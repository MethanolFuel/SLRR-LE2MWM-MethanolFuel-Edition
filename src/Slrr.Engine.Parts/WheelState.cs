using System.Numerics;
using Slrr.Vm;

namespace Slrr.Engine.Parts;

/// <summary>
/// Native wheel/suspension corner behind a <c>java.game.parts.WheelRef</c> (Chassis.getWheel(i)). The part scripts push
/// their computed constants here from updatevariables(); the physics (Phase 4) reads them. Every setter is stored
/// verbatim; argument roles beyond the names are open question P1 #8 (docs/engine/parts.md lists measured vs guessed).
/// </summary>
public sealed class WheelState
{
    public const int PacejkaCount = 15;

    internal WheelState(PartState chassis, int index, int handle)
    {
        Chassis = chassis;
        Index = index;
        Handle = handle;
    }

    public PartState Chassis { get; }

    /// <summary>0..3 = FL, FR, RL, RR (Chassis.java comments).</summary>
    public int Index { get; }

    /// <summary>Value of the WheelRef <c>ptr</c> field: an opaque handle RawEdit decodes (<c>ptr + 0xC8</c> = steer).</summary>
    public int Handle { get; }

    /// <summary>The script WheelRef object handed out for this corner (one per corner, reused).</summary>
    public VmObject? Ref { get; internal set; }

    public Vector3 Position { get; set; }

    public Ypr Orientation { get; set; }

    /// <summary>Fraction of engine torque on this wheel (setDrive).</summary>
    public float Drive { get; set; }

    /// <summary>Exe cell <c>[wheel+0xC8]</c>: getSteer/setSteer and <c>RawEdit.setF(whl.ptr+0xc8, ...)</c> (measured identity).</summary>
    public float Steer { get; set; }

    public float Radius { get; set; }

    public float Width { get; set; }

    /// <summary>setCPatch(a, b, c): Wheel passes cpatch width (m), max angle, offset (roles guessed).</summary>
    public (float A, float B, float C) ContactPatch { get; set; }

    public float Friction { get; set; }

    public float FrictionX { get; set; }

    public float Sliction { get; set; }

    public float Stiffness { get; set; }

    public float RollResistance { get; set; }

    public float Bearing { get; set; }

    public float MaxLoad { get; set; }

    public float LoadSmooth { get; set; }

    public float[] Pacejka { get; } = new float[PacejkaCount];

    /// <summary>Spring force (setForce).</summary>
    public float Force { get; set; }

    /// <summary>setDamping(d) sets both; setDamping(bump, rebound) each (order guessed from Spring: both equal there).</summary>
    public float DampingBump { get; set; }

    public float DampingRebound { get; set; }

    public float RestLength { get; set; }

    public float MinLength { get; set; }

    public float MaxLength { get; set; }

    /// <summary>setInstantCenter: six floats (two 3-vectors guessed); Chassis resets to 10000 = none.</summary>
    public float[] InstantCenter { get; } = new float[6];

    public float Brake { get; set; }

    public float HandBrake { get; set; }

    /// <summary>setArm: seven floats, roles unknown.</summary>
    public float[] Arm { get; } = new float[7];

    /// <summary>setHub: ten floats, roles unknown.</summary>
    public float[] Hub { get; } = new float[10];

    /// <summary>setOppWheel (registered, never declared): stored only.</summary>
    public int OppositeWheel { get; set; } = -1;

    /// <summary>Raw 32-bit cells written through RawEdit at offsets other than the known ones (kept so reads match writes).</summary>
    public Dictionary<int, int> RawCells { get; } = [];

    /// <summary>Number of setter calls since creation (lets callers detect that scripts pushed data).</summary>
    public int Writes { get; internal set; }

    public override string ToString() => $"wheel {Index} of {Chassis}";
}
