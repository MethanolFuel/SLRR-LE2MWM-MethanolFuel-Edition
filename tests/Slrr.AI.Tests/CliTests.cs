using Xunit;
using Xunit.Abstractions;

namespace Slrr.AI.Tests;

public sealed class CliTests(ITestOutputHelper output)
{
    [Fact]
    public void AiRaceCommandRunsASmallRace()
    {
        var o = new StringWriter();
        var code = Slrr.Cli.CmdAi.Run(["--cars", "2", "--traffic", "6", "--laps", "1", "--no-measure"], o, o);
        output.WriteLine(o.ToString());
        Assert.Equal(0, code);
        Assert.Contains("state hash", o.ToString());
        Assert.Equal(2, Slrr.Cli.CmdAi.Run(["--bogus"], TextWriter.Null, TextWriter.Null));
    }
}
