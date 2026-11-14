using Gyropad.Core.Config;

namespace Gyropad.Core.Tests;

public sealed class RedactTests : IDisposable
{
    public void Dispose()
    {
        // no shared state
    }

    [Theory]
    [InlineData("peerToken=abc123", "peerToken=<redacted>")]
    [InlineData("  peerToken = abc123", "peerToken=<redacted>")]
    [InlineData("peerToken=", "peerToken=<redacted>")]
    [InlineData("someTokenHash=deadbeef", "someTokenHash=<redacted>")]
    public void TokenLinesAreRedacted(string line, string expected)
        => Assert.Equal(expected, Redact.SettingsLine(line));

    [Theory]
    [InlineData("peerName=Pixel_7")]
    [InlineData("peerHost=192.168.1.20")]
    [InlineData("port=5005")]
    [InlineData("assist=0.5")]
    [InlineData("bind=dirt.exe|dirt.exe|0")]
    [InlineData("# a comment")]
    [InlineData("")]
    public void OtherLinesAreCopiedVerbatim(string line)
        => Assert.Equal(line, Redact.SettingsLine(line));

    [Fact]
    public void LeadingEqualsIsNotTreatedAsAKey()
        => Assert.Equal("=abc", Redact.SettingsLine("=abc"));
}
