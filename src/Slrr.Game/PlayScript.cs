using System.Globalization;
using System.Text;
using Slrr.Vm;

namespace Slrr.Game;

/// <summary>One parsed line of a play script.</summary>
public sealed record PlayStep(int Line, string Verb, IReadOnlyList<string> Args, string Text);

/// <summary>Result of one executed step.</summary>
public sealed record PlayStepResult(PlayStep Step, bool Ok, string Detail, float Time, string Screen);

/// <summary>
/// Scripted input for headless play. One command per line, <c>#</c> starts a comment, arguments are separated by
/// blanks and may be double-quoted. Commands:
/// <code>
/// wait SECONDS                    run frames for that much virtual time
/// frames N                        run N frames
/// until section|screen|text X [T] run frames until the active section is X / the screen key contains X / a visible text contains X (timeout T s, default 30)
/// click "LABEL"                   click the gadget with this label or tooltip (top Osd first)
/// click~ "TEXT"                   click the first gadget whose label/tooltip contains TEXT
/// clickcmd N|AXIS_NAME            click the gadget with this command id
/// osd N                           send osdCommand(N) to the top Osd's handler
/// key NAME                        tap a key (Enter, Esc, Up, A, F1, 28 ...)
/// keydown NAME / keyup NAME
/// type "TEXT"                     type characters
/// axis AXIS_NAME                  tap a virtual axis (SELECT, CANCEL, MENU_LEFT ...)
/// pad DEVICE AXIS VALUE           set a physical game controller axis
/// mouse X Y                       cursor position in OSD coordinates (-1..1)
/// drop "TEXT" car|ID              drag the gadget whose text contains TEXT onto the player's car chassis (or an instance id)
/// save DIR / load DIR             GameLogic.save / GameLogic.load (script path below save/)
/// expect PATH OP VALUE            assert (OP: == != &lt; &lt;= &gt; &gt;= contains); PATH as in GameView.Eval (money, parts, bin, section.class, GameLogic.player.name ...)
/// expect section NAME             assert the active section's class (short name)
/// expect text "TEXT"              assert a visible text contains TEXT
/// expect installed "CLASS-PART"   assert an installed part's class name contains CLASS-PART
/// print PATH|screen|choices|texts write to the transcript
/// stop                            end the script
/// </code>
/// </summary>
public static class PlayScript
{
    public static List<PlayStep> Parse(string text)
    {
        var steps = new List<PlayStep>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var tok = Tokenize(line);
            if (tok.Count == 0)
            {
                continue;
            }

            steps.Add(new PlayStep(i + 1, tok[0].ToLowerInvariant(), tok.Skip(1).ToList(), line));
        }

        return steps;
    }

    public static List<string> Tokenize(string line)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                any = true;
                continue;
            }

            if (!quoted && ch == '#' && sb.Length == 0 && !any)
            {
                break;
            }

            if (!quoted && char.IsWhiteSpace(ch))
            {
                if (any)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                    any = false;
                }

                continue;
            }

            sb.Append(ch);
            any = true;
        }

        if (any)
        {
            list.Add(sb.ToString());
        }

        return list;
    }
}

/// <summary>Runs a play script on a booted <see cref="GameHost"/> and records a transcript.</summary>
public sealed class PlayRunner(GameHost host)
{
    public List<PlayStepResult> Results { get; } = [];

    public List<string> Output { get; } = [];

    public bool StopOnFailure { get; init; }

    public bool Failed => Results.Any(r => !r.Ok);

    public void Run(IEnumerable<PlayStep> steps)
    {
        foreach (var s in steps)
        {
            if (s.Verb == "stop")
            {
                break;
            }

            PlayStepResult r;
            try
            {
                var (ok, detail) = Execute(s);
                r = new PlayStepResult(s, ok, detail, host.Vm.Time, host.Screens.Current);
            }
            catch (Exception ex) when (ex is ArgumentException or VmException or FormatException or InvalidOperationException)
            {
                r = new PlayStepResult(s, false, ex.Message, host.Vm.Time, host.Screens.Current);
            }

            Results.Add(r);
            if (!r.Ok && StopOnFailure)
            {
                break;
            }
        }
    }

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private string A(PlayStep s, int i) => i < s.Args.Count ? s.Args[i] : throw new ArgumentException($"line {s.Line}: '{s.Verb}' needs argument {i + 1}");

    private (bool, string) Execute(PlayStep s)
    {
        var input = host.Input;
        switch (s.Verb)
        {
            case "wait":
                host.RunFor(F(A(s, 0)));
                return (true, string.Empty);
            case "frames":
                host.RunFrames(int.Parse(A(s, 0), CultureInfo.InvariantCulture));
                return (true, string.Empty);
            case "until":
            {
                var what = A(s, 0);
                var arg = A(s, 1);
                var timeout = s.Args.Count > 2 ? F(s.Args[2]) : 30f;
                Func<bool> cond = what switch
                {
                    "section" => () => host.ActiveSection is { } a && ScreenTracker.Short(a.Class.Name).Equals(arg, StringComparison.OrdinalIgnoreCase),
                    "screen" => () => host.Screens.Current.Contains(arg, StringComparison.OrdinalIgnoreCase),
                    "text" => () => host.Driver.VisibleStrings().Any(t => t.Contains(arg, StringComparison.OrdinalIgnoreCase)),
                    _ => throw new ArgumentException($"until: unknown condition '{what}'"),
                };
                var ok = host.RunUntil(cond, timeout);
                return (ok, ok ? $"after {host.Vm.Time:F2}s" : $"timeout {timeout}s, screen {host.Screens.Current}");
            }

            case "click":
                return Settle(Retry(() => input.Click(A(s, 0))), $"'{A(s, 0)}'");
            case "click~":
                return Settle(Retry(() => input.Click(A(s, 0), contains: true)), $"~'{A(s, 0)}'");
            case "clickcmd":
            {
                var a = A(s, 0);
                var cmd = int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : input.AxisCode(a);
                return Settle(Retry(() => input.ClickCommand(cmd)), $"cmd {cmd}");
            }

            case "osd":
                return Settle(input.OsdCommand(int.Parse(A(s, 0), CultureInfo.InvariantCulture)), A(s, 0));
            case "key":
                input.TapKey(InputInjector.KeyCode(A(s, 0)));
                return (true, string.Empty);
            case "keydown":
                input.KeyDown(InputInjector.KeyCode(A(s, 0)));
                return (true, string.Empty);
            case "keyup":
                input.KeyUp(InputInjector.KeyCode(A(s, 0)));
                return (true, string.Empty);
            case "type":
                input.Type(A(s, 0));
                return (true, string.Empty);
            case "axis":
                input.TapAxis(input.AxisCode(A(s, 0)));
                return (true, string.Empty);
            case "pad":
                input.Gamepad(int.Parse(A(s, 0), CultureInfo.InvariantCulture), int.Parse(A(s, 1), CultureInfo.InvariantCulture), F(A(s, 2)));
                host.RunFrame();
                return (true, string.Empty);
            case "mouse":
                input.MoveMouse(F(A(s, 0)), F(A(s, 1)));
                return (true, string.Empty);
            case "drop":
            {
                var target = A(s, 1) == "car" ? Probe.Id(host.View.Chassis) : int.Parse(A(s, 1), CultureInfo.InvariantCulture);
                return Settle(target != 0 && Retry(() => input.DropOnto(A(s, 0), target)), $"onto {(uint)target:X8}");
            }

            case "save":
            {
                var st = host.SaveCareer(A(s, 0));
                return (st == 1, $"GameLogic.save -> {st}");
            }

            case "load":
            {
                var st = host.LoadCareer(A(s, 0));
                return (st == 0, $"GameLogic.load -> {st}");
            }

            case "expect":
                return Expect(s);
            case "print":
                Output.Add($"[{host.Vm.Time,8:F2}s] {Print(A(s, 0))}");
                return (true, string.Empty);
            default:
                throw new ArgumentException($"line {s.Line}: unknown command '{s.Verb}'");
        }
    }

    /// <summary>Seconds a click waits for its gadget to become clickable (dialogs build their Osd over a few frames).</summary>
    public float ClickTimeout { get; init; } = 3f;

    private bool Retry(Func<bool> attempt)
    {
        var end = host.Vm.Time + ClickTimeout;
        while (!attempt())
        {
            if (host.Vm.Time >= end)
            {
                return false;
            }

            host.RunFrame();
        }

        return true;
    }

    private (bool, string) Settle(bool ok, string what)
    {
        host.RunFor(0.25f);
        return (ok, ok ? what : what + " not found/not clickable");
    }

    private string Print(string what) => what switch
    {
        "screen" => host.Screens.Current,
        "choices" => string.Join(" | ", host.Screens.Choices()),
        "texts" => string.Join(" | ", host.Driver.VisibleStrings()),
        _ => $"{what} = {host.View.Format(host.View.Eval(what))}",
    };

    private (bool, string) Expect(PlayStep s)
    {
        var what = A(s, 0);
        switch (what)
        {
            case "section":
            {
                var cur = host.ActiveSection is { } a ? ScreenTracker.Short(a.Class.Name) : "-";
                return (cur.Equals(A(s, 1), StringComparison.OrdinalIgnoreCase), $"section {cur}");
            }

            case "text":
                return (host.Driver.VisibleStrings().Any(t => t.Contains(A(s, 1), StringComparison.OrdinalIgnoreCase)), $"text '{A(s, 1)}'");
            case "installed":
            {
                var hit = host.View.InstalledPartClasses.FirstOrDefault(c => c.Contains(A(s, 1), StringComparison.OrdinalIgnoreCase));
                return (hit is not null, hit ?? $"no installed part matches '{A(s, 1)}' ({host.View.InstalledParts.Count} parts)");
            }
        }

        var value = host.View.Eval(what);
        var op = A(s, 1);
        var expected = A(s, 2);
        var actual = host.View.Format(value);
        bool ok;
        if (op == "contains")
        {
            ok = actual.Contains(expected, StringComparison.OrdinalIgnoreCase);
        }
        else if (value.Kind is VmKind.Int or VmKind.Float && float.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var e))
        {
            var a = value.AsFloat;
            ok = op switch
            {
                "==" => Math.Abs(a - e) < 1e-4f,
                "!=" => Math.Abs(a - e) >= 1e-4f,
                "<" => a < e,
                "<=" => a <= e,
                ">" => a > e,
                ">=" => a >= e,
                _ => throw new ArgumentException($"expect: unknown operator '{op}'"),
            };
        }
        else
        {
            ok = op switch
            {
                "==" => actual == expected,
                "!=" => actual != expected,
                _ => throw new ArgumentException($"expect: operator '{op}' needs a number"),
            };
        }

        return (ok, $"{what} = {actual}");
    }
}
