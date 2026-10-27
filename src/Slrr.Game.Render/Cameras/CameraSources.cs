using System.Globalization;
using System.Numerics;
using Slrr.Engine.Resources;
using Slrr.Engine.Ui;
using Slrr.Game.Render.Cars;
using Slrr.Game.Render.Gl;
using Slrr.Game.Render.Scene;
using Slrr.Render.Content;

namespace Slrr.Game.Render.Cameras;

/// <summary>One 3D view to draw: a camera pose, a scene (subtree of the resource tree) and the part of the screen it fills.</summary>
/// <param name="SceneId">Resource id of the scene root (render instances and cars below it are drawn); 0 = everything.</param>
/// <param name="FovDegrees">Angle of view in degrees; <see cref="FovHorizontal"/> says which axis.</param>
public sealed record CameraBinding(
    string Name, RigPose Pose, float FovDegrees, float Near, float Far, ViewRect Rect, int Layer, int SceneId, string Kind)
{
    public bool FovHorizontal { get; init; } = true;

    /// <summary>The node of a car the rig follows (for diagnostics and the cockpit switch), or 0.</summary>
    public int FollowCarId { get; init; }
}

/// <summary>Source of camera views. The default reads the host; Slrr.Physics (camera verbs) can replace it.</summary>
public interface ICameraSource
{
    IReadOnlyList<CameraBinding> Cameras(IGameWorld world);
}

/// <summary>A fixed list of views (tests, scripted sequences).</summary>
public sealed class FixedCameraSource(params CameraBinding[] cameras) : ICameraSource
{
    public List<CameraBinding> List { get; } = [.. cameras];

    public IReadOnlyList<CameraBinding> Cameras(IGameWorld world) => List;
}

/// <summary>Views computed by a delegate each frame (a scripted rig following a car, for example).</summary>
public sealed class DelegateCameraSource(Func<IGameWorld, IReadOnlyList<CameraBinding>> factory) : ICameraSource
{
    public IReadOnlyList<CameraBinding> Cameras(IGameWorld world) => factory(world);
}

/// <summary>
/// Camera views of the running scripts, two kinds:
/// <list type="number">
/// <item><b>Script cameras</b> (<c>new java.render.Camera(scene, viewport, flags)</c>: MainMenu background, CarSelect, DriverSelect). Pose from
/// <see cref="RenderHooks"/> (the camera object's <c>setMatrix</c>); angle of view = 2 x the stored half angle (read as horizontal, GUESSED);
/// near/far from the model. Only cameras with an active viewport and a recorded pose are used.</item>
/// <item><b>Camera game objects</b> (<c>new GameRef(scene, RID_CAMERA, "x,y,z, yaw,pitch,roll, flags, ..")</c>: Garage, CarMarket, City, Catalog)
/// that the scripts drive with command strings. The verbs are read from the command logs (commands nobody consumed
/// <see cref="ResourceEngine.Commands"/> plus those consumed by Physics, passed as <c>consumed</c>) and interpreted as GUESSED rigs:
/// <c>render vp ..</c> binds the camera (without it the camera is not drawn), <c>move car off dist</c> + <c>look</c> = orbit around a car
/// at <c>dist</c> (clamped to <c>dist min max</c>, eye height from <c>height</c>), <c>move scene pos ..</c> = free position,
/// <c>look scene pos ..</c> = look-at point, <c>zoom fov ..</c> = angle of view, a controller's <c>camera id</c> + <c>controllable car</c> = chase
/// the car.</item>
/// </list>
/// </summary>
public sealed class HostCameraSource(RenderHooks hooks, Func<IEnumerable<(int Target, string Text)>>? consumed = null) : ICameraSource
{
    /// <summary>Default azimuth of the orbit rig when the camera's start position is not near the car (radians; GUESSED).</summary>
    public float DefaultAzimuth { get; set; } = -0.6f;

    public IReadOnlyList<CameraBinding> Cameras(IGameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var list = new List<CameraBinding>();
        ScriptCameras(world, list);
        CommandCameras(world, list);
        return list;
    }

    private void ScriptCameras(IGameWorld world, List<CameraBinding> list)
    {
        foreach (var c in world.Ui.Cameras)
        {
            if (c.Destroyed || world.Ui.FindViewport(c.Viewport) is not { Active: true, Destroyed: false } vp || !hooks.CameraPoses.TryGetValue(c.Object, out var pose))
            {
                continue;
            }

            var sceneId = ResourceEngine.IdOf(c.Parent);
            var rect = vp.Width > 0 && vp.Height > 0 && vp.Left >= 0 && vp.Top >= 0 && vp.Left + vp.Width <= 1.001f && vp.Top + vp.Height <= 1.001f
                ? new ViewRect(vp.Left, vp.Top, vp.Width, vp.Height)
                : ViewRect.Full;
            list.Add(new CameraBinding($"camera@{ResourceIds.Format(sceneId)}", CameraRigs.Free(pose.Position, pose.Ypr), c.HalfFov * 2f,
                Math.Max(c.Near, 0.02f), Math.Max(c.Far, 10f), rect, vp.Layer, sceneId, "script"));
        }
    }

    private static readonly char[] Sep = [' ', '\t'];

    private sealed class Verbs
    {
        public bool Rendered;
        public Vector2? Dist;
        public float? Height;
        public float? Zoom;
        public (int Target, Vector3 Offset, float Arg)? Move;
        public (int Target, Vector3 Offset)? Look;
    }

    private void CommandCameras(IGameWorld world, List<CameraBinding> list)
    {
        var tree = world.Resources.Tree;
        var log = new List<(int Target, string Text)>();
        foreach (var c in world.Resources.Commands)
        {
            log.Add((c.TargetId, c.Text));
        }

        if (consumed is not null)
        {
            log.AddRange(consumed());
        }

        if (log.Count == 0)
        {
            return;
        }

        var cameras = tree.Instances.Where(n => n.NativeKind == "camera").ToList();
        if (cameras.Count == 0)
        {
            return;
        }

        // Controller bindings: "camera <id>" and "controllable <id>" sent to the same target (player controller / bot brain).
        var camOf = new Dictionary<int, int>();
        var carOf = new Dictionary<int, int>();
        var verbs = new Dictionary<int, Verbs>();
        foreach (var (target, text) in log)
        {
            var t = text.Split(Sep, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 2)
            {
                continue;
            }

            switch (t[0])
            {
                case "camera" when int.TryParse(t[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cam):
                    camOf[target] = cam;
                    break;
                case "controllable" when int.TryParse(t[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var car):
                    carOf[target] = car;
                    break;
            }

            if (cameras.All(c => c.Id != target))
            {
                continue;
            }

            if (!verbs.TryGetValue(target, out var v))
            {
                verbs[target] = v = new Verbs();
            }

            Apply(v, t);
        }

        foreach (var node in cameras)
        {
            if (!verbs.TryGetValue(node.Id, out var v) || !v.Rendered || node.Destroyed)
            {
                continue;
            }

            list.Add(Solve(world, tree, node, v, camOf, carOf));
        }
    }

    private static void Apply(Verbs v, string[] t)
    {
        float F(int i) => i < t.Length && float.TryParse(t[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && float.IsFinite(x) ? x : 0f;
        switch (t[0])
        {
            case "render":
                v.Rendered = true;
                break;
            case "dist" when t.Length >= 3:
                v.Dist = new Vector2(F(1), F(2));
                break;
            case "height":
                v.Height = F(1);
                break;
            case "zoom":
                v.Zoom = F(1);
                break;
            case "move" when t.Length >= 3 && int.TryParse(t[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mt):
                v.Move = (mt, ParseVec(t[2]), t.Length > 3 ? F(3) : 0f);
                break;
            case "look" when t.Length >= 3 && int.TryParse(t[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lt):
                v.Look = (lt, ParseVec(t[2]));
                break;
        }
    }

    private static Vector3 ParseVec(string s)
    {
        var p = s.Split(',');
        float F(int i) => i < p.Length && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && float.IsFinite(x) ? x : 0f;
        return new Vector3(F(0), F(1), F(2));
    }

    private CameraBinding Solve(IGameWorld world, ResourceTree tree, ResourceNode node, Verbs v, Dictionary<int, int> camOf, Dictionary<int, int> carOf)
    {
        var p = (node.Params ?? string.Empty).Split(',').Select(s => s.Trim()).ToArray();
        float P(int i) => i < p.Length && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && float.IsFinite(x) ? x : 0f;
        var initPos = p.Length >= 3 ? new Vector3(P(0), P(1), P(2)) : new Vector3(node.Pos[0], node.Pos[1], node.Pos[2]);
        var initYpr = p.Length >= 6 ? new Vector3(P(3), P(4), P(5)) : new Vector3(node.Ori[0], node.Ori[1], node.Ori[2]);
        var fov = v.Zoom is > 5f ? v.Zoom.Value : 60f;
        var sceneId = node.ParentId;

        ResourceNode? Node(int id) => id == 0 ? null : tree.Get(id);
        Vector3 Point(int id, Vector3 off) => Node(id) is { } n ? Vector3.Transform(off, HostIds.World(tree, n)) : off;

        // 1. A controller bound to this camera and controlling a car: chase.
        var follow = camOf.Where(kv => kv.Value == node.Id).Select(kv => kv.Key).FirstOrDefault(c => carOf.ContainsKey(c));
        if (follow != 0 && Node(carOf[follow]) is { } carNode)
        {
            var body = HostIds.World(tree, carNode);
            var d = v.Dist is { } dd ? (dd.X + dd.Y) * 0.5f : 5f;
            return Bind(node, CameraRigs.Chase(body, d, 1.0f + (d * 0.15f), 2f), fov, sceneId, "chase", carNode.Id);
        }

        // 2. move/look around a car: orbit.
        if (v.Move is { } mv && Node(mv.Target) is { NativeKind: "car" } target)
        {
            var anchor = Point(mv.Target, mv.Offset);
            var dist = mv.Arg > 0.1f ? mv.Arg : 4f;
            if (v.Dist is { } dr)
            {
                dist = Math.Clamp(dist, Math.Min(dr.X, dr.Y), Math.Max(dr.X, dr.Y));
            }

            // The camera starts where its creation params put it (Garage: -3,1.7,-2 around a car at the origin): keep that direction, use the
            // verb's distance.
            var rel = initPos - anchor;
            var hasStart = rel.Length() is > 0.5f and < 40f;
            var azimuth = hasStart ? MathF.Atan2(rel.X, rel.Z) : DefaultAzimuth;
            var eyeY = v.Height ?? (hasStart ? initPos.Y : anchor.Y + (dist * 0.3f));
            var pitch = MathF.Asin(Math.Clamp((eyeY - anchor.Y) / dist, -0.9f, 0.9f));
            return Bind(node, CameraRigs.Orbit(anchor, azimuth, pitch, dist), fov, sceneId, "orbit", target.Id);
        }

        // 2b. A camera with a follow distance range (`dist a b`) and no move/look target tethers to the nearest car of its scene within 40 m
        // (the Track's "external track cam": created 6 m behind the start position, commands dist/smooth/force/torque/roll/render only; GUESSED).
        if (v.Dist is { } range && v.Move is null && v.Look is null)
        {
            var nearest = tree.Instances
                .Where(n => n.NativeKind == "car" && n.Script is not null && HostIds.IsInScene(tree, n, sceneId))
                .Select(n => (Node: n, World: HostIds.World(tree, n)))
                .Select(x => (x.Node, x.World, Dist: Vector3.Distance(x.World.Translation, initPos)))
                .Where(x => x.Dist < 40f)
                .OrderBy(x => x.Dist)
                .FirstOrDefault();
            if (nearest.Node is { } near)
            {
                var d = Math.Clamp((range.X + range.Y) * 0.5f, 3f, 12f);
                return Bind(node, CameraRigs.Chase(nearest.World, d, 1.0f + (d * 0.15f), 2f), fov, sceneId, "chase", near.Id);
            }
        }

        // 3. move to a point and/or look at a point: free.
        if (v.Move is { } m2)
        {
            var eye = Point(m2.Target, m2.Offset);
            var at = v.Look is { } lk ? Point(lk.Target, lk.Offset) : eye + HostIds.Basis(initYpr).Forward;
            return Bind(node, CameraRigs.LookAt(eye, at), fov, sceneId, "free", 0);
        }

        if (v.Look is { } l2)
        {
            return Bind(node, CameraRigs.LookAt(initPos, Point(l2.Target, l2.Offset)), fov, sceneId, "free", 0);
        }

        return Bind(node, CameraRigs.Free(initPos, initYpr), fov, sceneId, "free", 0);
    }

    private static CameraBinding Bind(ResourceNode node, RigPose pose, float fov, int sceneId, string kind, int follow) =>
        new($"{kind}-camera {ResourceIds.Format(node.Id)}", pose, fov, 0.1f, 1500f, ViewRect.Full, 5, sceneId, kind) { FollowCarId = follow };
}
