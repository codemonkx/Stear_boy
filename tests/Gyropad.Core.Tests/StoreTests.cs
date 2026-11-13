using Gyropad.Core.Config;

namespace Gyropad.Core.Tests;

public class StoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string? _previous;

    public StoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "gyropad-store-" + Guid.NewGuid().ToString("N"));
        _previous = Environment.GetEnvironmentVariable("GYROPAD_CONFIG_DIR");
        Environment.SetEnvironmentVariable("GYROPAD_CONFIG_DIR", _dir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GYROPAD_CONFIG_DIR", _previous);
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best effort
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var store = new Store
        {
            PeerName = "Pixel_7",
            PeerHost = "192.168.1.50",
            PeerToken = "0123456789abcdef0123456789abcdef",
            Port = 5010,
            OutSimPort = 30011,
            AssistStrength = 0.65f,
            AssistDeadbandDeg = 4.5f,
            AssistFullAtDeg = 28f,
            AssistInvert = true,
            ForceWheel = true,
            ForceWheelExe = "assetto.exe"
        };
        store.Bindings.Add(new Binding { Exe = "game.exe", ProfileExe = "profile.exe", Device = Binding.DevPad });
        store.Save();

        var loaded = new Store();
        loaded.Load();

        Assert.Equal("Pixel_7", loaded.PeerName);
        Assert.Equal("192.168.1.50", loaded.PeerHost);
        Assert.Equal("0123456789abcdef0123456789abcdef", loaded.PeerToken);
        Assert.Equal(5010, loaded.Port);
        Assert.Equal(30011, loaded.OutSimPort);
        Assert.Equal(0.65f, loaded.AssistStrength, 3);
        Assert.Equal(4.5f, loaded.AssistDeadbandDeg, 3);
        Assert.Equal(28f, loaded.AssistFullAtDeg, 3);
        Assert.True(loaded.AssistInvert);
        Assert.True(loaded.ForceWheel);
        Assert.Equal("assetto.exe", loaded.ForceWheelExe);
        Assert.True(loaded.HasPeer);
        // Upstream does not persist these: they arrive from the phone's settings
        // packet at runtime, so a fresh load falls back to the defaults.
        Assert.Equal(180, loaded.LockDegrees);
        Assert.Equal(0.02f, loaded.SteerDeadzone, 3);
        Binding binding = Assert.Single(loaded.Bindings);
        Assert.Equal("game.exe", binding.Exe);
        Assert.Equal("profile.exe", binding.ProfileExe);
        Assert.Equal(Binding.DevPad, binding.Device);
    }

    [Fact]
    public void MissingFileKeepsDefaults()
    {
        var store = new Store();
        store.Load();

        Assert.Equal(5005, store.Port);
        Assert.Equal(30001, store.OutSimPort);
        Assert.False(store.HasPeer);
    }

    [Fact]
    public void CorruptLinesAreIgnored()
    {
        Directory.CreateDirectory(Store.ConfigDirectory);
        File.WriteAllLines(Store.SettingsPath,
        [
            "not a key value pair",
            "=lonely",
            "port=notanumber",
            "port=5123",
            "bind=only|two",
            "bind=a.exe|b.exe|9",
            "peerName=Kate"
        ]);

        var store = new Store();
        store.Load();

        Assert.Equal(5123, store.Port);
        Assert.Equal("Kate", store.PeerName);
        // A malformed device index falls back to the profile binding, like upstream.
        Binding binding = Assert.Single(store.Bindings);
        Assert.Equal("a.exe", binding.Exe);
        Assert.Equal(Binding.DevProfile, binding.Device);
    }

    [Fact]
    public void ForgetClearsPeerAndPersists()
    {
        var store = new Store
        {
            PeerName = "Pixel",
            PeerHost = "10.0.0.2",
            PeerToken = "abcdefabcdefabcdefabcdefabcdefab"
        };
        store.Save();
        store.Forget();

        var loaded = new Store();
        loaded.Load();
        Assert.False(loaded.HasPeer);
        Assert.Equal("", loaded.PeerHost);
    }
}
