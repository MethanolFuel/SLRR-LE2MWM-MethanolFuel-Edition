using Slrr.Vm;

namespace Slrr.Engine.Ui;

/// <summary>
/// What the UI area needs from other engine areas (resource ids, event subscriptions, event masks). The UI does not
/// guess their internals: the default <see cref="ScriptUiWorld"/> goes through the scripts' own methods and fields,
/// and an integrated engine can plug in its resource/event tables here. Contract documented in docs/engine/ui.md.
/// </summary>
public interface IUiWorld
{
    /// <summary>Runtime id of a ResourceRef-derived object (what <c>ref.id()</c> returns to scripts).</summary>
    int IdOf(Vm.Vm vm, VmObject obj);

    /// <summary>
    /// Objects subscribed to <paramref name="eventCode"/> events of <paramref name="source"/> through
    /// <c>GameType.addNotification(source, mask, EVENT_SAME, null, handlerName)</c>, with the handler method name
    /// (<c>"handleEvent"</c> when none was given). Null = unknown (the UI then uses its script-level fallback).
    /// </summary>
    IReadOnlyList<(VmObject Target, string Method)>? Subscribers(Vm.Vm vm, VmObject source, int eventCode);

    /// <summary>Whether <paramref name="target"/>'s event mask (<c>setEventMask</c>) accepts the event. Null = unknown.</summary>
    bool? AcceptsEvent(Vm.Vm vm, VmObject target, int eventCode);
}

/// <summary>
/// Default world: ids come from the scripts' <c>id()</c> (whatever natives are registered for ResourceRef), event
/// masks and subscriptions are unknown, so the UI falls back to script-visible state (<c>Group.active</c>,
/// <c>Osd.button[]</c>).
/// </summary>
public sealed class ScriptUiWorld : IUiWorld
{
    public int IdOf(Vm.Vm vm, VmObject obj) => vm.InvokeVirtual(obj, "id").AsInt;

    public IReadOnlyList<(VmObject Target, string Method)>? Subscribers(Vm.Vm vm, VmObject source, int eventCode) => null;

    public bool? AcceptsEvent(Vm.Vm vm, VmObject target, int eventCode) => null;
}
