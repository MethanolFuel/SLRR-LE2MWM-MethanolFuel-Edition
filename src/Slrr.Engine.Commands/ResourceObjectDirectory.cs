using System.Numerics;
using Slrr.Engine.Resources;

namespace Slrr.Engine.Commands;

/// <summary>
/// <see cref="IObjectDirectory"/> and <see cref="IScriptEventSink"/> over Slrr.Engine.Resources: instance nodes of the
/// resource tree (kind from the gametype's <c>native</c> line, params, Pos/Ori/Vel) and its event queue.
/// Instance positions are used as world positions (GUESSED: the scripts parent cars, cameras and triggers to the map,
/// whose node sits at the origin).
/// </summary>
public sealed class ResourceObjectDirectory(ResourceEngine engine) : IObjectDirectory, IScriptEventSink
{
    public ResourceEngine Engine { get; } = engine;

    public ObjectInfo? Get(int id)
    {
        // Instances, and loaded resource nodes such as a track's map (GroundRef bound to the map archive entry).
        if (id == 0 || Engine.Tree.Get(id) is not { Destroyed: false } n)
        {
            return null;
        }

        return Info(n);
    }

    private static ObjectInfo Info(ResourceNode n) => new(n.Id, n.NativeKind, n.Script?.Class.Name, n.Name, n.ParentId, n.Params);

    public IEnumerable<ObjectInfo> OfKind(string kind) =>
        Engine.Tree.Instances.Where(n => string.Equals(n.NativeKind, kind, StringComparison.Ordinal)).OrderBy(n => n.Id).Select(Info);

    public bool TryGetPose(int id, out Pose pose)
    {
        if (id == 0 || Engine.Tree.Get(id) is not { Destroyed: false } n)
        {
            pose = default;
            return false;
        }

        pose = new Pose(new Vector3(n.Pos[0], n.Pos[1], n.Pos[2]), new Ypr(n.Ori[0], n.Ori[1], n.Ori[2]), new Vector3(n.Vel[0], n.Vel[1], n.Vel[2]));
        return true;
    }

    public void SetPose(int id, Pose pose)
    {
        if (Engine.Tree.Get(id) is not { Destroyed: false } n)
        {
            return;
        }

        (n.Pos[0], n.Pos[1], n.Pos[2]) = (pose.Position.X, pose.Position.Y, pose.Position.Z);
        (n.Ori[0], n.Ori[1], n.Ori[2]) = (pose.Orientation.Yaw, pose.Orientation.Pitch, pose.Orientation.Roll);
        (n.Vel[0], n.Vel[1], n.Vel[2]) = (pose.Velocity.X, pose.Velocity.Y, pose.Velocity.Z);
    }

    public void Queue(int targetId, int sourceId, int code, string text) => Engine.QueueEvent(targetId, sourceId, code, text);
}
