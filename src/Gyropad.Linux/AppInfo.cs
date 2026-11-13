using System.Reflection;

namespace Gyropad.Linux;

/// <summary>Identity strings shared by the UI, --version and --doctor.</summary>
internal static class AppInfo
{
    public const string Name = "Gyropad";

    public static string Version
    {
        get
        {
            Assembly assembly = typeof(AppInfo).Assembly;
            string? informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(informational))
            {
                int plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "1.39.0";
        }
    }
}
