namespace Slrr.Render.Host;

internal static class Program
{
    private static int Main(string[] args) => Slrr.Cli.CmdRender.Run(args, Console.Out, Console.Error);
}
