namespace Slrr.Engine.Parts.RawEdit;

/// <summary>Type of the 32-bit cell at an address in the original exe.</summary>
public enum RawCellType
{
    Int,
    Float,
}

/// <summary>One RawEdit address: the exe global it named and the typed property that replaces it.</summary>
/// <param name="Address">Absolute VA in <c>Slrr_GI.exe</c> (as in <c>RawEdit.class</c>).</param>
/// <param name="Name">Variable name used by <c>RawEdit.java</c> (<c>Address_</c> prefix removed).</param>
/// <param name="Property">Group.Property path in <see cref="EngineSettings"/>.</param>
/// <param name="Confidence">H/M/L as in docs/research/rawedit.md.</param>
/// <param name="Get">Reads the property as a number.</param>
/// <param name="Set">Writes it; null for read-only (host-owned) and derived cells, whose script writes are ignored.</param>
public sealed record RawAddressEntry(
    uint Address,
    string Name,
    string Property,
    RawCellType Type,
    char Confidence,
    Func<EngineSettings, double> Get,
    Action<EngineSettings, double>? Set);

/// <summary>
/// <c>uint address -&gt; typed property</c> (rawedit.md section 4 rule 1). Reads and writes keep the exe's 32-bit
/// semantics: <c>getI</c> on a float cell returns its IEEE bit pattern, <c>setF</c> on an int cell stores the
/// float's bit pattern (both measured: the natives are plain <c>mov</c>/<c>fld</c> with no type check).
/// </summary>
public static partial class RawAddressMap
{
    private static readonly RawAddressEntry[] s_entries = Build();
    private static readonly Dictionary<uint, RawAddressEntry> s_byAddress = s_entries.ToDictionary(e => e.Address);

    public static IReadOnlyList<RawAddressEntry> Entries => s_entries;

    public static RawAddressEntry? Find(uint address) => s_byAddress.GetValueOrDefault(address);

    /// <summary>The 32-bit pattern the exe would load from the cell.</summary>
    public static int ReadBits(EngineSettings s, RawAddressEntry e)
    {
        var v = e.Get(s);
        return e.Type == RawCellType.Float ? BitConverter.SingleToInt32Bits((float)v) : unchecked((int)(long)v);
    }

    /// <summary>Stores a 32-bit pattern into the cell (ignored for read-only/derived cells).</summary>
    public static bool WriteBits(EngineSettings s, RawAddressEntry e, int bits)
    {
        if (e.Set is null)
        {
            return false;
        }

        e.Set(s, e.Type == RawCellType.Float ? BitConverter.Int32BitsToSingle(bits) : bits);
        return true;
    }
}
