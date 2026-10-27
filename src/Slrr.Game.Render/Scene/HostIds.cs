using System.Numerics;
using Slrr.Core;
using Slrr.Engine.Resources;
using Slrr.Render.Content;
using Slrr.Render.Scene;

namespace Slrr.Game.Render.Scene;

/// <summary>
/// Helpers between the host's runtime resource ids (<c>archive slot &lt;&lt; 16 | local id</c>, instances <c>0xFFFF....</c>) and the
/// renderer's <see cref="ResRef"/>, plus node transforms and scene membership.
/// </summary>
public static class HostIds
{
    /// <summary>Runtime id of an rpk entry to <see cref="ResRef"/>; null for 0, instances and unmounted slots.</summary>
    public static ResRef? ToResRef(ResourceEngine engine, int id)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (id == 0 || ResourceIds.IsInstance(id))
        {
            return null;
        }

        return engine.Archives[ResourceIds.Archive(id)] is { } slot
            ? new ResRef(GameFileSystem.Normalize(slot.Name), (uint)ResourceIds.Local(id))
            : null;
    }

    /// <summary>Local transform of a node: yaw/pitch/roll then translation (same convention as cfg slots, see Slrr.Render).</summary>
    public static Matrix4x4 Local(ResourceNode n)
    {
        ArgumentNullException.ThrowIfNull(n);
        return CfgRenderInfo.Ypr(new Vector3(n.Ori[0], n.Ori[1], n.Ori[2])) * Matrix4x4.CreateTranslation(n.Pos[0], n.Pos[1], n.Pos[2]);
    }

    private static bool IsRenderEntry(ResourceTree tree, int id) =>
        id != 0 && !ResourceIds.IsInstance(id) && tree.Get(id) is { Type: ResourceTypes.RenderObject or ResourceTypes.RenderLight };

    /// <summary>
    /// The (render entry, scene parent) of a render instance. MEASURED on the running host: <c>RenderRef.create(scene, renderType, name)</c> is stored
    /// by the Resources natives with <c>ParentId = renderType</c> and <c>TypeId = scene</c> (arguments swapped against the field names; the scripts
    /// pass the scene first, e.g. <c>new RenderRef(bgScene, sunType, "menu_bgSun")</c>). Both orders are accepted, decided by which of the two ids
    /// is a render (type 14) or light (type 13) entry, so a later fix of the Resources area does not break the renderer.
    /// </summary>
    public static (int Render, int Scene) RenderParts(ResourceTree tree, ResourceNode n)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(n);
        if (n.Type != ResourceTypes.InstanceRender)
        {
            return (0, n.ParentId);
        }

        var parentIsRender = IsRenderEntry(tree, n.ParentId);
        var typeIsRender = IsRenderEntry(tree, n.TypeId);
        return typeIsRender && !parentIsRender ? (n.TypeId, n.ParentId) : (n.ParentId, n.TypeId);
    }

    /// <summary>The node a node is placed relative to: the scene parent of a render instance (see <see cref="RenderParts"/>), the parent of every other instance.</summary>
    public static int SceneParentId(ResourceTree tree, ResourceNode n) =>
        n.Type == ResourceTypes.InstanceRender ? RenderParts(tree, n).Scene : n.ParentId;

    /// <summary>World transform: the node's own transform times its instance ancestors' (resource entries stop the walk).</summary>
    public static Matrix4x4 World(ResourceTree tree, ResourceNode n)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var m = Local(n);
        var cur = n;
        for (var depth = 0; depth < 24; depth++)
        {
            var pid = SceneParentId(tree, cur);
            if (pid == 0 || !ResourceIds.IsInstance(pid) || tree.Get(pid) is not { } p)
            {
                break;
            }

            m *= Local(p);
            cur = p;
        }

        return m;
    }

    /// <summary>True when a render instance hangs below a Group or Osd script object (2D overlay content: rectangles, 3D icons of an Osd), which the overlay path draws.</summary>
    public static bool InsideOsd(ResourceTree tree, ResourceNode n)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var cur = n;
        for (var depth = 0; depth < 12; depth++)
        {
            var pid = SceneParentId(tree, cur);
            if (pid == 0 || !ResourceIds.IsInstance(pid) || tree.Get(pid) is not { } p)
            {
                return false;
            }

            if (p.Script is { } s && (s.Class.IsSubclassOf("java.render.Group") || s.Class.IsSubclassOf("java.render.Osd")))
            {
                return true;
            }

            cur = p;
        }

        return false;
    }

    /// <summary>True when <paramref name="sceneId"/> is the node itself or one of its (scene-)ancestors.</summary>
    public static bool IsInScene(ResourceTree tree, ResourceNode n, int sceneId)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (sceneId == 0)
        {
            return true;
        }

        var cur = n;
        for (var depth = 0; depth < 32; depth++)
        {
            if (cur.Id == sceneId)
            {
                return true;
            }

            var pid = SceneParentId(tree, cur);
            if (pid == sceneId)
            {
                return true;
            }

            if (pid == 0 || tree.Get(pid) is not { } p)
            {
                return false;
            }

            cur = p;
        }

        return false;
    }

    /// <summary>Camera-style basis of a yaw/pitch/roll: forward is -Z rotated, up is +Y rotated (MEASURED against the MainMenu camera path, see docs/race/game-render.md).</summary>
    public static (Vector3 Forward, Vector3 Up) Basis(Vector3 ypr)
    {
        var rot = CfgRenderInfo.Ypr(ypr);
        return (Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, rot)), Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, rot)));
    }
}
