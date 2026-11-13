namespace Gyropad.Core.Transport;

/// <summary>
/// A phone seen on the LAN via the discovery broadcast.
/// </summary>
public sealed class Discovered
{
    public string Name = "";
    public string Host = "";
    public int PairPort;
    public int SeenAt;

    public string Pretty => Name.Replace('_', ' ');
}
