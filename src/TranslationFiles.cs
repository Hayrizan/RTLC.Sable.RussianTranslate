using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using TextProviders.Implementations;

namespace RTLC.Sable.RussianTranslate;

internal static class TranslationFiles
{
    private static UnifiedTranslationCatalog? _unified;
    private static bool _unifiedMode;
    private const string InterfaceFileName = "interface_ru.tsv";
    private const string ItemsFileName = "items_ru.tsv";
    private const string StoryFileName = "story_ru.json";
    private static readonly Dictionary<string, string> Interface = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (string Name, string Desc, string ShopDesc)> Items = new(StringComparer.Ordinal);
    private static ManualLogSource? _log;
    private static bool _interfaceExported;
    private static bool _storyExported;

    internal static string DirectoryPath { get; private set; } = string.Empty;

    internal static void Initialize(ManualLogSource log, string? bundledTranslation = null)
    {
        _log = log;
        Interface.Clear();
        Items.Clear();
        _unified = null;
        _unifiedMode = false;
        _interfaceExported = false;
        _storyExported = false;
        DirectoryPath = Path.Combine(Paths.PluginPath, "RTLC.Sable.RussianTranslate", "Translations");
        try
        {
            if (bundledTranslation == null) Directory.CreateDirectory(DirectoryPath);
        }
        catch (Exception exception)
        {
            _log.LogError($"Could not prepare translation directory; continuing with available text: {exception}");
        }

        Interface["SettingsScreen_LanguageSettings_LanguageButton_Name"] = "Язык";
        Interface["SettingsScreen_LanguageOptions_Russian"] = "Русский";
        Interface["SettingsScreen_Title"] = "НАСТРОЙКИ";
        Interface["SettingsScreen_VideoTab_Name"] = "ВИДЕО";
        Interface["SettingsScreen_AudioTab_Name"] = "ЗВУК";
        Interface["SettingsScreen_ControlsTab_Name"] = "УПРАВЛЕНИЕ";
        Interface["SettingsScreen_GameTab_Name"] = "ИГРА";
        Interface["SettingsScreen_DisplayTab_Name"] = "ЭКРАН";

        // The game's built-in debug_player Ink knot is English-only and is
        // intentionally absent from the hand-edited story catalog. Keep its
        // entry menu usable in Russian without modifying translation.txt.
        Interface["Travel to a location"] = "Переместиться в локацию";
        Interface["Choose a location to travel to:"] = "Выберите место для перемещения:";
        Interface["You are currently in Eccria."] = "Вы сейчас в Эккрии.";
        Interface["You are at the Hercules Beetle Camp."] = "Вы на стоянке жуков-геркулесов.";
        Interface["You are in the Eastern Dunes."] = "Вы в Восточных дюнах.";
        Interface["You are at the Wind Tower."] = "Вы у Ветряной башни.";
        Interface["You are at the Parasite Beetle Camp."] = "Вы на стоянке жуков-паразитов.";
        Interface["You are in the White Desert."] = "Вы в Белой пустыне.";
        Interface["You are in the Petrified Forest."] = "Вы в Окаменелом лесу.";
        Interface["You are in the Observatory."] = "Вы в обсерватории.";
        Interface["You are in the Slicer Beetle Camp."] = "Вы на стоянке жуков-резаков.";
        Interface["You are in the Black Desert."] = "Вы в Чёрной пустыне.";
        Interface["You are in the Glow Worm Cave."] = "Вы в пещере светящихся червей.";
        Interface["You are in the Badlands."] = "Вы в Пустошах.";
        Interface["You are at the Bridge of the Betrayed."] = "Вы у Моста преданных.";
        Interface["You are at the Crystal Plateau."] = "Вы на Хрустальном плато.";
        Interface["You are in the Western Dunes."] = "Вы в Западных дюнах.";
        Interface["You are at the Hercules Beetle Nest."] = "Вы у гнезда жуков-геркулесов.";
        Interface["You are in the Salt Plains."] = "Вы на Соляных равнинах.";
        Interface["You are at the Atomic Heart."] = "Вы у Атомного сердца.";
        Interface["You are at Llhor's camp."] = "Вы в лагере Ллхора.";

        var unifiedPath = Path.Combine(DirectoryPath, "translation.txt");
        _unifiedMode = bundledTranslation != null || File.Exists(unifiedPath);
        if (_unifiedMode)
        {
            try
            {
                _unified = UnifiedTranslationCatalog.Parse(bundledTranslation ?? File.ReadAllText(unifiedPath));
                foreach (var entry in _unified.Entries)
                {
                    if (entry.Id.StartsWith("ui:", StringComparison.Ordinal))
                        Interface[entry.Id.Substring(3)] = entry.Russian;
                    else if (entry.Id.StartsWith("item:", StringComparison.Ordinal))
                    {
                        int separator = entry.Id.LastIndexOf(':');
                        if (separator <= 5) throw new FormatException("Invalid item context: " + entry.Id);
                        var id = entry.Id.Substring(5, separator - 5);
                        Items.TryGetValue(id, out var item);
                        switch (entry.Id.Substring(separator + 1))
                        {
                            case "name": item.Name = entry.Russian; break;
                            case "description": item.Desc = entry.Russian; break;
                            case "shop": item.ShopDesc = entry.Russian; break;
                            default: throw new FormatException("Invalid item field: " + entry.Id);
                        }
                        Items[id] = item;
                    }
                }
                _log.LogInfo($"Loaded {_unified.Count} entries from {(bundledTranslation != null ? "rtlc.bundle/translation" : "translation.txt")}. Legacy tables are not used.");
            }
            catch (Exception exception)
            {
                _unified = null;
                Interface.Clear();
                Items.Clear();
                _log.LogError($"Could not load {(bundledTranslation != null ? "rtlc.bundle/translation" : "translation.txt")}; using original text. Source was not modified. {exception}");
            }
            return;
        }

        TryLoadTable(InterfaceFileName, LoadInterface);
        TryLoadTable(ItemsFileName, LoadItems);
    }

    internal static bool TryGetItemName(string? id, out string name)
    {
        if (!string.IsNullOrEmpty(id) && Items.TryGetValue(id, out var tuple) && !string.IsNullOrEmpty(tuple.Name))
        {
            name = tuple.Name;
            return true;
        }
        name = string.Empty;
        return false;
    }

    internal static bool TryGetItemDescription(string? id, out string desc)
    {
        if (!string.IsNullOrEmpty(id) && Items.TryGetValue(id, out var tuple) && !string.IsNullOrEmpty(tuple.Desc))
        {
            desc = tuple.Desc;
            return true;
        }
        desc = string.Empty;
        return false;
    }

    internal static bool TryGetItemShopDescription(string? id, out string shop)
    {
        if (!string.IsNullOrEmpty(id) && Items.TryGetValue(id, out var tuple) && !string.IsNullOrEmpty(tuple.ShopDesc))
        {
            shop = tuple.ShopDesc;
            return true;
        }
        shop = string.Empty;
        return false;
    }

    internal static bool TryGetItemByEnglish(string? english, string field, out string russian)
    {
        string? match = null;
        if (_unified != null && english != null)
        {
            foreach (var entry in _unified.Entries)
            {
                if (!entry.Id.StartsWith("item:", StringComparison.Ordinal) ||
                    !entry.Id.EndsWith(":" + field, StringComparison.Ordinal) ||
                    !string.Equals(entry.English, english, StringComparison.Ordinal)) continue;
                if (match != null && !string.Equals(match, entry.Russian, StringComparison.Ordinal))
                {
                    russian = string.Empty;
                    return false;
                }
                match = entry.Russian;
            }
        }
        russian = match ?? string.Empty;
        return !string.IsNullOrEmpty(russian);
    }

    internal static string Translate(string key, string english)
    {
        if (Interface.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)) return value;
        return _unified != null && _unified.TryGetCaption(english, out value) && !string.IsNullOrEmpty(value)
            ? value : english;
    }

    internal static bool TryTranslateText(string? keyOrText, out string translated)
    {
        if (!string.IsNullOrEmpty(keyOrText) && Interface.TryGetValue(keyOrText, out var val) && !string.IsNullOrEmpty(val))
        {
            translated = val;
            return true;
        }
        if (!string.IsNullOrEmpty(keyOrText) && _unified != null &&
            _unified.TryGetCaption(keyOrText, out translated) && !string.IsNullOrEmpty(translated))
            return true;
        translated = string.Empty;
        return false;
    }

    internal static void ExportEnglishInterface(TextProvider_EN? provider)
    {
        if (_unifiedMode || _interfaceExported || provider == null)
            return;
        _interfaceExported = true;

        var path = Path.Combine(DirectoryPath, InterfaceFileName);
        if (File.Exists(path))
            return;

        try
        {
            var rows = typeof(TextProvider_EN)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => Escape(p.Name) + "\t" + Escape((string?)p.GetValue(provider) ?? string.Empty));
            var lines = new[] { "# key\ttranslation (currently copied from English)" }.Concat(rows).ToArray();
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
                foreach (var line in lines) writer.WriteLine(line);
            _log?.LogInfo($"Exported the English interface template to {path}");
            LoadInterface();
        }
        catch (Exception exception)
        {
            _log?.LogError($"Could not export interface text: {exception}");
        }
    }

    internal static void ExportEnglishInk(string englishJson)
    {
        if (_unifiedMode || _storyExported)
            return;
        _storyExported = true;

        var path = Path.Combine(DirectoryPath, StoryFileName);
        if (File.Exists(path))
            return;

        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
                writer.Write(englishJson);
            _log?.LogInfo($"Exported the English Ink story template to {path}");
        }
        catch (Exception exception)
        {
            _log?.LogError($"Could not export Ink story: {exception}");
        }
    }

    internal static string? LoadRussianInk(string englishJson)
    {
        var path = Path.Combine(DirectoryPath, StoryFileName);
        try
        {
            if (_unifiedMode) return _unified?.BuildInk(englishJson);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception)
        {
            _log?.LogError($"Could not prepare Russian Ink; using English: {exception}");
            return null;
        }
    }

    private static void TryLoadTable(string name, Action load)
    {
        try { load(); }
        catch (Exception exception)
        {
            _log?.LogError($"Could not load {name}; keeping fallback text: {exception}");
        }
    }

    private static void LoadInterface()
    {
        var path = Path.Combine(DirectoryPath, InterfaceFileName);
        if (!File.Exists(path))
            return;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith("#", StringComparison.Ordinal))
                continue;
            var tab = rawLine.IndexOf('\t');
            if (tab <= 0)
                continue;
            Interface[Unescape(rawLine.Substring(0, tab))] = Unescape(rawLine.Substring(tab + 1));
        }
        _log?.LogInfo($"Loaded {Interface.Count} interface translations.");
    }

    private static void LoadItems()
    {
        var path = Path.Combine(DirectoryPath, ItemsFileName);
        if (!File.Exists(path))
            return;

        int count = 0;
        foreach (var rawLine in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith("#", StringComparison.Ordinal))
                continue;

            var parts = rawLine.Split('\t');
            if (parts.Length >= 2)
            {
                var id = Unescape(parts[0]);
                var name = Unescape(parts[1]);
                var desc = parts.Length >= 3 ? Unescape(parts[2]) : string.Empty;
                var shop = parts.Length >= 4 ? Unescape(parts[3]) : string.Empty;
                Items[id] = (name, desc, shop);
                count++;
            }
        }
        _log?.LogInfo($"Loaded {count} item translations.");
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

    private static string Unescape(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                result.Append(value[i]);
                continue;
            }
            var next = value[++i];
            result.Append(next == 't' ? '\t' : next == 'r' ? '\r' : next == 'n' ? '\n' : next);
        }
        return result.ToString();
    }
}
