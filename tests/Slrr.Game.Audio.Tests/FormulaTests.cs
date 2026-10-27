using Slrr.Audio;
using Slrr.Decompiler;
using Slrr.Formats.Tufa;
using Xunit;
using R = Slrr.GameAudio.MeasuredSfxTable.RowResult;

namespace Slrr.GameAudio.Tests;

/// <summary>The exe's table update (0x48f0e0) as measured from disassembly.</summary>
public class FormulaTests
{
    private static void Eq(float e, float a, int d) => Assert.Equal((double)e, (double)a, d);

    private static EngineSfxItem Row(float a, float b, float c, float d, float e) => new(new SoundKey("t.rpk", 1), a, b, c, d, e);

    private static R[] Eval(EngineSfxItem[] rows, float rpm)
    {
        var r = new R[MeasuredSfxTable.MaxRows];
        MeasuredSfxTable.Evaluate(rows, rpm, r);
        return r;
    }

    [Fact]
    public void TableRpmIsAngularSpeedTimesNinePointFiveFourNinePlusOne()
    {
        Assert.Equal(1f, MeasuredSfxTable.TableRpm(0f));
        Eq(1001f, MeasuredSfxTable.TableRpm(1000f / 9.549297f), 2);
        Eq(1001f, MeasuredSfxTable.TableRpm(-1000f / 9.549297f), 2); // |omega|
    }

    [Fact]
    public void PitchIsRpmOverTheRowsFirstArgumentNotTheFileLabel()
    {
        // the 2000RPM.wav row of Baiern_Emer is registered with A = 3500: it plays at native pitch at 3500 rpm and at 0.571 at 2000 rpm
        var rows = new[] { Row(3500, 500, 6500, 0.4f, 0.5f) };
        Eq(1f, Eval(rows, 3500)[0].Pitch, 5);
        Eq(2000f / 3500f, Eval(rows, 2000)[0].Pitch, 5);
        Eq(6500f / 3500f, Eval(rows, 6500)[0].Pitch, 5);
    }

    [Fact]
    public void WindowAndVolumeInterpolationFollowTheRowBounds()
    {
        var rows = new[] { Row(1000, 500, 1500, 0.2f, 1.0f) };
        Assert.False(Eval(rows, 499)[0].Active);
        Assert.False(Eval(rows, 1501)[0].Active);
        Eq(0.2f, Eval(rows, 500)[0].Gain, 5);
        Eq(0.6f, Eval(rows, 1000)[0].Gain, 5);
        Eq(1.0f, Eval(rows, 1500)[0].Gain, 5);
    }

    [Fact]
    public void NeighbouringRowsCrossfadeLinearlyOverTheirOverlap()
    {
        // overlap 2000..3000: the lower row fades out 1 -> 0, the upper row fades in 0 -> 1 (both volumes 1.0)
        var rows = new[] { Row(1500, 500, 3000, 1, 1), Row(3500, 2000, 6000, 1, 1) };
        var below = Eval(rows, 1999);
        Eq(1f, below[0].Gain, 5);
        Assert.False(below[1].Active);
        var mid = Eval(rows, 2500);
        Eq(0.5f, mid[0].Gain, 5);
        Eq(0.5f, mid[1].Gain, 5);
        var late = Eval(rows, 2900);
        Eq(0.1f, late[0].Gain, 4);
        Eq(0.9f, late[1].Gain, 4);
        var above = Eval(rows, 3500);
        Assert.False(above[0].Active);
        Eq(1f, above[1].Gain, 5);
    }

    [Fact]
    public void FadeUsesTheOtherRowsBoundsAndKeepsSumNearOneForEqualVolumes()
    {
        var rows = new[] { Row(1500, 500, 3000, 1, 1), Row(3500, 2000, 6000, 1, 1) };
        for (var rpm = 2001f; rpm < 3000f; rpm += 37f)
        {
            var r = Eval(rows, rpm);
            Eq(1f, r[0].Gain + r[1].Gain, 4);
        }
    }

    [Fact]
    public void OnlyTheFirstSixteenRowsCountLikeAddItem()
    {
        var rows = Enumerable.Range(0, 20).Select(i => Row(1000, i * 100, (i * 100) + 50, 1, 1)).ToArray();
        Eq(1f, Eval(rows, (15 * 100) + 25)[15].Gain, 4);
        Assert.All(Eval(rows, (17 * 100) + 25), x => Assert.False(x.Active));
    }

    [Theory]
    [InlineData(0.0f, 1f, 0f)]
    [InlineData(0.3f, 1f, 0f)]
    [InlineData(1.0f, 1f, 0f)]
    public void EngineTableVolumesWithEmptyTableOneAreAlwaysFullOnTableZero(float throttle, float v0, float v1)
    {
        var (a, b) = MeasuredSfxTable.EngineTableVolumes(throttle, false);
        Assert.Equal(v0, a);
        Assert.Equal(v1, b);
    }

    [Fact]
    public void ThrottleSelectsTableOneBelowTenPercentAndCrossfadesUpToFiftyWhenItHasRows()
    {
        Assert.Equal((0f, 1f), MeasuredSfxTable.EngineTableVolumes(0.05f, true));
        var (t0, t1) = MeasuredSfxTable.EngineTableVolumes(0.3f, true);
        Eq(0.5f, t0, 5);
        Eq(0.5f, t1, 5);
        Assert.Equal((1f, 0f), MeasuredSfxTable.EngineTableVolumes(0.8f, true));
    }

    [Fact]
    public void ExhaustVolumeRunsFromMinVolAtIdleToOneAtFullThrottle()
    {
        Eq(0.6f, MeasuredSfxTable.ExhaustVolume(0f, 0.6f), 5);
        Eq(1f, MeasuredSfxTable.ExhaustVolume(1f, 0.6f), 5);
        Eq(0.8f, MeasuredSfxTable.ExhaustVolume(0.5f, 0.6f), 5);
    }

    [Fact]
    public void WhineFollowsDriveRpmAndTorqueMagnitude()
    {
        Assert.Null(MeasuredSfxTable.Whine(0f, 500f, 1400, 1400, 1f));
        var w = MeasuredSfxTable.Whine(1400f / 9.549297f, 3000f, 1400, 1400, 1f)!.Value;
        Assert.True(w.Forward);
        Eq(1401f / 1400f, w.Pitch, 4);
        Eq(0.3f, w.Gain, 5);
        var rev = MeasuredSfxTable.Whine(-700f / 9.549297f, -20000f, 1400, 1400, 2f)!.Value;
        Assert.False(rev.Forward);
        Eq(699f * 2f / 1400f, rev.Pitch, 4);
        Assert.Equal(1f, rev.Gain);
        // creeping: |x| < 50 is lifted to 50 and always forward
        var crawl = MeasuredSfxTable.Whine(-5f / 9.549297f, 100f, 1400, 1400, 1f)!.Value;
        Assert.True(crawl.Forward);
        Eq(50f / 1400f, crawl.Pitch, 5);
    }

    [Fact]
    public void ShippedBaiernRowsRegisterTheTwoThousandFileAtThirtyFiveHundred()
    {
        if (GameRoot.Find() is not { } root)
        {
            return;
        }

        var cls = Path.Combine(root, "parts", "engines", "Baiern_Emer", "scripts", "Baiern_Devils_6SFi_3_6_block.class");
        var setup = SfxTableScraper.Parse(TufaDecompiler.DecompileFile(TufaFile.Read(cls), out _));
        var row = setup.Tables[0].First(r => r.Sound.Id == 0xA2);
        Assert.Equal(3500f, row.RefRpm);
        // at the engine rpm the row names, the sample plays unshifted, whatever its file label says
        var r = new R[MeasuredSfxTable.MaxRows];
        MeasuredSfxTable.Evaluate(setup.Tables[0], 3500f, r);
        var idx = setup.Tables[0].IndexOf(row);
        Eq(1f, r[idx].Pitch, 5);
        Assert.True(r[idx].Active);
        // all shipped rows keep the order assumption of the fade: ascending lower bounds
        foreach (var t in new[] { 0, 2 })
        {
            for (var i = 1; i < setup.Tables[t].Count; i++)
            {
                Assert.True(setup.Tables[t][i].RpmMin >= setup.Tables[t][i - 1].RpmMin);
            }
        }
    }
}
