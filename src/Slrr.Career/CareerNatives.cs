using Slrr.Vm;

namespace Slrr.Career;

/// <summary>
/// The 16-character ring of recently typed characters that <c>GameLogic.kismajomCheck</c> compares against.
/// MEASURED (StreetLegal_Redline.exe): the ring is 16 bytes (0x640924..0x640933, write pointer at 0x612c68), one byte is
/// appended per typed character (0x47cc10, character from 0x556e00 bits 16..23), the pointer wraps after 16 bytes.
/// GUESSED: which keyboard path feeds it (here the harness calls <see cref="Type"/>; the window/app area should feed it
/// from WM_CHAR-like input).
/// </summary>
public sealed class TypedRing
{
    public const int Size = 16;

    private readonly byte[] _ring = new byte[Size];
    private int _write;

    /// <summary>Appends typed characters (Latin-1 bytes) like the exe's per-frame keyboard poll.</summary>
    public void Type(string text)
    {
        foreach (var ch in text)
        {
            _ring[_write] = (byte)ch;
            _write = (_write + 1) % Size;
        }
    }

    /// <summary>
    /// The exe's comparison (0x47cb50..0x47cc00, MEASURED): for i = length-1 down to 0, walk code string i backwards from
    /// its last character and the ring backwards from the last written byte; a code character c matches a ring byte b
    /// when <c>c - 1 == b</c> (the shipped strings are stored shifted by +1). The first fully matching index is returned
    /// and the last written ring byte is cleared (so the same typing does not match twice); -1 when nothing matches.
    /// </summary>
    public int Check(IReadOnlyList<string?> codes)
    {
        for (var i = codes.Count - 1; i >= 0; i--)
        {
            var code = codes[i] ?? string.Empty;
            var pos = _write;
            var ok = true;
            for (var k = code.Length - 1; k >= 0; k--)
            {
                pos = (pos + Size - 1) % Size;
                if (code[k] - 1 != _ring[pos])
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                _ring[(_write + Size - 1) % Size] = 0;
                return i;
            }
        }

        return -1;
    }

    /// <summary>Decodes one of the shipped shifted strings into what a player types (for transcripts).</summary>
    public static string Decode(string code) => new(code.Select(c => (char)(c - 1)).ToArray());
}

/// <summary>
/// Natives of the career flow no other area registers. <c>GameLogic.kismajomCheck([String)I</c> is the shipped game's
/// cheat-code check (registered by the exe at 0x487f3a → 0x47cb50, MEASURED); Garage calls it when "Fine tune specific
/// parts" is clicked and acts on the index: 0 "letmeroc" enter the ROC, 1 "begformoney" +$100000, 2 "fastpromotion" next
/// club, 3 "ilikecheating" cheats on, 4 "timemachine" day 180 (decoded from GameLogic's static strings, MEASURED).
/// </summary>
public sealed class CareerNatives : INativeModule
{
    public TypedRing Typed { get; } = new();

    /// <summary>Calls of kismajomCheck and their results (for transcripts).</summary>
    public List<int> Checks { get; } = [];

    public void Register(NativeRegistry registry)
    {
        registry.Register("java.game.GameLogic", "kismajomCheck", "([Ljava.lang.String;)I", c =>
        {
            var codes = new List<string?>();
            if (c.Object(0) is VmArray a)
            {
                codes.AddRange(a.Items.Select(v => c.Vm.GetString(v.AsObject)));
            }

            var r = Typed.Check(codes);
            Checks.Add(r);
            return Value.Int(r);
        }, replace: true);
    }
}
