using Gyropad.Core.Input;
using Gyropad.Core.Telemetry;

namespace Gyropad.Core.Tests;

public class DriftAssistTests
{
    private static OutSim LiveSim(float slipDeg) => new()
    {
        Live = true,
        SlipDeg = slipDeg,
        LastAt = Environment.TickCount
    };

    [Fact]
    public void NoSimLeavesDriverAlone()
    {
        var assist = new DriftAssist { Strength = 1f };
        Assert.Equal(0.3, assist.Apply(0.3, null));
        Assert.Equal(0f, assist.CatchNorm);
    }

    [Fact]
    public void DeadSimIsIgnored()
    {
        var assist = new DriftAssist { Strength = 1f };
        var sim = LiveSim(30f);
        sim.Live = false;

        Assert.Equal(-0.4, assist.Apply(-0.4, sim));
        Assert.Equal(0f, assist.LastAdd);
    }

    [Fact]
    public void SlipInsideDeadbandDoesNothing()
    {
        var assist = new DriftAssist { Strength = 1f, DeadbandDeg = 6f };

        Assert.Equal(0.2, assist.Apply(0.2, LiveSim(5f)));
        Assert.Equal(0f, assist.CatchNorm);
    }

    [Fact]
    public void AssistOnlyAddsTowardTheSlipDirection()
    {
        var assist = new DriftAssist { Strength = 1f, DeadbandDeg = 6f, FullAtDeg = 35f };

        // Already steering further than the catch target: no correction.
        Assert.Equal(1.0, assist.Apply(1.0, LiveSim(20f)));
        Assert.Equal(0f, assist.LastAdd);

        // Steering the wrong way: correction is positive but bounded by Strength.
        double corrected = assist.Apply(-0.5, LiveSim(20f));
        Assert.True(corrected > -0.5);
        Assert.True(corrected <= 1.0);
        Assert.True(assist.LastAdd > 0f);
    }

    [Fact]
    public void ZeroStrengthReportsCatchButDoesNotChangeDriver()
    {
        var assist = new DriftAssist { Strength = 0f, DeadbandDeg = 6f };

        double result = assist.Apply(0.0, LiveSim(-20f));

        Assert.Equal(0.0, result);
        Assert.True(assist.CatchNorm < 0f);
        Assert.Equal(0f, assist.LastAdd);
    }

    [Fact]
    public void OutputIsClampedToOne()
    {
        var assist = new DriftAssist { Strength = 1f, DeadbandDeg = 1f, FullAtDeg = 2f };

        double result = assist.Apply(0.99, LiveSim(90f));
        Assert.InRange(result, -1.0, 1.0);
    }

    [Fact]
    public void CatchIsCappedByMaxShare()
    {
        var assist = new DriftAssist { Strength = 1f, DeadbandDeg = 0f, FullAtDeg = 1f, MaxShare = 0.5f };

        assist.Apply(0.0, LiveSim(90f));
        Assert.Equal(0.5f, assist.CatchNorm, 4);
    }
}
