namespace Slrr.Vm;

/// <summary>
/// An lvalue on the operand stack (the engine's <i>Variable</i>, <c>vtbl[0]() == 2</c>): a local, a field or an
/// array element. Stores convert int↔float to the declared type, as the engine's assignment routine does.
/// </summary>
public abstract class VmVariable
{
    /// <summary>Declared type of the storage location (null when unknown).</summary>
    public abstract string? Descriptor { get; }

    public abstract Value Get();

    public abstract void Set(Value v);

    public abstract string Describe();

    public override string ToString() => Describe();
}

internal sealed class LocalVariable(VmFrame frame, int slot) : VmVariable
{
    public override string? Descriptor => frame.LocalTypes[slot];

    public override Value Get() => frame.Locals[slot];

    public override void Set(Value v) => frame.Locals[slot] = VmTypes.Coerce(v, Descriptor);

    public override string Describe() => $"local {slot}";
}

internal sealed class StaticVariable(VmField field) : VmVariable
{
    public override string? Descriptor => field.Descriptor;

    public override Value Get() => field.Owner.StaticValues[field.Slot];

    public override void Set(Value v) => field.Owner.StaticValues[field.Slot] = VmTypes.Coerce(v, Descriptor);

    public override string Describe() => field.ToString();
}

internal sealed class InstanceVariable(VmObject obj, VmField field) : VmVariable
{
    public override string? Descriptor => field.Descriptor;

    public override Value Get() => obj.Fields[field.Slot];

    public override void Set(Value v) => obj.Fields[field.Slot] = VmTypes.Coerce(v, Descriptor);

    public override string Describe() => $"{obj.Describe()}.{field.Name}";
}

internal sealed class ArrayElement(VmArray array, int index) : VmVariable
{
    public override string? Descriptor => array.ElementDescriptor;

    public override Value Get() => array.Items[index];

    public override void Set(Value v) => array.Items[index] = VmTypes.Coerce(v, Descriptor);

    public override string Describe() => $"{array.Describe()}[{index}]";
}

/// <summary>Read-only pseudo variable (array <c>length</c>).</summary>
internal sealed class ConstVariable(Value value, string name) : VmVariable
{
    public override string? Descriptor => "I";

    public override Value Get() => value;

    public override void Set(Value v) => throw new VmException($"cannot assign to {name}");

    public override string Describe() => name;
}
