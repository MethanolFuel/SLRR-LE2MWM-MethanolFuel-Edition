using Slrr.Engine.Resources;

namespace Slrr.Cli;

/// <summary>
/// <c>boot [gameRoot] [--stub] [--frames n] [--verbose] [--log] [--gametypes]</c>: headless start-up of the original
/// sequence (system.rpk → Config → Config.init = sl.rpk:0x40 → java.game.Init → GameLogic → SplashScreen); reports
/// how far it gets and which natives are missing next. Implementation: <see cref="BootCommand"/>
/// (src/Slrr.Engine.Resources/Boot.cs). Not wired into Commands.cs yet (the lead integrates).
/// </summary>
public static class CmdBoot
{
    public const string Usage = BootCommand.Usage;

    public static int Run(string[] args, TextWriter o, TextWriter e) => BootCommand.Run(args, o, e);
}
