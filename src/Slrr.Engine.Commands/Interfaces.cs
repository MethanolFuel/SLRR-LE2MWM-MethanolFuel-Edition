using System.Numerics;

namespace Slrr.Engine.Commands;

/// <summary>What the command engine knows about an engine object (an instance node of the resource tree).</summary>
/// <param name="Kind">Native kind of its gametype (<c>car</c>, <c>camera</c>, <c>bot</c>, <c>player</c>, <c>cursor</c>,
/// <c>trigger</c>, <c>ground</c>, ...; MEASURED from the rpk <c>native</c> lines), or null.</param>
/// <param name="ScriptClass">Script class bound to the instance (e.g. <c>java.game.Vehicle</c>), or null.</param>
/// <param name="Params">Creation params text (<c>x,y,z,yaw,pitch,roll[,...]</c>), or null.</param>
public sealed record ObjectInfo(int Id, string? Kind, string? ScriptClass, string Name, int ParentId, string? Params);

/// <summary>
/// The object world as the command engine needs it (implemented over Slrr.Engine.Resources by
/// <see cref="ResourceObjectDirectory"/>; tests may use their own).
/// </summary>
public interface IObjectDirectory
{
    ObjectInfo? Get(int id);

    /// <summary>Live instances whose native kind is <paramref name="kind"/>.</summary>
    IEnumerable<ObjectInfo> OfKind(string kind);

    /// <summary>Pose of any object (car, map, render node). Unknown ids return false.</summary>
    bool TryGetPose(int id, out Pose pose);

    /// <summary>Writes a pose back (the kinematic vehicles keep the instance tree in sync so scripts' getPos/getVel see it).</summary>
    void SetPose(int id, Pose pose);
}

/// <summary>Queues events to scripts (EVENT_COMMAND texts such as <c>car_add</c>, EVENT_TRIGGER_ON/OFF).</summary>
public interface IScriptEventSink
{
    /// <param name="targetId">Object the event is posted on (its script and its notification subscribers receive it).</param>
    /// <param name="sourceId">Source object id (0 = none).</param>
    /// <param name="code">GameRef.EVENT_* value.</param>
    void Queue(int targetId, int sourceId, int code, string text);
}

/// <summary>
/// Vehicle state as delivered by the physics adapter (Slrr.Physics) or by <see cref="KinematicVehicles"/> when there is
/// none. The command engine reads it for cameras, AI, triggers and the Vehicle natives (getSpeedSquare).
/// </summary>
public interface IVehicleState
{
    /// <summary>Pose of a car (world frame); false when the id is not a simulated car.</summary>
    bool TryGetVehicle(int carId, out Pose pose);
}

/// <summary>Driver inputs the AI brains produce for a car (consumed by the physics adapter or <see cref="KinematicVehicles"/>).</summary>
/// <param name="Throttle">0..1.</param>
/// <param name="Brake">0..1.</param>
/// <param name="Steer">-1 (left) .. 1 (right).</param>
public readonly record struct DriveInput(float Throttle, float Brake, float Steer)
{
    public static DriveInput None => default;

    public static DriveInput FullBrake => new(0, 1, 0);
}

/// <summary>Sink for driver inputs (AI brains, test drivers).</summary>
public interface IVehicleDriver
{
    void SetInput(int carId, DriveInput input);
}

/// <summary>Camera verbs (<c>look</c>, <c>move</c>, <c>dist</c>, <c>render</c>, ...) and the managed camera rigs they drive.</summary>
public interface ICameraRig
{
    /// <summary>Handles a camera verb sent to a camera object or a car's interior-camera verb; false = not a camera verb.</summary>
    bool Execute(ObjectInfo target, ParsedCommand command);

    /// <summary>Advances every rig by <paramref name="dt"/> seconds (deterministic).</summary>
    void Step(float dt);

    /// <summary>What renders into a viewport (null = nothing bound).</summary>
    CameraView? ViewFor(int viewportId);

    /// <summary>State of one camera object (null when it never received a verb).</summary>
    CameraState? Get(int cameraId);
}

/// <summary>Controllers, bot brains, triggers and race bookkeeping.</summary>
public interface IRaceDirector
{
    bool Execute(ObjectInfo target, ParsedCommand command);

    void Step(float dt);

    ControllerState? Controller(int id);

    /// <summary>The controller/brain that took <paramref name="objectId"/> with <c>controllable</c> (0 = none); answers GII_OWNER.</summary>
    int OwnerOf(int objectId);

    /// <summary>Every EVENT_TRIGGER_ON/OFF this director emitted, in order.</summary>
    IReadOnlyList<TriggerEvent> TriggerLog { get; }
}

/// <summary>Traffic cars handed over by brains (<c>AI_GoToTraffic</c>) or added by GroundRef natives.</summary>
public interface ITrafficDirector
{
    /// <summary>Adds <paramref name="carId"/> to the traffic of <paramref name="mapId"/>; returns the traffic id and posts
    /// <c>car_add carId trafficId</c> to the map (format MEASURED 0x610394, token roles MEASURED from City.handleEvent).</summary>
    int Add(int mapId, int carId, Vector3? position);

    /// <summary>Removes a traffic car and posts <c>car_rem carId trafficId</c> to its map.</summary>
    void Remove(int trafficId);

    void Step(float dt);

    IReadOnlyCollection<TrafficCar> Cars { get; }
}

/// <summary>Car verbs that are state for the physics adapter (start/stop/brake/steer/assists...).</summary>
public interface ICarControl
{
    bool Execute(ObjectInfo target, ParsedCommand command);

    /// <summary>Recorded control state of a car (null when the car never received a verb).</summary>
    CarControlState? Get(int carId);
}
