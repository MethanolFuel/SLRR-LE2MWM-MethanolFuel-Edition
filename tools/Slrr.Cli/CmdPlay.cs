using Slrr.Game;

namespace Slrr.Cli;

/// <summary>
/// <c>play</c>: headless game host with scripted input (src/Slrr.Game, docs/phase4/game.md).
/// Integration (lead): add a ProjectReference to src/Slrr.Game/Slrr.Game.csproj in Slrr.Cli.csproj and
/// <c>"play" =&gt; CmdPlay.Run(args[1..], o, e)</c> in Commands.cs.
/// </summary>
public static class CmdPlay
{
    public const string Usage = PlayCommand.Usage;

    public static int Run(string[] args, TextWriter o, TextWriter e) => PlayCommand.Run(args, o, e);
}
