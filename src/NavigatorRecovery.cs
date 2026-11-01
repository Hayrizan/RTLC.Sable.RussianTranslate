using System;
using System.Reflection;
using Core.GameManagerStates;
using GameTemplate;
using HarmonyLib;

namespace RTLC.Sable.RussianTranslate;

internal static class NavigatorRecovery
{
    internal static void Install(Harmony harmony) => harmony.Patch(
        typeof(GameplayState).GetMethod(nameof(GameplayState.Update)),
        prefix: new HarmonyMethod(typeof(NavigatorRecovery).GetMethod(nameof(BeforeGameplayUpdate),
            BindingFlags.Static | BindingFlags.NonPublic)));

    private static void BeforeGameplayUpdate(GameplayState __instance)
    {
        // Use the game's own navigation request so rebound keys/controllers work.
        // Restore the missing permanent unlock, then let the native state machine
        // decide whether opening the navigator is currently allowed.
        if (!__instance._tryNavigatorState) return;
        try
        {
            var character = __instance._mainCharacter;
            bool blocked = !GameplayState.IsActive || __instance.CurrentlyInInkOrTimeline ||
                           TutorialManager.BlockingMainMenu || DebugSaveOverlay.IsOpen;
            if (character == null || !NavigatorRecoveryPolicy.ShouldRestore(
                    (int)character.Actions, true, true, blocked)) return;

            var inventory = SingletonAsset.Instance<PlayerInventory>();
            if (inventory == null || inventory.GetAmountHeld("Navigator") <= 0) return;
            var saves = SingletonAsset.Instance<SaveManager>();
            var values = saves?.RuntimeDataToSave;
            if (values == null) return;
            for (int i = 0; i < values.Count; i++)
            {
                var value = values[i];
                if (value == null || value.name != "CustomMarkerTutorialComplete") continue;
                var completed = value.TryCast<BoolValue>();
                if (completed == null || !completed.Value) return;
                int previous = (int)character.Actions;
                character.Actions |= CharacterCapabilities.CustomMarker;
                RussianLanguagePatches.LogDebug($"[Navigator] Restored missing unlock after completed tutorial: {previous} -> {(int)character.Actions}.");
                return;
            }
        }
        catch (Exception exception)
        {
            RussianLanguagePatches.LogDebug($"[Navigator] Recovery check failed: {exception.Message}");
        }
    }
}
