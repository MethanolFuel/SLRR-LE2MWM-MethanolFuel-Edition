using System.Globalization;
using System.Text;

namespace Slrr.Vm;

/// <summary>
/// Engine-independent java.lang natives: Object, String, Integer, Float, Math, System (log/time/arraycopy) and
/// Thread (cooperative). Signatures are those of docs/research/natives-redline.tsv (plus
/// <c>Thread.getCurrentThread</c>, declared native in Thread.class but registered elsewhere in the exe). Behaviour
/// that could not be read from the exe is marked "hypothesis" and listed in docs/vm.md.
/// </summary>
public sealed class LangNatives : INativeModule
{
    private const string Obj = "java.lang.Object";
    private const string Str = "java.lang.String";
    private const string Sys = "java.lang.System";
    private const string Thr = "java.lang.Thread";

    public void Register(NativeRegistry r)
    {
        // java.lang.Object
        r.Register(Obj, "wait", "()V", c =>
        {
            c.Thread.WaitOn(c.Self);
            return Value.Void;
        });
        r.Register(Obj, "notify", "()V", c =>
        {
            c.Vm.Notify(c.Self, all: false);
            return Value.Void;
        });
        r.Register(Obj, "notifyAll", "()V", c =>
        {
            c.Vm.Notify(c.Self, all: true);
            return Value.Void;
        });
        r.Register(Obj, "toString", "()Ljava.lang.String;", c =>
            c.Str(c.Self.Native as string ?? $"{c.Self.Class.Name}@{c.Self.Id:x8}")); // format: hypothesis
        r.Register(Obj, "hashCode", "()I", c => Value.Int(c.Self.Id));
        r.Register(Obj, "disableGC", "()V", _ => Value.Void);
        r.Register(Obj, "enableGC", "()V", _ => Value.Void);

        // java.lang.String (text lives in VmObject.Native)
        r.Register(Str, "finalize", "()V", _ => Value.Void);
        r.Register(Str, "length", "()I", c => Value.Int(Text(c).Length));
        r.Register(Str, "intValue", "()I", c => Value.Int(Atoi(Text(c))));
        r.Register(Str, "floatValue", "()F", c => Value.Float(Atof(Text(c))));
        r.Register(Str, "append", "(I)Ljava.lang.String;", c => c.Str(Text(c) + (char)c.Int(0))); // hypothesis: char code
        r.Register(Str, "token", "(ILjava.lang.String;)Ljava.lang.String;", c =>
        {
            var delims = (c.String(1) ?? " \t\n").ToCharArray();
            var tokens = Text(c).Split(delims, StringSplitOptions.RemoveEmptyEntries);
            var i = c.Int(0);
            // strtok-like; null past the end: hypothesis. The token is the canonical (interned) String of its text:
            // the scripts compare tokens with literals by reference (City.handleEvent `token(0) == "car_add"`,
            // Bot.handleEvent "ai_info_*"), which only works in the shipped game if equal texts are one object (GUESSED).
            return i >= 0 && i < tokens.Length ? Value.Obj(c.Vm.InternLiteral(tokens[i])) : Value.Null;
        });

        // java.lang.Integer / Float
        r.Register("java.lang.Integer", "toString", "(I)Ljava.lang.String;", c => c.Str(c.Int(0).ToString(CultureInfo.InvariantCulture)));
        r.Register("java.lang.Integer", "toHexString", "(I)Ljava.lang.String;", c => c.Str(c.Int(0).ToString("x", CultureInfo.InvariantCulture)));
        r.Register("java.lang.Float", "toString", "(FLjava.lang.String;)Ljava.lang.String;", c =>
            c.Str(Printf.Format(c.String(1) ?? "%.3f", c.Float(0))));

        // java.lang.Math
        r.Register("java.lang.Math", "random", "()F", c => Value.Float(c.Vm.Random.NextSingle()));
        r.Register("java.lang.Math", "sqrt", "(F)F", c => Value.Float(MathF.Sqrt(c.Float(0))));
        r.Register("java.lang.Math", "randomize", "()V", c =>
        {
            c.Vm.Random = new Random(Environment.TickCount);
            return Value.Void;
        });
        r.Register("java.lang.Math", "setrandseed", "(I)V", c =>
        {
            c.Vm.Random = new Random(c.Int(0));
            return Value.Void;
        });

        // java.lang.System
        r.Register(Sys, "exit", "()V", _ => throw new VmExitException(null));
        r.Register(Sys, "exit", "(Ljava.lang.String;)V", c => throw new VmExitException(c.String(0)));
        r.Register(Sys, "log", "(Ljava.lang.String;)V", c =>
        {
            var s = c.String(0) ?? "null";
            c.Vm.Log.Add(s);
            c.Vm.Out.WriteLine(s);
            return Value.Void;
        });
        r.Register(Sys, "currentTime", "()F", c => Value.Float(c.Vm.Time));
        r.Register(Sys, "simTime", "()F", c => Value.Float(c.Vm.SimTime));
        r.Register(Sys, "gc", "()I", _ => Value.Zero);
        r.Register(Sys, "runFinalization", "()I", _ => Value.Zero);
        r.Register(Sys, "analyze", "(Ljava.lang.Object;)V", Analyze);
        r.Register(Sys, "analyze", "(F)V", Analyze);
        r.Register(Sys, "analyze", "(I)V", Analyze);
        r.Register(Sys, "arraycopy", "(Ljava.lang.Object;ILjava.lang.Object;II)V", ArrayCopy);

        // java.lang.Thread (cooperative; the VmThread hangs off the Thread object's Native slot)
        r.Register(Thr, "init", "(Ljava.lang.String;)V", c =>
        {
            Info(c.Self).Name = c.String(0);
            return Value.Void;
        });
        r.Register(Thr, "start", "()V", Start);
        r.Register(Thr, "stop", "()V", c =>
        {
            Info(c.Self).Thread?.Stop();
            return Value.Void;
        });
        r.Register(Thr, "isAlive", "()I", c => Value.Bool(Info(c.Self).Thread?.IsAlive ?? false));
        r.Register(Thr, "getPriority", "()I", c => Value.Int(Info(c.Self).Priority));
        r.Register(Thr, "setPriority", "(I)V", c =>
        {
            Info(c.Self).Priority = c.Int(0);
            return Value.Void;
        });
        r.Register(Thr, "setDaemon", "(I)V", c =>
        {
            Info(c.Self).Daemon = c.Int(0) != 0;
            return Value.Void;
        });
        r.Register(Thr, "sleep", "(F)V", c =>
        {
            // The argument is in milliseconds (SoftTimer: sleep(waitTime * 1000), HotkeyWatcher: sleep(50)).
            c.Thread.SleepFor(c.Float(0) / 1000f);
            return Value.Void;
        });
        r.Register(Thr, "suspend", "()V", c =>
        {
            Info(c.Self).Thread?.Suspend();
            return Value.Void;
        });
        r.Register(Thr, "resume", "()V", c =>
        {
            Info(c.Self).Thread?.Wake();
            return Value.Void;
        });
        r.Register(Thr, "getCurrentThread", "()Ljava.lang.Thread;", c =>
        {
            var t = c.Thread;
            if (t.ThreadObject is null)
            {
                t.ThreadObject = c.Vm.NewObject(c.Vm.LoadClass(Thr));
                Info(t.ThreadObject).Thread = t;
            }

            return Value.Obj(t.ThreadObject);
        });
    }

    /// <summary>C <c>atoi</c>: optional whitespace and sign, then digits; anything else stops (0 if none).</summary>
    public static int Atoi(string s)
    {
        var i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        var neg = false;
        if (i < s.Length && s[i] is '+' or '-')
        {
            neg = s[i] == '-';
            i++;
        }

        long v = 0;
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            v = Math.Min((v * 10) + (s[i++] - '0'), 1L << 32);
        }

        return unchecked((int)(neg ? -v : v));
    }

    /// <summary>C <c>atof</c>: the longest valid leading float literal (0 if none).</summary>
    public static float Atof(string s)
    {
        var t = s.TrimStart();
        var end = 0;
        var best = 0f;
        while (end < t.Length && (char.IsAsciiDigit(t[end]) || t[end] is '.' or '-' or '+' or 'e' or 'E'))
        {
            end++;
            if (float.TryParse(t.AsSpan(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                best = v;
            }
        }

        return best;
    }

    private static string Text(NativeCall c) =>
        c.Self.Native as string ?? throw new VmException($"{c.Method.Signature} on a String without text");

    private static Value Analyze(NativeCall c)
    {
        var line = "analyze: " + (c.Arg(0).Kind == VmKind.Object && c.Arg(0).Ref is { } o ? Dump(c.Vm, o) : c.Arg(0).Describe());
        c.Vm.Out.WriteLine(line);
        return Value.Void;
    }

    private static string Dump(Vm vm, VmObject o)
    {
        var sb = new StringBuilder(o.Describe());
        foreach (var cls in o.Class.Hierarchy())
        {
            foreach (var f in cls.InstanceFields)
            {
                sb.Append(CultureInfo.InvariantCulture, $" {f.Name}={o.Fields[f.Slot].Describe()}");
            }
        }

        _ = vm;
        return sb.ToString();
    }

    private static Value ArrayCopy(NativeCall c)
    {
        var src = c.Object(0) as VmArray ?? throw new VmException("System.arraycopy: source is not an array");
        var dst = c.Object(2) as VmArray ?? throw new VmException("System.arraycopy: destination is not an array");
        int sp = c.Int(1), dp = c.Int(3), n = c.Int(4);
        if (n < 0 || sp < 0 || dp < 0 || sp + n > src.Items.Length || dp + n > dst.Items.Length)
        {
            throw new VmException($"System.arraycopy: range out of bounds ({sp}+{n} of {src.Items.Length} -> {dp}+{n} of {dst.Items.Length})");
        }

        var tmp = src.Items.AsSpan(sp, n).ToArray();
        for (var i = 0; i < n; i++)
        {
            dst.Items[dp + i] = VmTypes.Coerce(tmp[i], dst.ElementDescriptor);
        }

        return Value.Void;
    }

    private static Value Start(NativeCall c)
    {
        var self = c.Self;
        var info = Info(self);
        if (info.Thread is { IsAlive: true })
        {
            return Value.Void;
        }

        var targetField = self.Class.FindField("target");
        var target = targetField is { IsStatic: false } ? self.Fields[targetField.Slot].Ref : null;
        var runnable = target ?? self;
        var run = runnable.Class.FindMethod("run", "()V") ?? throw new VmException($"{runnable.Class.Name} has no run()");
        info.Thread = c.Vm.Spawn(run, runnable, [], info.Name ?? $"Thread-{self.Id}", self);
        info.Thread.Priority = info.Priority;
        info.Thread.Daemon = info.Daemon;
        return Value.Void;
    }

    private static ThreadInfo Info(VmObject threadObject)
    {
        if (threadObject.Native is ThreadInfo ti)
        {
            return ti;
        }

        ti = new ThreadInfo();
        threadObject.Native = ti;
        return ti;
    }

    /// <summary>Engine-side state of a java.lang.Thread object.</summary>
    public sealed class ThreadInfo
    {
        public string? Name { get; set; }

        public int Priority { get; set; }

        public bool Daemon { get; set; }

        public VmThread? Thread { get; set; }
    }
}
