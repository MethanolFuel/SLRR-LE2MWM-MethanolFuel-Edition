using Silk.NET.Input;

namespace Slrr.App;

/// <summary>
/// Silk.NET / GLFW keys → DirectInput scan codes (DIK_*), the key codes the game's scripts and control file use (device 0 axis =
/// DIK code; e.g. AXIS_THROTTLE is mapped to DIK_UP 200 in the shipped control set, MEASURED in docs/race/integration.md).
/// GLFW key tokens name the physical position on a US keyboard, which is what a DirectInput scan code is, so the table is
/// layout-independent: an AZERTY "A" key reports <see cref="Key.Q"/> and DIK 16, like DirectInput would. The DIK values are the
/// DirectInput header constants (standard, not measured from the exe).
/// </summary>
public static class KeyMap
{
    private static readonly Dictionary<Key, int> Dik = Build();

    /// <summary>DIK code of a key, 0 when the game has no code for it.</summary>
    public static int ToDik(Key key) => Dik.GetValueOrDefault(key);

    /// <summary>
    /// The character a key carries to <c>Input.lastKey</c> when the platform sends no character event: Enter 13, Esc 27, Backspace 8,
    /// Tab 9 (the values <c>StringInput.key</c> tests, MEASURED from the decompiled script; Windows WM_CHAR values).
    /// </summary>
    public static char ControlChar(Key key) => key switch
    {
        Key.Enter or Key.KeypadEnter => '\r',
        Key.Escape => (char)27,
        Key.Backspace => '\b',
        Key.Tab => '\t',
        _ => '\0',
    };

    /// <summary>Keys whose press is normally followed by a character event (letters, digits, punctuation, space, keypad digits).</summary>
    public static bool ProducesChar(Key key) =>
        key is >= Key.A and <= Key.Z or >= Key.Number0 and <= Key.Number9 or >= Key.Keypad0 and <= Key.Keypad9 or Key.Space or Key.Apostrophe or Key.Comma
            or Key.Minus or Key.Period or Key.Slash or Key.Semicolon or Key.Equal or Key.LeftBracket or Key.BackSlash or Key.RightBracket
            or Key.GraveAccent or Key.KeypadDecimal or Key.KeypadDivide or Key.KeypadMultiply or Key.KeypadSubtract or Key.KeypadAdd or Key.World1 or Key.World2;

    /// <summary>Key by name for the input script (<c>Enter</c>, <c>Up</c>, <c>A</c>, <c>F1</c>, Silk.NET enum names; a few aliases).</summary>
    public static Key Parse(string name)
    {
        var alias = name.Trim().ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "return" => "Enter",
            "ctrl" or "control" => "ControlLeft",
            "shift" => "ShiftLeft",
            "alt" => "AltLeft",
            "pgup" => "PageUp",
            "pgdn" => "PageDown",
            "del" => "Delete",
            "ins" => "Insert",
            var s when s.Length == 1 && char.IsAsciiDigit(s[0]) => "Number" + s,
            _ => name.Trim(),
        };
        return Enum.TryParse<Key>(alias, ignoreCase: true, out var k) && k != Key.Unknown ? k : throw new ArgumentException($"unknown key '{name}'");
    }

    /// <summary>The key a plain ASCII character is typed with on a US layout (for synthetic typing), and whether Shift is needed.</summary>
    public static (Key Key, bool Shift) ForChar(char c)
    {
        if (char.IsAsciiLetterLower(c))
        {
            return (Key.A + (c - 'a'), false);
        }

        if (char.IsAsciiLetterUpper(c))
        {
            return (Key.A + (c - 'A'), true);
        }

        if (char.IsAsciiDigit(c))
        {
            return (Key.Number0 + (c - '0'), false);
        }

        return c switch
        {
            ' ' => (Key.Space, false),
            '.' => (Key.Period, false),
            ',' => (Key.Comma, false),
            '-' => (Key.Minus, false),
            '_' => (Key.Minus, true),
            '/' => (Key.Slash, false),
            _ => (Key.Period, false), // GUESS: same fallback as InputInjector.Type (DIK 52 with the character attached)
        };
    }

    private static Dictionary<Key, int> Build()
    {
        var d = new Dictionary<Key, int>
        {
            [Key.Escape] = 1, [Key.Minus] = 12, [Key.Equal] = 13, [Key.Backspace] = 14, [Key.Tab] = 15,
            [Key.LeftBracket] = 26, [Key.RightBracket] = 27, [Key.Enter] = 28, [Key.ControlLeft] = 29,
            [Key.Semicolon] = 39, [Key.Apostrophe] = 40, [Key.GraveAccent] = 41, [Key.ShiftLeft] = 42, [Key.BackSlash] = 43,
            [Key.Comma] = 51, [Key.Period] = 52, [Key.Slash] = 53, [Key.ShiftRight] = 54, [Key.KeypadMultiply] = 55,
            [Key.AltLeft] = 56, [Key.Space] = 57, [Key.CapsLock] = 58, [Key.NumLock] = 69, [Key.ScrollLock] = 70,
            [Key.Keypad7] = 71, [Key.Keypad8] = 72, [Key.Keypad9] = 73, [Key.KeypadSubtract] = 74, [Key.Keypad4] = 75,
            [Key.Keypad5] = 76, [Key.Keypad6] = 77, [Key.KeypadAdd] = 78, [Key.Keypad1] = 79, [Key.Keypad2] = 80,
            [Key.Keypad3] = 81, [Key.Keypad0] = 82, [Key.KeypadDecimal] = 83, [Key.World2] = 86, [Key.F11] = 87, [Key.F12] = 88,
            [Key.F13] = 100, [Key.F14] = 101, [Key.F15] = 102, [Key.KeypadEqual] = 141,
            [Key.KeypadEnter] = 156, [Key.ControlRight] = 157, [Key.KeypadDivide] = 181, [Key.PrintScreen] = 183, [Key.AltRight] = 184,
            [Key.Pause] = 197, [Key.Home] = 199, [Key.Up] = 200, [Key.PageUp] = 201, [Key.Left] = 203, [Key.Right] = 205,
            [Key.End] = 207, [Key.Down] = 208, [Key.PageDown] = 209, [Key.Insert] = 210, [Key.Delete] = 211,
            [Key.SuperLeft] = 219, [Key.SuperRight] = 220, [Key.Menu] = 221,
        };
        for (var i = 1; i <= 9; i++)
        {
            d[Key.Number0 + i] = 1 + i; // DIK_1 = 2 .. DIK_9 = 10
        }

        d[Key.Number0] = 11;
        foreach (var (row, start) in new[] { ("QWERTYUIOP", 16), ("ASDFGHJKL", 30), ("ZXCVBNM", 44) })
        {
            for (var i = 0; i < row.Length; i++)
            {
                d[Key.A + (row[i] - 'A')] = start + i;
            }
        }

        for (var i = 0; i < 10; i++)
        {
            d[Key.F1 + i] = 59 + i; // DIK_F1 = 59 .. DIK_F10 = 68
        }

        return d;
    }
}
