using System.Globalization;
using Slrr.Engine.Parts;
using Slrr.Engine.Resources;
using Slrr.Engine.SystemLib;
using Slrr.Engine.Ui;
using Slrr.Vm;

namespace Slrr.Cli;

/// <summary>Headless boot with every engine native area plugged in (resources, system/io, parts, ui).</summary>
public static class CmdEngine
{
    public const string Usage = """
        engine-boot [gameRoot] [--stub] [--frames n] [--verbose]
          like `boot`, but registers the System, Parts and Ui native modules as well.
        """;

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        string? root = null;
        var stub = false;
        var verbose = false;
        var frames = 60;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--stub": stub = true; break;
                case "--verbose": verbose = true; break;
                case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n):
                    frames = n;
                    i++;
                    break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        e.WriteLine(Usage);
                        return 2;
                    }

                    root = args[i];
                    break;
            }
        }

        root = Path.GetFullPath(root ?? Directory.GetCurrentDirectory());
        if (!File.Exists(Path.Combine(root, "system.rpk")))
        {
            e.WriteLine("error: no game root (a directory with system.rpk) given or found");
            return 1;
        }

        var saveRoot = Path.Combine(Path.GetTempPath(), "slrr-engine-boot-" + Environment.ProcessId);
        Directory.CreateDirectory(saveRoot);
        try
        {
            var modules = new INativeModule[]
            {
                new EngineSystem(new GameSandbox(root, saveRoot)) { RegisterOpenLib = false, RegisterTimerNatives = false, FixThreadSleep = false },
                new PartsNatives(),
                new UiNatives(new UiEngine()),
            };
            var report = Boot.Run(new BootOptions { Root = root, Stub = stub, Frames = frames, Modules = modules });
            Boot.Print(report, o, verbose);
            return report.StoppedBy is null ? 0 : 3;
        }
        finally
        {
            try
            {
                Directory.Delete(saveRoot, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
