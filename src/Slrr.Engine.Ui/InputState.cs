using System.Globalization;

namespace Slrr.Engine.Ui;

/// <summary>An input edge: a virtual axis or a raw key went down (pressed) or up.</summary>
public readonly record struct InputEdge(bool Virtual, int Code, bool Pressed);

/// <summary>
/// Headless input devices: physical axis values per (device, axis), virtual-axis overrides injected by a host, the
/// key queue read by <c>Input.lastKey</c> and the pending edges that hotkeys consume. Device 0 is the keyboard
/// (axis = DIK scan code), device 1 the system mouse (measured: <c>Input.initControllers</c> skips a device named
/// <c>"SysMouse"</c>; OptionsDialog treats device 0 as the keyboard and <c>RCDIK_ESCAPE</c> as "undefine").
/// </summary>
public sealed class InputState
{
    public const int FlagVirtual = 1;
    public const int FlagKey = 2;
    public const int KeyboardDevice = 0;

    private readonly Dictionary<(int Device, int Axis), float> _physical = [];
    private readonly Dictionary<int, float> _virtualOverride = [];

    /// <summary>
    /// <c>Input.getDeviceName</c> by index. The names are the DirectInput system device names the shipped control file
    /// stores (MEASURED: <c>save/controls/active_control_set</c> lists "SysKeyboard", "SysMouse", then the pads).
    /// <c>ControlSet.load</c> matches the file's names against these; an unmatched name becomes an unknown negative device,
    /// so with "Keyboard" here no keyboard mapping (throttle, brake, steering) ever reached a car.
    /// </summary>
    public List<string> Devices { get; } = ["SysKeyboard", "SysMouse"];

    /// <summary>Keys for <c>Input.lastKey</c>: scan code in the low 16 bits, character in the high 16 (Osd.animate uses <c>key &gt;&gt; 16</c>).</summary>
    public Queue<int> KeyQueue { get; } = new();

    /// <summary>Edges not yet consumed by <c>Input.checkHotkeys</c> (Osd hotkeys).</summary>
    public Queue<InputEdge> OsdEdges { get; } = new();

    /// <summary>Edges not yet delivered to global hotkeys (osd == null), delivered by the frame pump.</summary>
    public Queue<InputEdge> GlobalEdges { get; } = new();

    public float MouseSensitivity { get; set; }

    public float CursorX { get; set; }

    public float CursorY { get; set; }

    public (float X, float Y, float Z) PickedPos { get; set; }

    public List<(VmObjectRef Target, int VAxis, int Device, int Axis, float[] Args)> AxisMaps { get; } = [];

    public List<(VmObjectRef Target, int VAxis, float[] Args)> AxisSmooth { get; } = [];

    public float Physical(int device, int axis) => _physical.GetValueOrDefault((device, axis));

    public void SetPhysical(int device, int axis, float value)
    {
        if (value == 0)
        {
            _physical.Remove((device, axis));
        }
        else
        {
            _physical[(device, axis)] = value;
        }
    }

    public float? VirtualOverride(int axis) => _virtualOverride.TryGetValue(axis, out var v) ? v : null;

    public void SetVirtualOverride(int axis, float? value)
    {
        if (value is null)
        {
            _virtualOverride.Remove(axis);
        }
        else
        {
            _virtualOverride[axis] = value.Value;
        }
    }

    /// <summary>First axis of a device whose |value| exceeds 0.5, or -1 (the "press a key to assign" poll of OptionsDialog).</summary>
    public int ActiveAxis(int device)
    {
        foreach (var ((d, a), v) in _physical.OrderBy(k => k.Key.Axis))
        {
            if (d == device && Math.Abs(v) > 0.5f)
            {
                return a;
            }
        }

        return -1;
    }

    public void Enqueue(InputEdge e)
    {
        OsdEdges.Enqueue(e);
        GlobalEdges.Enqueue(e);
    }

    /// <summary>Display name of an axis (keyboard: DirectInput key names; guessed table, the exe's is not read).</summary>
    public string AxisName(int device, int axis)
    {
        if (device == KeyboardDevice && KeyNames.TryGetValue(axis, out var n))
        {
            return n;
        }

        if (device == KeyboardDevice)
        {
            return "Key " + axis.ToString(CultureInfo.InvariantCulture);
        }

        return (device < Devices.Count ? Devices[device] : "Device " + device.ToString(CultureInfo.InvariantCulture)) + " axis " + axis.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>DIK codes of <c>Input.RCDIK_*</c> (measured from Input.class) with readable names.</summary>
    public static readonly IReadOnlyDictionary<int, string> KeyNames = BuildKeyNames();

    private static Dictionary<int, string> BuildKeyNames()
    {
        var d = new Dictionary<int, string>
        {
            [1] = "Esc", [14] = "Backspace", [15] = "Tab", [28] = "Enter", [57] = "Space", [200] = "Up", [203] = "Left",
            [205] = "Right", [208] = "Down", [51] = ",", [52] = ".", [69] = "NumLock", [181] = "Num /", [55] = "Num *",
            [74] = "Num -", [78] = "Num +", [156] = "Num Enter", [83] = "Num .", [201] = "PgUp", [209] = "PgDn",
            [82] = "Num 0", [79] = "Num 1", [80] = "Num 2", [81] = "Num 3", [75] = "Num 4", [76] = "Num 5", [77] = "Num 6",
            [71] = "Num 7", [72] = "Num 8", [73] = "Num 9", [87] = "F11", [88] = "F12",
        };
        for (var i = 0; i < 9; i++)
        {
            d[2 + i] = ((char)('1' + i)).ToString();
        }

        d[11] = "0";
        foreach (var (row, start) in new[] { ("QWERTYUIOP", 16), ("ASDFGHJKL", 30), ("ZXCVBNM", 44) })
        {
            for (var i = 0; i < row.Length; i++)
            {
                d[start + i] = row[i].ToString();
            }
        }

        for (var i = 0; i < 10; i++)
        {
            d[59 + i] = "F" + (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        return d;
    }
}

/// <summary>Reference-identity wrapper so script objects can be stored in records.</summary>
public readonly record struct VmObjectRef(Slrr.Vm.VmObject? Object)
{
    public bool Equals(VmObjectRef other) => ReferenceEquals(Object, other.Object);

    public override int GetHashCode() => Object is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Object);
}
