using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.IL2CPP;
using HarmonyLib;
using Ink;
using Quadtree.TextProviders;
using TextProviders.Implementations;
using TextProviders.Interface;
using Settings;
using TMPro;
using UIComponents.Screens;
using UIComponents.Screens.Settings;
using UIComponents.Layout.Components.Lists;
using UIComponents.Layout.AtomicObjects;
using UIComponents.Layout.AtomicObjects.Buttons;
using UIComponents.Layout.AtomicObjects.Buttons.ButtonDerivatives;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnhollowerBaseLib;
using UnhollowerRuntimeLib;

namespace RTLC.Sable.RussianTranslate;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class RussianTranslationPlugin : BasePlugin
{
    public const string PluginGuid = "rtlc.sable.russiantranslate";
    public const string PluginName = "Sable Russian Translation";
    public const string PluginVersion = "2.3.1";

    private Harmony? _harmony;

    public override void Load()
    {
        var startupTimer = System.Diagnostics.Stopwatch.StartNew();
        // Ready-to-extract installs start in Russian. Preserve an explicit
        // language selection saved by a previous plugin session (including 0).
        int activePref = PlayerPrefs.GetInt("RTLC_Russian_Active", 1);
        RussianLanguagePatches.IsRussianActive = (activePref == 1);

        BundledAssets.Initialize(Log);
        TranslationFiles.Initialize(Log, BundledAssets.Translation);
        RussianLanguagePatches.Logger = Log;
        RussianLanguagePatches.PluginDirectory = Path.Combine(Paths.PluginPath, "RTLC.Sable.RussianTranslate");
        RussianLanguagePatches.LogDebug("Plugin loading...");
        RuntimeTextAudit.Start();

        RussianLanguagePatches.RecoverPersistedRussianLanguage();

        _harmony = new Harmony(PluginGuid);
        ApplyAllPatches(_harmony);
        LogoTextureReplacement.Install(_harmony);
        DebugQuickSave.Install(_harmony);
        NavigatorRecovery.Install(_harmony);
        PatchEnglishTextGetters(_harmony);
        Log.LogInfo($"[Timing] Translation loading and patches: {startupTimer.ElapsedMilliseconds} ms.");

        RussianLanguagePatches.EnsureRussianIsListed();

        if (RussianLanguagePatches.IsRussianActive)
        {
            RussianLanguagePatches.ApplyCyrillicFallbackToAllFonts();
            Log.LogInfo("Russian language active on boot, Cyrillic fonts applied.");
        }

        Log.LogInfo("Russian is installed as the game's primary TextLanguage option.");
        Log.LogInfo("Translation source: rtlc.bundle/translation");
        RussianLanguagePatches.LogDebug("Plugin loaded successfully.");
        Log.LogInfo($"[Timing] Plugin startup total: {startupTimer.ElapsedMilliseconds} ms.");
    }

    public override bool Unload()
    {
        RuntimeTextAudit.Stop();
        LogoTextureReplacement.RefreshAll(false);
        _harmony?.UnpatchSelf();
        return true;
    }

    private static void ApplyAllPatches(Harmony harmony)
    {
        RussianLanguagePatches.LogDebug("Applying explicit Harmony patches...");

        // 1. LanguageSettings.SetLanguage(ITextProvider)
        TryPatch(harmony,
            typeof(LanguageSettings).GetMethod("SetLanguage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.LanguageSettingsSetLanguagePrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 2. LanguageSettings.ApplyInitialSettings(TextLanguage)
        TryPatch(harmony,
            typeof(LanguageSettings).GetMethod("ApplyInitialSettings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.LanguageSettingsApplyInitialSettingsPrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 3. LanguageSettings.OnLanguageChanged(int)
        TryPatch(harmony,
            typeof(LanguageSettings).GetMethod("OnLanguageChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.LanguageSettingsOnLanguageChangedPrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 4. FontLibrary.UpdateFontFallbacks(TextLanguage)
        TryPatch(harmony,
            typeof(FontLibrary).GetMethod("UpdateFontFallbacks", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.FontLibraryUpdateFontFallbacksPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 5. OptionSwitchButton.UpdateName (fixes squares on toggle buttons)
        TryPatch(harmony,
            typeof(OptionSwitchButton).GetMethod("UpdateName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.OptionSwitchButtonUpdateNamePrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.OptionSwitchButtonUpdateNamePostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 6. SettingsManager.GameLanguage setter. Russian is a virtual plugin
        // language; never let the unsupported enum value reach native game state.
        TryPatch(harmony,
            typeof(SettingsManager).GetProperty("GameLanguage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetSetMethod(),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.SettingsManagerSetGameLanguagePrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 7. TextProviderRetriever.Language setter uses the same native fallback.
        TryPatch(harmony,
            typeof(TextProviderRetriever).GetProperty("Language", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetSetMethod(),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.TextProviderRetrieverSetLanguagePrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 8. TextProviderRetriever.GetProvider() (0 parameters)
        TryPatch(harmony,
            typeof(TextProviderRetriever).GetMethod("GetProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.GetProviderParameterlessPrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 9. TextProviderRetriever.GetProvider(TextLanguage) (1 parameter)
        TryPatch(harmony,
            typeof(TextProviderRetriever).GetMethod("GetProvider", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(TextLanguage) }, null),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.GetProviderPrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 10. InkFileRepo.GetInkFile(TextLanguage)
        TryPatch(harmony,
            typeof(InkFileRepo).GetMethod("GetInkFile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(TextLanguage) }, null),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.GetInkFilePrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 11. ItemDefinition.GetName(TextLanguage)
        TryPatch(harmony,
            typeof(ItemDefinition).GetMethod("GetName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(TextLanguage) }, null),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.ItemDefinitionGetNamePrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 12. ItemDefinition.GetDescription(TextLanguage)
        TryPatch(harmony,
            typeof(ItemDefinition).GetMethod("GetDescription", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(TextLanguage) }, null),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.ItemDefinitionGetDescriptionPrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 13. ItemDefinition.GetShopDescription(TextLanguage)
        TryPatch(harmony,
            typeof(ItemDefinition).GetMethod("GetShopDescription", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(TextLanguage) }, null),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.ItemDefinitionGetShopDescriptionPrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // Item pickup notifications read the English field directly instead of
        // calling ItemDefinition.GetName, so translate their finalized TMP text.
        TryPatch(harmony,
            typeof(UI.HUD.Notifications.NotificationTypes.ItemNotification).GetMethod("ShowNotification", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(ItemBuildModel) }, null),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.ItemNotificationShowPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 14. UiButtonBase.SetName(string)
        TryPatch(harmony,
            typeof(UiButtonBase).GetMethod("SetName", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(string) }, null),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.UiButtonBaseSetNamePrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.UiButtonBaseSetNamePostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // LocalisedText can write a caption after the provider was queried.
        // Refresh that one component from its stable localisation key.
        TryPatch(harmony,
            typeof(LocalisedText).GetMethod("UpdateText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.LocalisedTextUpdateTextPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 16. TitleScreen.SetupButtons()
        TryPatch(harmony,
            typeof(TitleScreen).GetMethod("SetupButtons", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.TitleScreenSetupButtonsPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 17. TitleScreen.UpdateButtonPromptText()
        TryPatch(harmony,
            typeof(TitleScreen).GetMethod("UpdateButtonPromptText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.TitleScreenUpdateButtonPromptTextPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 18. TitleScreen.CreateTitleScreenButton(TitleScreenButtons)
        TryPatch(harmony,
            typeof(TitleScreen).GetMethod("CreateTitleScreenButton", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.TitleScreenCreateTitleScreenButtonPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // 19. TitleScreen.OnOpen()
        TryPatch(harmony,
            typeof(TitleScreen).GetMethod("OnOpen", BindingFlags.Public | BindingFlags.Instance),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.TitleScreenOnOpenPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // Late title-screen stages overwrite button captions after OnOpen.
        foreach (var methodName in new[]
                 {
                     "OnOpenComplete",
                     "OnReturnFromSettings",
                     "ResetTitleOptionsListToDefaultState",
                     "OnLanguageChanged",
                     "_OnOpen_b__49_2",
                     "_OnOpen_b__49_3"
                 })
        {
            TryPatch(harmony,
                typeof(TitleScreen).GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.TitleScreenLateRefreshPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        }

        TryPatch(harmony,
            typeof(SettingsBase).GetProperty("ScreenName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetGetMethod(),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.SettingsBaseScreenNamePostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        foreach (var methodName in new[] { "OnOpen", "_OnOpen_b__1_1" })
        {
            TryPatch(harmony,
                typeof(SettingsScreen).GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.SettingsScreenLateRefreshPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        }

        foreach (var methodName in new[] { "OnOpen", "_OnOpen_b__5_0" })
        {
            TryPatch(harmony,
                typeof(TitleSettingsScreen).GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.TitleSettingsScreenLateRefreshPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        }

        // UiTab owns the top-level screen heading (for example, SETTINGS).
        // Patching its semantic update point avoids a global TMP text hook.
        TryPatch(harmony,
            typeof(UiTab).GetMethod("UpdateText", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(string) }, null),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.UiTabUpdateTextPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        // The "Press Any Button To Start" label is owned by this component,
        // outside TitleScreen's text hierarchy.  Refresh only after its own
        // safe lifecycle callbacks have populated the label.
        foreach (var methodName in new[] { "OnChangePromptText", "OnResetPromptText", "OnShowPromptRequested", "OnLanguageChanged", "OnEnable" })
        {
            TryPatch(harmony,
                typeof(UiContextualPromptText).GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.UiContextualPromptTextRefreshPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        }

        // Dialogue text must use one complete font. A fallback-only setup mixes
        // Montserrat Cyrillic with the game's wider Latin punctuation and digits.
        foreach (var methodName in new[] { "Initialize", "OnOpen", "ShowNewText", "ShowChoices", "SetSpeakerName" })
        {
            TryPatch(harmony,
                typeof(DialogueScreen).GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
                postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.DialogueScreenUseUnifiedFontPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        }

        // Inventory names/descriptions are written by UiTextDisplay after the
        // item definition getter has already returned the Russian string.
        // Apply the complete Cyrillic TMP font at that final write point.
        var uiTextDisplayType = typeof(UIComponents.Layout.AtomicObjects.Displays.UiTextDisplay);
        foreach (var method in new MethodBase?[]
        {
            uiTextDisplayType.GetMethod("UpdateText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new Type[] { typeof(string), typeof(bool) }, null),
            uiTextDisplayType.GetMethod("UpdateValues", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new Type[] { typeof(string) }, null),
            uiTextDisplayType.GetMethod("OnTextChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new Type[] { typeof(string) }, null)
        })
        {
            TryPatch(harmony, method,
                postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.UiTextDisplayUpdatePostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        }

        TryPatch(harmony,
            typeof(TextMeshProUGUI).GetMethod("OnPreRenderCanvas", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            prefix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.OriginalTypographyPrefix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.FontGlyphAuditPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        foreach (var method in new MethodBase?[]
        {
            typeof(ButtonPrompt).GetMethod("Initialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(ButtonPrompt).GetMethod("FixedUpdate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(UIComponents.Layout.AtomicObjects.ButtonIndicator).GetMethod("Initialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(UIComponents.Layout.AtomicObjects.ButtonIndicator).GetMethod("SetText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            // These components own the labels shown in the settings controls,
            // map hints and bike HUD. Their text is written after the generic
            // TMP lifecycle callback, so refresh the font at their final write
            // points as well.
            typeof(KeyBindingButton).GetMethod("Init", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(KeyBindingButton).GetMethod("UpdateDisplay", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(UIComponents.Layout.AtomicObjects.Displays.ControllerMappingDisplay.ControllerMappingDisplay).GetMethod("OnOpen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(UIComponents.Layout.AtomicObjects.Displays.ControllerMappingDisplay.ControllerMappingDisplay).GetMethod("SetText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(NonDescriptiveTip).GetMethod("Initialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null),
            typeof(NonDescriptiveTip).GetMethod("UpdateBindingText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(NonDescriptiveTip).GetMethod("RebuildText", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            // The game's runtime font updater can restore the original TMP
            // asset after an animation property pass. Reapply the localized
            // font after that pass so first-open UI does not revert to squares.
            typeof(TextMeshProRuntimeFontUpdater).GetMethod("Awake", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            typeof(TextMeshProRuntimeFontUpdater).GetMethod("OnDidApplyAnimationProperties", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        })
        {
            TryPatch(harmony, method,
                postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.ButtonPromptFontPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        }

        TryPatch(harmony,
            typeof(TradingScreen).GetMethod("OnItemHighlighted", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            postfix: typeof(RussianLanguagePatches).GetMethod(nameof(RussianLanguagePatches.TradingItemHighlightedPostfix), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

        var patched = harmony.GetPatchedMethods().ToList();
        RussianLanguagePatches.LogDebug($"Total active Harmony patched methods: {patched.Count}");
        foreach (var method in patched)
        {
            RussianLanguagePatches.LogDebug($"[ACTIVE HARMONY PATCH] {method.DeclaringType?.Name}.{method.Name}");
        }
    }

    private static void TryPatch(Harmony harmony, MethodBase? target, MethodInfo? prefix = null, MethodInfo? postfix = null)
    {
        if (target == null)
        {
            RussianLanguagePatches.LogDebug($"[PATCH ERROR] Target method not found! (prefix={prefix?.Name}, postfix={postfix?.Name})");
            return;
        }

        try
        {
            harmony.Patch(target,
                prefix: prefix != null ? new HarmonyMethod(prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(postfix) : null);
            RussianLanguagePatches.LogDebug($"[PATCH SUCCESS] Attached to {target.DeclaringType?.Name}.{target.Name}");
        }
        catch (Exception ex)
        {
            RussianLanguagePatches.LogDebug($"[PATCH FAILED] Could not patch {target.DeclaringType?.Name}.{target.Name}: {ex}");
        }
    }

    private static void PatchEnglishTextGetters(Harmony harmony)
    {
        var postfix = new HarmonyMethod(typeof(RussianLanguagePatches), nameof(RussianLanguagePatches.EnglishGetterPostfix));
        int count = 0;
        // TextProvider is abstract and its IL2CPP methods have no native target.
        // Patch only the concrete English implementation used as Russian fallback.
        foreach (var type in new[] { typeof(TextProvider_EN) })
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var getter = property.GetGetMethod();
                if (getter != null && getter.ReturnType == typeof(string) && getter.GetParameters().Length == 0)
                {
                    try
                    {
                        harmony.Patch(getter, postfix: postfix);
                        count++;
                    }
                    catch
                    {
                    }
                }
            }
        }
        RussianLanguagePatches.LogDebug($"Patched {count} English and base text getters.");
    }
}

internal static class RussianLanguagePatches
{
    internal static BepInEx.Logging.ManualLogSource? Logger;
    internal static string PluginDirectory = string.Empty;
    internal static bool IsRussianActive;
    private static TextAsset? _russianInkAsset;
    private static string? _validatedRussianInkJson;
    private static bool _russianInkChecked;
    private static bool _russianListLogged;
    private static bool _nativeLanguageNormalizationLogged;
    private static bool _titleScreenOpenedLogged;
    private static bool _titlePromptTranslatedLogged;
    private const string RussianActivePrefKey = "RTLC_Russian_Active";

    private static readonly string[] LanguageNames = new string[]
    {
        "Русский",
        "English",
        "Français",
        "Español",
        "Deutsch",
        "Português",
        "简体中文",
        "日本語"
    };

    private static int TextLanguageToUIIndex(TextLanguage lang)
    {
        if (lang == TextLanguage.Russian)
            return 0;

        int enumVal = (int)lang;
        if (enumVal >= 0 && enumVal <= 6)
            return enumVal + 1; // English(0)->1, French(1)->2, Spanish(2)->3, etc.

        return 0; // Default to Russian (index 0)
    }

    private static TextLanguage UIIndexToTextLanguage(int index)
    {
        if (index == 0)
            return TextLanguage.Russian;

        if (index >= 1 && index <= 7)
            return (TextLanguage)(index - 1); // Index 1 -> English(0), Index 2 -> French(1), etc.

        return TextLanguage.Russian;
    }

    internal static void LogDebug(string message)
    {
        try
        {
            Logger?.LogInfo(message);
        }
        catch
        {
        }
    }

    internal static void RecoverPersistedRussianLanguage()
    {
        try
        {
            // Sable serializes SettingsManager before its localization singletons
            // are created. Russian (enum value 9) has no native provider or Ink
            // field, so a value written by older plugin versions prevents startup.
            var settingsPath = Path.Combine(Application.persistentDataPath, "SaveData", "SettingsManager");
            if (!File.Exists(settingsPath))
                return;

            var contents = File.ReadAllText(settingsPath);
            const string key = "\"gameLanguage\"";
            int keyIndex = contents.IndexOf(key, StringComparison.Ordinal);
            if (keyIndex < 0)
                return;

            int colonIndex = contents.IndexOf(':', keyIndex + key.Length);
            if (colonIndex < 0)
                return;

            int valueStart = colonIndex + 1;
            while (valueStart < contents.Length && char.IsWhiteSpace(contents[valueStart]))
                valueStart++;

            int valueEnd = valueStart;
            if (valueEnd < contents.Length && contents[valueEnd] == '-')
                valueEnd++;
            while (valueEnd < contents.Length && char.IsDigit(contents[valueEnd]))
                valueEnd++;

            if (!int.TryParse(contents.Substring(valueStart, valueEnd - valueStart), out int savedLanguage) ||
                savedLanguage != (int)TextLanguage.Russian)
            {
                return;
            }

            IsRussianActive = true;
            PlayerPrefs.SetInt(RussianActivePrefKey, 1);
            PlayerPrefs.Save();

            var backupPath = settingsPath + ".rtlc-backup";
            if (!File.Exists(backupPath))
                File.Copy(settingsPath, backupPath);

            var repaired = contents.Substring(0, valueStart) +
                           ((int)TextLanguage.English).ToString() +
                           contents.Substring(valueEnd);
            File.WriteAllText(settingsPath, repaired);
            LogDebug($"Recovered unsupported saved gameLanguage={savedLanguage}; native setting reset to English. Backup: {backupPath}");
        }
        catch (Exception exception)
        {
            LogDebug($"Could not recover persisted Russian language: {exception}");
        }
    }

    private static void SetRussianActive(bool active)
    {
        bool changed = IsRussianActive != active;
        IsRussianActive = active;
        if (changed) LogoTextureReplacement.RefreshAll(active);
        PlayerPrefs.SetInt(RussianActivePrefKey, active ? 1 : 0);
        PlayerPrefs.Save();
        if (active)
            ApplyCyrillicFallbackToAllFonts();
    }

    private static void NormalizeNativeLanguage(ref TextLanguage language)
    {
        if (language != TextLanguage.Russian)
            return;

        SetRussianActive(true);
        language = TextLanguage.English;
        if (!_nativeLanguageNormalizationLogged)
        {
            _nativeLanguageNormalizationLogged = true;
            LogDebug("Mapped virtual Russian selection to native English localization state.");
        }
    }

    internal static void EnsureRussianIsListed()
    {
        try
        {
            LanguageNames[0] = TranslationFiles.Translate("SettingsScreen_LanguageOptions_Russian", "Русский");
            var current = LanguageSettings.languages;
            var values = current == null
                ? new List<TextLanguage>()
                : Enumerable.Range(0, current.Length).Select(i => current[i]).ToList();

            if (!values.Contains(TextLanguage.Russian))
            {
                values.Add(TextLanguage.Russian);
                LanguageSettings.languages = new Il2CppStructArray<TextLanguage>(values.ToArray());
                if (!_russianListLogged)
                {
                    _russianListLogged = true;
                    LogDebug("Added Russian to LanguageSettings.languages array.");
                }
            }
        }
        catch (Exception exception)
        {
            LogDebug($"Could not extend LanguageSettings.languages: {exception}");
        }
    }

    private const string CyrillicCharacters =
        "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя" +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz" +
        "0123456789.,!?:;…—–-\"'“”„’‘/\\()[]{}*+%=<>_№«» \u00a0\t\r\n";

    private static readonly MethodInfo? FontCreateFromPathMethod =
        typeof(Font).GetMethod("Internal_CreateFontFromPath", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

    // The table intentionally contains both title-case and upper-case spellings.
    // A case-insensitive comparer makes those entries duplicate keys and prevents
    // the entire plugin type from initializing.
    private static readonly Dictionary<string, string> DirectTranslations = new(StringComparer.Ordinal)
    {
        { "CONTINUE", "Продолжить" },
        { "Continue", "Продолжить" },
        { "NEW GAME", "Новая игра" },
        { "New Game", "Новая игра" },
        { "LOAD GAME", "Загрузить игру" },
        { "Load Game", "Загрузить игру" },
        { "SETTINGS", "Настройки" },
        { "Settings", "Настройки" },
        { "EXIT GAME", "Выход" },
        { "Exit Game", "Выход" },
        { "EXIT", "Выход" },
        { "Exit", "Выход" },
        { "QUIT GAME", "Выход" },
        { "Quit Game", "Выход" },
        { "QUIT", "Выход" },
        { "Quit", "Выход" },
        { "CHANGE PROFILE", "Сменить профиль" },
        { "Change Profile", "Сменить профиль" },
        { "WISHLIST", "В список желаемого" },
        { "Wishlist", "В список желаемого" },
        { "Press Any Button To Start", "Нажмите любую кнопку" },
        { "Press Any Button", "Нажмите любую кнопку" },
        { "PRESS ANY BUTTON TO START", "Нажмите любую кнопку" },
        { "PRESS ANY BUTTON", "Нажмите любую кнопку" },
        { "PRESS START", "Нажмите любую кнопку" },
        { "Press Start", "Нажмите любую кнопку" },
        { "PRESS ANY KEY", "Нажмите любую кнопку" },
        { "Press Any Key", "Нажмите любую кнопку" },
        { "PRESS ANY KEY TO START", "Нажмите любую кнопку" },
        { "Press Any Key To Start", "Нажмите любую кнопку" },
        { "Press any key to start", "Нажмите любую кнопку" },
        { "BACK", "Назад" },
        { "Back", "Назад" },
        { "ACCEPT", "Принять" },
        { "Accept", "Принять" },
        { "CANCEL", "Отмена" },
        { "Cancel", "Отмена" },
        { "APPLY", "Применить" },
        { "Apply", "Применить" },
        { "DEFAULT", "По умолчанию" },
        { "Default", "По умолчанию" },
        { "RESET", "Сброс" },
        { "Reset", "Сброс" },
        { "YES", "Да" },
        { "Yes", "Да" },
        { "NO", "Нет" },
        { "No", "Нет" },
        { "On", "Вкл." },
        { "Off", "Выкл." },
        { "ON", "Вкл." },
        { "OFF", "Выкл." },
        { "ENABLED", "Включено" },
        { "Enabled", "Включено" },
        { "DISABLED", "Отключено" },
        { "Disabled", "Отключено" },
        { "Controls", "Управление" },
        { "Display", "Экран" },
        { "Audio", "Звук" },
        { "Language", "Язык" },
        { "Accessibility", "Доступность" },
        { "Gameplay", "Игровой процесс" }
    };

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

    private static readonly List<TMP_FontAsset> _cyrillicFallbackFonts = new();
    private static bool _fontsLoaded;
    private static bool _completeFontPolicyLogged;

    internal static void RegisterCustomFontsFromDisk()
    {
        if (BundledAssets.IsLoaded) return;
        if (_fontsLoaded)
            return;
        _fontsLoaded = true;

        try
        {
            var fontsDir = Path.Combine(PluginDirectory, "Fonts");
            if (Directory.Exists(fontsDir))
            {
                int registered = 0;
                foreach (var fontFile in Directory.GetFiles(fontsDir, "*.*"))
                {
                    var ext = Path.GetExtension(fontFile).ToLowerInvariant();
                    if (ext == ".ttf" || ext == ".otf")
                    {
                        int res = AddFontResourceEx(fontFile, 0x10, IntPtr.Zero);
                        if (res > 0)
                            registered++;
                    }
                }
                LogDebug($"Registered {registered} custom font files via GDI AddFontResourceEx.");
            }
        }
        catch (Exception ex)
        {
            LogDebug($"RegisterCustomFontsFromDisk exception: {ex.Message}");
        }
    }

    public static Font? CreateFontFromFile(string filePath)
    {
        if (BundledAssets.IsLoaded) return BundledAssets.Font(filePath);
        if (!File.Exists(filePath))
            return null;

        try
        {
            var font = new Font();
            if (FontCreateFromPathMethod != null)
            {
                FontCreateFromPathMethod.Invoke(null, new object[] { font, filePath });
                font.name = Path.GetFileNameWithoutExtension(filePath);
                return font;
            }
            else
            {
                LogDebug("Font.Internal_CreateFontFromPath method not found via reflection!");
            }
        }
        catch (Exception ex)
        {
            LogDebug($"CreateFontFromFile failed for '{filePath}': {ex.Message}");
        }
        return null;
    }

    internal static List<TMP_FontAsset> GetOrCreateCyrillicFonts()
    {
        int removed = _cyrillicFallbackFonts.RemoveAll(font => !RuntimeFontLifetime.IsAlive(font));
        if (removed > 0) LogDebug($"Removed {removed} unloaded Cyrillic fonts from the cache.");
        if (_cyrillicFallbackFonts.Count > 0 && removed == 0)
            return _cyrillicFallbackFonts;

        RegisterCustomFontsFromDisk();

        var fontsDir = Path.Combine(PluginDirectory, "Fonts");
        LogDebug($"Loading Cyrillic fonts from: {fontsDir}");

        // Priority list of authentic Cyrillic fonts matching Sable typography:
        string[] fontFiles = new string[]
        {
            "Montserrat-Regular.ttf",
            "Montserrat-Medium.ttf",
            "Montserrat-Bold.ttf"
        };

        foreach (var file in fontFiles)
        {
            if (_cyrillicFallbackFonts.Any(font => font.name == "Cyrillic_" + Path.GetFileNameWithoutExtension(file)))
                continue;
            var fullPath = Path.Combine(fontsDir, file);
            if (!BundledAssets.IsLoaded && !File.Exists(fullPath))
                continue;

            try
            {
                var font = CreateFontFromFile(fullPath);
                if (font != null)
                {
                    var fontAsset = TMP_FontAsset.CreateFontAsset(
                        font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                        2048, 2048, AtlasPopulationMode.Dynamic, true);
                    if (fontAsset != null)
                    {
                        fontAsset.name = "Cyrillic_" + Path.GetFileNameWithoutExtension(file);
                        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                        fontAsset.isMultiAtlasTexturesEnabled = true;

                        try
                        {
                            fontAsset.TryAddCharacters(CyrillicCharacters, true);
                            TMP_FontAsset.UpdateAtlasTexturesForFontAssetsInQueue();
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"TryAddCharacters note: {ex.Message}");
                        }

                        RuntimeFontLifetime.Retain(fontAsset, font);
                        _cyrillicFallbackFonts.Add(fontAsset);
                        LogDebug($"Created dynamic TMP_FontAsset '{fontAsset.name}' from {file}.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to create dynamic font for {file}: {ex.Message}");
            }
        }

        // Guaranteed backup from Windows system fonts
        if (_cyrillicFallbackFonts.Count == 0)
        {
            string[] sysFonts = new string[]
            {
                @"C:\Windows\Fonts\arial.ttf",
                @"C:\Windows\Fonts\segoeui.ttf",
                @"C:\Windows\Fonts\tahoma.ttf"
            };

            foreach (var sysPath in sysFonts)
            {
                if (!File.Exists(sysPath))
                    continue;

                try
                {
                    var font = CreateFontFromFile(sysPath);
                    if (font != null)
                    {
                    var fontAsset = TMP_FontAsset.CreateFontAsset(
                        font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                        2048, 2048, AtlasPopulationMode.Dynamic, true);
                        if (fontAsset != null)
                        {
                            fontAsset.name = "Cyrillic_Sys_" + Path.GetFileNameWithoutExtension(sysPath);
                            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                            fontAsset.isMultiAtlasTexturesEnabled = true;
                            fontAsset.TryAddCharacters(CyrillicCharacters, true);
                            TMP_FontAsset.UpdateAtlasTexturesForFontAssetsInQueue();
                            RuntimeFontLifetime.Retain(fontAsset, font);
                            _cyrillicFallbackFonts.Add(fontAsset);
                            LogDebug($"Created dynamic TMP_FontAsset from system font '{sysPath}'.");
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"System font fallback exception for '{sysPath}': {ex.Message}");
                }
            }
        }

        return _cyrillicFallbackFonts;
    }

    internal static void DialogueScreenUseUnifiedFontPostfix(DialogueScreen __instance)
    {
        if (!IsRussianSelected() || __instance == null)
            return;

        try
        {
            var texts = __instance.GetComponentsInChildren<TMP_Text>(true);
            if (texts == null)
                return;

            int changed = 0;
            for (int i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null)
                    continue;
                if (TryTranslateDirectText(text.text, out var debugTranslated))
                    text.text = debugTranslated;
                if (!ContainsCyrillic(text.text))
                    continue;
                var before = text.font;
                ApplyUnifiedFontToLocalizedText(text);
                if (text.font == before)
                    continue;
                changed++;
            }
            if (changed > 0)
                LogDebug($"Applied complete single-face font to {changed} dialogue text components.");
        }
        catch (Exception exception)
        {
            LogDebug($"Could not apply unified dialogue font: {exception}");
        }
    }

    internal static void ApplyCyrillicFallbackToAllFonts()
    {
        OriginalTypography.Prewarm(IsRussianSelected());
        // Reserve fonts are only requested by FindCyrillicFont when no matching
        // original source is available. Do not build unused atlases at startup.
        if (!_completeFontPolicyLogged)
        {
            _completeFontPolicyLogged = true;
            LogDebug("Complete-font policy active: global TMP fallback mixing is disabled.");
        }
    }

    internal static void AttachCyrillicFontToText(TMP_Text? tmpText)
    {
        if (tmpText == null)
            return;

        ApplyUnifiedFontToLocalizedText(tmpText);
    }

    private static bool ContainsCyrillic(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        for (int i = 0; i < value.Length; i++)
            if ((value[i] >= '\u0400' && value[i] <= '\u052f') || value[i] == 'Ё' || value[i] == 'ё')
                return true;
        return false;
    }

    private static TMP_FontAsset? FindCyrillicFont(string suffix)
    {
        var fonts = GetOrCreateCyrillicFonts();
        string expected = "Cyrillic_Montserrat-" + suffix;
        for (int i = 0; i < fonts.Count; i++)
            if (fonts[i] != null && fonts[i].name == expected)
                return fonts[i];
        return fonts.Count > 0 ? fonts[0] : null;
    }

    private static readonly HashSet<int> ImmediateMeshRefreshes = new();
    private static readonly HashSet<int> CanvasAtlasSyncsLogged = new();
    private static bool _immediateMeshRefreshLogged;

    private static void RefreshLocalizedMeshAfterFontChange(TMP_Text text, bool changed, bool calledDuringPreRender)
    {
        if (!FontRenderPolicy.NeedsImmediateMeshRefresh(
                IsRussianSelected(), ContainsCyrillic(text.text), changed, calledDuringPreRender))
            return;

        int id = text.GetInstanceID();
        if (!ImmediateMeshRefreshes.Add(id))
            return;

        try
        {
            // SetAllDirty schedules TMP's graphic/material rebuild. When a
            // replacement is created during the first inventory or location
            // display, that delayed pass is too late for the already queued
            // canvas mesh and its UVs still belong to the old font atlas.
            // Upload the populated atlas and reparse the text immediately.
            TMP_FontAsset.UpdateAtlasTexturesForFontAssetsInQueue();
            text.ForceMeshUpdate(true, true);
            text.SetMaterialDirty();
            if (!_immediateMeshRefreshLogged)
            {
                _immediateMeshRefreshLogged = true;
                LogDebug("Immediate TMP mesh refresh active after first localized font assignment.");
            }
        }
        finally
        {
            ImmediateMeshRefreshes.Remove(id);
        }
    }

    private static void SynchronizeCanvasRendererAtlas(TMP_Text text, bool changed)
    {
        if (text == null) return;
        var uiText = text.TryCast<TextMeshProUGUI>();
        if (uiText == null || text.font == null || uiText.canvasRenderer == null)
            return;

        bool replacementFontActive = text.font.name.StartsWith("RTLC_Original_", StringComparison.Ordinal) ||
                                     text.font.name.StartsWith("Cyrillic_", StringComparison.Ordinal);
        var expectedAtlas = text.font.material != null ? text.font.material.mainTexture : null;
        if (expectedAtlas == null) return;

        var renderer = uiText.canvasRenderer;
        Material? renderedMaterial = null;
        if (renderer.materialCount > 0)
            renderedMaterial = renderer.GetMaterial(0);
        bool rendererAtlasMatches = renderedMaterial != null && renderedMaterial.mainTexture == expectedAtlas;
        if (!FontRenderPolicy.NeedsCanvasRendererAtlasSync(
                IsRussianSelected(), ContainsCyrillic(text.text), replacementFontActive, changed, rendererAtlasMatches))
            return;

        var renderMaterial = uiText.materialForRendering ?? text.fontSharedMaterial ?? text.font.material;
        if (renderMaterial == null) return;
        if (renderer.materialCount < 1) renderer.materialCount = 1;
        renderer.SetMaterial(renderMaterial, 0);
        renderer.SetTexture(expectedAtlas);

        if (CanvasAtlasSyncsLogged.Add(text.GetInstanceID()))
            LogDebug($"Synchronized CanvasRenderer atlas: {text.name}, font={text.font.name}, previousMaterial={renderedMaterial?.name ?? "<none>"}.");
    }

    private static void ApplyUnifiedFontToLocalizedText(TMP_Text? text, bool calledDuringPreRender = false)
    {
        if (!IsRussianSelected() || text == null || !ContainsCyrillic(text.text))
            return;

        try
        {
            // Prefer the matching Euclid/SangBleu source face extracted from
            // this installation. It contains Cyrillic, Latin, digits and
            // punctuation in one atlas. Complete native TMP assets are kept.
            if (OriginalTypography.Apply(text, out var originalChanged))
            {
                RefreshLocalizedMeshAfterFontChange(text, originalChanged, calledDuringPreRender);
                SynchronizeCanvasRendererAtlas(text, originalChanged);
                return;
            }

            string sourceName = text.font != null ? text.font.name.ToLowerInvariant() : string.Empty;
            bool bold = (text.fontStyle & FontStyles.Bold) != 0 || (int)text.fontWeight >= 600 ||
                        sourceName.Contains("bold") || sourceName.Contains("heavy") ||
                        sourceName.Contains("black") || sourceName.Contains("semibold") ||
                        sourceName.Contains("demi");
            bool medium = !bold && (sourceName.Contains("medium") || (int)text.fontWeight >= 500);
            var replacement = FindCyrillicFont(bold ? "Bold" : medium ? "Medium" : "Regular");
            if (replacement == null) return;
            bool changed = text.font != replacement;
            if (changed) text.font = replacement;
            // TMP text components may retain a material preset from the
            // original Latin asset.  The glyph coordinates then point into
            // the wrong atlas and appear as sliced/fragmented artifacts.
            // Keep the replacement font and its atlas paired at the final
            // write point (item cards use this path heavily).
            var expectedMaterial = replacement.material;
            var currentMaterial = text.fontSharedMaterial;
            bool atlasMatches = expectedMaterial != null && currentMaterial != null &&
                                currentMaterial.mainTexture == expectedMaterial.mainTexture;
            if (expectedMaterial != null && FontRenderPolicy.NeedsAtlasRepair(
                    true, true, true, atlasMatches))
            {
                Material? compatible = null;
                try
                {
                    if (currentMaterial != null)
                        compatible = TMP_MaterialManager.GetFallbackMaterial(currentMaterial, expectedMaterial);
                }
                catch
                {
                    // The generated font material is still atlas-compatible.
                }
                text.fontSharedMaterial = compatible ?? expectedMaterial;
                text.UpdateMeshPadding();
                changed = true;
            }
            if (changed) text.SetAllDirty();
            RefreshLocalizedMeshAfterFontChange(text, changed, calledDuringPreRender);
            SynchronizeCanvasRendererAtlas(text, changed);
        }
        catch (Exception exception)
        {
            LogDebug($"Could not apply unified localized font: {exception.Message}");
        }
    }

    internal static void OriginalTypographyPrefix(TextMeshProUGUI __instance)
    {
        if (!IsRussianSelected()) return;
        try { ApplyUnifiedFontToLocalizedText(__instance, true); }
        catch (Exception exception) { LogDebug($"Original typography failed: {exception.Message}"); }
    }

    private static readonly HashSet<int> AuditedGlyphComponents = new();

    internal static void FontGlyphAuditPostfix(TextMeshProUGUI __instance)
    {
        if (!IsRussianSelected() || __instance == null || !ContainsCyrillic(__instance.text)) return;
        try
        {
            var info = __instance.textInfo;
            if (info == null || info.characterCount == 0 || !AuditedGlyphComponents.Add(__instance.GetInstanceID())) return;
            var fonts = new HashSet<string>();
            for (int i = 0; i < info.characterCount; i++)
            {
                var character = info.characterInfo[i];
                fonts.Add(character.fontAsset != null ? character.fontAsset.name : "<none>");
            }
            LogDebug($"Font glyph audit: component={__instance.name}, primary={__instance.font?.name}, rendered={string.Join(",", fonts)}, style={__instance.fontStyle}, text={__instance.text}");
        }
        catch (Exception exception) { LogDebug($"Font glyph audit failed: {exception.Message}"); }
    }

    internal static void ButtonPromptFontPostfix(object __instance)
    {
        if (!IsRussianSelected() || __instance == null) return;
        try
        {
            if (__instance is ButtonPrompt prompt && prompt.promptText != null)
                ApplyUnifiedFontToLocalizedText(prompt.promptText);
            else if (__instance is UIComponents.Layout.AtomicObjects.ButtonIndicator indicator && indicator.indicatorText != null)
                ApplyUnifiedFontToLocalizedText(indicator.indicatorText);

            // The exported game project shows several independent prompt
            // owners (KeyBindingButton, ControllerMappingDisplay and
            // NonDescriptiveTip). They all create their TMP labels late and
            // were therefore missed by the old ButtonPrompt-only patch. Walk
            // only this component's hierarchy and repair localized labels at
            // the point where the game has finished writing them.
            if (__instance is Component component)
            {
                var texts = component.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    var text = texts[i];
                    if (text != null && ContainsCyrillic(text.text))
                        ApplyUnifiedFontToLocalizedText(text);
                }
            }
        }
        catch (Exception exception) { LogDebug($"Action prompt font refresh failed: {exception.Message}"); }
    }

    internal static void UiTextDisplayUpdatePostfix(UIComponents.Layout.AtomicObjects.Displays.UiTextDisplay __instance)
    {
        if (!IsRussianSelected() || __instance == null || __instance.textMeshPro == null)
            return;
        try
        {
            var source = __instance.textMeshPro.text;
            if (TranslationFiles.TryGetItemByEnglish(source, "description", out var translated) ||
                TranslationFiles.TryGetItemByEnglish(source, "name", out translated))
                __instance.textMeshPro.text = translated;
            ApplyUnifiedFontToLocalizedText(__instance.textMeshPro);
        }
        catch (Exception exception)
        {
            LogDebug($"Item text font refresh failed: {exception.Message}");
        }
    }

    internal static void TradingItemHighlightedPostfix(TradingScreen __instance)
    {
        if (!IsRussianSelected() || __instance == null || __instance.itemDisplay == null) return;
        try
        {
            var texts = __instance.itemDisplay.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null) continue;
                // Trading uses a separate card update after inventory callbacks.
                // Prefer explicit shop wording; maps can reuse the normal description.
                if (TranslationFiles.TryGetItemByEnglish(text.text, "shop", out var translated) ||
                    TranslationFiles.TryGetItemByEnglish(text.text, "description", out translated))
                {
                    text.text = translated;
                    ApplyUnifiedFontToLocalizedText(text);
                }
            }
        }
        catch (Exception exception) { LogDebug($"Trading item translation failed: {exception.Message}"); }
    }

    internal static void AttachCyrillicFontToButton(OptionSwitchButton button)
    {
        if (button != null && button.text != null)
        {
            AttachCyrillicFontToText(button.text);
        }
    }

    internal static void OptionSwitchButtonUpdateNamePrefix(OptionSwitchButton __instance)
    {
        try
        {
            if (__instance == null) return;
            AttachCyrillicFontToButton(__instance);


        }
        catch
        {
        }
    }

    internal static void OptionSwitchButtonUpdateNamePostfix(OptionSwitchButton __instance)
    {
        // Translate only the rendered caption. The game's source options remain
        // intact, so the next UpdateName can restore the selected native language.
        if (IsRussianSelected() && __instance != null && __instance.text != null &&
            TryTranslateDirectText(__instance.text.text, out var translated))
            __instance.text.text = translated;
        if (__instance != null)
            ApplyUnifiedFontToLocalizedText(__instance.text);
    }

    internal static void UiButtonBaseSetNamePrefix(UiButtonBase __instance, ref string buttonName)
    {
        if (!IsRussianSelected() || string.IsNullOrEmpty(buttonName))
            return;

        if (TryTranslateDirectText(buttonName, out var translated))
        {
            buttonName = translated;
        }
        else if (TranslationFiles.TryTranslateText(buttonName, out translated))
        {
            buttonName = translated;
        }
    }

    internal static void UiButtonBaseSetNamePostfix(UiButtonBase __instance)
    {
        if (__instance != null && __instance.buttonText != null)
        {
            AttachCyrillicFontToText(__instance.buttonText);
            ApplyUnifiedFontToLocalizedText(__instance.buttonText);
        }
    }

    internal static void TitleScreenSetupButtonsPostfix(TitleScreen __instance)
    {
        try
        {
            LogoTextureReplacement.RefreshAll(IsRussianActive);
            if (!IsRussianSelected() || __instance == null)
                return;

            ApplyCyrillicFallbackToAllFonts();
            TranslateTitleScreenButtons(__instance);
            TranslateTitleScreenPrompt(__instance);
        }
        catch (Exception ex)
        {
            LogDebug($"TitleScreenSetupButtonsPostfix error: {ex.Message}");
        }
    }

    internal static void TitleScreenUpdateButtonPromptTextPostfix(TitleScreen __instance)
    {
        try
        {
            if (!IsRussianSelected() || __instance == null)
                return;

            TranslateTitleScreenPrompt(__instance);
        }
        catch (Exception ex)
        {
            LogDebug($"TitleScreenUpdateButtonPromptTextPostfix error: {ex.Message}");
        }
    }

    internal static void TitleScreenCreateTitleScreenButtonPostfix(TitleScreen __instance)
    {
        try
        {
            if (!IsRussianSelected() || __instance == null)
                return;

            TranslateTitleScreenButtons(__instance);
        }
        catch
        {
        }
    }

    internal static void TitleScreenOnOpenPostfix(TitleScreen __instance)
    {
        try
        {
            if (!IsRussianSelected() || __instance == null)
                return;

            ApplyCyrillicFallbackToAllFonts();
            TranslateTitleScreenButtons(__instance);
            TranslateTitleScreenPrompt(__instance);
            if (!_titleScreenOpenedLogged)
            {
                _titleScreenOpenedLogged = true;
                LogDebug("Title screen opened successfully with virtual Russian language active.");
            }
        }
        catch
        {
        }
    }

    private static void TranslateTitleScreenButtons(TitleScreen titleScreen)
    {
        if (titleScreen == null) return;

        var buttons = titleScreen.GetComponentsInChildren<UiButtonBase>(true);
        if (buttons == null) return;

        for (int i = 0; i < buttons.Length; i++)
        {
            var btn = buttons[i];
            if (btn == null) continue;

            if (btn.buttonText != null)
            {
                AttachCyrillicFontToText(btn.buttonText);
                var currentText = btn.buttonText.text;
                if (!string.IsNullOrEmpty(currentText))
                {
                    if (TryTranslateDirectText(currentText, out var translated))
                    {
                        btn.buttonText.text = translated;
                    }
                    else if (DirectTranslations.TryGetValue(currentText.ToUpperInvariant(), out translated))
                    {
                        btn.buttonText.text = translated;
                    }
                    else if (TranslationFiles.TryTranslateText(currentText, out translated))
                    {
                        btn.buttonText.text = translated;
                    }
                }
                ApplyUnifiedFontToLocalizedText(btn.buttonText);
            }
        }
    }

    private static void TranslateTitleScreenPrompt(TitleScreen titleScreen)
    {
        if (titleScreen == null) return;

        var promptTexts = titleScreen.GetComponentsInChildren<TMP_Text>(true);
        if (promptTexts != null)
        {
            string promptRu = TranslationFiles.Translate("TitleScreen_AnyButton", "Нажмите любую кнопку");
            for (int i = 0; i < promptTexts.Length; i++)
            {
                var pt = promptTexts[i];
                if (pt != null)
                {
                    AttachCyrillicFontToText(pt);
                    var txt = pt.text;
                    if (!string.IsNullOrEmpty(txt) &&
                        (txt.IndexOf("Press", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        txt.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        txt.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        pt.text = promptRu;
                        ApplyUnifiedFontToLocalizedText(pt);
                        if (!_titlePromptTranslatedLogged)
                        {
                            _titlePromptTranslatedLogged = true;
                            LogDebug($"Translated title prompt '{txt}' -> '{promptRu}'.");
                        }
                    }
                }
            }
        }
    }

    internal static void TitleScreenLateRefreshPostfix(TitleScreen __instance)
    {
        if (!IsRussianSelected() || __instance == null)
            return;

        TranslateKnownTextInHierarchy(__instance);
    }

    internal static void SettingsBaseScreenNamePostfix(ref string __result)
    {
        if (IsRussianSelected())
            __result = TranslationFiles.Translate("SettingsScreen_Name", "Настройки");
    }

    internal static void SettingsScreenLateRefreshPostfix(SettingsScreen __instance)
    {
        if (IsRussianSelected() && __instance != null)
            TranslateKnownTextInHierarchy(__instance);
    }

    internal static void TitleSettingsScreenLateRefreshPostfix(TitleSettingsScreen __instance)
    {
        if (IsRussianSelected() && __instance != null)
            TranslateKnownTextInHierarchy(__instance);
    }

    internal static void LocalisedTextUpdateTextPostfix(LocalisedText __instance)
    {
        if (!IsRussianSelected() || __instance == null || __instance._text == null)
            return;

        try
        {
            var translated = TranslationFiles.Translate(__instance.LocalisationKey, __instance._text.text);
            if (!string.Equals(translated, __instance._text.text, StringComparison.Ordinal))
            {
                __instance._text.text = translated;
                AttachCyrillicFontToText(__instance._text);
                ApplyUnifiedFontToLocalizedText(__instance._text);
            }
            ApplyUnifiedFontToLocalizedText(__instance._text);
        }
        catch
        {
        }
    }

    internal static void UiTabUpdateTextPostfix(UiTab __instance)
    {
        if (!IsRussianSelected() || __instance == null || __instance.tabText == null)
            return;

        try
        {
            var source = __instance.tabText.text;
            if (TryTranslateDirectText(source, out var translated))
            {
                __instance.tabText.text = translated;
                AttachCyrillicFontToText(__instance.tabText);
                ApplyUnifiedFontToLocalizedText(__instance.tabText);
                LogDebug($"Translated screen heading '{source}' -> '{translated}'.");
            }
            ApplyUnifiedFontToLocalizedText(__instance.tabText);
        }
        catch
        {
        }
    }

    internal static void UiContextualPromptTextRefreshPostfix(UiContextualPromptText __instance)
    {
        if (!IsRussianSelected() || __instance == null || __instance.text == null)
            return;

        try
        {
            var source = __instance.text.text;
            ApplyUnifiedFontToLocalizedText(__instance.text);
            if (!string.IsNullOrEmpty(source) && TryTranslateDirectText(source, out var direct) &&
                !string.Equals(direct, source, StringComparison.Ordinal))
            {
                __instance.text.text = direct;
                AttachCyrillicFontToText(__instance.text);
                ApplyUnifiedFontToLocalizedText(__instance.text);
                if (!_titlePromptTranslatedLogged)
                {
                    _titlePromptTranslatedLogged = true;
                    LogDebug($"Translated contextual prompt '{source}' -> '{direct}'.");
                }
                return;
            }

            if (string.IsNullOrEmpty(source) ||
                source.IndexOf("Press", StringComparison.OrdinalIgnoreCase) < 0 ||
                (source.IndexOf("Button", StringComparison.OrdinalIgnoreCase) < 0 &&
                 source.IndexOf("Key", StringComparison.OrdinalIgnoreCase) < 0))
                return;

            var translated = TranslationFiles.Translate("TitleScreen_AnyButton", "Нажмите любую кнопку");
            __instance.text.text = translated;
            AttachCyrillicFontToText(__instance.text);
            ApplyUnifiedFontToLocalizedText(__instance.text);

            if (!_titlePromptTranslatedLogged)
            {
                _titlePromptTranslatedLogged = true;
                LogDebug($"Translated contextual title prompt '{source}' -> '{translated}'.");
            }
        }
        catch
        {
        }
    }

    private static bool TryTranslateDirectText(string? source, out string translated)
    {
        if (TranslationFiles.TryTranslateText(source, out translated))
            return true;
        if (!string.IsNullOrEmpty(source) &&
            (DirectTranslations.TryGetValue(source, out translated!) ||
             DirectTranslations.TryGetValue(source.ToUpperInvariant(), out translated!)))
        {
            return true;
        }

        translated = string.Empty;
        return false;
    }

    private static void TranslateKnownTextInHierarchy(Component root)
    {
        try
        {
            var texts = root.GetComponentsInChildren<TMP_Text>(true);
            if (texts == null)
                return;

            int translatedCount = 0;
            for (int i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null)
                    continue;

                AttachCyrillicFontToText(text);
                var current = text.text;
                if (string.IsNullOrEmpty(current))
                    continue;

                if (TryTranslateDirectText(current, out var translated))
                {
                    text.text = translated;
                    translatedCount++;
                }
                ApplyUnifiedFontToLocalizedText(text);
            }

            if (translatedCount > 0)
                LogDebug($"Translated {translatedCount} finalized UI labels under {root.GetType().Name}.");
        }
        catch (Exception exception)
        {
            LogDebug($"Late UI translation failed for {root.GetType().Name}: {exception}");
        }
    }

    internal static bool LanguageSettingsSetLanguagePrefix(LanguageSettings __instance, ITextProvider textProvider)
    {
        try
        {
            LogDebug("LanguageSettings.SetLanguage called.");
            EnsureRussianIsListed();

            var button = __instance.languageButton;
            if (button == null)
            {
                LogDebug("LanguageSettings.SetLanguage: languageButton is null!");
                return true;
            }

            AttachCyrillicFontToButton(button);

            // Injects all 8 language options starting with "Русский" at index 0
            button.InjectOptions(LanguageNames.Length, new Il2CppStringArray(LanguageNames));

            // Set button label
            string buttonTitle = IsRussianActive
                ? TranslationFiles.Translate("SettingsScreen_LanguageSettings_LanguageButton_Name", "Язык")
                : textProvider.SettingsScreen_LanguageSettings_LanguageButton_Name;
            button.SetName(buttonTitle);

            // Determine currently selected language
            var currentLanguage = TextLanguage.Russian;
            if (IsRussianActive)
            {
                currentLanguage = TextLanguage.Russian;
            }
            else if (__instance.settingsManager != null)
            {
                currentLanguage = __instance.settingsManager.GameLanguage;
            }
            else if (TextProviderRetriever.Instance != null)
            {
                currentLanguage = TextProviderRetriever.Instance.Language;
            }

            int selectedIndex = TextLanguageToUIIndex(currentLanguage);

            button.index = selectedIndex;
            button.UpdateName();
            button.CheckIfLeftOrRightArrowShouldBeShown();

            LogDebug($"LanguageSettings.SetLanguage: set 8 options, active language {currentLanguage} at index {selectedIndex}.");
            return false;
        }
        catch (Exception exception)
        {
            LogDebug($"Error in LanguageSettings.SetLanguage prefix: {exception}");
            return true;
        }
    }

    internal static bool LanguageSettingsApplyInitialSettingsPrefix(LanguageSettings __instance, TextLanguage language)
    {
        try
        {
            LogDebug($"LanguageSettings.ApplyInitialSettings called with {language}.");
            EnsureRussianIsListed();

            var button = __instance.languageButton;
            if (button == null)
            {
                LogDebug("LanguageSettings.ApplyInitialSettings: languageButton is null!");
                return true;
            }

            AttachCyrillicFontToButton(button);

            var displayedLanguage = IsRussianActive ? TextLanguage.Russian : language;
            int selectedIndex = TextLanguageToUIIndex(displayedLanguage);
            button.SetInitialValue(selectedIndex);

            if (displayedLanguage == TextLanguage.Russian)
            {
                SetRussianActive(true);
                button.SetName(TranslationFiles.Translate("SettingsScreen_LanguageSettings_LanguageButton_Name", "Язык"));
            }

            LogDebug($"LanguageSettings.ApplyInitialSettings: applied initial index {selectedIndex} for {displayedLanguage} (native {language}).");
            return false;
        }
        catch (Exception exception)
        {
            LogDebug($"Error in LanguageSettings.ApplyInitialSettings prefix: {exception}");
            return true;
        }
    }

    internal static bool LanguageSettingsOnLanguageChangedPrefix(LanguageSettings __instance, int value)
    {
        try
        {
            LogDebug($"LanguageSettings.OnLanguageChanged called with index {value}.");
            EnsureRussianIsListed();

            if (value < 0 || value >= LanguageNames.Length)
                return false;
            bool wasRussian = IsRussianActive;
            TextLanguage targetLanguage = UIIndexToTextLanguage(value);
            LogDebug($"Language changed via UI: index {value} -> {targetLanguage}");

            if (targetLanguage == TextLanguage.Russian)
            {
                SetRussianActive(true);
            }
            else
            {
                SetRussianActive(false);
            }

            // Russian is exposed as a full UI option, but internally Sable must
            // remain on a language for which it actually ships providers/assets.
            var nativeLanguage = targetLanguage == TextLanguage.Russian
                ? TextLanguage.English
                : targetLanguage;

            if (__instance.settingsManager != null)
            {
                var previousNativeLanguage = __instance.settingsManager.GameLanguage;
                __instance.settingsManager.GameLanguage = nativeLanguage;
                // English <-> Russian changes the virtual mode without changing
                // the native enum. Explicitly use the game's refresh entry point.
                if (wasRussian != IsRussianActive && previousNativeLanguage == nativeLanguage)
                    __instance.settingsManager.UpdateLanguageSettings();
            }
            else if (TextProviderRetriever.Instance != null)
            {
                TextProviderRetriever.Instance.Language = nativeLanguage;
            }

            if (wasRussian != IsRussianActive)
            {
                var switches = Resources.FindObjectsOfTypeAll(Il2CppType.Of<OptionSwitchButton>());
                for (int i = 0; i < switches.Length; i++)
                {
                    var option = switches[i]?.TryCast<OptionSwitchButton>();
                    if (option == null || !option.gameObject.activeInHierarchy ||
                        option.text == null || option.names == null ||
                        option.index < 0 || option.index >= option.names.Length)
                        continue;
                    try { option.UpdateName(); }
                    catch (Exception exception)
                    {
                        LogDebug($"Could not refresh option caption: {exception}");
                    }
                }
            }

            var button = __instance.languageButton;
            if (button != null)
            {
                AttachCyrillicFontToButton(button);
                button.index = value;
                button.UpdateName();
                button.CheckIfLeftOrRightArrowShouldBeShown();
                if (IsRussianActive)
                {
                    button.SetName(TranslationFiles.Translate("SettingsScreen_LanguageSettings_LanguageButton_Name", "Язык"));
                }
                else
                {
                    var provider = TextProviderRetriever.Instance?.GetProvider(nativeLanguage);
                    if (provider != null)
                        button.SetName(provider.SettingsScreen_LanguageSettings_LanguageButton_Name);
                }
            }

            return false;
        }
        catch (Exception exception)
        {
            LogDebug($"Error in LanguageSettings.OnLanguageChanged prefix: {exception}");
            // The native handler uses a different index order. Passing our index
            // through after a partial failure could select the wrong language.
            return false;
        }
    }

    internal static void FontLibraryUpdateFontFallbacksPostfix(FontLibrary __instance, TextLanguage language)
    {
        try
        {
            if (language == TextLanguage.Russian || IsRussianActive)
            {
                ApplyCyrillicFallbackToAllFonts();
            }
        }
        catch (Exception exception)
        {
            LogDebug($"Error in FontLibrary.UpdateFontFallbacks postfix: {exception}");
        }
    }

    internal static void SettingsManagerSetGameLanguagePrefix(ref TextLanguage value)
    {
        NormalizeNativeLanguage(ref value);
    }

    internal static void TextProviderRetrieverSetLanguagePrefix(ref TextLanguage value)
    {
        NormalizeNativeLanguage(ref value);
    }

    internal static bool GetProviderParameterlessPrefix(TextProviderRetriever __instance, ref ITextProvider __result)
    {
        if (IsRussianSelected())
        {
            var en = TextProviderRetriever.TextProviderEN;
            if (en != null)
            {
                __result = new ITextProvider(en.Pointer);
                TranslationFiles.ExportEnglishInterface(en);
                return false;
            }
        }
        return true;
    }

    internal static bool GetProviderPrefix(TextLanguage language, ref ITextProvider __result)
    {
        if (language == TextLanguage.Russian || IsRussianSelected())
        {
            var en = TextProviderRetriever.TextProviderEN;
            if (en != null)
            {
                __result = new ITextProvider(en.Pointer);
                TranslationFiles.ExportEnglishInterface(en);
                return false;
            }
        }
        return true;
    }

    internal static bool GetInkFilePrefix(InkFileRepo __instance, TextLanguage language, ref TextAsset __result)
    {
        if (language != TextLanguage.Russian && !IsRussianActive)
            return true;

        var english = __instance.inkFile_EN;
        if (english == null)
            return true;

        if (!_russianInkChecked)
        {
            _russianInkChecked = true;
            TranslationFiles.ExportEnglishInk(english.text);
            var russianJson = TranslationFiles.LoadRussianInk(english.text);
            if (!string.IsNullOrWhiteSpace(russianJson))
            {
                try
                {
                    InkTranslationValidator.Validate(english.text, russianJson);
                    _validatedRussianInkJson = russianJson;
                    LogDebug($"Validated Russian Ink ({russianJson.Length} chars).");
                }
                catch (Exception exception)
                {
                    Logger?.LogError($"Russian Ink rejected; using English for this session. Translation file was not modified. {exception}");
                }
            }
        }
        if (_russianInkAsset == null && _validatedRussianInkJson != null)
        {
            try
            {
                _russianInkAsset = new TextAsset(_validatedRussianInkJson)
                {
                    hideFlags = HideFlags.DontUnloadUnusedAsset
                };
            }
            catch (Exception exception)
            {
                _russianInkAsset = null;
                LogDebug($"Could not create Russian Ink asset from validated JSON; using English: {exception}");
            }
        }
        __result = _russianInkAsset != null ? _russianInkAsset : english;
        return false;
    }

    public static void EnglishGetterPostfix(MethodBase __originalMethod, ref string __result)
    {
        var key = __originalMethod.Name.StartsWith("get_", StringComparison.Ordinal)
            ? __originalMethod.Name.Substring(4)
            : __originalMethod.Name;

        // Keep the option recognizable even while another language is active.
        if (key == "SettingsScreen_LanguageOptions_Russian")
        {
            __result = TranslationFiles.Translate(key, "Русский");
            return;
        }

        if (!IsRussianSelected())
            return;

        __result = TranslationFiles.Translate(key, __result);
    }

    private static bool IsRussianSelected()
    {
        return IsRussianActive;
    }

    internal static bool ItemDefinitionGetNamePrefix(ItemDefinition __instance, TextLanguage language, ref string __result)
    {
        if (__instance != null && (language == TextLanguage.Russian || IsRussianSelected()))
        {
            if (TranslationFiles.TryGetItemName(__instance.Name, out var ruName))
            {
                __result = ruName;
                return false;
            }
        }
        return true;
    }

    internal static bool ItemDefinitionGetDescriptionPrefix(ItemDefinition __instance, TextLanguage language, ref string __result)
    {
        if (__instance != null && (language == TextLanguage.Russian || IsRussianSelected()))
        {
            if (TranslationFiles.TryGetItemDescription(__instance.Name, out var ruDesc))
            {
                __result = ruDesc;
                return false;
            }
        }
        return true;
    }

    internal static bool ItemDefinitionGetShopDescriptionPrefix(ItemDefinition __instance, TextLanguage language, ref string __result)
    {
        if (__instance != null && (language == TextLanguage.Russian || IsRussianSelected()))
        {
            if (TranslationFiles.TryGetItemShopDescription(__instance.Name, out var ruShop))
            {
                __result = ruShop;
                return false;
            }
        }
        return true;
    }

    internal static void ItemDefinitionNameENPostfix(ItemDefinition __instance, ref string __result)
    {
        if (__instance != null && IsRussianSelected())
        {
            if (TranslationFiles.TryGetItemName(__instance.Name, out var ruName))
            {
                __result = ruName;
            }
            else if (TranslationFiles.TryGetItemByEnglish(__result, "name", out var byEnglish))
            {
                __result = byEnglish;
            }
        }
    }

    internal static void ItemDefinitionDescriptionENPostfix(ItemDefinition __instance, ref string __result)
    {
        if (__instance != null && IsRussianSelected())
        {
            if (TranslationFiles.TryGetItemDescription(__instance.Name, out var ruDesc))
            {
                __result = ruDesc;
            }
            else if (TranslationFiles.TryGetItemByEnglish(__result, "description", out var byEnglish))
            {
                __result = byEnglish;
            }
        }
    }

    internal static void ItemDefinitionShopDescriptionENPostfix(ItemDefinition __instance, ref string __result)
    {
        if (__instance != null && IsRussianSelected())
        {
            if (TranslationFiles.TryGetItemShopDescription(__instance.Name, out var ruShop))
            {
                __result = ruShop;
            }
            else if (TranslationFiles.TryGetItemByEnglish(__result, "shop", out var byEnglish))
            {
                __result = byEnglish;
            }
        }
    }

    internal static void ItemNotificationShowPostfix(
        UI.HUD.Notifications.NotificationTypes.ItemNotification __instance,
        ItemBuildModel model)
    {
        if (!IsRussianSelected() || __instance == null || model == null)
            return;

        var definition = model.Definition;
        if (definition == null || __instance.notificationText == null)
            return;

        if (TranslationFiles.TryGetItemName(definition.Name, out var ruName))
        {
            __instance.notificationText.text = ruName;
            ApplyUnifiedFontToLocalizedText(__instance.notificationText);
        }
    }
}

internal static class RuntimeTextAudit
{
    private const int MaxComponents = 8192;
    private const int MaxRows = 30000;
    private const int MaxCharacters = 8192;
    private const int MaxFieldLength = 65536;
    private const int ColumnCount = 25;
    private static readonly Dictionary<int, string> Fingerprints = new();
    private static readonly Queue<int> ComponentOrder = new();
    private static StreamWriter? _writer;
    private static SHA256? _hash;
    private static bool _started;
    private static double _nextTick;
    private static int _tick, _rows, _failures;
    private static string _utc = "";

    internal static bool IsRequested(string[]? commandLine, string? environment, bool triggerFileExists)
    {
        if (triggerFileExists)
            return true;
        if (commandLine != null)
            for (int i = 0; i < commandLine.Length; i++)
                if (string.Equals(commandLine[i], "--rtlc-audit", StringComparison.Ordinal))
                    return true;
        return environment == "1";
    }

    internal static void Start()
    {
        if (_started) return;
        _started = true;
        try
        {
            var args = Environment.GetCommandLineArgs();
            var env = Environment.GetEnvironmentVariable("SABLE_RTLC_AUDIT");
            var trigger = File.Exists(Path.Combine(RussianLanguagePatches.PluginDirectory, "runtime-text-audit.enabled"));
            RussianLanguagePatches.Logger?.LogInfo(
                $"[RuntimeTextAudit] Entry: env={env ?? "<null>"}, trigger={trigger}, args=[{string.Join(" ", args)}]");
            if (!IsRequested(args, env, trigger)) return;
            var path = Path.Combine(RussianLanguagePatches.PluginDirectory,
                $"runtime-text-audit-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.tsv");
            _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false), 65536);
            _hash = SHA256.Create();
            _writer.WriteLine("kind\tutc\ttick\tscene\tscene_handle\thierarchy\tcomponent_id\trussian_active\traw_text\tprimary_font\trendered_fonts\tcharacter_count\texamined_characters\tvisible_characters\tmax_visible_characters\tvisibility_limited\ttext_info_status\tproperties_changed\toverflow_flag\toverflow_mode\tmissing_element_count\tmissing_or_zero_glyph_count\treplacement_candidate_count\tcharacter_evidence\tnote");
            _utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            WriteEvent("start", "schema=1; interval_seconds=2; cache_limit=8192 FIFO; row_limit=30000 including events; character_limit=8192/component; field_limit=65536 UTF-16 units; observe_only; active does not mean on-screen; textInfo may be stale; overflow/replacement/visibility flags are candidates, not confirmed defects; character_evidence=i,raw_index,UTF16,element_unicode,glyph_index,font_id,visible,type,missing_element,missing_glyph,zero_glyph,replacement; lists use | with backslash escaping");
            WriteEvent("heartbeat", "startup; scan_not_yet_run");
            _writer.Flush();
            RussianLanguagePatches.Logger?.LogInfo("[RuntimeTextAudit] Started: " + path);
        }
        catch (Exception exception) { Disable(exception); }
    }

    internal static void Tick()
    {
        if (_writer == null) return;
        try
        {
            double now = Time.realtimeSinceStartup;
            if (now < _nextTick) return;
            _nextTick = now + 2;
            _tick++;
            _utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            int found = 0, active = 0, sampled = 0, changed = 0, errors = 0;
            var sceneCounts = new Dictionary<int, int>();
            try
            {
                var texts = Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_Text>());
                found = texts.Length;
                for (int i = 0; i < texts.Length && _rows < MaxRows - 2; i++)
                {
                    try
                    {
                        var text = texts[i]?.TryCast<TMP_Text>();
                        if (text == null || !text.enabled) continue;
                        var gameObject = text.gameObject;
                        if (gameObject == null || !gameObject.activeInHierarchy) continue;
                        var scene = gameObject.scene;
                        if (!scene.IsValid()) continue;
                        active++;
                        sceneCounts.TryGetValue(scene.handle, out int count);
                        sceneCounts[scene.handle] = count + 1;
                        var row = Observe(text);
                        sampled++;
                        int id = text.GetInstanceID();
                        var fingerprint = Convert.ToBase64String(_hash!.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\t", row.Skip(3)))));
                        if (Fingerprints.TryGetValue(id, out var previous) && previous == fingerprint) continue;
                        if (!Fingerprints.ContainsKey(id))
                        {
                            if (Fingerprints.Count >= MaxComponents) Fingerprints.Remove(ComponentOrder.Dequeue());
                            ComponentOrder.Enqueue(id);
                        }
                        Fingerprints[id] = fingerprint;
                        WriteRow(row);
                        changed++;
                    }
                    catch (Exception exception) when (exception is not IOException && exception is not UnauthorizedAccessException)
                    {
                        errors++;
                        RecordFailure("component", exception);
                    }
                }
            }
            catch (Exception exception) when (exception is not IOException && exception is not UnauthorizedAccessException)
            {
                errors++;
                RecordFailure("scan", exception);
            }
            ObserveScenes(sceneCounts);
            bool rowLimited = _rows >= MaxRows - 2;
            WriteEvent("heartbeat", $"found={found}; active_encountered={active}; sampled={sampled}; changed={changed}; errors={errors}; cache={Fingerprints.Count}; rows_before_heartbeat={_rows}; scan_row_limited={rowLimited}", true);
            if (_rows >= MaxRows - 2)
            {
                WriteEvent("stop", "row_limit_reached; remaining observations omitted", true);
                Stop();
            }
            else _writer.Flush();
        }
        catch (Exception exception) { Disable(exception); }
    }

    private static string[] Observe(TMP_Text text)
    {
        var row = NewRow("text");
        var scene = text.gameObject.scene;
        row[3] = Escape(scene.name);
        row[4] = Number(scene.handle);
        row[5] = Escape(Hierarchy(text.transform));
        row[6] = Number(text.GetInstanceID());
        var raw = text.text ?? "";
        row[8] = Escape(raw);
        row[9] = Escape(FontName(text.font));
        var info = text.textInfo;
        var characters = info?.characterInfo;
        int count = info?.characterCount ?? 0;
        int examined = Math.Min(Math.Max(0, count), Math.Min(characters?.Length ?? 0, MaxCharacters));
        int visible = 0, missingElements = 0, missingGlyphs = 0, replacements = 0;
        var fonts = new SortedSet<string>(StringComparer.Ordinal);
        var evidence = new StringBuilder();
        bool evidenceTruncated = false;
        for (int i = 0; i < examined; i++)
        {
            var character = characters![i];
            if (character == null) throw new InvalidOperationException("Null characterInfo entry at " + i);
            var font = character.fontAsset;
            fonts.Add(Escape(FontName(font)));
            var element = character.textElement;
            var glyph = element?.glyph;
            bool missingElement = element == null;
            bool missingGlyph = glyph == null;
            bool zeroGlyph = glyph != null && glyph.index == 0;
            uint unicode = element?.unicode ?? 0;
            bool replacement = character.elementType == TMP_TextElementType.Character &&
                (character.character == '\ufffd' || unicode == 0xfffd || unicode == 0x25a1 || unicode == 0x25a0);
            if (character.isVisible) visible++;
            if (missingElement) missingElements++;
            if (missingGlyph || zeroGlyph) missingGlyphs++;
            if (replacement) replacements++;
            if (evidence.Length < MaxFieldLength - 256)
            {
                if (evidence.Length > 0) evidence.Append('|');
                evidence.Append(i).Append(',').Append(character.index).Append(',')
                    .Append(((int)character.character).ToString("X4", CultureInfo.InvariantCulture)).Append(',')
                    .Append(element == null ? "none" : unicode.ToString("X", CultureInfo.InvariantCulture)).Append(',')
                    .Append(glyph == null ? "none" : Number(glyph.index)).Append(',')
                    .Append(font == null ? "none" : Number(font.GetInstanceID())).Append(',')
                    .Append(character.isVisible ? '1' : '0').Append(',').Append(character.elementType).Append(',')
                    .Append(missingElement ? '1' : '0').Append(',').Append(missingGlyph ? '1' : '0').Append(',')
                    .Append(zeroGlyph ? '1' : '0').Append(',').Append(replacement ? '1' : '0');
            }
            else evidenceTruncated = true;
        }
        int maxVisible = text.maxVisibleCharacters;
        row[10] = Escape(string.Join("|", fonts));
        row[11] = Number(count);
        row[12] = Number(examined);
        row[13] = Number(visible);
        row[14] = Number(maxVisible);
        row[15] = maxVisible < count ? "1" : "0";
        row[16] = info == null ? "unavailable" : characters == null ? "no_character_array" :
            count < 0 || count > characters.Length ? "inconsistent" : examined < count ? "audit_character_limit" :
            count == 0 && raw.Length > 0 ? "empty_for_nonempty_raw" : "cached_not_forced";
        row[17] = text.havePropertiesChanged ? "1" : "0";
        row[18] = text.isTextOverflowing ? "1" : "0";
        row[19] = Escape(text.overflowMode.ToString());
        row[20] = Number(missingElements);
        row[21] = Number(missingGlyphs);
        row[22] = Number(replacements);
        row[23] = evidence.ToString();
        row[24] = Escape($"raw_utf16_length={raw.Length}; raw_truncated={raw.Length > MaxFieldLength}; evidence_truncated={evidenceTruncated}; counts_include_whitespace_controls_sprites_and_not_yet_visible; maxVisible_is_not_proof_of_typewriter; fontStyle={text.fontStyle}; fontSize={Number(text.fontSize)}");
        return row;
    }

    private static void ObserveScenes(Dictionary<int, int> counts)
    {
        try
        {
            int count = SceneManager.sceneCount;
            for (int i = 0; i < count && _rows < MaxRows - 2; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                var row = NewRow("scene");
                row[3] = Escape(scene.name);
                row[4] = Number(scene.handle);
                counts.TryGetValue(scene.handle, out int active);
                row[24] = Escape($"loaded={scene.isLoaded}; active_tmp_encountered={active}; scene_path={scene.path}; build_index={scene.buildIndex}; enumerated_scenes={count}; loaded_is_not_visited_or_rendered");
                WriteRow(row);
            }
        }
        catch (Exception exception) when (exception is not IOException && exception is not UnauthorizedAccessException)
        {
            RecordFailure("scene_enumeration", exception);
        }
    }

    private static string Hierarchy(Transform transform)
    {
        var parts = new List<string>();
        Transform? current = transform;
        while (current != null && parts.Count < 128)
        {
            parts.Add(Escape(current.name) + "[" + Number(current.GetSiblingIndex()) + "]");
            current = current.parent;
        }
        if (current != null) parts.Add("<hierarchy_limit>");
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string FontName(TMP_FontAsset? font) => font == null ? "<none>" : font.name + "#" + Number(font.GetInstanceID());
    private static string Number<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);

    private static string Escape(string? value)
    {
        if (value == null) return "\\N";
        var escaped = new StringBuilder();
        int count = Math.Min(value.Length, MaxFieldLength);
        for (int i = 0; i < count; i++)
        {
            char c = value[i];
            if (c == '\\') escaped.Append("\\\\");
            else if (c == '\t') escaped.Append("\\t");
            else if (c == '\r') escaped.Append("\\r");
            else if (c == '\n') escaped.Append("\\n");
            else if (char.IsControl(c) || char.IsSurrogate(c) || c == '\u2028' || c == '\u2029' ||
                     c == '|' || c == '/' || c == '[' || c == ']' ||
                     (i == 0 && (c == '=' || c == '+' || c == '-' || c == '@')))
                escaped.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            else escaped.Append(c);
        }
        if (value.Length > count) escaped.Append("\\TRUNCATED");
        return escaped.ToString();
    }

    private static string[] NewRow(string kind)
    {
        var row = new string[ColumnCount];
        Array.Fill(row, "");
        row[0] = kind;
        row[1] = _utc;
        row[2] = Number(_tick);
        row[7] = RussianLanguagePatches.IsRussianActive ? "1" : "0";
        return row;
    }

    private static void WriteRow(string[] row, bool final = false)
    {
        if (_writer == null || _rows >= (final ? MaxRows : MaxRows - 2)) return;
        _writer.WriteLine(string.Join("\t", row));
        _rows++;
    }

    private static void WriteEvent(string kind, string note, bool final = false)
    {
        var row = NewRow(kind);
        row[24] = Escape(note);
        WriteRow(row, final);
    }

    private static void RecordFailure(string scope, Exception exception)
    {
        WriteEvent("error", scope + ": " + exception);
        if (_failures++ < 10)
            RussianLanguagePatches.Logger?.LogError("[RuntimeTextAudit] " + scope + ": " + exception);
    }

    private static void Disable(Exception exception)
    {
        Stop();
        try { RussianLanguagePatches.Logger?.LogError("[RuntimeTextAudit] Disabled: " + exception); }
        catch { }
    }

    internal static void Stop()
    {
        var writer = _writer;
        _writer = null;
        try { writer?.Dispose(); }
        catch (Exception exception)
        {
            try { RussianLanguagePatches.Logger?.LogError("[RuntimeTextAudit] Close failed: " + exception); }
            catch { }
        }
        _hash?.Dispose();
        _hash = null;
        Fingerprints.Clear();
        ComponentOrder.Clear();
    }
}
