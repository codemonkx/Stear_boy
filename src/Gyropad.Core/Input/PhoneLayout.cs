namespace Gyropad.Core.Input;

/// <summary>
/// Shared phone-screen geometry. The phone reports its aspect ratio in the
/// settings packet; the UI uses it to keep painted controls proportional.
/// The layout tables themselves are ported with the Avalonia UI.
/// </summary>
public static class PhoneLayout
{
    public const float H = 1080f;

    public static volatile float W = 2340f;

    public static void SetAspect(float wOverH)
    {
        if (!(wOverH < 1.2f) && !(wOverH > 3f))
            W = 1080f * wOverH;
    }
}
