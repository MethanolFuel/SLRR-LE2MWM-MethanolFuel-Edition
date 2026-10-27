using Slrr.Vm;

namespace Slrr.Engine.Ui;

/// <summary>
/// Headless state of the java.render / java.sound / java.io input natives for ONE VM: viewports, cameras, text
/// instances, hotkeys, controllers, video and sound. No drawing. Register <see cref="UiNatives"/> built on this
/// engine in the VM's <see cref="NativeRegistry"/>, and drive it with <see cref="UiDriver"/>.
/// </summary>
public sealed class UiEngine
{
    private readonly Dictionary<VmObject, ViewportModel> _viewports = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<VmObject, CameraModel> _cameras = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<VmObject, TextModel> _texts = new(ReferenceEqualityComparer.Instance);
    private readonly List<TextModel> _textOrder = [];
    private readonly List<HotkeyModel> _hotkeys = [];
    private readonly Dictionary<VmObject, ControllerModel> _controllers = new(ReferenceEqualityComparer.Instance);
    private Vm.Vm? _vm;
    private bool _videoInit;

    public UiEngine(IUiWorld? world = null)
    {
        World = world ?? new ScriptUiWorld();
    }

    public IUiWorld World { get; set; }

    public Vm.Vm Vm => _vm ?? throw new InvalidOperationException("UiEngine is not bound to a VM yet (call Bind or run a UI native first)");

    public bool IsBound => _vm is not null;

    public VideoState Video { get; } = new();

    public SoundState Sound { get; } = new();

    public InputState Input { get; } = new();

    public IReadOnlyCollection<ViewportModel> Viewports => _viewports.Values;

    public IReadOnlyCollection<CameraModel> Cameras => _cameras.Values;

    public IReadOnlyList<TextModel> Texts => _textOrder;

    public IReadOnlyList<HotkeyModel> Hotkeys => _hotkeys;

    public IReadOnlyDictionary<VmObject, ControllerModel> Controllers => _controllers;

    /// <summary>Script threads started by the engine to deliver events (hotkeys, cursor events).</summary>
    public List<string> DispatchLog { get; } = [];

    /// <summary>Binds the engine to its VM (done automatically by the first UI native).</summary>
    public void Bind(Vm.Vm vm)
    {
        if (_vm is not null && !ReferenceEquals(_vm, vm))
        {
            throw new InvalidOperationException("a UiEngine serves exactly one VM");
        }

        _vm = vm;
    }

    // ---------------------------------------------------------------- lookups

    public ViewportModel Viewport(VmObject vp)
    {
        if (!_viewports.TryGetValue(vp, out var m))
        {
            m = new ViewportModel { Object = vp };
            _viewports[vp] = m;
        }

        return m;
    }

    public ViewportModel? FindViewport(VmObject? vp) => vp is not null && _viewports.TryGetValue(vp, out var m) ? m : null;

    public CameraModel Camera(VmObject cam)
    {
        if (!_cameras.TryGetValue(cam, out var m))
        {
            m = new CameraModel { Object = cam };
            _cameras[cam] = m;
        }

        return m;
    }

    public TextModel Text(VmObject text)
    {
        if (!_texts.TryGetValue(text, out var m))
        {
            m = new TextModel { Object = text };
            _texts[text] = m;
            _textOrder.Add(m);
        }

        return m;
    }

    public TextModel? FindText(VmObject? text) => text is not null && _texts.TryGetValue(text, out var m) ? m : null;

    public ControllerModel Controller(VmObject controller)
    {
        if (!_controllers.TryGetValue(controller, out var m))
        {
            m = new ControllerModel();
            _controllers[controller] = m;
        }

        return m;
    }

    // ---------------------------------------------------------------- video

    /// <summary>The current video mode; starts from <c>Config.video_x/y/depth</c> (read by the exe at start-up, measured).</summary>
    public (int W, int H, int Bpp) VideoMode
    {
        get
        {
            if (!_videoInit && _vm is not null)
            {
                _videoInit = true;
                Video.Width = ConfigInt("video_x", 800);
                Video.Height = ConfigInt("video_y", 600);
                Video.Depth = ConfigInt("video_depth", 32);
            }

            return (Video.Width == 0 ? 800 : Video.Width, Video.Height == 0 ? 600 : Video.Height, Video.Depth == 0 ? 32 : Video.Depth);
        }
    }

    public void SetVideoMode(int w, int h, int bpp)
    {
        _ = VideoMode;
        _videoInit = true;
        Video.Width = w;
        Video.Height = h;
        Video.Depth = bpp;
        Video.ModeChanges.Add((w, h, bpp));
    }

    private int ConfigInt(string field, int fallback)
    {
        try
        {
            var v = Script.GetStatic(Vm, "java.util.Config", field);
            return v.Kind == VmKind.Int ? v.AsInt : fallback;
        }
        catch (VmException)
        {
            return fallback;
        }
    }

    // ---------------------------------------------------------------- hotkeys and input

    public void AddHotkey(HotkeyModel hk)
    {
        _hotkeys.RemoveAll(h => ReferenceEquals(h.Hotkey, hk.Hotkey));
        _hotkeys.Add(hk);
    }

    public void RemoveHotkey(VmObject hotkey) => _hotkeys.RemoveAll(h => ReferenceEquals(h.Hotkey, hotkey));

    /// <summary>Current value of a virtual axis: a host override, else the strongest controller mapping (guessed combination).</summary>
    public float VirtualAxis(int vaxis, VmObject? controller = null)
    {
        if (Input.VirtualOverride(vaxis) is { } o)
        {
            return o;
        }

        var best = 0f;
        foreach (var (obj, c) in _controllers)
        {
            if (controller is not null && !ReferenceEquals(obj, controller))
            {
                continue;
            }

            foreach (var m in c.Mappings)
            {
                if (m.VirtualAxis == vaxis)
                {
                    var v = m.Map(Input.Physical(m.Device, m.Axis));
                    if (Math.Abs(v) > Math.Abs(best))
                    {
                        best = v;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Value a Controller reports for <c>getInfo(GII_AXIS, vaxis)</c> (asked by <c>Input.getInput</c>): the virtual axis
    /// scaled to ±128 (inferred from OptionsDialog.animate, which divides throttle by 128 and steering by -256). For the
    /// area that implements <c>GameRef.getInfo</c>.
    /// </summary>
    public int AxisInfo(VmObject? controller, int vaxis) => (int)MathF.Round(VirtualAxis(vaxis, controller) * 128f);

    private IEnumerable<int> MappedVirtualAxes() =>
        _controllers.Values.SelectMany(c => c.Mappings).Select(m => m.VirtualAxis).Distinct();

    /// <summary>A key went down: physical value 1, a key-queue entry, a raw edge and edges of every virtual axis it moves.</summary>
    public void KeyDown(int dik, char ch = '\0') => SetKey(dik, true, ch);

    public void KeyUp(int dik) => SetKey(dik, false, '\0');

    private void SetKey(int dik, bool down, char ch)
    {
        var axes = MappedVirtualAxes().ToList();
        var before = axes.ToDictionary(a => a, a => Pressed(VirtualAxis(a)));
        Input.SetPhysical(InputState.KeyboardDevice, dik, down ? 1f : 0f);
        Input.Enqueue(new InputEdge(false, dik, down));
        if (down)
        {
            Input.KeyQueue.Enqueue((dik & 0xFFFF) | (ch << 16));
        }

        foreach (var a in axes)
        {
            var now = Pressed(VirtualAxis(a));
            if (now != before[a])
            {
                Input.Enqueue(new InputEdge(true, a, now));
            }
        }
    }

    /// <summary>Sets a virtual axis directly (bypassing the controller table) and queues its edge.</summary>
    public void SetVirtualAxis(int vaxis, float? value)
    {
        var was = Pressed(VirtualAxis(vaxis));
        Input.SetVirtualOverride(vaxis, value);
        var now = Pressed(VirtualAxis(vaxis));
        if (was != now)
        {
            Input.Enqueue(new InputEdge(true, vaxis, now));
        }
    }

    private static bool Pressed(float v) => Math.Abs(v) > 0.5f;

    /// <summary>
    /// <c>Input.checkHotkeys(controller, osd)</c>: consumes pending edges until one matches an active hotkey of
    /// <paramref name="osd"/> (key, VIRTUAL/KEY kind, event filter), sets <c>hotkey.state</c> and starts the handler's
    /// <c>handleEvent(Hotkey)</c> on its own script thread. At most one event per call (the script polls every 50 ms),
    /// so <c>state</c> cannot be overwritten before the handler reads it. Hypothesis: the exe's exact queueing is unknown.
    /// </summary>
    public void CheckHotkeys(VmObject? osd)
    {
        while (Input.OsdEdges.TryDequeue(out var e))
        {
            if (DispatchEdge(e, h => osd is not null && ReferenceEquals(h.Osd, osd)))
            {
                return;
            }
        }
    }

    /// <summary>Delivers pending edges to global hotkeys (osd == null), one event per call.</summary>
    public void DeliverGlobalHotkeys()
    {
        while (Input.GlobalEdges.TryDequeue(out var e))
        {
            if (DispatchEdge(e, h => h.Osd is null))
            {
                return;
            }
        }
    }

    private bool DispatchEdge(InputEdge e, Func<HotkeyModel, bool> scope)
    {
        var dispatched = false;
        foreach (var h in _hotkeys.ToList())
        {
            if (!scope(h) || h.Key != e.Code || h.IsVirtual != e.Virtual)
            {
                continue;
            }

            var filter = e.Pressed ? 1 : 2;
            if ((h.EventFilter & filter) == 0)
            {
                continue;
            }

            var target = h.Handler ?? h.Osd;
            if (target is null)
            {
                continue;
            }

            Script.Set(h.Hotkey, "state", Value.Int(e.Pressed ? 1 : 0));
            if (SpawnHandler(target, "handleEvent", [Value.Obj(h.Hotkey)], "THRD-HOTKEY", "Ljava.io.Hotkey;"))
            {
                dispatched = true;
            }
        }

        return dispatched;
    }

    /// <summary>
    /// Starts <c>target.method(args)</c> on a new scheduled script thread (engine event delivery may block: dialogs
    /// call <c>wait()</c>). Returns false when the target has no such method.
    /// </summary>
    public bool SpawnHandler(VmObject target, string method, Value[] args, string threadName, string? firstParam = null)
    {
        var m = target.Class.FindMethods(method, args.Length)
            .FirstOrDefault(x => !x.IsStatic && (x.Code is not null || x.IsNative) && (firstParam is null || (x.Params.Count > 0 && x.Params[0] == firstParam)));
        if (m is null)
        {
            return false;
        }

        DispatchLog.Add($"{threadName}: {target.Class.Name}.{method}({string.Join(", ", args.Select(a => a.Describe()))})");
        Vm.Spawn(m, target, args, threadName);
        return true;
    }
}
