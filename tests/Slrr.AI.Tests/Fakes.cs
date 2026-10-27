using Slrr.Engine.Commands;
using Slrr.Game.Physics;

namespace Slrr.AI.Tests;

/// <summary>A minimal object world for the commands layer's RaceDirector: brains (kind "bot") and physics cars.</summary>
internal sealed class FakeWorld(PhysicsScene scene) : IObjectDirectory, IScriptEventSink, IVehicleState, IVehicleDriver
{
    private readonly Dictionary<int, ObjectInfo> _objects = [];

    public List<(int Target, int Code, string Text)> Events { get; } = [];

    public List<(int Car, DriveInput Input)> Inputs { get; } = [];

    public ObjectInfo Add(int id, string kind, int parent = 1)
    {
        var o = new ObjectInfo(id, kind, null, $"{kind}{id}", parent, null);
        _objects[id] = o;
        return o;
    }

    public ObjectInfo? Get(int id) => _objects.GetValueOrDefault(id);

    public IEnumerable<ObjectInfo> OfKind(string kind) => _objects.Values.Where(o => o.Kind == kind);

    public bool TryGetPose(int id, out Pose pose) => TryGetVehicle(id, out pose);

    public void SetPose(int id, Pose pose)
    {
    }

    public void Queue(int targetId, int sourceId, int code, string text) => Events.Add((targetId, code, text));

    public bool TryGetVehicle(int carId, out Pose pose)
    {
        if (scene.Car(carId) is not { } c)
        {
            pose = default;
            return false;
        }

        var ypr = Conv.ToYpr(c.Orientation);
        pose = new Pose(c.Position.ToVector3(), new Ypr(ypr.Yaw, ypr.Pitch, ypr.Roll), c.Velocity.ToVector3());
        return true;
    }

    public void SetInput(int carId, DriveInput input) => Inputs.Add((carId, input));
}
