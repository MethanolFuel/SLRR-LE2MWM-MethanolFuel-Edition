namespace Slrr.Cli;

/// <summary>
/// <c>app [slrr-app options]</c>: the interactive windowed game (src/Slrr.App, docs/app.md) started from the CLI, e.g.
/// <c>slrr app --root .</c>, or headless for CI: <c>slrr app --root . --headless-frames 3000 --input-script builtin:drive</c>.
/// Same options and exit codes as the <c>slrr-app</c> executable (0 ok, 1 data/boot/script failure, 2 usage, 3 no window/GL).
/// </summary>
public static class CmdApp
{
    public const string Usage = "app [--root dir] [--size WxH] [--fullscreen] [--headless-frames n] [--input-script file|builtin:drive] ...   (slrr app --help)";

    public static int Run(string[] args, TextWriter o, TextWriter e) => Slrr.App.AppMain.Run(args, o, e);
}
