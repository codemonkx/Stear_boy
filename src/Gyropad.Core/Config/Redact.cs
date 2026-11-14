namespace Gyropad.Core.Config;

/// <summary>
/// Redaction for settings lines copied into logs and diagnostic reports. The
/// pairing token grants control of the virtual devices, so it must never leave
/// the machine; every other value is kept because it is needed for diagnosis.
/// </summary>
public static class Redact
{
    public static string SettingsLine(string line)
    {
        int equals = line.IndexOf('=');
        if (equals <= 0)
            return line;

        string key = line[..equals].Trim();
        return key.Contains("token", StringComparison.OrdinalIgnoreCase)
            ? key + "=<redacted>"
            : line;
    }
}
