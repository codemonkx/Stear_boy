using System;
using System.Linq;
using Avalonia;

namespace Gyropad.Linux;

internal static class Program
{
    /// <summary>
    /// Entry point. <c>--doctor</c> runs the environment checks in a terminal
    /// without starting the UI, which is the fastest way to debug a fresh
    /// install over SSH or in a bug report.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => a is "--doctor" or "doctor"))
            return Doctor.Run();

        if (args.Any(a => a is "--help" or "-h" or "/?"))
        {
            Console.WriteLine("Gyropad " + AppInfo.Version);
            Console.WriteLine();
            Console.WriteLine("usage: Gyropad [--doctor] [--version]");
            Console.WriteLine();
            Console.WriteLine("  --doctor    check uinput, permissions, ports and config");
            Console.WriteLine("  --version   print the version and exit");
            return 0;
        }

        if (args.Any(a => a is "--version" or "-v"))
        {
            Console.WriteLine(AppInfo.Version);
            return 0;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Gyropad could not start: " + ex.Message);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
