using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Engine.Ui;

namespace Slrr.Game;

/// <summary>
/// What the host offers to the simulation/presentation areas (Slrr.Physics, Slrr.Render, Slrr.Audio): read access to
/// the live engine state they need. Deliberately the engine objects themselves (they are public types of the
/// Engine.* projects), not copies, so the areas can read the resource tree (instances, positions, render nodes),
/// the parts world (chassis contract, wheels, settings) and the UI state (viewports, cameras, texts, sound volumes).
/// Writes go through the documented members of those objects (e.g. <see cref="ResourceNode.Pos"/>).
/// </summary>
public interface IGameWorld
{
    /// <summary>The script VM (virtual clocks <c>Time</c>/<c>SimTime</c>).</summary>
    Slrr.Vm.Vm Vm { get; }

    /// <summary>Resource tree, gametype instances, event plumbing.</summary>
    ResourceEngine Resources { get; }

    /// <summary>Native twins of parts, chassis contract snapshots, wheels, RawEdit settings.</summary>
    PartsWorld Parts { get; }

    /// <summary>Headless viewports, cameras, texts, sound/video state, input.</summary>
    UiEngine Ui { get; }

    /// <summary>Frame counter of the host loop.</summary>
    long Frame { get; }
}

/// <summary>
/// Physics of cars and objects (Phase 4 area Slrr.Physics). The host calls <see cref="Attach"/> once after boot,
/// routes every <c>EVENT_COMMAND</c> text sent to a non-part game object through <see cref="Command"/> (the car /
/// camera / bot verbs of docs/engine-api/entry-points.md section 5: <c>start</c>, <c>idle</c>, <c>setsteer</c>,
/// <c>reset</c>, <c>look</c>, ...), and calls <see cref="Step"/> once per frame after the script threads ran.
/// </summary>
public interface IPhysicsWorld
{
    void Attach(IGameWorld world);

    /// <summary>A command for <paramref name="targetId"/>. Return true when consumed (the host then does not
    /// queue it as a script event).</summary>
    bool Command(int targetId, string command);

    /// <summary>Advances the simulation by <paramref name="dt"/> seconds of simulation time (already scaled by timeWarp).</summary>
    void Step(float dt);
}

/// <summary>
/// Presentation (Phase 4/6 area Slrr.Render). Called once per host frame after physics; must not change script state.
/// Headless hosts use <see cref="NullRenderer"/>.
/// </summary>
public interface IRenderer
{
    void Attach(IGameWorld world);

    void Render(float time);
}

/// <summary>
/// Sound output (area Slrr.Audio). <see cref="Play"/> receives <c>SfxRef.play/nplay</c> calls routed by the host
/// natives; <see cref="Update"/> runs once per frame (engine sound from the chassis telemetry, music set and volumes
/// from <see cref="UiEngine.Sound"/>).
/// </summary>
public interface IAudioSink
{
    void Attach(IGameWorld world);

    /// <summary>
    /// <c>SfxRef.nplay(pos, a, b, c, flags, extra)</c>: resource id of the sound, optional world position (null = 2D),
    /// the three floats (scripts pass <c>0.0, 1.0, 1.0</c> for <c>play()</c>; roles unknown, open question P1 #10) and the
    /// flags (<c>SFX_LOOP</c> 1, <c>SFX_NOAUTOSTOP</c> 0x80000000). Returns the play handle that <c>stop(int)</c> gets.
    /// </summary>
    int Play(int soundId, System.Numerics.Vector3? position, float a, float b, float c, int flags);

    /// <summary><c>SfxRef.stop(handle)</c>.</summary>
    void Stop(int handle);

    void Update(float dt);
}

/// <summary>No physics: commands are not consumed, nothing moves.</summary>
public sealed class NullPhysicsWorld : IPhysicsWorld
{
    public void Attach(IGameWorld world)
    {
    }

    public bool Command(int targetId, string command) => false;

    public void Step(float dt)
    {
    }
}

public sealed class NullRenderer : IRenderer
{
    public void Attach(IGameWorld world)
    {
    }

    public void Render(float time)
    {
    }
}

/// <summary>Records what would be played (for transcripts and tests).</summary>
public sealed class RecordingAudioSink : IAudioSink
{
    private IGameWorld? _world;
    private int _next;

    public List<(float Time, int SoundId, int Handle, int Flags)> Played { get; } = [];

    public HashSet<int> Stopped { get; } = [];

    public void Attach(IGameWorld world) => _world = world;

    public int Play(int soundId, System.Numerics.Vector3? position, float a, float b, float c, int flags)
    {
        var h = ++_next;
        Played.Add((_world?.Vm.Time ?? 0f, soundId, h, flags));
        return h;
    }

    public void Stop(int handle) => Stopped.Add(handle);

    public void Update(float dt)
    {
    }
}
