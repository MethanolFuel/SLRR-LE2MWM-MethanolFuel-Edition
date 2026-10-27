using System.Numerics;
using Slrr.Engine.Ui;
using Slrr.Render.UiBridge;
using Slrr.Vm;

namespace Slrr.Game.Render.Cameras;

/// <summary>A pose written by a script through <c>RenderRef.setMatrix(Vector3, Ypr)</c> on a <c>java.render.Camera</c> object.</summary>
public readonly record struct ScriptPose(Vector3 Position, Vector3 Ypr);

/// <summary>
/// Native module the adapter needs the host to register <b>last</b> (<c>GameHostOptions.ExtraModules</c>). It contains
/// <list type="bullet">
/// <item><see cref="RenderTap"/> from Slrr.Render (which texture a <c>RenderRef</c> got, which instances are Osds);</item>
/// <item>a wrapper of <c>RenderRef.setMatrix(Vector3, Ypr)</c> that records the pose of <c>java.render.Camera</c> objects. The Resources
/// area stores matrices on resource nodes, but a Camera object never receives a node (the Ui area's <c>Camera.create</c> keeps only the
/// model), so without this wrapper the MainMenu background camera path would be lost (MEASURED: <c>CameraModel.Object</c> has no node).</item>
/// </list>
/// </summary>
public sealed class RenderHooks : INativeModule
{
    private const string RenderRef = "java.util.resource.RenderRef";
    private const string SetMatrixDesc = "(Ljava.lang.Vector3;Ljava.lang.Ypr;)V";

    private readonly Dictionary<VmObject, ScriptPose> _poses = new(ReferenceEqualityComparer.Instance);

    public RenderTap Tap { get; } = new();

    public IReadOnlyDictionary<VmObject, ScriptPose> CameraPoses => _poses;

    /// <summary>Number of camera <c>setMatrix</c> calls seen.</summary>
    public int CameraPoseWrites { get; private set; }

    public void Register(NativeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Tap.Register(registry);
        var prev = registry.Find(RenderRef, "setMatrix", SetMatrixDesc);
        registry.Register(RenderRef, "setMatrix", SetMatrixDesc, c =>
        {
            if (c.This is { } self && Script.IsA(self, "java.render.Camera"))
            {
                _poses.TryGetValue(self, out var old);
                var pos = c.Object(0) is { } v ? new Vector3(Script.GetFloat(v, "x"), Script.GetFloat(v, "y"), Script.GetFloat(v, "z")) : old.Position;
                var ypr = c.Object(1) is { } y ? new Vector3(Script.GetFloat(y, "y"), Script.GetFloat(y, "p"), Script.GetFloat(y, "r")) : old.Ypr;
                _poses[self] = new ScriptPose(pos, ypr);
                CameraPoseWrites++;
            }

            return prev?.Invoke(c) ?? Value.Void;
        }, replace: true);
    }
}
