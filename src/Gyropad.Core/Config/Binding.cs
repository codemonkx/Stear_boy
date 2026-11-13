namespace Gyropad.Core.Config;

/// <summary>
/// A per-executable device binding: which output persona this game expects.
/// </summary>
public sealed class Binding
{
    public const int DevProfile = 0;
    public const int DevVjoy = 1;
    public const int DevPad = 2;

    public string Exe = "";
    public string ProfileExe = "";
    public int Device;
}
