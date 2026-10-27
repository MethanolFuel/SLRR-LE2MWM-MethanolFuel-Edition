namespace Slrr.Vm;

/// <summary>One activation: method (null for a field initializer), its class, code, receiver and locals.</summary>
public sealed class VmFrame
{
    internal VmFrame(VmClass cls, VmMethod? method, VmCode code, VmObject? self, int localCount, bool pushResult, string? initializerOf)
    {
        Class = cls;
        Method = method;
        Code = code;
        This = self;
        Locals = new Value[Math.Max(localCount, 1)];
        LocalTypes = new string?[Locals.Length];
        PushResult = pushResult;
        InitializerOf = initializerOf;
    }

    public VmClass Class { get; }

    public VmMethod? Method { get; }

    public VmCode Code { get; }

    public VmObject? This { get; }

    public Value[] Locals { get; private set; }

    public string?[] LocalTypes { get; private set; }

    /// <summary>Index of the next instruction.</summary>
    public int Ip { get; internal set; }

    /// <summary>Index of the instruction being executed (for line numbers in errors).</summary>
    public int CurrentIp { get; internal set; }

    /// <summary>Operand-stack height when the frame was entered.</summary>
    internal int StackBase { get; set; }

    /// <summary>Whether the caller expects a value (calls) or not (constructor calls).</summary>
    internal bool PushResult { get; }

    /// <summary>Field name when this frame runs a field-initializer body.</summary>
    public string? InitializerOf { get; }

    public int Line => CurrentIp < Code.Length ? Code.Ins[CurrentIp].Line : Code.Length > 0 ? Code.Ins[^1].Line : 0;

    public string Describe() =>
        (Method is not null ? Method.Signature : $"{Class.Name}.<{InitializerOf} initializer>") + $" line {Line}";

    internal void EnsureSlot(int slot)
    {
        if (slot < 0 || slot > 4096)
        {
            throw new VmException($"bad local slot {slot}");
        }

        if (slot >= Locals.Length)
        {
            var n = Math.Max(slot + 1, Locals.Length * 2);
            var l = Locals;
            var t = LocalTypes;
            Array.Resize(ref l, n);
            Array.Resize(ref t, n);
            Locals = l;
            LocalTypes = t;
        }
    }
}
