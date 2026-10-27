using Slrr.Core;
using Slrr.Vm;

namespace Slrr.Engine.Ui.Tests;

/// <summary>
/// Test-only stand-ins for natives OUTSIDE the UI area (resource ids, positions), so the shipped dialogs can run
/// before the resource/game areas exist. Minimal and explicit: unique ids per ResourceRef (literal ids kept), stored
/// positions for setMatrix/getPos; every other missing native is a no-op returning 0/null (NativeRegistry.DefaultStub).
/// These are NOT engine implementations and are not shipped in Slrr.Engine.Ui.
/// </summary>
public static class TestWorld
{
    private const string Res = "java.util.resource.ResourceRef";
    private const string Vec3 = "Ljava.lang.Vector3;";
    private const string Ypr = "Ljava.lang.Ypr;";

    public static readonly Lazy<string?> Root = new(FindRoot);

    private static readonly Lazy<VmClassPath?> Path = new(() => Root.Value is { } r ? new VmClassPath(new GameFileSystem(r)) : null);

    public static VmClassPath? ClassPath => Path.Value;

    private static string? FindRoot()
    {
        var env = Environment.GetEnvironmentVariable("SLRR_GAME_ROOT");
        if (!string.IsNullOrEmpty(env) && File.Exists(System.IO.Path.Combine(env, "sl.rpk")))
        {
            return env;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Slrr.sln")) && File.Exists(System.IO.Path.Combine(dir.FullName, "sl.rpk")))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    /// <summary>A driver over the shipped classes with UI natives + these stand-ins; null without game data.</summary>
    public static UiDriver? NewDriver()
    {
        if (ClassPath is not { } cp)
        {
            return null;
        }

        var d = UiDriver.Create(cp, Register);
        d.Vm.InstructionBudget = 400_000_000;
        return d;
    }

    public static void Register(NativeRegistry r)
    {
        var ids = new Dictionary<VmObject, int>(ReferenceEqualityComparer.Instance);
        var pos = new Dictionary<VmObject, (float X, float Y, float Z)>(ReferenceEqualityComparer.Instance);
        var next = 0x10000000;
        r.Register(Res, "newNative", "()V", c =>
        {
            ids[c.Self] = next++;
            return Value.Void;
        });
        r.Register(Res, "id", "()I", c => Value.Int(ids.GetValueOrDefault(c.Self)));
        r.Register(Res, "set", "(I)V", c =>
        {
            ids[c.Self] = c.Int(0);
            return Value.Void;
        });
        r.Register(Res, "set", $"(L{Res};)V", c =>
        {
            ids[c.Self] = c.Object(0) is { } o ? ids.GetValueOrDefault(o) : 0;
            return Value.Void;
        });

        Value SetMatrix(NativeCall c)
        {
            if (c.Object(0) is { } v)
            {
                pos[c.Self] = (Script.GetFloat(v, "x"), Script.GetFloat(v, "y"), Script.GetFloat(v, "z"));
            }

            return Value.Void;
        }

        Value GetPos(NativeCall c)
        {
            var p = pos.GetValueOrDefault(c.Self);
            return Value.Obj(Script.NewVector3(c.Vm, p.X, p.Y, p.Z));
        }

        // A parent/child tree for GameRef.create(parent, type, params, name) so getFirstChild/getNextChild work
        // (MouseCursor.cleanup walks its children; with none, ResourceRef.getChildNodes returns null).
        var children = new Dictionary<VmObject, List<VmObject>>(ReferenceEqualityComparer.Instance);
        var parentOf = new Dictionary<VmObject, VmObject>(ReferenceEqualityComparer.Instance);
        r.Register("java.util.resource.GameRef", "create", "(Ljava.util.resource.GameRef;Ljava.util.resource.GameRef;Ljava.lang.String;Ljava.lang.String;)Ljava.lang.GameType;", c =>
        {
            if (c.Object(0) is { } parent)
            {
                if (!children.TryGetValue(parent, out var list))
                {
                    children[parent] = list = [];
                }

                list.Add(c.Self);
                parentOf[c.Self] = parent;
            }

            return Value.Null;
        });
        r.Register(Res, "getFirstChild", $"()L{Res};", c =>
            Value.Obj(children.TryGetValue(c.Self, out var l) && l.Count > 0 ? l[0] : null));
        r.Register(Res, "getNextChild", $"()L{Res};", c =>
        {
            if (!parentOf.TryGetValue(c.Self, out var p))
            {
                return Value.Null;
            }

            var l = children[p];
            var i = l.FindIndex(x => ReferenceEquals(x, c.Self));
            return Value.Obj(i >= 0 && i + 1 < l.Count ? l[i + 1] : null);
        });

        r.Register("java.util.resource.PhysicsRef", "setMatrix", $"({Vec3}{Ypr})V", SetMatrix);
        r.Register("java.util.resource.PhysicsRef", "getPos", $"(){Vec3}", GetPos);
        r.Register("java.util.resource.GameRef", "setMatrix", $"({Vec3}{Ypr})V", SetMatrix);
        r.Register("java.util.resource.GameRef", "getPos", $"(){Vec3}", GetPos);
        // Thread.sleep takes MILLISECONDS in the shipped scripts (SoftTimer: sleep(waitTime * 1000); HotkeyWatcher polls
        // with sleep(50) "every 50 ms"); Slrr.Vm's LangNatives sleeps that many seconds. Reported to the VM owner;
        // overridden here so the 50 ms HotkeyWatcher poll works in tests.
        r.Register("java.lang.Thread", "sleep", "(F)V", c =>
        {
            c.Thread.SleepFor(c.Float(0) / 1000f);
            return Value.Void;
        }, replace: true);
        r.Fallback = NativeRegistry.DefaultStub;
    }
}
