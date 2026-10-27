namespace Slrr.Physics.Dyno;

/// <summary>
/// Port of the engine model behind <c>DynoData.calcDyno</c> in <c>StreetLegal_Redline.exe</c> (MEASURED: static
/// disassembly, nothing executed). It is a single-cylinder, crank-angle-stepped thermodynamic simulation:
/// slider-crank volume, adiabatic compression (P·V^1.3, T·V^0.3), square-wave valves with a leak area, a linear burn
/// that releases <c>mixture_H</c> per kg of charge, quadratic wall heat loss, a turbo boost window and fuel/air flow
/// limits. One 720° cycle is ~231 steps; each table rpm point repeats cycles until the mean effective pressure
/// converges. Field comments give the offset in the exe's 0x1EC-byte native object; routine addresses are cited per
/// method.
/// <para>Storage is <c>float</c> like the exe's struct; the x87 code keeps temporaries in 80-bit registers, here
/// they are <c>double</c>. Bit-exact parity is therefore not claimed (and was never measured against a run of the
/// original).</para>
/// </summary>
public sealed class EngineCycleModel
{
    // Constants read from .rdata (addresses in comments).
    private const double MinutePerSecond = 1.0 / 60.0; // 0x5f153c
    private const double FourPi = 12.566370614359172; // 0x5f1530 (float 0x41490fdb)
    private const double RpmToRadPerSec = 0.10471975803375244; // 0x5f0ef0
    private const double HpPerWatt = 0.0013409999664872885; // 0x5f1534
    private const double FrictionPowerCoeff = 1.999999987845058e-08; // 0x5f1538
    private const double TorqueFactor = 0.125; // 0x5f1540
    private const double StepFraction = 0.0043290043249726295; // 0x5f1548 (= 1/231 of a cycle)
    private const double PaToAtm = 9.86923259915784e-06; // 0x5f1510

    // ------------------------------------------------------------- geometry and constants (defaults: ctor 0x4b6670)
    public float CrankRadius = 0.05317499861121178f; // +0x00 = stroke/2
    public float BoreRadius = 0.04899999871850014f; // +0x04 = bore/2
    public float RodLength = 0.15000000596046448f; // +0x0C (no script field)
    public float Vmin = 9.999999747378752e-05f; // +0x10 clearance volume per cylinder [m^3]
    public float Cylinders = 4f; // +0x14
    public float TimeBurn = 0.003f; // +0x1C burn duration [s]
    public float BurnRpmFactor = 4.999999873689376e-05f; // +0x20
    public float AirDensity = 1.2928999662399292f; // +0x24 [kg/m^3]
    public float Cv = 712f; // +0x28 [J/(kg K)]
    public float IntakePressure = 101325f; // +0x2C [Pa]
    public float ExhaustPressure = 111457.5f; // +0x30 [Pa]
    public float IntakeTemperature = 293.1499938964844f; // +0x34 [K]; *= intercooling, *= nitro_cooling
    public float ExhaustTemperature = 293.1499938964844f; // +0x38
    public float MixtureH = 1_000_000f; // +0x3C heat per kg of charge [J/kg]; *= nitro_H
    public float ExhaustH = 0f; // +0x40
    public float InMin = 0.005f; // +0x44 intake leak area factor
    public float OutMin = 0.005f; // +0x48
    public float InMax = 0.8f; // +0x4C
    public float OutMax = 0.8f; // +0x50
    public float TLoss = 95f; // +0x54 heat-loss time constant. NOTE: the script field T_loss is NOT read by the exe.
    public float RpmLimit = 7000f; // +0x58

    // derived geometry (0x4b69a0)
    public float Bore; // +0x5C
    public float Stroke; // +0x60
    public float Displacement; // +0x64 all cylinders [m^3]
    public float PistonArea; // +0x68
    public float SweptVolume; // +0x6C per cylinder
    public float TotalVolume; // +0x70
    public float Compression; // +0x74
    public float RhoIntake; // +0x7C
    public float RhoExhaust; // +0x80
    public float Dt; // +0x84
    public float Rpm = 1000f; // +0x88
    public float MaxRpm; // +0x8C

    // valve/spark timing as fractions of the 720-degree cycle
    public float Spark = 0.48f; // +0x90
    public float InOpen = 0.001f; // +0x94
    public float InClose = 0.25f; // +0x98
    public float OutOpen = 0.75f; // +0x9C
    public float OutClose = 0.99f; // +0xA0
    public float InOpenBase = 0.001f; // +0xA4
    public float InDuration = 0.249f; // +0xA8
    public float InOpenInc; // +0xAC
    public float InRpm0; // +0xB0
    public float InRpm1; // +0xB4
    public float OutOpenBase = 0.75f; // +0xB8
    public float OutDuration = 0.24f; // +0xBC
    public float OutOpenInc; // +0xC0
    public float OutRpm0; // +0xC4
    public float OutRpm1; // +0xC8
    public float SparkMin = 0.48f; // +0xCC
    public float SparkInc = -0.05f; // +0xD0
    public float SparkRpm0 = 2000f; // +0xD4
    public float SparkRpm1 = 5000f; // +0xD8
    public float TurboMul = 20f; // +0xDC
    public float TurboOpt = 85000f; // +0xE0
    public float TurboDead; // +0xE4 (always 0 after the field reader)
    public float TurboRange = 30000f; // +0xE8
    public float TurboMax; // +0xEC
    public float TurboWaste = 3f; // +0xF0
    public float MixtureRatio = 14f; // +0xF4
    public float MaxFuelConsumption = 100f; // +0xF8 [kg/s]; 0 = no limit
    public float MaxAirConsumption = 100f; // +0xFC

    // ------------------------------------------------------------------------------------- cycle state
    private float _inArea; // +0x100
    private float _outArea; // +0x104
    private float _burn; // +0x108
    private float _wallTemperature = 353.1499938964844f; // +0x110
    private float _heatLoss; // +0x118
    private float _time; // +0x120
    private float _cycleTime; // +0x124
    private float _omega; // +0x128
    private float _angle; // +0x12C
    private float _volume; // +0x134
    private float _mass; // +0x13C
    private float _p; // +0x140
    private float _t; // +0x144
    private float _h; // +0x148
    private float _airFraction; // +0x14C
    private float _fuelFraction; // +0x150
    private float _hIn; // +0x154
    private float _pIn; // +0x158
    private float _tIn; // +0x15C
    private float _rhoIn; // +0x160
    private float _burnTime; // +0x164
    private float _fuelMass; // +0x168
    private float _airMass; // +0x16C
    private int _cycles; // +0x174
    private float _work; // +0x178

    /// <summary>Results of the last cycle (+0x184.. +0x1A4).</summary>
    public float Mep; // +0x184

    public float Torque; // +0x188 = W * cylinders * 0.125 (the exe's factor; physical 4-stroke would be 1/(4 pi))

    public float Hp; // +0x18C

    public float Power; // +0x190 = W * cylinders * rpm / 60

    public float NetPower; // +0x194 = Power - 2e-8 rpm^3

    /// <summary>+0x198 / +0x19C: fuel and air mass flow of the last cycle [kg/s]. Not reset by the constructor in the
    /// exe (heap garbage before the first cycle); GUESSED 0 here. Only matters when max_*_consumption &gt; 0.</summary>
    public float FuelFlow;

    public float AirFlow;

    public float Bsfc; // +0x1A0 lb/(hp h)

    public float VolumetricEfficiency; // +0x1A4

    // table maxima (0x4b77f0)
    public float MaxMep; // +0x1A8

    public float MaxTorque; // +0x1AC

    public float MaxHp; // +0x1B0

    public float MaxPower; // +0x1B4

    public float MaxNetPower; // +0x1B8

    public float RpmMaxTorque; // +0x1C0

    public float RpmMaxHp; // +0x1C4

    public int TableStep; // +0x1D0

    public int TableCount; // +0x1D4

    /// <summary>Torque tables (+0x1DC/+0x1E0): [0] normal, [1] nitro; entry i is rpm i*step, entry 0 = 0.</summary>
    public float[]?[] TorqueTables { get; } = new float[]?[2];

    /// <summary>Net power tables in watts (+0x1E4/+0x1E8; <c>getHP</c> interpolates these, so it returns watts).</summary>
    public float[]?[] PowerTables { get; } = new float[]?[2];

    /// <summary>0x4b6a80: mixture fractions and intake/exhaust densities.</summary>
    public void UpdateMixture()
    {
        double mr1 = MixtureRatio + 1.0;
        _airFraction = (float)(MixtureRatio / mr1);
        _fuelFraction = (float)(1.0 / mr1);
        RhoIntake = (float)(IntakePressure * PaToAtm * AirDensity);
        RhoExhaust = (float)(ExhaustPressure * PaToAtm * AirDensity);
    }

    /// <summary>0x4b69a0: derived geometry, displacement, compression and the default max rpm (45 m/s mean... see doc).</summary>
    public void UpdateGeometry()
    {
        _hIn = MixtureH;
        _pIn = IntakePressure;
        _tIn = IntakeTemperature;
        _rhoIn = RhoIntake;
        Bore = BoreRadius * 2f;
        Stroke = CrankRadius * 2f;
        PistonArea = (float)((double)BoreRadius * BoreRadius * 3.1415927410125732);
        SweptVolume = (float)((double)PistonArea * Stroke);
        TotalVolume = (float)((double)SweptVolume + Vmin);
        Compression = (float)((double)TotalVolume / Vmin);
        MaxRpm = (float)(45.0 / CrankRadius * 9.549296379089355); // 0x5f1514 = 45, 0x5f0c14 = 60/(2 pi)
        Displacement = (float)((double)SweptVolume * Cylinders);
    }

    /// <summary>0x4b6bf0: rpm-dependent spark/valve timing, turbo boost, flow limits and burn duration.</summary>
    private void UpdateForRpm()
    {
        UpdateMixture();
        Spark = Interp(SparkMin, SparkInc, SparkRpm0, SparkRpm1);
        InOpen = Interp(InOpenBase, InOpenInc, InRpm0, InRpm1);
        InClose = InDuration + InOpen;
        OutOpen = Interp(OutOpenBase, OutOpenInc, OutRpm0, OutRpm1);
        OutClose = OutDuration + OutOpen;

        double p = IntakePressure, h = MixtureH, t = IntakeTemperature, rho = RhoIntake;
        _pIn = (float)p;
        _hIn = (float)h;
        _tIn = (float)t;
        _rhoIn = (float)rho;
        if (TurboMul > 0 && TurboRange > 0)
        {
            var s = Math.Sqrt(BoostWindow(Rpm) + 1.0);
            _pIn = (float)(s * s * p);
            _tIn = (float)(t * s);
            _rhoIn = (float)(rho * s);
        }

        if (MaxFuelConsumption > 0 && FuelFlow > MaxFuelConsumption)
        {
            _hIn = (float)(h / (FuelFlow / (double)MaxFuelConsumption));
        }

        if (MaxAirConsumption > 0 && AirFlow > MaxAirConsumption)
        {
            _pIn = (float)(_pIn / (AirFlow / (double)MaxAirConsumption));
        }

        var x = Math.Min(Rpm * (double)BurnRpmFactor, 0.8999999761581421);
        _burnTime = (float)((1.0 - x) * TimeBurn);
    }

    private float Interp(float baseValue, float inc, float rpm0, float rpm1)
    {
        var range = rpm1 - (double)rpm0;
        if (!(range > 0))
        {
            return baseValue;
        }

        var f = (Rpm - (double)rpm0) / range;
        if (f < 0)
        {
            return baseValue;
        }

        return f > 1 ? (float)(inc + (double)baseValue) : (float)(f * inc + baseValue);
    }

    /// <summary>Boost pressure ratio over ambient (0x4b6ad0 / inlined in 0x4b6bf0): a triangular window around
    /// <c>rpm*rpm_turbo_mul = rpm_turbo_opt</c>, scaled by P_turbo_max and capped by P_turbo_waste.</summary>
    public double TurboBoost(double rpm) =>
        TurboMul > 0 && TurboRange > 0 && TurboMax > 0 && TurboWaste > 0 ? BoostWindow(rpm) : 0;

    /// <summary>The window as inlined in 0x4b6bf0 (only rpm_turbo_mul and rpm_turbo_range are tested there).</summary>
    private double BoostWindow(double rpm)
    {
        var d = Math.Abs(rpm * TurboMul - TurboOpt);
        double f;
        if (d < TurboDead)
        {
            f = 1;
        }
        else
        {
            f = 1.0 - (d - TurboDead) / (TurboRange - (double)TurboDead);
            if (!(f > 0))
            {
                f = 0;
            }
        }

        var b = f * TurboMax;
        return b < TurboWaste ? b : TurboWaste;
    }

    /// <summary>0x4b68c0: start a cycle at crank angle 0 (TDC) with a fresh intake charge in the clearance volume.</summary>
    private void ResetCycle()
    {
        _h = _hIn;
        _p = _pIn;
        _t = _tIn;
        _time = 0;
        _angle = 0;
        _inArea = _outArea = _burn = 0;
        _heatLoss = 0;
        _work = 0;
        Mep = 0;
        Power = 0;
        _fuelMass = _airMass = 0;
        _volume = (float)CylinderVolume(0);
        _mass = (float)((double)_volume * _rhoIn);
    }

    private double CylinderVolume(double angle)
    {
        double r = CrankRadius, l = RodLength;
        var rs = r * Math.Sin(angle);
        var x = Math.Sqrt(l * l - rs * rs) + Math.Cos(angle) * r; // piston pin distance from the crank axis
        return (l + r - x) * PistonArea + Vmin;
    }

    /// <summary>0x4b7020: <paramref name="steps"/> integration steps of <see cref="Dt"/>.</summary>
    private void Step(int steps)
    {
        for (var n = 0; n < steps; n++)
        {
            double vOld = _volume, burnOld = _burn, pOld = _p, tOld = _t;
            _time = (float)(Dt + (double)_time);
            if (_time >= _cycleTime)
            {
                _cycles++;
                _time = (float)(_time - (double)_cycleTime);
                double w = _work;
                _work = 0;
                var swept = TotalVolume - (double)Vmin;
                Mep = (float)(w / swept);
                Torque = (float)(swept * Mep * Cylinders * TorqueFactor);
                Power = (float)(swept * (Rpm * MinutePerSecond) * Mep * Cylinders);
                var friction = (double)Rpm * Rpm * Rpm * FrictionPowerCoeff;
                NetPower = (float)(Power - friction);
                Hp = (float)(NetPower * HpPerWatt);
            }

            var ang = Dt * (double)_omega + _angle;
            _angle = (float)(ang > FourPi ? ang - FourPi : ang);
            _volume = (float)CylinderVolume(_angle);
            _p = (float)(Math.Pow(vOld / _volume, 1.3) * pOld);
            _t = (float)(Math.Pow(vOld / _volume, 0.3) * tOld);

            _inArea = ValveOpen(InOpen, InClose) ? InMax : InMin;
            _outArea = ValveOpen(OutOpen, OutClose) ? OutMax : OutMin;

            // burn window [spark, spark + burnTime] (wrapping at the cycle end)
            var sparkT = (double)(float)(Spark * (double)_cycleTime);
            var endT = sparkT + _burnTime;
            if (endT > _cycleTime)
            {
                endT -= _cycleTime;
            }

            bool inBurn = sparkT < endT ? _time > sparkT && !(endT < _time) : _time > sparkT || !(endT < _time);
            _burn = inBurn ? (float)((_time - sparkT) / _burnTime) : burnOld > 0 ? 1f : 0f;
            if (_burn > 0)
            {
                var dT = (_burn - burnOld) * _h * _mass / (Cv * (double)_mass);
                var tNew = dT + _t;
                _p = (float)(tNew / _t * _p);
                _t = (float)tNew;
                if (!(_burn < 1))
                {
                    _burn = 0;
                    _h = 0;
                }
            }

            if (_inArea > 0)
            {
                Flow(_pIn, _inArea, inlet: true);
            }

            if (_outArea > 0)
            {
                Flow(ExhaustPressure, _outArea, inlet: false);
            }

            // wall heat loss: q = min(dt/T_loss, 1) * d * |d|, |q| <= |d|, d = T - T_wall
            var f = Dt / (double)TLoss;
            if (!(f < 1))
            {
                f = 1;
            }

            var d = _t - (double)_wallTemperature;
            var sign = d == 0 ? 0.0 : d < 0 ? -1.0 : 1.0;
            var q = f * d * d * sign;
            if (q / d > 1)
            {
                q = d;
            }

            _heatLoss = (float)(q + _heatLoss);
            var tq = _t - q;
            _p = (float)(tq / _t * _p);
            _t = (float)tq;
            _work = (float)((pOld + _p) * 0.5 * (_volume - vOld) + _work);
        }
    }

    private bool ValveOpen(float open, float close)
    {
        var openT = open * (double)_cycleTime;
        var closeT = close * (double)_cycleTime;
        return open < close ? openT < _time && closeT > _time : openT < _time || closeT > _time;
    }

    /// <summary>Gas exchange through one valve (intake 0x4b743a, exhaust 0x4b7573).</summary>
    private void Flow(double pPort, double area, bool inlet)
    {
        var k = Dt * area * 1000.0;
        if (k > 1)
        {
            k = 1;
        }

        var pTarget = (pPort - _p) * k + _p;
        var vAt = _volume * (double)_p / pTarget; // volume the trapped gas takes at the new pressure
        if (vAt < _volume)
        {
            // inflow
            var rho = inlet ? pTarget / _pIn * _rhoIn : pTarget / ExhaustPressure * RhoExhaust;
            var dm = (_volume - vAt) * rho;
            var m2 = dm + _mass;
            var tPort = inlet ? _tIn : ExhaustTemperature;
            var hPort = inlet ? _hIn : ExhaustH;
            _t = (float)((dm * tPort + _t * (double)_mass) / m2);
            _h = (float)((dm * hPort + _h * (double)_mass) / m2);
            _mass = (float)m2;
            if (inlet)
            {
                _airMass = (float)(dm * _airFraction + _airMass);
                _fuelMass = (float)(dm * _fuelFraction + _fuelMass);
            }
        }
        else
        {
            // outflow: the part of the charge beyond the cylinder volume leaves, T and H unchanged
            _mass = (float)(_mass - (vAt - _volume) / _volume * _mass);
        }

        _p = (float)pTarget;
    }

    /// <summary>0x4b7740: run until one cycle boundary is crossed, then derive flows and BSFC.</summary>
    private void RunCycle()
    {
        _fuelMass = _airMass = 0;
        _heatLoss = 0;
        _cycles = 0;
        do
        {
            Step(1);
        }
        while (_cycles == 0);

        FuelFlow = (float)(_fuelMass / (double)_cycleTime * Cylinders);
        AirFlow = (float)(_airMass / (double)_cycleTime * Cylinders);
        VolumetricEfficiency = (float)((_airMass + (double)_fuelMass) / (SweptVolume * (double)AirDensity));
        Bsfc = (float)(FuelFlow * 3600.0 / Hp * 2.2);
        _heatLoss = (float)(_heatLoss / (double)_cycleTime);
    }

    /// <summary>
    /// 0x4b77f0: torque/power tables at rpm = i*step for i = 1 .. (int)maxRPM/step - 1 (entry 0 = 0) into table
    /// <paramref name="index"/>, and the maxima. Each point: set rpm (cycle time 120/rpm, dt = cycle/231), reset at TDC,
    /// one cycle, then up to 10 more until |mep_prev/mep - 1| &lt; 0.05 (mep_prev = 0.4 mep_prev + 0.6 mep).
    /// </summary>
    public void BuildTable(int step, int index)
    {
        MaxPower = MaxNetPower = MaxMep = MaxTorque = MaxHp = 0;
        RpmMaxTorque = RpmMaxHp = 0;
        TableStep = step;
        TableCount = (int)MaxRpm / step;
        var torque = new float[Math.Max(TableCount, 0)];
        var power = new float[Math.Max(TableCount, 0)];
        TorqueTables[index] = torque;
        PowerTables[index] = power;
        for (var i = 1; i < TableCount; i++)
        {
            Rpm = i * step;
            _angle = 0;
            _time = 0;
            _cycleTime = (float)(1.0 / (Rpm * 0.008333333767950535));
            _omega = (float)(Rpm * RpmToRadPerSec);
            UpdateForRpm();
            Dt = (float)(_cycleTime * StepFraction);
            ResetCycle();
            RunCycle();
            var mepPrev = Mep;
            for (var tries = 10; tries > 0; tries--)
            {
                RunCycle();
                if (Math.Abs(mepPrev / (double)Mep - 1.0) < 0.05)
                {
                    break;
                }

                mepPrev = (float)(mepPrev * 0.4 + Mep * 0.6);
            }

            if (Power > MaxPower)
            {
                MaxPower = Power;
            }

            if (NetPower > MaxNetPower)
            {
                MaxNetPower = NetPower;
                MaxHp = (float)(NetPower * HpPerWatt);
                RpmMaxHp = Rpm;
            }

            if (Mep > MaxMep)
            {
                MaxMep = Mep;
                RpmMaxTorque = Rpm;
                MaxTorque = Torque;
            }

            torque[i] = Torque;
            power[i] = NetPower;
        }

        Torque = MaxTorque;
        Power = MaxPower;
        NetPower = MaxNetPower;
    }

    /// <summary>
    /// Table lookup of <c>DynoData.getTorque</c> (0x46b0f0) / <c>getHP</c> (0x46b210): rpm &lt; 0 gives 0; find the first
    /// i &gt;= 1 with i*step &gt; rpm and interpolate linearly between entries i-1 and i; past the table end 0. The
    /// second (boost) argument is ignored by the exe; both use table 0.
    /// </summary>
    public static float Lookup(float[]? table, int step, float rpm)
    {
        if (table is null || rpm < 0 || step <= 0)
        {
            return 0f;
        }

        for (var i = 1; i < table.Length; i++)
        {
            if (i * step > rpm)
            {
                var r0 = (i - 1) * step;
                return (float)((rpm - (double)r0) * (table[i] - (double)table[i - 1]) / step + table[i - 1]);
            }
        }

        return 0f;
    }
}
