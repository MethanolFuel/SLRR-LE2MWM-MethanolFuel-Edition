using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using Silk.NET.Input;

namespace Slrr.App;

/// <summary>Something the script can aim the mouse at: its box in OSD coordinates and a description for the log.</summary>
public readonly record struct ScriptTarget(OsdRect Rect, string Description)
{
    public override string ToString() => Description;
}

/// <summary>What a running <see cref="InputScript"/> reads from the app (implemented by the runners; faked in tests).</summary>
public interface IScriptContext
{
    /// <summary>Game time (VM seconds).</summary>
    float Time { get; }

    string Section { get; }

    string Screen { get; }

    bool InTrack { get; }

    float SpeedKmh { get; }

    bool HasText(string text);

    /// <summary>A clickable target by text (or by command with <paramref name="command"/>), null when not on screen.</summary>
    ScriptTarget? Find(string? text, bool contains, int? command);

    int InputConst(string name);

    ViewportMapper Mapper { get; }

    /// <summary>Clicks the router delivered so far (to check that a synthetic click hit its target).</summary>
    IReadOnlyList<string> Clicks { get; }

    void Capture(string name);

    string Describe();
}

/// <summary>
/// Test-only synthetic input (<c>--input-script file</c> or <c>builtin:drive</c>). Unlike Slrr.Game's play scripts, which call the UI
/// driver directly, every action here becomes a raw window event (mouse move/press/release at window coordinates computed from the
/// gadget's box, key down/up by key name, characters) that goes through the same <see cref="InputRouter"/>, coordinate mapping and
/// hit testing as a real mouse and keyboard. Commands (one per line, <c>#</c> comments):
/// <code>
/// wait 2.5                       until section|screen|text "..."|track [timeout s]
/// click "NEW CAREER"             click~ "part of a label"        clickcmd SELECT|n
/// key Enter                      keydown Up / keyup Up            type "Bob"
/// mouse 400 300                  (800x600 virtual UI pixels)      pad RightTrigger 1
/// on screen~ Options click "OK"  (one-shot handler while waiting)
/// expect section Garage | expect track | expect speed &gt; 30 | expect text "..."
/// capture name                   print                            quit
/// </code>
/// </summary>
public sealed class InputScript
{
    public const float TapTime = 0.12f;
    public const float ClickRetry = 3f;

    private readonly List<Step> _steps;
    private readonly List<(float At, InputEvent Event)> _timed = [];
    private readonly List<(string Screen, string Label, bool Fired)> _handlers = [];
    private int _index;
    private float? _waitUntil;
    private (Func<IScriptContext, bool> Done, float Deadline, string What)? _until;
    private float? _clickDeadline;
    private (string What, int Before, float Deadline)? _verifyClick;
    private bool _yield;

    private InputScript(List<Step> steps) => _steps = steps;

    public sealed record Step(int Line, string Verb, IReadOnlyList<string> Args, string Text);

    public IReadOnlyList<Step> Steps => _steps;

    public List<string> Log { get; } = [];

    public bool Done => _index >= _steps.Count && _timed.Count == 0;

    public bool Failed { get; private set; }

    public string? Error { get; private set; }

    public bool QuitRequested { get; private set; }

    public int Clicks { get; private set; }

    /// <summary>Loads a script file, or an embedded one with <c>builtin:name</c>.</summary>
    public static InputScript Load(string spec)
    {
        if (spec.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
        {
            var name = spec["builtin:".Length..];
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Slrr.App.Scripts.{name}.input")
                          ?? throw new ArgumentException($"no built-in input script '{name}' (known: drive)");
            using var r = new StreamReader(s, Encoding.UTF8);
            return Parse(r.ReadToEnd());
        }

        return Parse(File.ReadAllText(spec));
    }

    public static InputScript Parse(string text)
    {
        var steps = new List<Step>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var tokens = Tokenize(lines[i]);
            if (tokens.Count == 0)
            {
                continue;
            }

            steps.Add(new Step(i + 1, tokens[0].ToLowerInvariant(), tokens.Skip(1).ToList(), lines[i].Trim()));
        }

        return new InputScript(steps);
    }

    /// <summary>Whitespace-separated tokens, double quotes group, <c>#</c> outside quotes starts a comment.</summary>
    public static List<string> Tokenize(string line)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
                continue;
            }

            if (!quoted && c == '#')
            {
                break;
            }

            if (!quoted && char.IsWhiteSpace(c))
            {
                if (any)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                    any = false;
                }

                continue;
            }

            sb.Append(c);
            any = true;
        }

        if (any)
        {
            list.Add(sb.ToString());
        }

        return list;
    }

    /// <summary>Advances the script by one app loop iteration, appending synthetic events for the router.</summary>
    public void Update(IScriptContext ctx, List<InputEvent> events)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(events);
        if (Failed)
        {
            return;
        }

        var now = ctx.Time;
        foreach (var t in _timed.Where(t => t.At <= now + 1e-5f).ToList())
        {
            _timed.Remove(t);
            events.Add(t.Event);
        }

        for (var i = 0; i < _handlers.Count; i++)
        {
            var h = _handlers[i];
            if (!h.Fired && ctx.Screen.Contains(h.Screen, StringComparison.OrdinalIgnoreCase) && ctx.Find(h.Label, false, null) is { } target)
            {
                _handlers[i] = h with { Fired = true };
                EmitClick(ctx, target, events, $"handler on {h.Screen}");
                return;
            }
        }

        if (_yield)
        {
            _yield = false;
            return;
        }

        if (_verifyClick is { } v)
        {
            if (ctx.Clicks.Count > v.Before)
            {
                Log.Add($"  -> {ctx.Clicks[^1]}");
                _verifyClick = null;
            }
            else if (now > v.Deadline)
            {
                Fail($"the synthetic click on {v.What} reached no gadget (mapping or hit test mismatch)");
                return;
            }
            else
            {
                return;
            }
        }

        if (_waitUntil is { } w)
        {
            if (now + 1e-5f < w)
            {
                return;
            }

            _waitUntil = null;
        }

        if (_until is { } u)
        {
            if (u.Done(ctx))
            {
                Log.Add(string.Create(CultureInfo.InvariantCulture, $"[{now,7:F2}] reached {u.What}"));
                _until = null;
            }
            else if (now > u.Deadline)
            {
                Fail($"timeout waiting for {u.What} (state: {ctx.Describe()})");
                return;
            }
            else
            {
                return;
            }
        }

        // Run commands until one has to wait.
        while (_index < _steps.Count && !Failed && !_yield && _waitUntil is null && _until is null && _verifyClick is null && !QuitRequested)
        {
            var step = _steps[_index];
            try
            {
                if (!Execute(step, ctx, events, now))
                {
                    return; // retry this step next iteration
                }
            }
            catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException)
            {
                Fail($"line {step.Line} '{step.Text}': {e.Message}");
                return;
            }

            _index++;
        }
    }

    private bool Execute(Step s, IScriptContext ctx, List<InputEvent> events, float now)
    {
        string Arg(int i) => i < s.Args.Count ? s.Args[i] : throw new ArgumentException($"'{s.Verb}' needs more arguments");
        float Num(int i, float fallback) => i < s.Args.Count ? float.Parse(s.Args[i], NumberStyles.Float, CultureInfo.InvariantCulture) : fallback;
        if (s.Verb is not ("wait"))
        {
            Log.Add(string.Create(CultureInfo.InvariantCulture, $"[{now,7:F2}] {s.Text}"));
        }

        switch (s.Verb)
        {
            case "wait":
                _waitUntil = now + Num(0, 1);
                return true;
            case "until":
                var timeout = Num(Arg(0) is "track" ? 1 : 2, 30);
                Func<IScriptContext, bool> done = Arg(0).ToLowerInvariant() switch
                {
                    "section" => c => string.Equals(c.Section, Arg(1), StringComparison.OrdinalIgnoreCase),
                    "screen" => c => c.Screen.Contains(Arg(1), StringComparison.OrdinalIgnoreCase),
                    "text" => c => c.HasText(Arg(1)),
                    "track" => c => c.InTrack,
                    var x => throw new ArgumentException($"until {x}?"),
                };
                _until = (done, now + timeout, string.Join(' ', s.Args));
                return true;
            case "click" or "click~" or "clickcmd":
                ScriptTarget? target = s.Verb == "clickcmd"
                    ? ctx.Find(null, false, int.TryParse(Arg(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n :
                        ctx.InputConst(Arg(0).StartsWith("AXIS_", StringComparison.OrdinalIgnoreCase) ? Arg(0).ToUpperInvariant() : "AXIS_" + Arg(0).ToUpperInvariant()))
                    : ctx.Find(Arg(0), s.Verb == "click~", null);
                if (target is null)
                {
                    _clickDeadline ??= now + ClickRetry;
                    if (now > _clickDeadline)
                    {
                        _clickDeadline = null;
                        throw new InvalidOperationException($"nothing to click for {string.Join(' ', s.Args)} (state: {ctx.Describe()})");
                    }

                    Log.RemoveAt(Log.Count - 1);
                    return false;
                }

                _clickDeadline = null;
                EmitClick(ctx, target.Value, events, string.Join(' ', s.Args));
                return true;
            case "key":
                var key = KeyMap.Parse(Arg(0));
                events.Add(InputEvent.Down(key));
                _timed.Add((now + TapTime, InputEvent.Up(key)));
                _waitUntil = now + (2 * TapTime);
                return true;
            case "keydown":
                events.Add(InputEvent.Down(KeyMap.Parse(Arg(0))));
                return true;
            case "keyup":
                events.Add(InputEvent.Up(KeyMap.Parse(Arg(0))));
                return true;
            case "type":
                var text = Arg(0);
                for (var i = 0; i < text.Length; i++)
                {
                    var (k, _) = KeyMap.ForChar(text[i]);
                    var at = now + (i * 2 * TapTime);
                    _timed.Add((at, InputEvent.Down(k)));
                    _timed.Add((at, InputEvent.Typed(text[i])));
                    _timed.Add((at + TapTime, InputEvent.Up(k)));
                }

                // the first press goes out now so the down/char pair stays adjacent in this iteration's events
                foreach (var t in _timed.Where(t => t.At <= now + 1e-5f).ToList())
                {
                    _timed.Remove(t);
                    events.Add(t.Event);
                }

                _waitUntil = now + (text.Length * 2 * TapTime);
                return true;
            case "mouse":
                events.Add(InputEvent.Move(ctx.Mapper.VirtualToWindow(new Vector2(Num(0, 400), Num(1, 300)))));
                return true;
            case "pad":
                events.Add(Arg(0).StartsWith("button:", StringComparison.OrdinalIgnoreCase)
                    ? InputEvent.PadKey(Enum.Parse<PadButton>(Arg(0)[7..], true), Num(1, 1) > 0.5f)
                    : InputEvent.Axis(Enum.Parse<PadAxis>(Arg(0), true), Num(1, 1)));
                return true;
            case "on":
                // on screen~ <Name> click "<label>"
                if (s.Args.Count < 4 || !s.Args[0].StartsWith("screen", StringComparison.OrdinalIgnoreCase) || s.Args[2] != "click")
                {
                    throw new ArgumentException("usage: on screen~ <Name> click \"label\"");
                }

                _handlers.Add((s.Args[1], s.Args[3], false));
                return true;
            case "expect":
                var ok = Arg(0).ToLowerInvariant() switch
                {
                    "section" => string.Equals(ctx.Section, Arg(1), StringComparison.OrdinalIgnoreCase),
                    "track" => ctx.InTrack,
                    "text" => ctx.HasText(Arg(1)),
                    "speed" => Compare(ctx.SpeedKmh, Arg(1), Num(2, 0)),
                    var x => throw new ArgumentException($"expect {x}?"),
                };
                if (!ok)
                {
                    throw new InvalidOperationException($"expectation failed (state: {ctx.Describe()})");
                }

                return true;
            case "capture":
                ctx.Capture(Arg(0));
                _yield = true; // the picture is taken after this iteration's frame, before the next command acts (also while paused)
                return true;
            case "print":
                Log.Add("  " + ctx.Describe());
                return true;
            case "quit":
                QuitRequested = true;
                return true;
            default:
                throw new ArgumentException($"unknown command '{s.Verb}'");
        }
    }

    private static bool Compare(float value, string op, float rhs) => op switch
    {
        ">" => value > rhs,
        ">=" => value >= rhs,
        "<" => value < rhs,
        "<=" => value <= rhs,
        _ => throw new ArgumentException($"comparison '{op}'?"),
    };

    private void EmitClick(IScriptContext ctx, ScriptTarget target, List<InputEvent> events, string what)
    {
        var p = ctx.Mapper.OsdToWindow(target.Rect.Center);
        events.Add(InputEvent.Move(p));
        events.Add(InputEvent.Press(p));
        events.Add(InputEvent.Release(p));
        Clicks++;
        Log.Add(string.Create(CultureInfo.InvariantCulture, $"  mouse at window ({p.X:F1}, {p.Y:F1}) for {target}"));
        _verifyClick = (what, ctx.Clicks.Count, ctx.Time + 1f);
    }

    private void Fail(string message)
    {
        Failed = true;
        Error = message;
        Log.Add("FAILED: " + message);
    }
}
