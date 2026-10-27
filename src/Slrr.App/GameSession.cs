using System.Diagnostics;
using System.Globalization;
using Slrr.Game;
using Slrr.Race;

namespace Slrr.App;

/// <summary>Console + file log with wall-clock offsets.</summary>
public sealed class AppLog : IDisposable
{
    private readonly TextWriter _console;
    private readonly StreamWriter? _file;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public AppLog(TextWriter console, string? path)
    {
        _console = console;
        if (path is not null)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
                _file = new StreamWriter(path, append: false) { AutoFlush = true };
                Path = path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                console.WriteLine($"[log] cannot write {path}: {e.Message}");
            }
        }
    }

    public string? Path { get; }

    public bool Verbose { get; set; }

    public void Info(string line)
    {
        var s = string.Create(CultureInfo.InvariantCulture, $"[{_clock.Elapsed.TotalSeconds,7:F2}] {line}");
        _console.WriteLine(s);
        _file?.WriteLine(s);
    }

    public void Debug(string line)
    {
        if (Verbose)
        {
            Info(line);
        }
        else
        {
            _file?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{_clock.Elapsed.TotalSeconds,7:F2}] {line}"));
        }
    }

    public void Dispose() => _file?.Dispose();
}

/// <summary>
/// The game behind the window (or behind the headless loop): the race-wave composition root (<see cref="RaceHost"/>), input routing with
/// gadget hit testing, and the optional synthetic input script. Single-threaded: everything runs on the thread that owns the GL context.
/// </summary>
public sealed class GameSession : IScriptContext, IDisposable
{
    private readonly List<string> _pendingCaptures = [];

    public GameSession(RaceHost race, AppLog log)
    {
        Race = race;
        Log = log;
        Hits = new GadgetHitTester(race.Host);
        GameInput = new HostGameInput(race.Host, Hits);
        Router = new InputRouter(GameInput);
    }

    public RaceHost Race { get; }

    public GameHost Host => Race.Host;

    public AppLog Log { get; }

    public GadgetHitTester Hits { get; }

    public HostGameInput GameInput { get; }

    public InputRouter Router { get; }

    public InputScript? Script { get; set; }

    public ViewportMapper Mapper
    {
        get => Router.Mapper;
        set => Router.Mapper = value;
    }

    public long HostFrames { get; private set; }

    public double HostMsTotal { get; private set; }

    public double HostMsMax { get; private set; }

    public int ScriptErrorsSeen { get; private set; }

    /// <summary>Capture names the script asked for since the last <see cref="TakeCaptures"/>.</summary>
    public List<string> TakeCaptures()
    {
        var l = _pendingCaptures.ToList();
        _pendingCaptures.Clear();
        return l;
    }

    public bool Boot()
    {
        var sw = Stopwatch.StartNew();
        var ok = Host.Boot();
        var steps = string.Join("; ", Host.BootSteps.Select(s => $"{s.Name} {(s.Ok ? "ok" : "FAILED " + s.Detail)}"));
        Log.Info(string.Create(CultureInfo.InvariantCulture, $"boot {(ok ? "ok" : "FAILED")} in {sw.Elapsed.TotalSeconds:F2} s: {steps}"));
        return ok;
    }

    /// <summary>Script + router for one loop iteration. Returns the app commands (F-keys).</summary>
    public List<AppCommand> ProcessInput(List<InputEvent> events)
    {
        if (Script is { Done: false, Failed: false } s)
        {
            var before = s.Log.Count;
            s.Update(this, events);
            foreach (var l in s.Log.Skip(before))
            {
                Log.Info("script: " + l);
            }
        }

        var commands = new List<AppCommand>();
        Router.Process(events, commands);
        events.Clear();
        return commands;
    }

    /// <summary>One fixed host step (scripts, events, physics, audio).</summary>
    public void Step()
    {
        var sw = Stopwatch.StartNew();
        Host.RunFrame();
        var ms = sw.Elapsed.TotalMilliseconds;
        HostFrames++;
        HostMsTotal += ms;
        HostMsMax = Math.Max(HostMsMax, ms);
        if (Host.ThreadErrors.Count > ScriptErrorsSeen)
        {
            foreach (var t in Host.ThreadErrors.Skip(ScriptErrorsSeen))
            {
                Log.Info("script thread error: " + t);
            }

            ScriptErrorsSeen = Host.ThreadErrors.Count;
        }
    }

    // ------------------------------------------------------------------ IScriptContext

    public float Time => Host.Vm.Time;

    public string Section => Race.Section;

    public string Screen => Host.Screens.Current;

    public bool InTrack => Race.InTrack;

    public float SpeedKmh => (Race.PlayerTelemetry?.Speed ?? 0f) * 3.6f;

    public bool HasText(string text) => Host.Driver.VisibleStrings().Any(s => s.Contains(text, StringComparison.OrdinalIgnoreCase));

    public ScriptTarget? Find(string? text, bool contains, int? command) =>
        (command is { } c ? Hits.FindCommand(c) : text is null ? null : Hits.Find(text, contains)) is { } t ? new ScriptTarget(t.Rect, t.ToString()) : null;

    public int InputConst(string name) => Host.Driver.InputConst(name);

    public IReadOnlyList<string> Clicks => Router.Clicks;

    public void Capture(string name)
    {
        Log.Debug($"capture {name} requested at host frame {Host.Frame}");
        _pendingCaptures.Add(name);
    }

    public string Describe()
    {
        var t = Race.PlayerTelemetry;
        return string.Create(CultureInfo.InvariantCulture,
            $"t {Time:F1} s, section {Section}, screen {Screen}, money {Host.View.Money}, car {Race.PlayerCar?.Class.Name ?? "none"}, in track {InTrack}, " +
            $"speed {SpeedKmh:F1} km/h{(t is null ? "" : $" rpm {t.Rpm:F0} gear {t.Gear}")}, thread errors {Host.ThreadErrors.Count}");
    }

    public void Dispose() => Race.Dispose();
}
