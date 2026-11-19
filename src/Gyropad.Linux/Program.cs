using System;
using Avalonia;

namespace Gyropad.Linux;

internal static class Program
{
    /// <summary>
    /// Entry point. Every mode that starts the app takes the single-instance
    /// lock; the one-shot commands (<c>--status</c>, <c>--unpair</c>,
    /// <c>--doctor</c>) read files or bind transient ports and never take it.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (!Options.TryParse(args, out Options options, out string error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }

        if (options.Help)
        {
            Console.WriteLine("Gyropad " + AppInfo.Version);
            Console.WriteLine();
            Console.WriteLine(Options.Usage);
            return 0;
        }

        if (options.Version)
        {
            Console.WriteLine(AppInfo.Version);
            return 0;
        }

        if (options.Doctor)
            return Doctor.Run();

        if (options.Status)
            return StatusReport.Run();

        if (options.Unpair)
            return UnpairCommand.Run();

        if (options.Report)
            return Report.Run();

        using InstanceLock? guard = InstanceLock.TryAcquire(out string lockError, out string warning);
        if (guard == null)
        {
            Console.Error.WriteLine(lockError);
            return 1;
        }

        if (warning.Length > 0)
            Console.Error.WriteLine(warning);

        RacepadOverride.Routing = options.Racepad;

        if (options.Headless)
            return Headless.Run(options);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Gyropad could not open a window: " + ex.Message);
            Console.Error.WriteLine("On a machine without a desktop session use: Gyropad --headless");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
