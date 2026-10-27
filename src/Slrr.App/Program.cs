namespace Slrr.App;

public static class Program
{
    // Synchronous Main on the process's first thread: Cocoa (macOS) requires windowing and the GL context on the main thread.
    [STAThread]
    public static int Main(string[] args) => AppMain.Run(args, Console.Out, Console.Error);
}
