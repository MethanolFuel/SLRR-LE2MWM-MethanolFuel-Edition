using Slrr.Engine.Parts;
using Slrr.Engine.Ui;
using Slrr.Game;
using Slrr.Physics.Dyno;
using Xunit;

namespace Slrr.Race.Tests;

/// <summary>Integration fixes that need no game data.</summary>
public sealed class UnitTests
{
    private static DynoParams Engine(float cylinders, float bore, float stroke, float vmin)
    {
        var values = DynoParams.FieldNames.ToDictionary(n => n, _ => 0f);
        values["cylinders"] = cylinders;
        values["bore"] = bore;
        values["stroke"] = stroke;
        values["Vmin"] = vmin;
        values["RPM_limit"] = 7000f;
        values["maxRPM"] = 8000f;
        values["torque"] = 1f;
        values["torque2"] = 1f;
        return new DynoParams { Values = values };
    }

    [Fact]
    public void CalcDynoReportsCompressionAndDisplacement()
    {
        // A 1.6 l four: bore 80 mm, stroke 80 mm, clearance volume for 10:1.
        var swept = MathF.PI / 4f * 0.08f * 0.08f * 0.08f;
        var d = Engine(4, 0.08f, 0.08f, swept / 9f);

        foreach (IDynoCalculator calc in new IDynoCalculator[] { new ExeDynoCalculator(), new PlaceholderDynoCalculator() })
        {
            var curve = calc.Calculate(d, 32);
            Assert.NotNull(curve);
            Assert.NotNull(curve!.Compression);
            Assert.InRange(curve.Compression!.Value, 9.99f, 10.01f);
            Assert.InRange(curve.Displacement!.Value, 4 * swept * 0.999f, 4 * swept * 1.001f);
        }
    }

    [Fact]
    public void TheKeyboardHasTheDirectInputNameTheShippedControlFileUses()
    {
        // ControlSet.load matches the names stored in save/controls/active_control_set ("SysKeyboard", "SysMouse", ...).
        var input = new InputState();
        Assert.Equal("SysKeyboard", input.Devices[InputState.KeyboardDevice]);
        Assert.Equal("SysMouse", input.Devices[1]);
    }
}
