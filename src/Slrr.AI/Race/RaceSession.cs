using System.Numerics;
using Slrr.AI.Driving;
using Slrr.AI.Roads;
using Slrr.Game.Physics;
using Slrr.Physics;

namespace Slrr.AI.Race;

/// <summary>Race rules. Labels per field.</summary>
public sealed record RaceRules
{
    /// <summary>Laps on a closed route (ignored for an open, point-to-point route such as City's day/night races).</summary>
    public int Laps { get; init; } = 2;

    /// <summary>Seconds from the "3" to GO (MEASURED: City's timers 9→10→11→12 and ROCTrack's countdown tick once per
    /// second: "3", "2", "1", "GO!").</summary>
    public double Countdown { get; init; } = 3.0;

    /// <summary>Launch revs on the grid: <c>AI_BeginRace 0.5</c> from the start of the countdown, <c>1.0</c> at "1"
    /// (values MEASURED from City/ROCTrack; meaning as revs GUESSED).</summary>
    public double GridRevs { get; init; } = 0.5;

    public double FinalRevs { get; init; } = 1.0;

    /// <summary>Cars still racing this long after the winner finished are DNF (MEASURED for City's night race:
    /// <c>addTimer(10, 7)</c> after the first car crosses the finish; elsewhere GUESSED).</summary>
    public double FinishTimeout { get; init; } = 10.0;

    /// <summary>A car that makes no progress for this long is DNF (GUESSED).</summary>
    public double NoProgressTimeout { get; init; } = 60.0;

    /// <summary>Hard limit on the race time (GUESSED).</summary>
    public double MaxTime { get; init; } = 900.0;

    /// <summary>A checkpoint counts within its half width plus this margin (GUESSED).</summary>
    public double CheckpointMargin { get; init; } = 10.0;

    /// <summary>Rubber-banding (off by default): an AI car up to <see cref="RubberBandRange"/> metres ahead of the reference
    /// (the player's car, else the field's mean progress) scales its speed targets down by up to <see cref="RubberBandSlow"/>,
    /// behind it up by up to <see cref="RubberBandBoost"/>. GUESSED.</summary>
    public bool RubberBand { get; init; }

    public double RubberBandRange { get; init; } = 150;

    public double RubberBandSlow { get; init; } = 0.08;

    public double RubberBandBoost { get; init; } = 0.05;
}

public enum EntrantStatus
{
    Grid,
    Racing,
    Finished,
    Dnf,
}

/// <summary>One car in a <see cref="RaceSession"/>.</summary>
public sealed class RaceEntrant
{
    internal RaceEntrant(int index, string name, SimCar car, PathFollowingDriver? driver)
    {
        Index = index;
        Name = name;
        Car = car;
        Driver = driver;
    }

    public int Index { get; }

    public string Name { get; }

    public SimCar Car { get; }

    /// <summary>The AI driver (null = driven by another input, e.g. the player).</summary>
    public PathFollowingDriver? Driver { get; }

    public EntrantStatus Status { get; internal set; } = EntrantStatus.Grid;

    public string? DnfReason { get; internal set; }

    /// <summary>Unwrapped distance along the route from the start line (negative on the grid behind it).</summary>
    public double Progress { get; internal set; }

    public int NextCheckpoint { get; internal set; }

    public int Laps { get; internal set; }

    public List<double> LapTimes { get; } = [];

    public List<(int Checkpoint, int Lap, double Time)> Splits { get; } = [];

    public double? FinishTime { get; internal set; }

    /// <summary>Largest |lateral distance| from the route centreline while racing (m).</summary>
    public double MaxLateral { get; internal set; }

    /// <summary>Steps (while racing) with the car outside the route's half width.</summary>
    public long OffRoadSteps { get; internal set; }

    public long RacingSteps { get; internal set; }

    internal double SMod;
    internal double LastProgressTime;
    internal double BestProgress;
    internal readonly List<double> Marks = []; // time at every 10 m of progress

    public override string ToString() => $"{Name} {Status} laps {Laps} progress {Progress:F0}";
}

/// <summary>
/// A headless race run by this project (no scripts): grid placement, countdown, checkpoints and laps, positions and gaps,
/// finish detection, DNF and results. The shipped scripts run their own races through triggers and the brain verbs
/// (see <see cref="AiRaceDirector"/>); this session is for AI tests, the CLI and any host that wants managed races.
/// Rules are labelled in <see cref="RaceRules"/>; the bookkeeping (progress = projection on the route, a checkpoint counts
/// when the progress passes its arc length within the gate width) is GUESSED.
/// </summary>
public sealed class RaceSession
{
    public const double MarkSpacing = 10.0;

    private readonly PhysicsScene _scene;
    private readonly List<RaceEntrant> _entrants = [];
    private readonly IReadOnlyList<Checkpoint> _checkpoints;
    private double _startTime = double.NaN;
    private double _countdownStart = double.NaN;
    private double _firstFinish = double.NaN;

    public RaceSession(PhysicsScene scene, IRoadSource roads, IRoadPath route, RaceRules? rules = null)
    {
        _scene = scene;
        Roads = roads;
        Route = route;
        Rules = rules ?? new RaceRules();
        _checkpoints = roads.Checkpoints(route);
        scene.Stepped += OnStepped;
    }

    public IRoadSource Roads { get; }

    public IRoadPath Route { get; }

    public RaceRules Rules { get; }

    public IReadOnlyList<Checkpoint> Checkpoints => _checkpoints;

    public IReadOnlyList<RaceEntrant> Entrants => _entrants;

    public int LapsToRun => Route.Closed ? Math.Max(1, Rules.Laps) : 1;

    /// <summary>Race distance (m).</summary>
    public double Distance => Route.Length * LapsToRun;

    public RacePhase Phase { get; private set; } = RacePhase.Setup;

    /// <summary>Scene time of GO (NaN before).</summary>
    public double StartTime => _startTime;

    public double RaceTime => double.IsNaN(_startTime) ? 0 : _scene.Time - _startTime;

    /// <summary>Raised once per finisher and once per DNF.</summary>
    public event Action<RaceEntrant>? EntrantDone;

    public event Action<RaceSession>? Finished;

    /// <summary>Adds a car; with <paramref name="ai"/> an AI driver is created and attached as its input.</summary>
    public RaceEntrant Add(SimCar car, string name, bool ai = true, VehicleCaps? caps = null, DriverSettings? settings = null)
    {
        PathFollowingDriver? driver = null;
        if (ai)
        {
            driver = new PathFollowingDriver(car, caps, settings) { Others = _scene.AllCars };
            car.Input = driver;
        }

        var e = new RaceEntrant(_entrants.Count, name, car, driver);
        _entrants.Add(e);
        return e;
    }

    /// <summary>Places every entrant on its grid slot (held, engine idling: what the scripts' <c>reset; setMatrix; stop; idle</c> do).</summary>
    public void PlaceOnGrid()
    {
        var slots = Roads.StartGrid(Route, _entrants.Count);
        for (var i = 0; i < _entrants.Count; i++)
        {
            var e = _entrants[i];
            var slot = slots[i];
            e.Car.Place(new Vec3(slot.Position.X, slot.Position.Y + 0.5, slot.Position.Z), Conv.FromYpr(slot.Yaw, 0, 0));
            e.Car.State = CarRunState.Held;
            e.Status = EntrantStatus.Grid;
            // open routes may cross themselves (the ROC spline does): search near the start only
            var s = Route.Closed ? Route.Project(slot.Position) : Route.Project(slot.Position, 0f, 60f);
            e.SMod = s;
            e.Progress = Route.Closed && s > Route.Length / 2 ? s - Route.Length : s;
            if (!Route.Closed && Vector3.Dot(slot.Position - Route.Sample(0).Position, Route.Sample(0).Tangent) < 0)
            {
                e.Progress = Vector3.Dot(slot.Position - Route.Sample(0).Position, Route.Sample(0).Tangent);
            }

            if (!Route.Closed)
            {
                e.SMod = e.Progress; // open routes track the extended arc length (negative behind the start)
            }

            if (e.Driver is { } d)
            {
                d.SetPath(Route);
                d.LaneOffset = slot.Column == 0 ? RaceLayout.ColumnOffset : -RaceLayout.ColumnOffset; // keep the grid column until the field spreads (GUESSED)
                d.Mode = DriverMode.Launch;
                d.LaunchThrottle = Rules.GridRevs;
            }
        }

        Phase = RacePhase.Grid;
    }

    /// <summary>Starts the countdown (the session then starts the race by itself after <see cref="RaceRules.Countdown"/>).</summary>
    public void StartCountdown()
    {
        if (Phase == RacePhase.Setup)
        {
            PlaceOnGrid();
        }

        _countdownStart = _scene.Time;
        Phase = RacePhase.Countdown;
    }

    private void Go(double t)
    {
        _startTime = t;
        Phase = RacePhase.Racing;
        foreach (var e in _entrants)
        {
            e.Car.State = CarRunState.Driving;
            e.Status = EntrantStatus.Racing;
            e.LastProgressTime = t;
            e.BestProgress = e.Progress;
            if (e.Driver is { } d)
            {
                d.Mode = DriverMode.Drive;
            }
        }
    }

    private void OnStepped(PhysicsScene scene)
    {
        var t = scene.Time;
        switch (Phase)
        {
            case RacePhase.Countdown:
                var left = Rules.Countdown - (t - _countdownStart);
                foreach (var e in _entrants)
                {
                    if (e.Driver is { } d)
                    {
                        d.LaunchThrottle = left <= 1.0 ? Rules.FinalRevs : Rules.GridRevs;
                    }
                }

                if (left <= 1e-9)
                {
                    Go(t);
                }

                return;
            case RacePhase.Racing:
                break;
            default:
                return;
        }

        foreach (var e in _entrants)
        {
            if (e.Status == EntrantStatus.Racing)
            {
                Track(e, t);
            }
        }

        if (Rules.RubberBand)
        {
            RubberBand();
        }

        // DNF rules
        var raceTime = t - _startTime;
        foreach (var e in _entrants.Where(e => e.Status == EntrantStatus.Racing))
        {
            string? reason = null;
            if (!double.IsNaN(_firstFinish) && t - _firstFinish >= Rules.FinishTimeout)
            {
                reason = "finish timeout";
            }
            else if (t - e.LastProgressTime >= Rules.NoProgressTimeout)
            {
                reason = "no progress";
            }
            else if (raceTime >= Rules.MaxTime)
            {
                reason = "race time limit";
            }

            if (reason is not null)
            {
                e.Status = EntrantStatus.Dnf;
                e.DnfReason = reason;
                if (e.Driver is { } d)
                {
                    d.Mode = DriverMode.Hold;
                }

                EntrantDone?.Invoke(e);
            }
        }

        if (_entrants.All(e => e.Status is EntrantStatus.Finished or EntrantStatus.Dnf))
        {
            Phase = RacePhase.Done;
            Finished?.Invoke(this);
        }
    }

    private void Track(RaceEntrant e, double t)
    {
        var p = e.Car.Position.ToVector3();
        var s = (double)Route.Project(p, (float)Math.Clamp(e.SMod, 0, Route.Length), 30f);
        if (!Route.Closed && (s >= Route.Length - 0.01 || s <= 0.01))
        {
            // past an end of an open route: continue along the end tangent (the projection clamps there)
            var end = Route.Sample((float)s);
            s += Math.Clamp(Vector3.Dot(p - end.Position, end.Tangent), s <= 0.01 ? double.NegativeInfinity : 0, s <= 0.01 ? 0 : double.PositiveInfinity);
        }

        var d = s - e.SMod;
        if (Route.Closed)
        {
            if (d > Route.Length / 2)
            {
                d -= Route.Length;
            }
            else if (d < -Route.Length / 2)
            {
                d += Route.Length;
            }
        }

        e.SMod = s;
        e.Progress += d;
        var smp = Route.Sample((float)s);
        var lat = Math.Abs(Vector3.Dot(p - smp.Position, RoadMath.LeftOf(smp.Tangent)));
        e.MaxLateral = Math.Max(e.MaxLateral, lat);
        e.RacingSteps++;
        if (lat > smp.HalfWidth)
        {
            e.OffRoadSteps++;
        }

        if (e.Progress > e.BestProgress + 1.0)
        {
            e.BestProgress = e.Progress;
            e.LastProgressTime = t;
        }

        if (e.Driver is { } drv && e.Progress > 200 && e.FinishTime is null && drv.LaneOffset != 0)
        {
            drv.LaneOffset = 0; // the field has spread: race on the centreline (GUESSED)
        }

        while (e.Progress >= (e.Marks.Count + 1) * MarkSpacing)
        {
            e.Marks.Add(t);
        }

        // checkpoints in order; the last gate of a lap is the start/finish line
        var n = _checkpoints.Count;
        while (e.Status == EntrantStatus.Racing)
        {
            var cp = _checkpoints[e.NextCheckpoint];
            var at = (e.Laps * (double)Route.Length) + cp.S;
            if (e.Progress < at || lat > cp.HalfWidth + Rules.CheckpointMargin)
            {
                break;
            }

            e.Splits.Add((cp.Index, e.Laps, t - _startTime));
            e.NextCheckpoint++;
            if (e.NextCheckpoint == n)
            {
                e.NextCheckpoint = 0;
                e.Laps++;
                var prev = e.LapTimes.Sum();
                e.LapTimes.Add(t - _startTime - prev);
                if (e.Laps >= LapsToRun)
                {
                    e.Status = EntrantStatus.Finished;
                    e.FinishTime = t - _startTime;
                    if (double.IsNaN(_firstFinish))
                    {
                        _firstFinish = t;
                    }

                    if (e.Driver is { } dr)
                    {
                        if (Route.Closed)
                        {
                            // cool-down lap at 12 m/s; the cars still racing pass it (GUESSED)
                            dr.SpeedCap = 12;
                        }
                        else
                        {
                            dr.Mode = DriverMode.Hold; // the scripts brake the car after the finish trigger (City/ROCTrack)
                        }
                    }

                    EntrantDone?.Invoke(e);
                }
            }
        }
    }

    private void RubberBand()
    {
        // reference: the car without an AI driver (the player), else the mean progress of the field (GUESSED)
        var human = _entrants.FirstOrDefault(e => e.Driver is null);
        var racing = _entrants.Where(e => e.Status == EntrantStatus.Racing).ToList();
        if (racing.Count == 0)
        {
            return;
        }

        var reference = human?.Progress ?? racing.Average(e => e.Progress);
        foreach (var e in racing)
        {
            if (e.Driver is not { } d)
            {
                continue;
            }

            var x = Math.Clamp((e.Progress - reference) / Rules.RubberBandRange, -1, 1);
            d.SpeedScale = x > 0 ? 1 - (Rules.RubberBandSlow * x) : 1 + (Rules.RubberBandBoost * -x);
        }
    }

    /// <summary>Current order: finishers by time, then racing cars by progress, then DNFs by progress.</summary>
    public IReadOnlyList<RaceEntrant> Standings() =>
        _entrants
            .OrderBy(e => e.Status switch { EntrantStatus.Finished => 0, EntrantStatus.Dnf => 2, _ => 1 })
            .ThenBy(e => e.FinishTime ?? 0)
            .ThenByDescending(e => e.Progress)
            .ThenBy(e => e.Index)
            .ToList();

    /// <summary>Time gap of <paramref name="e"/> behind <paramref name="leader"/> at the last 10 m mark both passed (s).</summary>
    public static double? Gap(RaceEntrant e, RaceEntrant leader)
    {
        if (ReferenceEquals(e, leader))
        {
            return 0;
        }

        if (e.FinishTime is { } a && leader.FinishTime is { } b)
        {
            return a - b;
        }

        var k = Math.Min(e.Marks.Count, leader.Marks.Count) - 1;
        return k < 0 ? null : e.Marks[k] - leader.Marks[k];
    }

    public RaceResults Results()
    {
        var order = Standings();
        var leader = order.FirstOrDefault();
        var rows = order.Select((e, i) => new RaceResultRow(
            i + 1, e.Name, e.Car.Id, e.Status, e.DnfReason, e.FinishTime, e.LapTimes.ToList(),
            e.LapTimes.Count > 0 ? e.LapTimes.Min() : null, leader is null ? null : Gap(e, leader), e.Progress, e.Driver?.Recoveries.Count ?? 0)).ToList();
        return new RaceResults(Route.Name, LapsToRun, Distance, rows);
    }
}

public enum RacePhase
{
    Setup,
    Grid,
    Countdown,
    Racing,
    Done,
}
