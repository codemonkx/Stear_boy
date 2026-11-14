using Gyropad.Core.Config;
using Gyropad.Core.State;

namespace Gyropad.Core.Tests;

public class RuntimeStatusTests : IDisposable
{
    private readonly string _dir;
    private readonly string? _previous;

    public RuntimeStatusTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "gyropad-status-" + Guid.NewGuid().ToString("N"));
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
    public void SnapshotRoundTripsEveryField()
    {
        long started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var status = new RuntimeStatus
        {
            Pid = Environment.ProcessId,
            StartedAtUnixMs = started,
            Mode = "headless",
            Connected = true,
            PadMode = true,
            WheelViaPad = true,
            PadBecauseGame = true,
            PeerName = "Pixel_7",
            PeerHost = "192.168.1.50",
            StatusLine = "linked",
            InputPort = 5005,
            PairPort = 5007,
            InputFault = "",
            DeviceStatus = "ready",
            DeviceDetail = "wheel and pad",
            Steer = -0.25f,
            Throttle = 0.5f,
            Brake = 0.75f,
            Clutch = 0.1f,
            Handbrake = 1f,
            Buttons = 0x8001,
            LeftX = 0.2f,
            LeftY = -0.2f,
            RightX = 0.3f,
            RightY = -0.3f,
            LeftTrigger = 0.4f,
            RightTrigger = 0.6f,
            PadButtons = 0x1000
        };

        status.Write();
        RuntimeStatus? read = RuntimeStatus.Read();

        Assert.NotNull(read);
        Assert.Equal(status.Pid, read!.Pid);
        Assert.Equal(started, read.StartedAtUnixMs);
        Assert.Equal("headless", read.Mode);
        Assert.True(read.Connected);
        Assert.True(read.PadMode);
        Assert.True(read.WheelViaPad);
        Assert.True(read.PadBecauseGame);
        Assert.Equal("Pixel_7", read.PeerName);
        Assert.Equal("192.168.1.50", read.PeerHost);
        Assert.Equal("linked", read.StatusLine);
        Assert.Equal(5005, read.InputPort);
        Assert.Equal(5007, read.PairPort);
        Assert.Equal("ready", read.DeviceStatus);
        Assert.Equal("wheel and pad", read.DeviceDetail);
        Assert.Equal(-0.25f, read.Steer, 3);
        Assert.Equal(0.5f, read.Throttle, 3);
        Assert.Equal(0.75f, read.Brake, 3);
        Assert.Equal(0.1f, read.Clutch, 3);
        Assert.Equal(1f, read.Handbrake, 3);
        Assert.Equal(0x8001, read.Buttons);
        Assert.Equal(0.2f, read.LeftX, 3);
        Assert.Equal(-0.2f, read.LeftY, 3);
        Assert.Equal(0.3f, read.RightX, 3);
        Assert.Equal(-0.3f, read.RightY, 3);
        Assert.Equal(0.4f, read.LeftTrigger, 3);
        Assert.Equal(0.6f, read.RightTrigger, 3);
        Assert.Equal(0x1000, read.PadButtons);
    }

    [Fact]
    public void MissingFileReadsAsNull()
    {
        Assert.Null(RuntimeStatus.Read());
    }

    [Fact]
    public void CorruptFileReadsAsNullAndNeverThrows()
    {
        Directory.CreateDirectory(Store.ConfigDirectory);
        File.WriteAllText(RuntimeStatus.StatusPath, "{ this is not json");

        Assert.Null(RuntimeStatus.Read());
    }

    [Fact]
    public void ClearRemovesTheSnapshot()
    {
        new RuntimeStatus { Pid = Environment.ProcessId, Mode = "window" }.Write();
        Assert.True(File.Exists(RuntimeStatus.StatusPath));

        RuntimeStatus.Clear();

        Assert.False(File.Exists(RuntimeStatus.StatusPath));
        Assert.Null(RuntimeStatus.Read());
    }

    [Fact]
    public void WriteReplacesAnExistingSnapshot()
    {
        new RuntimeStatus { Pid = 1, Mode = "window", StatusLine = "old" }.Write();
        new RuntimeStatus { Pid = 2, Mode = "headless", StatusLine = "new" }.Write();

        RuntimeStatus? read = RuntimeStatus.Read();
        Assert.NotNull(read);
        Assert.Equal(2, read!.Pid);
        Assert.Equal("headless", read.Mode);
        Assert.Equal("new", read.StatusLine);
    }

    [Fact]
    public void CurrentProcessIsAlive()
    {
        Assert.True(RuntimeStatus.IsProcessAlive(Environment.ProcessId));
    }

    [Fact]
    public void ImpossiblePidsAreNotAlive()
    {
        Assert.False(RuntimeStatus.IsProcessAlive(0));
        Assert.False(RuntimeStatus.IsProcessAlive(-1));
        Assert.False(RuntimeStatus.IsProcessAlive(int.MaxValue));
    }

    [Fact]
    public void UptimeIsDerivedFromTheStartTime()
    {
        var status = new RuntimeStatus
        {
            StartedAtUnixMs = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds()
        };

        Assert.InRange(status.Uptime, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void MissingStartTimeMeansZeroUptime()
    {
        Assert.Equal(TimeSpan.Zero, new RuntimeStatus().Uptime);
    }
}
