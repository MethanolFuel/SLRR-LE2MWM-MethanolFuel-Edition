using System.Numerics;

namespace Slrr.GameAudio;

/// <summary>One 60 Hz frame of the scripted drive (all values are what a physics adapter would report).</summary>
public sealed record ScenarioFrame(
    double Time,
    float EngineRpm,
    float Throttle,
    bool Starter,
    int Gear,
    float Speed,
    float WheelSlip,
    float DriveRpm,
    float DriveTorque,
    bool Braking,
    float ImpactSpeed);

/// <summary>
/// A deterministic, physics-free 60 s "drive-like" telemetry sequence for headless audio proofs: starter and idle (0..8 s), two rev blips,
/// a full-throttle launch with wheelspin and five upshifts at 6500 rpm, a lift-off, hard braking with locked-wheel skid and automatic
/// downshifts to a stop, a second launch into a sliding corner, and a 14 m/s wall hit. The numbers are invented (GUESSED); they only have to
/// be self-consistent (engine rpm follows the gear ratio and the speed) so the sound can be checked against them.
/// </summary>
public static class DriveScenario
{
    public const double Duration = 60.0;
    public const int Hz = 60;
    public const float WheelRadius = 0.32f;
    public const float FinalDrive = 3.7f;
    public const float IdleRpm = 900f;
    public const float ShiftRpm = 6500f;
    public static readonly float[] Ratios = [0f, 3.5f, 2.2f, 1.5f, 1.1f, 0.9f];

    public static ScenarioFrame[] Build()
    {
        var n = (int)(Duration * Hz);
        var frames = new ScenarioFrame[n];
        const float Dt = 1f / Hz;
        var v = 0f;
        var gear = 0;
        var rpm = 0f;
        var shiftTimer = 0f;
        for (var i = 0; i < n; i++)
        {
            var t = i * (double)Dt;
            float throttle;
            var brake = false;
            var starter = false;
            var slip = 0f;
            var impact = 0f;
            var launching = (t >= 8 && t < 36) || (t >= 48 && t < 56.5);
            var braking = t >= 36 && t < 44.5 && v > 0.2f;
            var cruise = t >= 31 && t < 36;

            if (t < 1.5)
            {
                starter = true;
                throttle = 0f;
                rpm = 250f * (float)(t / 1.5);
            }
            else if (t < 8 && !launching)
            {
                throttle = RevBlip(t);
                if (t < 2.0)
                {
                    rpm = Lerp(250f, 1400f, (float)((t - 1.5) / 0.5));
                }
                else if (t < 3.5)
                {
                    rpm = Lerp(1400f, IdleRpm, (float)((t - 2.0) / 1.5));
                }
                else
                {
                    rpm += (IdleRpm + (throttle * 4200f) - rpm) * 0.25f;
                }
            }
            else if (launching)
            {
                throttle = cruise ? 0.25f : 1f;
                if (gear == 0)
                {
                    gear = 1;
                }

                if (shiftTimer > 0f)
                {
                    shiftTimer -= Dt;
                    throttle = 0f;
                }

                var accel = cruise ? 0.2f : Math.Max(7.2f - (gear * 1.15f), 1.2f) * (1f - (v / 75f));
                if (shiftTimer > 0f)
                {
                    accel = -0.3f;
                }

                v = Math.Max(v + (accel * Dt), 0f);
                var geared = v / WheelRadius * Ratios[gear] * FinalDrive * 9.549297f;
                var launchHold = gear == 1 && t < 9.2 ? 3600f + ((float)(t - 8) * 1800f) : 0f;
                var target = Math.Max(geared, Math.Max(launchHold, IdleRpm));
                rpm += (target - rpm) * Math.Min(1f, 14f * Dt);
                if (rpm >= ShiftRpm && gear < 5 && shiftTimer <= 0f && !cruise)
                {
                    gear++;
                    shiftTimer = 0.35f;
                }

                if (t < 9.4)
                {
                    slip = 0.75f * (float)Math.Max(0.0, 1.0 - ((t - 8.0) / 1.4));
                }

                if (t >= 52 && t < 55.5)
                {
                    slip = 0.55f;
                }

                if (t >= 56.4 && t < 56.45)
                {
                    impact = 14f;
                }

                if (t >= 56.4)
                {
                    v = Math.Max(v - (120f * Dt), 0f);
                    throttle = 0f;
                }
            }
            else if (braking)
            {
                throttle = 0f;
                brake = true;
                v = Math.Max(v - (9.5f * Dt), 0f);
                slip = v > 4f ? 0.9f : 0.2f;
                while (gear > 1 && v / WheelRadius * Ratios[gear] * FinalDrive * 9.549297f < 2200f)
                {
                    gear--;
                }

                var geared = v / WheelRadius * Ratios[Math.Max(gear, 1)] * FinalDrive * 9.549297f;
                rpm += (Math.Max(geared, IdleRpm) - rpm) * Math.Min(1f, 10f * Dt);
            }
            else
            {
                throttle = 0f;
                gear = 0;
                v = Math.Max(v - ((t >= 56.4 ? 120f : 4f) * Dt), 0f);
                rpm += (IdleRpm - rpm) * Math.Min(1f, 3f * Dt);
            }

            if (t >= 56.5 && v <= 0.01f)
            {
                gear = 0;
                rpm += (IdleRpm - rpm) * Math.Min(1f, 3f * Dt);
            }

            var wheelOmega = v / WheelRadius;
            var driveOmega = wheelOmega * FinalDrive;
            var torque = brake || gear == 0 ? -40f : throttle * 250f * Ratios[gear] * FinalDrive;
            frames[i] = new ScenarioFrame(t, rpm, throttle, starter, gear, v, slip, driveOmega * 9.549297f, torque, brake, impact);
        }

        return frames;
    }

    /// <summary>Applies a frame to a telemetry object (wheels 0/1 front, 2/3 rear; wheelspin on the rear, locked wheels on all four).</summary>
    public static void Apply(ScenarioFrame f, ManualTelemetry t)
    {
        t.Active = true;
        t.Position = new Vector3(0f, 0f, 0f);
        t.EngineAngularSpeed = ManualTelemetry.RpmToRad(f.EngineRpm);
        t.Throttle = f.Throttle;
        t.StarterEngaged = f.Starter;
        t.DriveShaftAngularSpeed = ManualTelemetry.RpmToRad(f.DriveRpm);
        t.DriveShaftTorque = f.DriveTorque;
        t.Speed = f.Speed;
        t.Slip = f.Braking ? [f.WheelSlip, f.WheelSlip, f.WheelSlip, f.WheelSlip] : [0.05f, 0.05f, f.WheelSlip, f.WheelSlip];
        t.WheelPositions = [new(-0.8f, 0f, 1.3f), new(0.8f, 0f, 1.3f), new(-0.8f, 0f, -1.3f), new(0.8f, 0f, -1.3f)];
        if (f.ImpactSpeed > 0f)
        {
            t.AddImpact(new ImpactEvent(f.ImpactSpeed, new Vector3(0f, 0.5f, 2.2f)));
        }
    }

    private static float RevBlip(double t)
    {
        // two blips at 5.0..5.8 s and 6.4..7.2 s
        static float Bell(double x) => x is < 0 or > 1 ? 0f : (float)Math.Sin(Math.PI * x);
        return Math.Max(Bell((t - 5.0) / 0.8), Bell((t - 6.4) / 0.8));
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * Math.Clamp(t, 0f, 1f));
}
