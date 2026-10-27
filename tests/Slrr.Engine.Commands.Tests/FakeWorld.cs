using System.Numerics;

namespace Slrr.Engine.Commands.Tests;

/// <summary>In-memory object world for unit tests (no VM, no game data).</summary>
internal sealed class FakeWorld : IObjectDirectory, IScriptEventSink
{
    private readonly Dictionary<int, ObjectInfo> _objects = [];
    private readonly Dictionary<int, Pose> _poses = [];

    public List<(int Target, int Source, int Code, string Text)> Events { get; } = [];

    public ObjectInfo Add(int id, string? kind, string name = "obj", string? parms = null, Vector3 pos = default, float yaw = 0, int parent = 1)
    {
        var o = new ObjectInfo(id, kind, null, name, parent, parms);
        _objects[id] = o;
        _poses[id] = new Pose(pos, new Ypr(yaw, 0, 0), default);
        return o;
    }

    public void Remove(int id)
    {
        _objects.Remove(id);
        _poses.Remove(id);
    }

    public ObjectInfo? Get(int id) => _objects.GetValueOrDefault(id);

    public IEnumerable<ObjectInfo> OfKind(string kind) => _objects.Values.Where(o => o.Kind == kind).OrderBy(o => o.Id);

    public bool TryGetPose(int id, out Pose pose) => _poses.TryGetValue(id, out pose);

    public void SetPose(int id, Pose pose)
    {
        if (_objects.ContainsKey(id))
        {
            _poses[id] = pose;
        }
    }

    public Pose PoseOf(int id) => _poses[id];

    public void Queue(int targetId, int sourceId, int code, string text) => Events.Add((targetId, sourceId, code, text));
}
