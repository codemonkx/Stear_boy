using System;
using Gyropad.Core.Config;
using Gyropad.Core.State;

namespace Gyropad.Linux;

/// <summary><c>--unpair</c>: forget the stored phone without opening the window.</summary>
internal static class UnpairCommand
{
    public static int Run()
    {
        var store = new Store();
        store.Load();

        RuntimeStatus? status = RuntimeStatus.Read();
        if (status != null && RuntimeStatus.IsProcessAlive(status.Pid))
            Console.Error.WriteLine(
                "note: pid " + status.Pid + " is running and keeps the old link until it restarts.");

        if (!store.HasPeer)
        {
            Console.WriteLine("No phone is paired.");
            return 0;
        }

        string name = store.PeerName.Length > 0 ? store.PeerName.Replace('_', ' ') : store.PeerHost;
        store.Forget();
        Console.WriteLine("Forgot " + name + ".");
        Console.WriteLine("settings: " + Store.SettingsPath);
        return 0;
    }
}
