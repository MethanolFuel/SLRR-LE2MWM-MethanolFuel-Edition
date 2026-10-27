using System.Globalization;

namespace Slrr.Core;

/// <summary>
/// A 32-bit resource id as written in cfg/script files (<c>0x0004F242</c>) and in RPAK entry tables.
/// In RPAK files the high 16 bits select an external-reference slot (1-based index into the archive's
/// reference table, 0 = the archive itself) and the low 16 bits are the id inside that archive.
/// This interpretation is a working hypothesis from the Phase 1 survey (see docs/formats/rpak.md).
/// </summary>
public readonly record struct ResourceId(uint Value)
{
    public ushort Archive => (ushort)(Value >> 16);
    public ushort Local => (ushort)(Value & 0xFFFF);

    public override string ToString() => $"0x{Value:X8}";

    public static bool TryParse(string text, out ResourceId id)
    {
        id = default;
        var t = text.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(t.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            id = new ResourceId(v);
            return true;
        }

        return false;
    }
}
