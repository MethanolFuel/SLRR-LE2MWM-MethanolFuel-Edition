namespace Slrr.Vm;

/// <summary>Base class of all VM errors. <see cref="ScriptTrace"/> is filled in when the error crosses script frames.</summary>
public class VmException : Exception
{
    public VmException(string message)
        : base(message)
    {
    }

    public VmException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Script call stack at the point of failure, innermost first, e.g. <c>java.lang.Integer.toOrdinal(I) line 19</c>.</summary>
    public List<string> ScriptTrace { get; } = [];

    public override string ToString() =>
        ScriptTrace.Count == 0 ? $"{GetType().Name}: {Message}" : $"{GetType().Name}: {Message}\n  at " + string.Join("\n  at ", ScriptTrace);
}

/// <summary>A method flagged native (0x40) was called but no implementation is registered for (class, name, descriptor).</summary>
public sealed class VmNativeMissingException : VmException
{
    public VmNativeMissingException(string className, string name, string descriptor)
        : base($"native implementation missing for \"{className}.{name}{descriptor}\"")
    {
        ClassName = className;
        Name = name;
        Descriptor = descriptor;
    }

    public string ClassName { get; }

    public string Name { get; }

    public string Descriptor { get; }
}

/// <summary>An opcode or operator that the shipped corpus never uses, or that the engine itself treats as fatal.</summary>
public sealed class VmOpcodeException : VmException
{
    public VmOpcodeException(string message)
        : base(message)
    {
    }
}

/// <summary>A class could not be found on the class path.</summary>
public sealed class VmClassNotFoundException : VmException
{
    public VmClassNotFoundException(string className)
        : base($"class not found: {className}")
    {
        ClassName = className;
    }

    public string ClassName { get; }
}

/// <summary>The instruction budget given to a run was exhausted (endless loop guard).</summary>
public sealed class VmBudgetExceededException : VmException
{
    public VmBudgetExceededException(long budget)
        : base($"instruction budget of {budget} exhausted")
    {
    }
}

/// <summary><c>System.exit</c> was called.</summary>
public sealed class VmExitException : VmException
{
    public VmExitException(string? reason)
        : base(reason is null ? "System.exit()" : $"System.exit(\"{reason}\")")
    {
        Reason = reason;
    }

    public string? Reason { get; }
}
