using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Slrr.Engine.MapNatives;
using Slrr.Engine.Resources;
using Slrr.Game;
using Slrr.Vm;
using Slrr.World;

namespace Slrr.Cli;

/// <summary>
/// <c>maps</c>: loads every map of the track selector through the shipped scripts (MainMenu → SELECT TRACK →
/// TrackSelect → map button → TAKE IT → <c>StartMap</c> → <c>Track.enter</c>) on a headless <see cref="GameHost"/> with
/// <see cref="MapNativesModule"/> plugged in, and compares script-visible ground heights with Slrr.World.
/// Integration (lead): ProjectReference to src/Slrr.Engine.MapNatives in Slrr.Cli.csproj and
/// <c>"maps" =&gt; CmdMaps.Run(args[1..], o, e)</c> in Commands.cs.
/// </summary>
public static class CmdMaps
{
    public const string Usage = """
        maps [--root dir] [--map name|index] [--no-map-natives] [--settle seconds] [--list]
          Loads each track-selector map through the shipped scripts on a fresh headless host and reports
          enter time, world build time, missing natives, thread errors and the ground height under the start.
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        string? Opt(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var root = Opt("--root") ?? PlayCommand.FindRoot(Directory.GetCurrentDirectory());
        if (root is null || !File.Exists(Path.Combine(root, "system.rpk")))
        {
            e.WriteLine("maps: game root not found (use --root)");
            e.WriteLine(Usage);
            return 2;
        }

        var harness = new TrackLoadHarness(root)
        {
            UseMapNatives = !args.Contains("--no-map-natives"),
            SettleSeconds = float.Parse(Opt("--settle") ?? "3", CultureInfo.InvariantCulture),
        };
        var names = harness.MapNames();
        if (args.Contains("--list"))
        {
            for (var i = 0; i < names.Count; i++)
            {
                o.WriteLine($"{i,2} {names[i].Name} ({names[i].Class})");
            }

            return 0;
        }

        var only = Opt("--map");
        var picked = Enumerable.Range(0, names.Count)
            .Where(i => only is null || i.ToString(CultureInfo.InvariantCulture) == only || names[i].Name.Trim().Equals(only, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var results = picked.Select(i => harness.RunOne(i)).ToList();
        foreach (var r in results)
        {
            TrackLoadHarness.Print(r, o);
        }

        o.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"summary: {results.Count(r => r.Entered)}/{results.Count} maps entered; {results.Count(r => r.World is not null)} worlds built; map natives {(harness.UseMapNatives ? "on" : "off")}"));
        return results.All(r => r.Entered) ? 0 : 1;
    }
}

/// <summary>Result of loading one map through the track selector.</summary>
public sealed class TrackLoadResult
{
    public required int Index { get; init; }

    public required string MapName { get; init; }

    public required string TrackClass { get; init; }

    /// <summary>Track.enter finished: the active section is the map's Track and its animate hook is registered (last call of Track.enter).</summary>
    public bool Entered { get; set; }

    public string Section { get; set; } = "-";

    /// <summary>Wall time from TAKE IT until <see cref="Entered"/> (or the timeout).</summary>
    public double EnterWallMs { get; set; }

    /// <summary>Virtual (script) time from TAKE IT until entered.</summary>
    public float EnterVirtualS { get; set; }

    public double BootWallMs { get; set; }

    public string? GroundArchive { get; set; }

    public int GroundId { get; set; }

    public StaticWorld? World { get; set; }

    public double WorldBuildMs { get; set; }

    public string? WorldError { get; set; }

    public int PlacedInstances { get; set; }

    public FogSetting? Fog { get; set; }

    public WaterPlane? Water { get; set; }

    public Vector3? PosStart { get; set; }

    /// <summary>The car's stored position after <c>lockCar</c> (<c>setMatrix(posStart, oriStart)</c>).</summary>
    public Vector3? CarPos { get; set; }

    /// <summary>The car's parent after entering (should be the ground: <c>player.car.setParent(map)</c>).</summary>
    public int CarParent { get; set; }

    /// <summary>y of <c>map.alignToRoad(posStart)[0]</c>, run through the VM (script-visible).</summary>
    public float? ScriptGroundY { get; set; }

    /// <summary>y of <c>map.getNearestCross(posStart, 0)</c>, run through the VM.</summary>
    public float? ScriptCrossY { get; set; }

    /// <summary>Slrr.World directly: the ground at posStart searching from posStart.y + 2 m, then from the top.</summary>
    public float? WorldGroundY { get; set; }

    /// <summary>Slrr.World directly: the top-most surface at posStart.</summary>
    public float? WorldTopY { get; set; }

    public int WorldSurface { get; set; } = -1;

    public string? WorldSurfaceName { get; set; }

    public List<string> Missing { get; } = [];

    public List<string> ThreadErrors { get; } = [];

    public List<string> Screens { get; } = [];

    public Dictionary<string, int> GroundCalls { get; set; } = [];

    public int Animations { get; set; }

    public int ParticleSystems { get; set; }

    public string? Note { get; set; }

    /// <summary>Section after leaving the track again (expected: TrackSelect).</summary>
    public string? ExitSection { get; set; }

    public double ExitWallMs { get; set; }

    /// <summary><c>map.unload()</c> in Track.exit dropped the world.</summary>
    public bool WorldDropped { get; set; }
}

/// <summary>Drives the track selector of the shipped scripts on a fresh <see cref="GameHost"/> per map.</summary>
public sealed class TrackLoadHarness(string root)
{
    private const int CmdOk = 1;
    private const int CmdDown = 4;
    private const int CmdMap0 = 10;

    public string Root { get; } = root;

    public bool UseMapNatives { get; init; } = true;

    /// <summary>Script time to keep running after the map was entered (timers, animate callbacks).</summary>
    public float SettleSeconds { get; init; } = 3f;

    public float EnterTimeout { get; init; } = 90f;

    public MapNativesModule? LastModule { get; private set; }

    public GameHost? LastHost { get; private set; }

    /// <summary>A booted host in TrackSelect (null and an error text when the menu path fails).</summary>
    public (GameHost? Host, MapNativesModule? Module, string? Error) OpenTrackSelect()
    {
        GameHost? host = null;
        var module = UseMapNatives
            ? new MapNativesModule(null, new MapWorldOptions { GameRoot = Root }, new MapNativeOptions
            {
                FogSink = (id, c, n, f) =>
                {
                    if (host is not null)
                    {
                        host.Natives.Fog[id] = (c, n, f);
                    }
                },
            })
            : null;
        host = new GameHost(new GameHostOptions { Root = Root, ExtraModules = module is null ? [] : [module] });
        module?.Attach(host.Resources);
        LastHost = host;
        LastModule = module;
        if (!host.Boot())
        {
            return (host, module, "boot failed: " + string.Join("; ", host.BootSteps.Where(s => !s.Ok)));
        }

        if (!host.RunUntil(() => Section(host) == "MainMenu", 30))
        {
            return (host, module, "MainMenu not reached: " + host.Describe());
        }

        host.Input.Click("SELECT TRACK");
        if (!host.RunUntil(() => Section(host) == "TrackSelect", 30))
        {
            return (host, module, "TrackSelect not reached: " + host.Describe());
        }

        host.RunFor(0.5f);
        return (host, module, null);
    }

    public static string Section(GameHost host) => host.ActiveSection is { } s ? ScreenTracker.Short(s.Class.Name) : "-";

    /// <summary>MapName and class of every <c>GameLogic.MapBox</c> entry, in selector order.</summary>
    public List<(string Name, string Class)> MapNames()
    {
        var (host, _, error) = OpenTrackSelect();
        if (host is null || error is not null)
        {
            throw new InvalidOperationException(error ?? "no host");
        }

        using (host)
        {
            return MapBox(host).Select(m => (StaticString(host, m, "MapName") ?? "?", m.Class.Name)).ToList();
        }
    }

    private static List<VmObject> MapBox(GameHost host) =>
        Probe.Vector(host.Vm, Probe.Static(host.Vm, "java.game.GameLogic", "MapBox").AsObject).Select(v => v.AsObject).OfType<VmObject>().ToList();

    private static string? StaticString(GameHost host, VmObject o, string field) =>
        o.Class.FindField(field) is { } f ? Probe.Str(host.Vm, f.IsStatic ? f.Owner.StaticValues[f.Slot] : o.Fields[f.Slot]) : null;

    public TrackLoadResult RunOne(int index)
    {
        var boot = Stopwatch.StartNew();
        var (host, module, error) = OpenTrackSelect();
        var maps = host is null ? [] : MapBox(host);
        var entry = index < maps.Count ? maps[index] : null;
        var r = new TrackLoadResult
        {
            Index = index,
            MapName = host is not null && entry is not null ? StaticString(host, entry, "MapName")?.Trim() ?? "?" : "?",
            TrackClass = entry?.Class.Name ?? "?",
            BootWallMs = boot.Elapsed.TotalMilliseconds,
        };
        if (host is null || error is not null || entry is null)
        {
            r.Note = error ?? "map index out of range";
            host?.Dispose();
            return r;
        }

        using (host)
        {
            var missingBefore = host.Missing.Count;
            var errorsBefore = host.ThreadErrors.Count;
            var screensBefore = host.Screens.Visits.Count;

            // page and map button (TrackSelect.osdCommand: CMD_DOWN, CMD_MAP0 + n -> CurrentMapNumber), then TAKE IT
            for (var page = 0; page < index / 10; page++)
            {
                host.Input.ClickCommand(CmdDown);
                host.RunFor(0.3f);
            }

            host.Input.ClickCommand(CmdMap0 + (index % 10));
            host.RunFor(0.3f);
            var sw = Stopwatch.StartNew();
            var t0 = host.Vm.Time;
            host.Input.ClickCommand(CmdOk);

            // DealerTrack / Physic_Lab_Track open an options dialog first: take its default (OK)
            var dialogDone = false;
            host.RunUntil(() =>
            {
                if (!dialogDone && host.Screens.Current.Contains("Options", StringComparison.Ordinal))
                {
                    dialogDone = true;
                    host.Input.Click("OK");
                }

                return EnteredTrack(host);
            }, EnterTimeout);
            r.EnterWallMs = sw.Elapsed.TotalMilliseconds;
            r.EnterVirtualS = host.Vm.Time - t0;
            r.Entered = EnteredTrack(host);
            host.RunFor(SettleSeconds);
            r.Section = Section(host);

            var track = host.ActiveSection;
            Inspect(host, module, track, r);

            // leave again the way the in-game menu does (GameLogic.changeActiveSection(parentState)): Track.exit ends with map.unload()
            if (r.Entered && track is not null && Probe.Field(track, "parentState").AsObject is { } parent)
            {
                var exit = Stopwatch.StartNew();
                host.CallStaticOnThread("java.game.GameLogic", "changeActiveSection", 30f, Value.Obj(parent));
                host.RunFor(0.5f);
                r.ExitWallMs = exit.Elapsed.TotalMilliseconds;
                r.ExitSection = Section(host);
                r.WorldDropped = module is not null && module.Maps.State(r.GroundId).World is null;
            }

            r.Missing.AddRange(host.Missing.Skip(missingBefore).Select(m => $"{m.Key} x{m.Calls} [{m.Screen}] {string.Join(" < ", m.FirstTrace.Take(2))}"));
            r.ThreadErrors.AddRange(host.ThreadErrors.Skip(errorsBefore));
            r.Screens.AddRange(host.Screens.Visits.Skip(screensBefore).Select(v => v.Key).Distinct());
        }

        return r;
    }

    /// <summary>
    /// The map's Track is the active section and its enter has run to its end: <c>Track.enter</c> calls
    /// <c>enableAnimateHook()</c> as one of its last statements (registerCallback GII_ANIMATE on the track's node).
    /// Valocity is entered through ValoCity_Track.StartMap, so any Track subclass counts.
    /// </summary>
    private static bool EnteredTrack(GameHost host)
    {
        var s = host.ActiveSection;
        if (s is null || !s.Class.IsSubclassOf("java.game.Track"))
        {
            return false;
        }

        return host.Resources.NodeOf(s)?.Callbacks.Contains(HostNatives.GiiAnimate) == true;
    }

    private static void Inspect(GameHost host, MapNativesModule? module, VmObject? track, TrackLoadResult r)
    {
        if (track is null || !track.Class.IsSubclassOf("java.game.Scene"))
        {
            return;
        }

        var map = Probe.Field(track, "map").AsObject;
        var posStart = Probe.Field(track, "posStart").AsObject;
        r.PosStart = ScriptValues.ReadVector(posStart);
        r.GroundId = ResourceEngine.IdOf(map);
        r.GroundArchive = host.Resources.NodeOf(map)?.Archive?.Name;
        if (Probe.Field(host.Player, "car").AsObject is { } car && host.Resources.NodeOf(car) is { } carNode)
        {
            r.CarPos = new Vector3(carNode.Pos[0], carNode.Pos[1], carNode.Pos[2]);
            r.CarParent = carNode.ParentId;
        }

        if (module is null || map is null)
        {
            return;
        }

        var state = module.Maps.State(r.GroundId);
        r.World = state.World;
        r.WorldBuildMs = state.LoadMs;
        r.WorldError = state.LoadError;
        r.PlacedInstances = state.PlacedInstances.Count;
        r.Fog = state.Fog;
        r.Water = state.Water;
        r.GroundCalls = new Dictionary<string, int>(module.Ground.Calls);
        r.Animations = module.Animations.Created.Count;
        r.ParticleSystems = module.Particles.Created.Count;

        if (posStart is not null)
        {
            // script-visible: the same natives City uses to put a car on the road (alignToRoad(p)[0].y, getNearestCross)
            try
            {
                var arr = host.Vm.InvokeVirtual(map, "alignToRoad", Value.Obj(posStart)).AsObject as VmArray;
                r.ScriptGroundY = arr is { Items.Length: > 0 } ? ScriptValues.ReadVector(arr.Items[0].AsObject)?.Y : null;
                r.ScriptCrossY = ScriptValues.ReadVector(host.Vm.InvokeVirtual(map, "getNearestCross", Value.Obj(posStart), Value.Float(0)).AsObject)?.Y;
            }
            catch (VmException ex)
            {
                r.Note = "script query: " + ex.Message;
            }
        }

        if (state.Ground is { } g && r.PosStart is { } p)
        {
            if (MapWorlds.TryGround(g, p, module.Ground.GroundMargin, out var hit))
            {
                r.WorldGroundY = hit.Position.Y;
                r.WorldSurface = hit.Surface;
                r.WorldSurfaceName = hit.Surface >= 0 && hit.Surface < g.Surfaces.Count ? g.Surfaces.Items[hit.Surface].Name : null;
            }

            r.WorldTopY = g.TryGetGround(p.X, p.Z, float.PositiveInfinity, out var top) ? top.Position.Y : null;
        }
    }

    public static void Print(TrackLoadResult r, TextWriter o)
    {
        string F(float? v) => v is { } x ? x.ToString("0.###", CultureInfo.InvariantCulture) : "-";
        string V(Vector3? v) => v is { } x ? $"({F(x.X)}, {F(x.Y)}, {F(x.Z)})" : "-";
        o.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"[{r.Index,2}] {r.MapName} ({r.TrackClass}): {(r.Entered ? "ENTERED" : "NOT ENTERED")} section={r.Section} enter {r.EnterWallMs:F0} ms wall / {r.EnterVirtualS:F2} s script, boot+menu {r.BootWallMs:F0} ms"));
        o.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"     ground {r.GroundArchive ?? "-"} {ResourceIds.Format(r.GroundId)}; world {(r.World is { } w ? $"{w.Collision.TriangleCount} tris, {w.Objects.Count} objects, {r.PlacedInstances} instances, built in {r.WorldBuildMs:F0} ms" : r.WorldError ?? "not built")}"));
        o.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"     posStart {V(r.PosStart)} car {V(r.CarPos)} parent {(r.CarParent == r.GroundId && r.GroundId != 0 ? "= ground" : ResourceIds.Format(r.CarParent))}; ground y: script alignToRoad {F(r.ScriptGroundY)} getNearestCross {F(r.ScriptCrossY)} | world {F(r.WorldGroundY)} (top {F(r.WorldTopY)}, surface {r.WorldSurface} {r.WorldSurfaceName})"));
        if (r.Fog is { } fog)
        {
            o.WriteLine(string.Create(CultureInfo.InvariantCulture, $"     fog 0x{fog.Color:X6} {fog.Near}..{fog.Far}; water {(r.Water is { } wa ? V(wa.Point) : "-")}; anims {r.Animations}; particle systems {r.ParticleSystems}"));
        }

        if (r.GroundCalls.Count > 0)
        {
            o.WriteLine("     GroundRef calls: " + string.Join(", ", r.GroundCalls.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key} {k.Value}")));
        }

        if (r.ExitSection is not null)
        {
            o.WriteLine(string.Create(CultureInfo.InvariantCulture, $"     exit -> {r.ExitSection} in {r.ExitWallMs:F0} ms; world dropped by unload: {r.WorldDropped}"));
        }

        o.WriteLine("     screens: " + string.Join(" -> ", r.Screens));
        foreach (var m in r.Missing)
        {
            o.WriteLine("     missing: " + m);
        }

        foreach (var t in r.ThreadErrors)
        {
            o.WriteLine("     thread error: " + t);
        }

        if (r.Note is not null)
        {
            o.WriteLine("     note: " + r.Note);
        }
    }
}
