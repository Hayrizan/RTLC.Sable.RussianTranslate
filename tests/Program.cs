using System;
using System.IO;
using System.Text.Json;
using System.Linq;
using RTLC.Sable.RussianTranslate;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        _passed++;
        Console.WriteLine("PASS " + name);
    }
    private static void Reject(string original, string changed, string name)
    {
        bool rejected = false;
        try { InkTranslationValidator.Validate(original, changed); }
        catch { rejected = true; }
        Check(rejected, name);
    }
    private static void Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--audit-game-ink")
        {
            var source = File.ReadAllText(args[1]);
            var live = UnifiedTranslationCatalog.Parse(File.ReadAllText(args[2]));
            var translated = live.BuildInk(source);
            InkTranslationValidator.Validate(source, translated);
            Check(translated.Contains("inventory_add: Navigator"), "Sizo's Navigator grant is preserved in the complete translated story");
            Console.WriteLine($"Full game Ink validated against {live.Count} catalog entries.");
            return;
        }
        Check(!FontRenderPolicy.IsOwnCharacter(0x410, 0x25A1, true),
            "Cached missing-glyph square does not count as Cyrillic coverage");
        Check(!FontRenderPolicy.IsOwnCharacter(0x410, 0x410, false),
            "Cached fallback glyph does not count as native Cyrillic coverage");
        Check(FontRenderPolicy.IsOwnCharacter(0x410, 0x410, true),
            "Native Cyrillic glyph remains valid");
        Check(NavigatorRecoveryPolicy.ShouldRestore(-641, true, true, false),
            "Completed Sizo tutorial with the saved pre-unlock mask is recovered");
        Check(!NavigatorRecoveryPolicy.ShouldRestore(-641, false, true, false),
            "Navigator recovery never grants an unowned item");
        Check(!NavigatorRecoveryPolicy.ShouldRestore(-641, true, false, false),
            "Navigator recovery does not skip the tutorial");
        Check(!NavigatorRecoveryPolicy.ShouldRestore(-641, true, true, true),
            "Navigator recovery respects dialogue and menu restrictions");
        Check(!NavigatorRecoveryPolicy.ShouldRestore(0, true, true, false) &&
              !NavigatorRecoveryPolicy.ShouldRestore(512, true, true, false),
            "Navigator recovery leaves restricted capability masks untouched");
        Check(!NavigatorRecoveryPolicy.ShouldRestore(-129, true, true, false),
            "Navigator recovery leaves an already unlocked navigator untouched");
        Check(FontRenderPolicy.NeedsCompleteFont(true, true, false),
            "Cyrillic text replaces an incomplete primary font instead of mixing fallbacks");
        Check(DebugDialogueCatalog.Knots.Contains("jadi_conversation"),
            "Jadi is available in the global dialogue catalog");
        Check(DebugDialogueCatalog.Knots.Contains("eliisabet_conversation") &&
              DebugDialogueCatalog.Knots.Contains("eliisabet_hercules_conversation") &&
              DebugDialogueCatalog.Knots.Contains("eliisabet_slicer_conversation") &&
              DebugDialogueCatalog.Knots.Contains("eliisabet_parasite_conversation"),
            "All exported Eliisabet dialogue entries are available");
        Check(DebugDialogueCatalog.Knots.Count == DebugDialogueCatalog.Knots.Distinct(StringComparer.Ordinal).Count(),
            "Global dialogue catalog has no duplicate Ink paths");
        Check(DebugSaveSlotStore.MakeSafeName(" Jourdan before purchase ") == "Jourdan before purchase",
            "Debug save slot names are trimmed");
        Check(DebugSaveSlotStore.MakeSafeName("a:b") == "ab",
            "Debug save slot names remove invalid filename characters");
        var truncatedSlot = DebugSaveSlotStore.MakeSafeName(new string('a', 79) + ".b");
        Check(truncatedSlot == new string('a', 79),
            "Truncation removes a newly exposed trailing dot");
        Check(DebugSaveSlotStore.MakeSafeName(truncatedSlot) == truncatedSlot,
            "Truncated debug save slot names remain identical when loaded");
        Check(DebugSaveSlotStore.MakeSafeName(new string('a', 79) + " b") == new string('a', 79),
            "Truncation removes a newly exposed trailing space");
        Check(DebugSaveSlotStore.MakeSafeName(new string('a', 78) + " .b") == new string('a', 78),
            "Truncation trims mixed trailing dots and spaces");
        Check(DebugSaveSlotStore.MakeSafeName(new string('a', 81)) == new string('a', 80),
            "Debug save slot names are limited to 80 characters");
        Check(DebugSaveSlotStore.MakeSafeName(new string('a', 80)) == new string('a', 80),
            "Debug save slot names at the length limit are unchanged");
        foreach (var invalidSlot in new[] { "", " . . ", new string('.', 80) + "b" })
        {
            bool rejected = false;
            try { DebugSaveSlotStore.MakeSafeName(invalidSlot); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Empty or dot-only debug save slot name is rejected: length " + invalidSlot.Length);
        }
        Check(!FontRenderPolicy.NeedsCompleteFont(true, true, true),
            "Complete native Cyrillic font is preserved");
        Check(FontRenderPolicy.NeedsCyrillicFont(true, true, false),
            "Font without Russian glyphs uses the corresponding original source font");
        Check(!FontRenderPolicy.NeedsCyrillicFont(true, true, true),
            "Native font with Russian glyphs is kept unchanged even if rare punctuation is absent");
        Check(FontRenderPolicy.NeedsAtlasRepair(true, true, true, false),
            "Replacement atlas is repaired even when the replacement font is already assigned");
        Check(!FontRenderPolicy.NeedsAtlasRepair(true, true, true, true),
            "Matching replacement atlas is preserved");
        Check(FontRenderPolicy.NeedsImmediateMeshRefresh(true, true, true, false),
            "First localized render reparses the mesh after a font or material change");
        Check(!FontRenderPolicy.NeedsImmediateMeshRefresh(true, true, true, true),
            "Pre-render hook never recursively forces a TMP mesh rebuild");
        Check(!FontRenderPolicy.NeedsImmediateMeshRefresh(true, true, false, false),
            "Stable localized text does not rebuild its mesh repeatedly");
        Check(FontRenderPolicy.NeedsOriginalFontPrewarm(true, false, 18),
            "Russian mode prewarms available original font atlases before first UI render");
        Check(!FontRenderPolicy.NeedsOriginalFontPrewarm(false, false, 18),
            "Original font atlases are not prewarmed outside Russian mode");
        Check(!FontRenderPolicy.NeedsOriginalFontPrewarm(true, true, 18),
            "Original font atlases are prewarmed only once");
        Check(!FontRenderPolicy.NeedsOriginalFontPrewarm(true, false, 0),
            "Missing original font files skip prewarm safely");
        Check(FontRenderPolicy.NeedsCanvasRendererAtlasSync(true, true, true, false, false),
            "Localized TMP UI synchronizes a stale CanvasRenderer atlas immediately");
        Check(FontRenderPolicy.NeedsCanvasRendererAtlasSync(true, true, true, true, true),
            "First font assignment refreshes the CanvasRenderer texture override even when material metadata matches");
        Check(!FontRenderPolicy.NeedsCanvasRendererAtlasSync(true, true, true, false, true),
            "CanvasRenderer with the selected font atlas is preserved");
        Check(!FontRenderPolicy.NeedsCanvasRendererAtlasSync(true, true, false, true, false),
            "Native TMP UI does not receive replacement renderer material handling");

        const string en = "{\"inkVersion\":21,\"root\":[\"^Hello\",\"done\",{\"#f\":1}]}";
        Check(UnifiedTranslationCatalog.Parse("== Джади ==\nYes=Да\n== Хилал ==\nNo=Нет\n").Count == 2,
            "Double-equals dialogue headings do not become empty translation keys");
        const string inventoryInk = "{\"root\":[\"ev\",\"str\",\"^CatEarCanyonMap\",\"/str\",{\"x()\":\"inventory_get_quantity\",\"exArgs\":1},0,\">\",\"/ev\"]}";
        var inventoryCatalog = UnifiedTranslationCatalog.Parse("#@item:CatEarCanyonMap:name\nEwer Map=Карта Кувшина\n#@ink:/root/2\nCatEarCanyonMap=Карта Кувшина\n");
        Check(inventoryCatalog.BuildInk(inventoryInk).Contains("^CatEarCanyonMap"),
            "Jourdan inventory condition preserves the item ID despite a translated Ink entry");
        InkTranslationValidator.Validate(en, en.Replace("Hello", "Привет"));
        Check(true, "Narrative translation accepted");
        InkTranslationValidator.Validate(en, "{\"root\":[\"^Привет\",\"done\",{\"#f\":1}],\"inkVersion\":21}");
        Check(true, "Object key order ignored");
        Reject(en, "{broken", "Malformed JSON rejected");
        Reject(en, en + "{}", "Trailing JSON rejected");
        Reject(en, en.Replace("done", "end"), "Control command change rejected");
        Reject(en, en.Replace("21", "22"), "Ink version change rejected");
        Reject(en, en.Replace("#f", "#n"), "Metadata key change rejected");
        Reject(en, en.Replace("^Hello", "Hello"), "Narrative marker removal rejected");
        Reject(en, en.Replace("1}", "2}"), "Numeric metadata change rejected");
        Reject(en, en.Replace("\"done\",", ""), "Container length change rejected");
        Reject(en, en.Replace("\"inkVersion\":21", "\"inkVersion\":21,\"inkVersion\":21"), "Duplicate keys rejected");
        const string escapedJson = "{\"root\":[\"^Line\\nQuote: \\\"yes\\\" \\u263a\",null,true,false,-1.25e+2]}";
        var escapedTree = SafeJson.Parse(escapedJson);
        InkTranslationValidator.Validate(escapedJson, escapedTree.Write());
        Check(true, "Escapes and scalar JSON values round-trip without runtime dependencies");

        var temp = Path.Combine(Path.GetTempPath(), "SableRegression-" + Guid.NewGuid());
        Directory.CreateDirectory(temp);
        // Fixtures use only the unique temporary directory, never real translations.
        BepInEx.Paths.PluginPath = temp;
        var directory = Path.Combine(temp, "RTLC.Sable.RussianTranslate", "Translations");
        Directory.CreateDirectory(directory);
        var ui = Path.Combine(directory, "interface_ru.tsv");
        var items = Path.Combine(directory, "items_ru.tsv");
        File.WriteAllText(ui, "Custom\tРучная правка\n");
        File.WriteAllText(items, "item\tНазвание\tОписание\n");
        var logger = new BepInEx.Logging.ManualLogSource();
        using (var locked = new FileStream(ui, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            TranslationFiles.Initialize(logger);
        Check(logger.Errors > 0, "Locked UI file reported without aborting initialization");
        Check(TranslationFiles.TryGetItemName("item", out var name) && name == "Название", "Items still load after UI failure");
        TranslationFiles.Initialize(logger);
        Check(TranslationFiles.Translate("Custom", "English") == "Ручная правка", "Manual UI value loaded");
        TranslationFiles.ExportEnglishInterface(new TextProviders.Implementations.TextProvider_EN());
        Check(File.ReadAllText(ui) == "Custom\tРучная правка\n", "Existing UI file preserved");
        var ink = Path.Combine(directory, "story_ru.json");
        File.WriteAllText(ink, "manual story");
        TranslationFiles.ExportEnglishInk(en);
        Check(File.ReadAllText(ink) == "manual story", "Existing story file preserved");
        BepInEx.Paths.PluginPath = ui; // A file cannot serve as a parent directory.
        TranslationFiles.Initialize(logger);
        Check(true, "Unavailable translation directory does not abort initialization");

        var catalog = UnifiedTranslationCatalog.Parse("###Диалоги\n#@ink:/root/0\nHello=Привет\n\n###Настройки\n#@ui:Custom\nEnglish=Новая правка\n#@item:item:name\nItem=Предмет\n#@item:item:description\nDescription=Описание\n#@item:item:shop\nShop=Магазин\n");
        Check(catalog.Count == 5, "Sectioned unified file parsed");
        var annotated = UnifiedTranslationCatalog.Parse("=== Первый диалог ===\n[Нарратив Сэйбл]\n#@ui:Custom\nEnglish=Перевод\n");
        Check(annotated.Count == 1 && annotated.TryGet("ui:Custom", out var annotatedText) && annotatedText == "Перевод",
            "Dialogue filter labels are ignored");
        var noted = UnifiedTranslationCatalog.Parse("Монолог перед алтарём\n#@ui:Custom\nEnglish=Перевод\n");
        Check(noted.Count == 1 && noted.TryGet("ui:Custom", out var notedText) && notedText == "Перевод",
            "Free-form notes between entries are ignored");
        Check(catalog.TryGet("ui:Custom", out var caption) && caption == "Новая правка", "UI context retained");
        var commonWithOverride = UnifiedTranslationCatalog.Parse("Yes=Да\n#@ui:Travel\nYes=Переместиться сюда?\n");
        Check(commonWithOverride.TryGetCaption("Yes", out var commonYes) && commonYes == "Да" &&
              commonWithOverride.TryGet("ui:Travel", out var travelYes) && travelYes == "Переместиться сюда?",
              "Explicit common caption coexists with contextual override");
        Check(catalog.BuildInk(en).Contains("Привет"), "Unified narrative inserted into original Ink");
        var repeated = UnifiedTranslationCatalog.Parse("#@ink:/root/0\nHello=Привет\n#@ink:/root/1\nHello=Здравствуйте\n");
        var repeatedJson = repeated.BuildInk("{\"inkVersion\":21,\"root\":[\"^Hello\",\"^Hello\"]}");
        Check(repeatedJson.Contains("Привет") && repeatedJson.Contains("Здравствуйте"), "Identical English keeps context-specific translations");
        var formattingOnly = UnifiedTranslationCatalog.Parse("#@ink:/root/0\nA very special thing=Особая вещь\n");
        Check(formattingOnly.BuildInk("{\"inkVersion\":21,\"root\":[\"^A very <i>special</i> thing\"]}").Contains("Особая вещь"),
            "Italic-only English differences do not disable narrative translation");
        var escaped = UnifiedTranslationCatalog.Parse("#@ui:Escaped\n\\#A\\=B\\nC=\\#А\\=Б\\nВ\\\\Г\n");
        Check(escaped.TryGet("ui:Escaped", out var special) && special == "#А=Б\nВ\\Г", "Equals, newline, comment marker and backslash round-trip");
        var empty = UnifiedTranslationCatalog.Parse("#@ink:/root/0\nHello=\n");
        Check(empty.BuildInk(en).Contains("\"^\""), "Empty narrative translation preserved");
        void RejectCatalog(string input, string label)
        {
            bool rejected = false;
            try { UnifiedTranslationCatalog.Parse(input); } catch (FormatException) { rejected = true; }
            Check(rejected, label);
        }
        RejectCatalog("#@ui:X\nA=B\n#@ui:X\nA=C", "Duplicate context rejected");
        RejectCatalog("#@ui:X\n", "Orphan context rejected");
        RejectCatalog("#@ui:X\nA", "Missing separator rejected");
        RejectCatalog("#@ui:X\nA=\\q", "Invalid escape rejected");
        bool sourceMismatch = false;
        try { catalog.BuildInk(en.Replace("Hello", "Changed")); } catch (FormatException) { sourceMismatch = true; }
        Check(sourceMismatch, "Changed game source rejected before applying translation");
        var partial = repeated.BuildInk(en);
        Check(partial.Contains("Привет"), "Unmatched old story contexts do not disable current translations");
        var reordered = UnifiedTranslationCatalog.Parse("###Other\n#@ink:/root/1\nHello=Здравствуйте\n###New\n#@ink:/root/0\nHello=Привет");
        Check(reordered.BuildInk("{\"inkVersion\":21,\"root\":[\"^Hello\",\"^Hello\"]}") == repeatedJson, "Moving complete entries between sections preserves context");

        BepInEx.Paths.PluginPath = temp;
        var unifiedPath = Path.Combine(directory, "translation.txt");
        const string unified = "#@ui:Custom\nEnglish=Новая правка\n#@item:item:name\nItem=Предмет\n#@item:item:description\nDescription=Новое описание\n#@item:item:shop\nShop=Для магазина\n#@ink:/root/0\nHello=Новый сюжет\n";
        File.WriteAllText(unifiedPath, unified);
        TranslationFiles.Initialize(logger);
        Check(TranslationFiles.Translate("Custom", "English") == "Новая правка", "Unified UI takes precedence over old TSV");
        Check(TranslationFiles.TryGetItemName("item", out name) && name == "Предмет", "Unified item name loaded");
        Check(TranslationFiles.TryGetItemDescription("item", out var description) && description == "Новое описание", "Unified item description loaded");
        Check(TranslationFiles.TryGetItemShopDescription("item", out var shop) && shop == "Для магазина", "Unified shop description loaded");
        Check(TranslationFiles.LoadRussianInk(en)!.Contains("Новый сюжет"), "Unified story takes precedence over old JSON");
        TranslationFiles.ExportEnglishInk(en);
        TranslationFiles.ExportEnglishInterface(new TextProviders.Implementations.TextProvider_EN());
        Check(File.ReadAllText(unifiedPath) == unified && File.ReadAllText(ink) == "manual story", "Unified mode never rewrites either translation format");
        File.WriteAllText(unifiedPath, "#@item:CatEarCanyonMap:description\nA map of the Ewer sold by members of the Cartography guild=Карта каньона Кувшин, составленная гильдией картографов.\n");
        TranslationFiles.Initialize(logger);
        Check(TranslationFiles.TryGetItemByEnglish("A map of the Ewer sold by members of the Cartography guild", "description", out var mapDescription) && mapDescription.StartsWith("Карта каньона"),
            "Raw item card description resolves without an item ID");
        File.AppendAllText(unifiedPath, "#@item:Other:description\nA map of the Ewer sold by members of the Cartography guild=Другой перевод\n");
        TranslationFiles.Initialize(logger);
        Check(!TranslationFiles.TryGetItemByEnglish("A map of the Ewer sold by members of the Cartography guild", "description", out _),
            "Ambiguous item descriptions require context instead of selecting the first translation");
        File.WriteAllText(unifiedPath, "broken line");
        TranslationFiles.Initialize(logger);
        Check(TranslationFiles.Translate("Custom", "English") == "English" && TranslationFiles.LoadRussianInk(en) == null,
            "Broken unified file falls back to original instead of stale legacy translations");

        TranslationFiles.Initialize(logger, unified);
        Check(TranslationFiles.Translate("Custom", "English") == "Новая правка",
            "Bundled catalog takes precedence over a broken loose translation");
        Check(TranslationFiles.TryGetItemName("item", out name) && name == "Предмет" &&
              TranslationFiles.LoadRussianInk(en)!.Contains("Новый сюжет"),
            "Bundled catalog supplies item names and narrative");
        var bundleOnlyRoot = Path.Combine(temp, "bundle-only");
        Directory.CreateDirectory(bundleOnlyRoot);
        BepInEx.Paths.PluginPath = bundleOnlyRoot;
        TranslationFiles.Initialize(logger, unified);
        TranslationFiles.ExportEnglishInk(en);
        TranslationFiles.ExportEnglishInterface(new TextProviders.Implementations.TextProvider_EN());
        Check(!Directory.Exists(TranslationFiles.DirectoryPath) && Directory.GetFileSystemEntries(bundleOnlyRoot).Length == 0,
            "Bundle-only localization creates no loose translation directories or exports");
        TranslationFiles.Initialize(logger, "broken line");
        Check(TranslationFiles.Translate("Custom", "English") == "English" && TranslationFiles.LoadRussianInk(en) == null,
            "Malformed bundled catalog safely uses original game text");

        if (args.Length == 1)
        {
            var complete = UnifiedTranslationCatalog.Parse(File.ReadAllText(args[0]));
            Check(complete.Count > 0, "Complete translation catalog parsed");
            Console.WriteLine($"Complete catalog: {complete.Count} entries");
        }
        if (args.Length >= 2)
        {
            InkTranslationValidator.Validate(File.ReadAllText(args[0]), File.ReadAllText(args[1]));
            Check(true, "Complete game translation accepted");
        }
        if (args.Length >= 3)
        {
            var complete = UnifiedTranslationCatalog.Parse(File.ReadAllText(args[2]));
            var rebuilt = complete.BuildInk(File.ReadAllText(args[0]));
            using var actual = JsonDocument.Parse(rebuilt);
            using var expected = JsonDocument.Parse(File.ReadAllText(args[1]));
            Check(JsonElement.DeepEquals(actual.RootElement, expected.RootElement), "All narrative and control values match the manually edited story");
            Console.WriteLine($"Complete catalog: {complete.Count} entries");
        }
        Console.WriteLine($"{_passed} checks passed. Fixtures: {temp}");
    }
}

// Only the boundary to BepInEx/IL2CPP is stubbed; file loading and Ink validation
// above execute the same source files compiled into the plugin.
namespace BepInEx { public static class Paths { public static string PluginPath = ""; } }
namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public int Errors;
        public void LogInfo(object message) { }
        public void LogError(object message) { Errors++; }
    }
}
namespace TextProviders.Implementations { public sealed class TextProvider_EN { public string Example => "English"; } }
