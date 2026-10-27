using System.Globalization;
using System.Numerics;

namespace Slrr.Engine.Commands;

/// <summary>One traffic car.</summary>
public sealed class TrafficCar(int trafficId, int mapId, int carId)
{
    public int TrafficId { get; } = trafficId;

    public int MapId { get; } = mapId;

    public int CarId { get; } = carId;

    /// <summary><c>GroundRef.setTrafficCarBehaviour(id, mode)</c> value (roles unknown, stored).</summary>
    public int Behaviour { get; set; }

    public bool Slow { get; set; }

    /// <summary>Cruise speed (m/s, GUESSED: 12, slow variant 6).</summary>
    public float CruiseSpeed => Slow ? 6f : 12f;
}

/// <summary>
/// Traffic bookkeeping. MEASURED: the event texts <c>car_add %d %d</c> / <c>car_rem %d %d</c> (exe 0x610394 / 0x6103a4,
/// also at 0x61ff98..) and their consumer City.handleEvent (token 1 = car instance id, token 2 = traffic id; posted to
/// the map, which City subscribes to). GUESSED: traffic cars cruise straight on at a fixed speed (no road graph here:
/// following the map's road splines belongs to the world/ground area and is the main open item).
/// The GroundRef traffic natives (<c>addTrafficCar</c>, <c>remTrafficCar</c>, <c>setTrafficCarBehaviour</c>, <c>delTraffic</c>)
/// can be implemented on top of <see cref="Add"/>/<see cref="Remove"/> by whichever area owns GroundRef.
/// </summary>
public sealed class TrafficDirector(IVehicleState vehicles, IVehicleDriver driver, IScriptEventSink events) : ITrafficDirector
{
    public const int EventCommand = 16;

    private readonly SortedDictionary<int, TrafficCar> _cars = [];
    private int _next = 1;

    public IReadOnlyCollection<TrafficCar> Cars => _cars.Values;

    public TrafficCar? Get(int trafficId) => _cars.GetValueOrDefault(trafficId);

    public TrafficCar? ByCar(int carId) => _cars.Values.FirstOrDefault(c => c.CarId == carId);

    public int Add(int mapId, int carId, Vector3? position)
    {
        if (ByCar(carId) is { } existing)
        {
            return existing.TrafficId;
        }

        var t = new TrafficCar(_next++, mapId, carId);
        _cars[t.TrafficId] = t;
        events.Queue(mapId, carId, EventCommand, string.Create(CultureInfo.InvariantCulture, $"car_add {carId} {t.TrafficId}"));
        return t.TrafficId;
    }

    public void Remove(int trafficId)
    {
        if (!_cars.Remove(trafficId, out var t))
        {
            return;
        }

        driver.SetInput(t.CarId, DriveInput.None);
        events.Queue(t.MapId, t.CarId, EventCommand, string.Create(CultureInfo.InvariantCulture, $"car_rem {t.CarId} {t.TrafficId}"));
    }

    public void RemoveAll()
    {
        foreach (var id in _cars.Keys.ToList())
        {
            Remove(id);
        }
    }

    public void SetBehaviour(int trafficId, int mode)
    {
        if (_cars.TryGetValue(trafficId, out var t))
        {
            t.Behaviour = mode;
        }
    }

    public void SetSlow(int trafficId, bool slow)
    {
        if (_cars.TryGetValue(trafficId, out var t))
        {
            t.Slow = slow;
        }
    }

    public void Step(float dt)
    {
        foreach (var t in _cars.Values)
        {
            if (!vehicles.TryGetVehicle(t.CarId, out var pose))
            {
                continue;
            }

            var ahead = pose.Position + (pose.Orientation.Forward * 50f);
            driver.SetInput(t.CarId, RaceDirector.Pursue(pose, ahead, t.CruiseSpeed));
        }
    }
}
