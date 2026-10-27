using System.Globalization;
using Slrr.Vm;

namespace Slrr.Engine.Ui;

/// <summary>
/// The 64 natives of the UI area as registered by <c>StreetLegal_Redline.exe</c> (docs/research/natives-redline.tsv):
/// java.render Camera 3, GfxEngine 11, Text 9, Viewport 11; java.sound.Sound 10; java.io Input 11, Controller 7,
/// MouseCursor 2. Every native acts on the headless <see cref="UiEngine"/>; nothing is drawn or played. Whether a
/// behaviour is measured or guessed is stated per native in docs/engine/ui.md.
/// </summary>
public sealed class UiNatives : INativeModule
{
    public const string CameraClass = "java.render.Camera";
    public const string GfxClass = "java.render.GfxEngine";
    public const string TextClass = "java.render.Text";
    public const string ViewportClass = "java.render.Viewport";
    public const string SoundClass = "java.sound.Sound";
    public const string InputClass = "java.io.Input";
    public const string ControllerClass = "java.io.Controller";
    public const string CursorClass = "java.io.MouseCursor";

    private const string RRef = "Ljava.util.resource.ResourceRef;";
    private const string GRef = "Ljava.util.resource.GameRef;";
    private const string Vec3 = "Ljava.lang.Vector3;";
    private const string StrD = "Ljava.lang.String;";

    public UiNatives(UiEngine engine)
    {
        Engine = engine;
    }

    public UiEngine Engine { get; }

    /// <summary>All (class, name, descriptor) keys this module registers.</summary>
    public static IReadOnlyList<NativeKey> Keys { get; } = CollectKeys();

    public void Register(NativeRegistry r)
    {
        RegisterCamera(r);
        RegisterGfx(r);
        RegisterText(r);
        RegisterViewport(r);
        RegisterSound(r);
        RegisterInput(r);
        RegisterController(r);
        RegisterCursor(r);
    }

    private static List<NativeKey> CollectKeys()
    {
        var reg = new NativeRegistry();
        new UiNatives(new UiEngine()).Register(reg);
        return reg.Keys.ToList();
    }

    private UiEngine E(NativeCall c)
    {
        Engine.Bind(c.Vm);
        return Engine;
    }

    // ---------------------------------------------------------------- java.render.Camera

    private void RegisterCamera(NativeRegistry r)
    {
        r.Register(CameraClass, "create", $"({RRef}Ljava.render.Viewport;IFFFFFII)V", c =>
        {
            var m = E(c).Camera(c.Self);
            m.Parent = c.Object(0);
            m.Viewport = c.Object(1);
            m.Flags = c.Int(2);
            m.HalfFov = c.Float(3);
            m.Near = c.Float(4);
            m.Far = c.Float(5);
            m.LodBias = c.Float(6);
            m.LodAmp = c.Float(7);
            m.Culling = c.Int(8);
            m.PlaneTest = c.Int(9);
            m.Destroyed = false;
            return Value.Void;
        });
        r.Register(CameraClass, "destroy", "()V", c =>
        {
            E(c).Camera(c.Self).Destroyed = true;
            return Value.Void;
        });
        r.Register(CameraClass, "setFog", "(IFF)V", c =>
        {
            E(c).Camera(c.Self).Fog = (c.Int(0), c.Float(1), c.Float(2));
            return Value.Void;
        });
    }

    // ---------------------------------------------------------------- java.render.GfxEngine

    private void RegisterGfx(NativeRegistry r)
    {
        r.Register(GfxClass, "forceRendering", "()V", c =>
        {
            E(c).Video.ForcedFrames++;
            return Value.Void;
        });
        r.Register(GfxClass, "setGlobalEnvmap", $"({RRef})V", c =>
        {
            E(c).Video.GlobalEnvmap = c.Object(0);
            return Value.Void;
        });
        r.Register(GfxClass, "numDisplayModes", "()I", c => Value.Int(E(c).Video.DisplayModes.Count));
        r.Register(GfxClass, "currDisplayMode", "()I", c =>
        {
            var e = E(c);
            var mode = e.VideoMode;
            return Value.Int(e.Video.DisplayModes.IndexOf(mode));
        });
        r.Register(GfxClass, "displayModeName", "(I)Ljava.lang.String;", c =>
        {
            var modes = E(c).Video.DisplayModes;
            var i = c.Int(0);
            return i >= 0 && i < modes.Count
                ? c.Str(string.Create(CultureInfo.InvariantCulture, $"{modes[i].W}x{modes[i].H}x{modes[i].Bpp}"))
                : Value.Null;
        });
        r.Register(GfxClass, "changeVideoMode", "(III)V", c =>
        {
            E(c).SetVideoMode(c.Int(0), c.Int(1), c.Int(2));
            return Value.Void;
        });
        r.Register(GfxClass, "printScreen", $"({StrD})V", c =>
        {
            E(c).Video.Screenshots.Add(c.String(0) ?? string.Empty);
            return Value.Void;
        });
        r.Register(GfxClass, "flush", "()V", c =>
        {
            E(c).Video.Flushes++;
            return Value.Void;
        });
        r.Register(GfxClass, "openVideo", $"({StrD}II)I", c =>
        {
            // 0 = success, inferred from GfxEngine.playVideo (`if (openVideo(..) == 0) { while (isPlayingVideo()) .. }`)
            // and MainMenuDialog/TrackSelect, which build a 3D background only when the result is non-zero.
            var e = E(c);
            e.Video.VideoPath = c.String(0);
            if (!e.Video.VideoAvailable)
            {
                return Value.Int(1);
            }

            e.Video.VideoOpen = true;
            e.Video.VideoStart = c.Vm.Time;
            return Value.Int(0);
        });
        r.Register(GfxClass, "closeVideo", "()V", c =>
        {
            E(c).Video.VideoOpen = false;
            return Value.Void;
        });
        r.Register(GfxClass, "isPlayingVideo", "()I", c =>
        {
            var v = E(c).Video;
            return Value.Bool(v.VideoOpen && c.Vm.Time - v.VideoStart < v.VideoLength);
        });
    }

    // ---------------------------------------------------------------- java.render.Text

    private void RegisterText(NativeRegistry r)
    {
        r.Register(TextClass, "getWidthPixels", $"({StrD}{RRef})F", c =>
        {
            var s = c.String(0) ?? string.Empty;
            var font = c.Object(1);
            var size = font is null ? 20 : Script.CallStatic(c.Vm, TextClass, "getFontSize", null, Value.Obj(font)).AsInt;
            E(c);
            return Value.Float(s.Length * size * 0.5f); // guessed metric: half the nominal size per character
        });
        r.Register(TextClass, "create", $"({RRef}{RRef}FF)V", c =>
        {
            var m = E(c).Text(c.Self);
            m.Parent = c.Object(0);
            m.Font = c.Object(1);
            m.X = c.Float(2);
            m.Y = c.Float(3);
            return Value.Void;
        });
        r.Register(TextClass, "changeColor", "(I)V", c =>
        {
            E(c).Text(c.Self).Color = c.Int(0);
            return Value.Void;
        });
        r.Register(TextClass, "changeAlign", "(I)V", c =>
        {
            E(c).Text(c.Self).Align = c.Int(0);
            return Value.Void;
        });
        r.Register(TextClass, "setPos", "(FF)V", c =>
        {
            var m = E(c).Text(c.Self);
            m.X = c.Float(0);
            m.Y = c.Float(1);
            return Value.Void;
        });
        r.Register(TextClass, "getPos", $"(){Vec3}", c =>
        {
            var m = E(c).Text(c.Self);
            return Value.Obj(Script.NewVector3(c.Vm, m.X, m.Y, 0));
        });
        r.Register(TextClass, "update", "()V", c =>
        {
            var m = E(c).Text(c.Self);
            m.Text = c.Vm.GetString(Script.Get(c.Self, "text").AsObject);
            m.Updates++;
            return Value.Void;
        });
        r.Register(TextClass, "getDefaultColor", "()I", c => Value.Int(E(c).Text(c.Self).DefaultColor));
        r.Register(TextClass, "setDefaultColor", "(I)V", c =>
        {
            E(c).Text(c.Self).DefaultColor = c.Int(0);
            return Value.Void;
        });
    }

    // ---------------------------------------------------------------- java.render.Viewport

    private void RegisterViewport(NativeRegistry r)
    {
        r.Register(ViewportClass, "create", "(IFFFF)V", c =>
        {
            var m = E(c).Viewport(c.Self);
            m.Layer = c.Int(0);
            m.Left = c.Float(1);
            m.Top = c.Float(2);
            m.Width = c.Float(3);
            m.Height = c.Float(4);
            m.Destroyed = false;
            return Value.Void;
        });
        r.Register(ViewportClass, "destroy", "()V", c =>
        {
            var m = E(c).Viewport(c.Self);
            m.Destroyed = true;
            m.Active = false;
            return Value.Void;
        });
        r.Register(ViewportClass, "activate", "(I)V", c =>
        {
            var m = E(c).Viewport(c.Self);
            m.Active = true;
            m.RenderFlags = c.Int(0);
            m.Activations++;
            return Value.Void;
        });
        r.Register(ViewportClass, "deactivate", "()V", c =>
        {
            E(c).Viewport(c.Self).Active = false;
            return Value.Void;
        });
        r.Register(ViewportClass, "getAspect", "()F", c =>
        {
            var e = E(c);
            var m = e.Viewport(c.Self);
            var (w, h, _) = e.VideoMode;
            return Value.Float(m.Height == 0 ? 0 : m.Width * w / (m.Height * h));
        });
        r.Register(ViewportClass, "getWidth", "()F", c => Value.Float(E(c).Viewport(c.Self).Width));
        r.Register(ViewportClass, "getHeight", "()F", c => Value.Float(E(c).Viewport(c.Self).Height));
        r.Register(ViewportClass, "getTop", "()F", c => Value.Float(E(c).Viewport(c.Self).Top));
        r.Register(ViewportClass, "getLeft", "()F", c => Value.Float(E(c).Viewport(c.Self).Left));
        r.Register(ViewportClass, "resize", "(FFFF)V", c =>
        {
            var m = E(c).Viewport(c.Self);
            m.Left = c.Float(0);
            m.Top = c.Float(1);
            m.Width = c.Float(2);
            m.Height = c.Float(3);
            return Value.Void;
        });
        r.Register(ViewportClass, "unproject", $"({Vec3}I){Vec3}", c =>
        {
            E(c);
            var v = c.Object(0);
            return v is null
                ? Value.Null
                : Value.Obj(Script.NewVector3(c.Vm, Script.GetFloat(v, "x"), Script.GetFloat(v, "y"), Script.GetFloat(v, "z")));
        });
    }

    // ---------------------------------------------------------------- java.sound.Sound

    private void RegisterSound(NativeRegistry r)
    {
        r.Register(SoundClass, "changeMusicSet", "(I)V", c =>
        {
            var s = E(c).Sound;
            s.MusicSet = c.Int(0);
            s.Track = 0;
            s.Log.Add("musicset " + s.MusicSet.ToString(CultureInfo.InvariantCulture));
            return Value.Void;
        });
        r.Register(SoundClass, "nextTrack", "()V", c =>
        {
            var s = E(c).Sound;
            s.Track++;
            s.Log.Add("next");
            return Value.Void;
        });
        r.Register(SoundClass, "prevTrack", "()V", c =>
        {
            var s = E(c).Sound;
            s.Track--;
            s.Log.Add("prev");
            return Value.Void;
        });
        r.Register(SoundClass, "setVolume", "(IF)V", c =>
        {
            var s = E(c).Sound;
            var ch = c.Int(0);
            if (ch >= 0 && ch < s.Volumes.Length)
            {
                s.Volumes[ch] = c.Float(1);
            }

            return Value.Void;
        });
        r.Register(SoundClass, "getVolume", "(I)F", c =>
        {
            var s = E(c).Sound;
            var ch = c.Int(0);
            return Value.Float(ch >= 0 && ch < s.Volumes.Length ? s.Volumes[ch] : 0);
        });
        r.Register(SoundClass, "enableDebugDump", $"({RRef})I", c => Value.Bool(E(c).Sound.DebugDumpAvailable));
        r.Register(SoundClass, "getMode", "()I", c => Value.Int(E(c).Sound.Mode));
        r.Register(SoundClass, "setMode", "(I)I", c =>
        {
            var s = E(c).Sound;
            var old = s.Mode;
            s.Mode = c.Int(0);
            return Value.Int(old);
        });
        r.Register(SoundClass, "has3DHardware", "()I", c => Value.Bool(E(c).Sound.Has3DHardware));
        r.Register(SoundClass, "hasMixHardware", "()I", c => Value.Bool(E(c).Sound.HasMixHardware));
    }

    // ---------------------------------------------------------------- java.io.Input

    private void RegisterInput(NativeRegistry r)
    {
        r.Register(InputClass, "getAxis", "(II)F", c =>
        {
            var e = E(c);
            int device = c.Int(0), axis = c.Int(1);
            if (axis < 0)
            {
                // OptionsDialog: Input.getAxis(1, -1 - Config.mouseSensitivity * 100) right before cursor.config():
                // a negative axis on the mouse sets the sensitivity (inferred from that single call site).
                e.Input.MouseSensitivity = (-1 - axis) / 100f;
                return Value.Float(0);
            }

            return Value.Float(e.Input.Physical(device, axis));
        });
        r.Register(InputClass, "getDeviceName", $"(I){StrD}", c =>
        {
            var d = E(c).Input.Devices;
            var i = c.Int(0);
            return i >= 0 && i < d.Count ? c.Str(d[i]) : Value.Null;
        });
        r.Register(InputClass, "mapAxis", $"({GRef}IIIFFFF)V", c =>
        {
            E(c).Input.AxisMaps.Add((new VmObjectRef(c.Object(0)), c.Int(1), c.Int(2), c.Int(3), [c.Float(4), c.Float(5), c.Float(6), c.Float(7)]));
            return Value.Void;
        });
        r.Register(InputClass, "setAxisSmooth", $"({GRef}IFFFF)V", c =>
        {
            E(c).Input.AxisSmooth.Add((new VmObjectRef(c.Object(0)), c.Int(1), [c.Float(2), c.Float(3), c.Float(4), c.Float(5)]));
            return Value.Void;
        });
        r.Register(InputClass, "activeAxis", "(I)I", c => Value.Int(E(c).Input.ActiveAxis(c.Int(0))));
        r.Register(InputClass, "axisName", $"(II){StrD}", c => c.Str(E(c).Input.AxisName(c.Int(0), c.Int(1))));
        r.Register(InputClass, "lastKey", "()I", c => Value.Int(E(c).Input.KeyQueue.TryDequeue(out var k) ? k : 0));
        r.Register(InputClass, "createHotkey", $"(II{"Ljava.io.Hotkey;"}{GRef}Ljava.render.Osd;I)V", c =>
        {
            var hk = c.Object(2) ?? throw new VmException("Input.createHotkey: null Hotkey");
            E(c).AddHotkey(new HotkeyModel
            {
                Hotkey = hk,
                Key = c.Int(0),
                Flags = c.Int(1),
                Handler = c.Object(3),
                Osd = c.Object(4),
                EventFilter = c.Int(5),
            });
            return Value.Void;
        });
        r.Register(InputClass, "deleteHotkey", "(Ljava.io.Hotkey;)V", c =>
        {
            if (c.Object(0) is { } hk)
            {
                E(c).RemoveHotkey(hk);
            }

            return Value.Void;
        });
        r.Register(InputClass, "checkHotkeys", $"({GRef}Ljava.render.Osd;)V", c =>
        {
            E(c).CheckHotkeys(c.Object(1));
            return Value.Void;
        });
        r.Register(InputClass, "flushHotkeys", "()V", c =>
        {
            var i = E(c).Input;
            i.OsdEdges.Clear();
            i.GlobalEdges.Clear();
            return Value.Void;
        });
    }

    // ---------------------------------------------------------------- java.io.Controller

    private void RegisterController(NativeRegistry r)
    {
        r.Register(ControllerClass, "user_Add", "(IIIFFFF)I", c =>
        {
            var m = E(c).Controller(c.Self);
            m.Mappings.Add(new AxisMapping(c.Int(0), c.Int(1), c.Int(2), c.Float(3), c.Float(4), c.Float(5), c.Float(6)));
            return Value.Int(1);
        });
        r.Register(ControllerClass, "user_Del", "(III)I", c =>
        {
            var m = E(c).Controller(c.Self);
            int v = c.Int(0), d = c.Int(1), a = c.Int(2);
            return Value.Int(m.Mappings.RemoveAll(x => x.VirtualAxis == v && x.Device == d && x.Axis == a));
        });
        r.Register(ControllerClass, "user_GetAxisVal", "(I)F", c => Value.Float(E(c).VirtualAxis(c.Int(0), c.Self)));
        r.Register(ControllerClass, "user_Reset", "()V", c =>
        {
            var m = E(c).Controller(c.Self);
            m.Mappings.Clear();
            m.Smoothing.Clear();
            m.Resets++;
            return Value.Void;
        });
        r.Register(ControllerClass, "user_SetAxisForce", "(IFF)V", c =>
        {
            E(c).Controller(c.Self).Force[c.Int(0)] = (c.Float(1), c.Float(2));
            return Value.Void;
        });
        r.Register(ControllerClass, "user_SetAxisSmooth", "(IFFFFF)I", c =>
        {
            E(c).Controller(c.Self).Smoothing[c.Int(0)] = [c.Float(1), c.Float(2), c.Float(3), c.Float(4), c.Float(5)];
            return Value.Int(1);
        });
        r.Register(ControllerClass, "user_SetAxisSpeed", "(IF)V", c =>
        {
            E(c).Controller(c.Self).Speed[c.Int(0)] = c.Float(1);
            return Value.Void;
        });
    }

    // ---------------------------------------------------------------- java.io.MouseCursor

    private void RegisterCursor(NativeRegistry r)
    {
        r.Register(CursorClass, "getPos", $"(){Vec3}", c =>
        {
            var i = E(c).Input;
            return Value.Obj(Script.NewVector3(c.Vm, i.CursorX, i.CursorY, 0));
        });
        r.Register(CursorClass, "getPickedPos", $"(){Vec3}", c =>
        {
            var p = E(c).Input.PickedPos;
            return Value.Obj(Script.NewVector3(c.Vm, p.X, p.Y, p.Z));
        });
    }
}
