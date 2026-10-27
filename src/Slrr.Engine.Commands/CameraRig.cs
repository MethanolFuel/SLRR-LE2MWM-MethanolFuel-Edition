using System.Numerics;

namespace Slrr.Engine.Commands;

/// <summary>An object + offset in its frame (<c>look</c>/<c>move</c> targets). <see cref="ObjectId"/> 0 or an object
/// without a pose means world coordinates.</summary>
public readonly record struct CameraTarget(int ObjectId, Vector3 Offset);

/// <summary>The <c>move</c> anchor: object, pivot offset, stiffness float and the optional second offset.</summary>
public readonly record struct CameraAnchor(int ObjectId, Vector3 Pivot, float Stiffness, Vector3? CameraOffset);

/// <summary>A viewport binding made by <c>render</c> (camera or car interior view).</summary>
/// <param name="SourceId">Camera object or car that renders.</param>
/// <param name="Interior">True for a car's interior camera (<c>render vp owner camIndex</c> sent to a car).</param>
public sealed record ViewBinding(int ViewportId, int SourceId, bool Interior, int CameraIndex, int OwnerId, int Flags);

/// <summary>Resolved camera of a viewport for the renderer.</summary>
public sealed record CameraView(ViewBinding Binding, Pose Pose, float FovDegrees, float Roll);

/// <summary>
/// State of one camera object. Every field mirrors one verb (formats MEASURED); how the fields combine into a pose in
/// <see cref="CameraRig.Step"/> is GUESSED (the exe's camera body simulation was not traced, open question P1 #9).
/// </summary>
public sealed class CameraState
{
    public CameraState(int id) => Id = id;

    public int Id { get; }

    public Vector3 Position { get; set; }

    public Ypr Orientation { get; set; }

    public bool Placed { get; set; }

    /// <summary>Last position written back to the object (to notice script <c>setMatrix</c> calls).</summary>
    public Vector3? Written { get; set; }

    public CameraTarget? Look { get; set; }

    public CameraAnchor? Move { get; set; }

    public float DistMin { get; set; }

    public float DistMax { get; set; } = float.PositiveInfinity;

    public bool HasDist { get; set; }

    public float SmoothPosition { get; set; }

    public float SmoothRotation { get; set; }

    public Vector3 Force { get; set; } = new(1.6f, 0.5f, -0.7f);

    public bool HasForce { get; set; }

    public float Torque { get; set; }

    /// <summary><c>angle a b c d</c>: GUESSED a = yaw offset (rad), b = orbit speed (deg/s), c = elevation (rad).</summary>
    public Vector4 Angle { get; set; }

    public int AngleFields { get; set; }

    public float OrbitYaw { get; set; }

    public float Roll { get; set; }

    public float Height { get; set; }

    public float Pitch { get; set; }

    public float MaxPitch { get; set; } = 1.5f;

    public float Fov { get; set; } = 60f;

    public float TargetFov { get; set; } = 60f;

    public float ZoomRate { get; set; } = 5f;

    public bool AutoZoom { get; set; }

    public Vector3 AutoZoomParams { get; set; }

    /// <summary><c>simulate 1</c> = smoothed camera body, <c>simulate 0</c> = rigid (GUESSED). Default 1.</summary>
    public bool Simulate { get; set; } = true;

    public int Follow { get; set; }

    public int Status { get; set; }

    public int Fog { get; set; }

    public bool Active { get; set; } = true;

    public int Updates { get; set; }

    /// <summary>Verbs this camera received, in order (for transcripts).</summary>
    public List<string> Verbs { get; } = [];
}

/// <summary>
/// Pure-managed, deterministic camera rigs for the camera verbs (<c>look</c>, <c>move</c>, <c>dist</c>, <c>smooth</c>,
/// <c>force</c>, <c>torque</c>, <c>angle</c>, <c>zoom</c>, <c>autozoom</c>, <c>roll</c>, <c>height</c>, <c>pitch</c>,
/// <c>maxpitch</c>, <c>simulate</c>, <c>render</c>, <c>hide</c>, ...) and the car interior views (<c>render vp owner cam</c>
/// sent to a car). Parsing MEASURED (exe formats); the motion model GUESSED:
/// focus = look target (or the object the owning controller drives, for the follow camera bound with
/// <c>camera id</c>); desired position = move anchor (+ second offset) or a point behind the focus at the current
/// distance clamped to [dist min, max] and <c>force.X</c> metres up; exponential smoothing with time constant
/// <c>move</c> stiffness / <c>smooth</c>; the camera faces the focus with <c>roll</c>.
/// </summary>
public sealed class CameraRig(IObjectDirectory world, IVehicleState vehicles) : ICameraRig
{
    private readonly Dictionary<int, CameraState> _cams = [];
    private readonly Dictionary<int, ViewBinding> _views = [];

    /// <summary>For a camera bound to a controller (<c>camera id</c>): the object that controller drives (0 = none).</summary>
    public Func<int, int> FollowTargetOf { get; set; } = _ => 0;

    public IReadOnlyCollection<CameraState> Cameras => _cams.Values;

    public IReadOnlyDictionary<int, ViewBinding> Views => _views;

    /// <summary>Seat position of interior cameras in the car frame (GUESSED).</summary>
    public Vector3 InteriorEye { get; set; } = new(-0.35f, 1.1f, 0.1f);

    public CameraState? Get(int cameraId) => _cams.GetValueOrDefault(cameraId);

    public CameraState GetOrCreate(int id)
    {
        if (_cams.TryGetValue(id, out var c))
        {
            return c;
        }

        c = new CameraState(id);
        if (world.TryGetPose(id, out var p))
        {
            c.Position = p.Position;
            c.Orientation = p.Orientation;
            c.Placed = true;
        }

        _cams[id] = c;
        return c;
    }

    public bool Execute(ObjectInfo target, ParsedCommand cmd)
    {
        if (target.Kind == "car")
        {
            return ExecuteInterior(target, cmd);
        }

        var c = GetOrCreate(target.Id);
        c.Verbs.Add(cmd.Text);
        switch (cmd.Verb)
        {
            case "look" when cmd.Fields >= 1:
                c.Look = new CameraTarget(cmd.Int(0), cmd.Vec(1));
                return true;
            case "move" when cmd.Fields >= 1:
                c.Move = new CameraAnchor(cmd.Int(0), cmd.Vec(1), cmd.Float(4), cmd.Fields >= 8 ? cmd.Vec(5) : null);
                return true;
            case "dist" when cmd.Fields >= 1:
                c.DistMin = cmd.Float(0);
                c.DistMax = cmd.Fields >= 2 ? cmd.Float(1) : c.DistMin;
                c.HasDist = true;
                return true;
            case "smooth" when cmd.Fields >= 1:
                c.SmoothPosition = cmd.Float(0);
                c.SmoothRotation = cmd.Float(1, c.SmoothPosition);
                return true;
            case "force" when cmd.Fields >= 1:
                c.Force = cmd.Vec(0, c.Force);
                c.HasForce = true;
                return true;
            case "torque" when cmd.Fields >= 1:
                c.Torque = cmd.Float(0);
                return true;
            case "angle":
                c.Angle = new Vector4(cmd.Float(0), cmd.Float(1), cmd.Float(2), cmd.Float(3));
                c.AngleFields = cmd.Fields;
                c.OrbitYaw = cmd.Float(0);
                return true;
            case "zoom" when cmd.Fields >= 1:
                c.TargetFov = cmd.Float(0);
                c.ZoomRate = cmd.Float(1, c.ZoomRate);
                c.AutoZoom = false;
                return true;
            case "autozoom" when cmd.Fields >= 1:
                c.AutoZoom = cmd.Int(0) != 0;
                c.AutoZoomParams = cmd.Vec(1);
                return true;
            case "roll" when cmd.Fields >= 1:
                c.Roll = cmd.Float(0);
                return true;
            case "height" when cmd.Fields >= 1:
                c.Height = cmd.Float(0);
                return true;
            case "pitch" when cmd.Fields >= 1:
                c.Pitch = cmd.Float(0);
                return true;
            case "maxpitch" when cmd.Fields >= 1:
                c.MaxPitch = cmd.Float(0);
                return true;
            case "simulate" when cmd.Fields >= 1:
                c.Simulate = cmd.Int(0) != 0;
                return true;
            case "follow" when cmd.Fields >= 1:
                c.Follow = cmd.Int(0);
                return true;
            case "status" when cmd.Fields >= 1:
                c.Status = cmd.Int(0);
                return true;
            case "fog":
                c.Fog = cmd.Int(0);
                return true;
            case "activate":
                c.Active = true;
                return true;
            case "deactivate":
                c.Active = false;
                return true;
            case "control":
                return true;
            case "render" when cmd.Fields >= 1:
                Bind(new ViewBinding(cmd.Int(0), c.Id, false, cmd.Int(1), cmd.Int(2), cmd.Int(4)));
                return true;
            case "hide" when cmd.Fields >= 1:
                Unbind(cmd.Int(0), c.Id);
                return true;
            default:
                return false;
        }
    }

    private bool ExecuteInterior(ObjectInfo car, ParsedCommand cmd)
    {
        switch (cmd.Verb)
        {
            case "render" when cmd.Fields >= 1:
                // car copy "%*s %d %d %d" (0x61047c): viewport, owner (controller), interior camera index (MEASURED from Track.changeCamInternal / Mirror)
                Bind(new ViewBinding(cmd.Int(0), car.Id, true, cmd.Int(2), cmd.Int(1), 0));
                return true;
            case "hide" when cmd.Fields >= 1:
                Unbind(cmd.Int(0), car.Id);
                return true;
            default:
                return false;
        }
    }

    private void Bind(ViewBinding b)
    {
        // A viewport shows one source; the scripts always hide the old renderer first (Track.changeCamNone), the
        // replacement here is GUESSED for when they do not.
        _views[b.ViewportId] = b;
    }

    private void Unbind(int viewportId, int sourceId)
    {
        if (_views.TryGetValue(viewportId, out var b) && b.SourceId == sourceId)
        {
            _views.Remove(viewportId);
        }
    }

    /// <summary>Removes cameras whose objects no longer exist (destroyed by the scripts).</summary>
    public void Prune()
    {
        foreach (var id in _cams.Keys.Where(id => world.Get(id) is null).ToList())
        {
            _cams.Remove(id);
        }

        foreach (var vp in _views.Values.Where(v => world.Get(v.SourceId) is null).Select(v => v.ViewportId).ToList())
        {
            _views.Remove(vp);
        }
    }

    public void Step(float dt)
    {
        Prune();
        foreach (var c in _cams.Values)
        {
            StepCamera(c, dt);
        }
    }

    private bool PoseOf(int id, out Pose pose)
    {
        if (id != 0 && vehicles.TryGetVehicle(id, out pose))
        {
            return true;
        }

        if (id != 0 && world.Get(id) is { Kind: not "ground" } && world.TryGetPose(id, out pose))
        {
            return true;
        }

        pose = default;
        return false;
    }

    /// <summary>World point of a target: object frame when the object has a pose, else the offset is a world point.</summary>
    private Vector3 Resolve(CameraTarget t) => PoseOf(t.ObjectId, out var p) ? p.ToWorld(t.Offset) : t.Offset;

    private void StepCamera(CameraState c, float dt)
    {
        if (world.TryGetPose(c.Id, out var node) && (!c.Placed || c.Written is not { } w || Vector3.DistanceSquared(w, node.Position) > 1e-6f))
        {
            // The script placed the camera itself (create params, cam.setMatrix in Track/City): adopt that pose.
            c.Position = node.Position;
            c.Orientation = node.Orientation;
            c.Placed = true;
        }

        Vector3? focus = null;
        Pose followPose = default;
        var following = false;
        if (c.Look is { } look)
        {
            focus = Resolve(look);
        }
        else if (FollowTargetOf(c.Id) is var f and not 0 && PoseOf(f, out followPose))
        {
            following = true;
            focus = followPose.Position + new Vector3(0, 0.5f, 0);
        }

        var desired = c.Position;
        if (c.Move is { } mv)
        {
            desired = PoseOf(mv.ObjectId, out var ap) ? ap.ToWorld(mv.CameraOffset ?? mv.Pivot) : (mv.CameraOffset ?? mv.Pivot);
            desired.Y += c.Height;
        }
        else if (following)
        {
            // Behind the followed car at the current distance (rubber band inside [dist min, max]), force.X metres up.
            var back = -followPose.Orientation.Forward;
            back.Y = 0;
            back = back.LengthSquared() > 1e-6f ? Vector3.Normalize(back) : new Vector3(0, 0, 1);
            var d = Vector3.Distance(c.Position, focus!.Value);
            var dist = c.HasDist ? Math.Clamp(d, c.DistMin, c.DistMax) : Math.Max(d, 4f);
            if (!c.Placed || float.IsNaN(dist))
            {
                dist = c.HasDist ? (c.DistMin + Math.Min(c.DistMax, c.DistMin * 3)) / 2 : 6f;
            }

            desired = followPose.Position + (back * dist) + new Vector3(0, (c.HasForce ? c.Force.X : 1.6f) + c.Height, 0);
        }

        if (focus is { } fp && c.AngleFields >= 2 && (c.Angle.Y != 0 || c.Angle.Z != 0))
        {
            // Orbit (GUESSED roles): b deg/s around the focus at elevation c, at the clamped distance.
            c.OrbitYaw += c.Angle.Y * (MathF.PI / 180f) * dt;
            var r = c.HasDist ? Math.Clamp(Vector3.Distance(c.Position, fp), c.DistMin, c.DistMax) : Vector3.Distance(c.Position, fp);
            if (r < 0.5f)
            {
                r = 5f;
            }

            var elev = c.Angle.Z;
            desired = fp + new Vector3(MathF.Sin(c.OrbitYaw) * MathF.Cos(elev) * r, MathF.Sin(elev) * r, MathF.Cos(c.OrbitYaw) * MathF.Cos(elev) * r);
        }
        else if (focus is { } f2 && c.HasDist)
        {
            var to = desired - f2;
            var len = to.Length();
            if (len > 1e-4f && (len < c.DistMin || len > c.DistMax))
            {
                desired = f2 + (to / len * Math.Clamp(len, c.DistMin, c.DistMax));
            }
        }

        var tau = c.Move is { } m2 ? m2.Stiffness : c.SmoothPosition;
        c.Position = !c.Placed || !c.Simulate || tau <= 1e-4f ? desired : Vector3.Lerp(c.Position, desired, 1f - MathF.Exp(-dt / tau));
        c.Placed = true;
        if (focus is { } f3 && c.HasDist)
        {
            // dist acts as a rope after smoothing (GUESSED): the camera never trails further than max or closer than min.
            var to = c.Position - f3;
            var len = to.Length();
            if (len > 1e-4f && (len < c.DistMin || len > c.DistMax))
            {
                c.Position = f3 + (to / len * Math.Clamp(len, c.DistMin, c.DistMax));
            }
        }

        if (focus is { } aim && Vector3.DistanceSquared(aim, c.Position) > 1e-8f)
        {
            var o = Ypr.FromDirection(aim - c.Position);
            var pitch = Math.Clamp(o.Pitch + c.Pitch, -c.MaxPitch, c.MaxPitch);
            var want = new Ypr(o.Yaw, pitch, c.Roll);
            var tr = c.SmoothRotation;
            if (!c.Simulate || tr <= 1e-4f)
            {
                c.Orientation = want;
            }
            else
            {
                var k = 1f - MathF.Exp(-dt / tr);
                c.Orientation = new Ypr(
                    c.Orientation.Yaw + (Ypr.AngleDiff(want.Yaw, c.Orientation.Yaw) * k),
                    c.Orientation.Pitch + ((want.Pitch - c.Orientation.Pitch) * k),
                    want.Roll);
            }
        }
        else
        {
            c.Orientation = c.Orientation with { Roll = c.Roll };
        }

        if (c.AutoZoom && focus is { } az)
        {
            // GUESSED: AutoZoomParams.Y = fraction of the view a 4 m object fills.
            var frac = Math.Clamp(c.AutoZoomParams.Y, 0.05f, 1f);
            var dd = Math.Max(1f, Vector3.Distance(az, c.Position));
            c.TargetFov = Math.Clamp(2f * MathF.Atan(2f / (dd * frac)) * (180f / MathF.PI), 5f, 90f);
        }

        var zk = Math.Clamp(c.ZoomRate * dt, 0f, 1f);
        c.Fov += (c.TargetFov - c.Fov) * zk;
        c.Updates++;
        world.SetPose(c.Id, new Pose(c.Position, c.Orientation, default));
        c.Written = c.Position;
    }

    public CameraView? ViewFor(int viewportId)
    {
        if (!_views.TryGetValue(viewportId, out var b))
        {
            return null;
        }

        if (b.Interior)
        {
            if (!PoseOf(b.SourceId, out var car))
            {
                return null;
            }

            return new CameraView(b, new Pose(car.ToWorld(InteriorEye), car.Orientation, car.Velocity), 75f, car.Orientation.Roll);
        }

        return _cams.TryGetValue(b.SourceId, out var c)
            ? new CameraView(b, new Pose(c.Position, c.Orientation, default), c.Fov, c.Roll)
            : null;
    }
}
