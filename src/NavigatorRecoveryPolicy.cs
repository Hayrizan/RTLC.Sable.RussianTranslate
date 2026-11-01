namespace RTLC.Sable.RussianTranslate;

internal static class NavigatorRecoveryPolicy
{
    internal const int Navigator = 0x200;
    // The affected tutorial save retains all ordinary actions, but still has
    // the pre-Sizo mask with CallBike and CustomMarker disabled (e.g. -641).
    // Never lift restrictions belonging to a cutscene or another action state.
    internal static bool ShouldRestore(int actions, bool ownsNavigator, bool tutorialComplete, bool blocked) =>
        !blocked && ownsNavigator && tutorialComplete && (actions & 0x3ff) == 0x17f;
}
