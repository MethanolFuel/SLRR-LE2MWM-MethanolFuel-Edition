namespace Slrr.Engine.SystemLib;

/// <summary>Kinds of SDAT file the shipped scripts write (magic int right after the SDAT header).</summary>
public enum SaveKind
{
    Unknown,
    Career,
    Vehicle,
    Skin,
    Options,
    ControlSet,
}

/// <summary>
/// Magic ids and versions of the game's SDAT files. Values come from the script constants
/// (GameLogic.SAVEFILEID_MAIN / SAVEFILEVERSION_MAIN[_LOAD], ControlSet.CTRLFILEID / CTRLFILEVERSION) and from the
/// magics in docs/engine-api/java.io.md (vehicle, skin and options magics were read from bytecode, not from samples).
/// </summary>
public static class SaveFormats
{
    public const int CareerId = unchecked((int)0x87654321);
    public const int CareerVersion = 21;
    public const int CareerMinLoadVersion = 20;
    public const int VehicleId = -2006563806;
    public const int VehicleVersion = 5;
    public const int SkinId = -2108385177;
    public const int SkinVersion = 0;
    public const int OptionsId = -19088744;
    public const int OptionsVersion = 12;
    public const int ControlSetId = 0x4C525443;
    public const int ControlSetVersion = 16;

    /// <summary>Directories scripts use below <c>save/</c> (GameLogic.carrerSaveDir, tempSaveDir, ...).</summary>
    public static readonly string[] Directories = ["save/carrer", "save/temp", "save/cars", "save/skins", "save/controls", "save/game"];

    public static SaveKind KindOf(int magic) => magic switch
    {
        CareerId => SaveKind.Career,
        VehicleId => SaveKind.Vehicle,
        SkinId => SaveKind.Skin,
        OptionsId => SaveKind.Options,
        ControlSetId => SaveKind.ControlSet,
        _ => SaveKind.Unknown,
    };

    /// <summary>Reads magic and version of an SDAT image; null if it is not an SDAT file or too short.</summary>
    public static (SaveKind Kind, int Magic, int Version)? Probe(byte[] data)
    {
        var r = new SdatReader(data);
        if (!r.Valid || r.Remaining < 8)
        {
            return null;
        }

        var magic = r.ReadInt();
        return (KindOf(magic), magic, r.ReadInt());
    }

    /// <summary>The same checks <c>GameLogic.fileCheck</c> does: career magic and version at least the minimum load version.</summary>
    public static bool IsLoadableCareer(byte[] data) =>
        Probe(data) is { Kind: SaveKind.Career } p && p.Version >= CareerMinLoadVersion;
}

/// <summary>One binding of a control file (ControlSet group/vaxisID/deviceID/axisID/from/to/dead_zone arrays).</summary>
public readonly record struct ControlBinding(int Group, int VirtualAxis, int Device, int DeviceAxis, float FromMin, float FromMax, float ToMin, float ToMax, float DeadZone);

/// <summary>VirtualAxisSmoothProperties record.</summary>
public readonly record struct AxisSmoothing(int VirtualAxis, float CenterRange, float FactorCenter, float FactorOpposite, float FactorSame, float Power);

/// <summary>
/// <c>save/controls/active_control_set</c> as <c>ControlSet.load/save</c> reads and writes it: id 'CTRL', version 16,
/// device names, bindings (9 values each), smoothing records (6 values each). Measured on the shipped sample, which
/// also carries 12 unexplained trailing bytes (<c>0, 0, 8</c>) after the last record; they are kept in
/// <see cref="Trailer"/> and not interpreted (ControlSet.load never reads them).
/// </summary>
public sealed class ControlSetFile
{
    public List<string> Devices { get; } = [];

    public List<ControlBinding> Bindings { get; } = [];

    public List<AxisSmoothing> Smoothing { get; } = [];

    public byte[] Trailer { get; private set; } = [];

    public static ControlSetFile? Parse(byte[] data)
    {
        var r = new SdatReader(data);
        if (!r.Valid || r.ReadInt() != SaveFormats.ControlSetId || r.ReadInt() != SaveFormats.ControlSetVersion)
        {
            return null;
        }

        var f = new ControlSetFile();
        var nDev = r.ReadInt();
        for (var i = 0; i < nDev && !r.Truncated; i++)
        {
            f.Devices.Add(r.ReadString() ?? string.Empty);
        }

        var n = r.ReadInt();
        for (var i = 0; i < n && !r.Truncated; i++)
        {
            f.Bindings.Add(new(r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat()));
        }

        var nv = r.ReadInt();
        for (var i = 0; i < nv && !r.Truncated; i++)
        {
            f.Smoothing.Add(new(r.ReadInt(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat()));
        }

        if (r.Truncated)
        {
            return null;
        }

        f.Trailer = data[r.Position..];
        return f;
    }

    public byte[] ToBytes() => SdatWriter.Build(w =>
    {
        w.WriteInt(SaveFormats.ControlSetId);
        w.WriteInt(SaveFormats.ControlSetVersion);
        w.WriteInt(Devices.Count);
        foreach (var d in Devices)
        {
            w.WriteString(d);
        }

        w.WriteInt(Bindings.Count);
        foreach (var b in Bindings)
        {
            w.WriteInt(b.Group);
            w.WriteInt(b.VirtualAxis);
            w.WriteInt(b.Device);
            w.WriteInt(b.DeviceAxis);
            w.WriteFloat(b.FromMin);
            w.WriteFloat(b.FromMax);
            w.WriteFloat(b.ToMin);
            w.WriteFloat(b.ToMax);
            w.WriteFloat(b.DeadZone);
        }

        w.WriteInt(Smoothing.Count);
        foreach (var s in Smoothing)
        {
            w.WriteInt(s.VirtualAxis);
            w.WriteFloat(s.CenterRange);
            w.WriteFloat(s.FactorCenter);
            w.WriteFloat(s.FactorOpposite);
            w.WriteFloat(s.FactorSame);
            w.WriteFloat(s.Power);
        }
    });
}
