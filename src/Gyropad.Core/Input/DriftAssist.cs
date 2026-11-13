using Gyropad.Core.Telemetry;

namespace Gyropad.Core.Input;

/// <summary>
/// Counter-steer assist: nudges the steering value toward the slip direction
/// reported by Live for Speed's OutSim. Pure math, no platform dependencies.
/// </summary>
public sealed class DriftAssist
{
    public volatile float Strength;
    public volatile float DeadbandDeg = 6f;
    public volatile float FullAtDeg = 35f;
    public volatile float MaxShare = 0.85f;

    public volatile float CatchNorm;
    public volatile float LastAdd;

    private float _smoothed;

    public double Apply(double driver, OutSim? sim)
    {
        CatchNorm = 0f;
        LastAdd = 0f;

        if (sim == null || !sim.Live)
        {
            _smoothed = 0f;
            return driver;
        }

        double fullAt = Math.Max(1.0, FullAtDeg);
        double slip = sim.SlipDeg;
        double beyondDeadband = Math.Abs(slip) - DeadbandDeg;
        if (beyondDeadband <= 0.0)
        {
            _smoothed = 0f;
            return driver;
        }

        double share = Math.Sign(slip) * Math.Min(Math.Abs(slip), fullAt) / fullAt;
        if (share > MaxShare)
            share = MaxShare;
        if (share < 0f - MaxShare)
            share = 0f - MaxShare;
        CatchNorm = (float)share;

        if (Strength <= 0.001f)
            return driver;

        double add = 0.0;
        if (share > 0.0 && driver < share)
            add = (share - driver) * Strength;
        else if (share < 0.0 && driver > share)
            add = (share - driver) * Strength;

        _smoothed += (float)((add - _smoothed) * 0.35);
        LastAdd = _smoothed;

        double result = driver + _smoothed;
        if (result > 1.0)
            result = 1.0;
        if (result < -1.0)
            result = -1.0;
        return result;
    }
}
