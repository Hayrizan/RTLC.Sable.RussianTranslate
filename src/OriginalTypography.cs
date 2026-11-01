using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace RTLC.Sable.RussianTranslate;

// The locally installed game's source fonts contain Cyrillic. Keep their own
// outlines, advances and weight instead of mixing unrelated fallback families.
internal static class OriginalTypography
{
    private static readonly Dictionary<int, TMP_FontAsset> Replacements = new();
    private static readonly Dictionary<string, TMP_FontAsset> PreparedByFile = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<int> Unmatched = new();
    private static readonly HashSet<int> MaterialRepairsLogged = new();
    private static readonly HashSet<int> InvalidGlyphsLogged = new();
    private static bool _prewarmAttempted;
    private const string RussianAlphabet = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя";
    private const string RequiredCharacters = RussianAlphabet +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
        ".,!?;:…—–«»№\"'“”„’‘()[]{}<>+-=*/\\_ \u00a0";

    private static bool ContainsOwnGlyphs(TMP_FontAsset font, string characters)
    {
        foreach (char character in characters)
        {
            if (!font.HasCharacter(character, false, false)) return false;
            var entry = font.characterLookupTable[(uint)character];
            if (!FontRenderPolicy.IsOwnCharacter(character, entry.unicode, entry.textAsset == font))
            {
                if (InvalidGlyphsLogged.Add(font.GetInstanceID()))
                    RussianLanguagePatches.LogDebug($"Rejected cached substitute: font={font.name}, requested=U+{(int)character:X4}, actual=U+{entry.unicode:X4}, owner={entry.textAsset?.name}.");
                return false;
            }
            var glyph = entry.glyph;
            // A character-table entry may refer to .notdef rather than a letter.
            // HasCharacter alone accepts such entries as Cyrillic coverage.
            if (glyph == null || glyph.index == 0 ||
                (!char.IsWhiteSpace(character) && (glyph.glyphRect.width == 0 || glyph.glyphRect.height == 0)))
            {
                if (InvalidGlyphsLogged.Add(font.GetInstanceID()))
                    RussianLanguagePatches.LogDebug($"Invalid native glyph: font={font.name}, unicode=U+{(int)character:X4}, glyph={glyph?.index}. Replacing incomplete font.");
                return false;
            }
        }
        return true;
    }

    private static bool SupportsCyrillic(TMP_FontAsset font)
    {
        // The game's native asset remains untouched when it has the complete
        // Russian alphabet. Rare punctuation must not force a font replacement.
        return ContainsOwnGlyphs(font, RussianAlphabet);
    }
    private static string[]? Files;
    private static string Key(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string[] GetSourceFiles()
    {
        if (Files != null) return Files;
        if (BundledAssets.IsLoaded)
            return Files = BundledAssets.OriginalFonts.OrderByDescending(p => Path.GetFileNameWithoutExtension(p).Length).ToArray();
        var directory = Path.Combine(Path.GetDirectoryName(typeof(OriginalTypography).Assembly.Location)!, "Fonts", "Original");
        Files = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.otf").OrderByDescending(p => Path.GetFileNameWithoutExtension(p).Length).ToArray()
            : Array.Empty<string>();
        return Files;
    }

    private static TMP_FontAsset? GetOrCreatePreparedFont(string file)
    {
        if (PreparedByFile.TryGetValue(file, out var cached))
        {
            if (RuntimeFontLifetime.IsAlive(cached)) return cached;
            PreparedByFile.Remove(file);
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();

        var source = RussianLanguagePatches.CreateFontFromFile(file);
        if (source == null) return null;
        var replacement = TMP_FontAsset.CreateFontAsset(source, 90,
            9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        if (replacement == null)
        {
            if (!BundledAssets.IsLoaded) UnityEngine.Object.Destroy(source);
            return null;
        }

        replacement.name = "RTLC_Original_" + Path.GetFileNameWithoutExtension(file);
        replacement.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        replacement.isMultiAtlasTexturesEnabled = true;
        replacement.TryAddCharacters(RequiredCharacters, true);
        if (!ContainsOwnGlyphs(replacement, RequiredCharacters))
        {
            RussianLanguagePatches.LogDebug($"Incomplete original source font {Path.GetFileName(file)}; keeping Cyrillic replacement fallback.");
            UnityEngine.Object.Destroy(replacement);
            if (!BundledAssets.IsLoaded) UnityEngine.Object.Destroy(source);
            return null;
        }

        RuntimeFontLifetime.Retain(replacement, source);
        PreparedByFile.Add(file, replacement);
        RussianLanguagePatches.LogDebug($"[Timing] Font {Path.GetFileName(file)}: {timer.ElapsedMilliseconds} ms.");
        return replacement;
    }

    internal static void Prewarm(bool russianSelected)
    {
        // Only the two faces used by the title screen are needed at startup.
        // Resolve prepares and uploads other faces synchronously before assignment.
        var files = GetSourceFiles().Where(p =>
            Path.GetFileName(p).Equals("EuclidFlex-Bold.otf", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(p).Equals("EuclidFlex-Medium.otf", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!FontRenderPolicy.NeedsOriginalFontPrewarm(russianSelected, _prewarmAttempted, files.Length)) return;

        _prewarmAttempted = true;
        int prepared = 0;
        for (int i = 0; i < files.Length; i++)
            if (GetOrCreatePreparedFont(files[i]) != null) prepared++;

        TMP_FontAsset.UpdateAtlasTexturesForFontAssetsInQueue();
        RussianLanguagePatches.LogDebug($"Prewarmed {prepared}/{files.Length} original TMP font atlases before localized UI rendering.");
    }

    internal static TMP_FontAsset? Resolve(TMP_FontAsset original)
    {
        if (original == null) return null;
        // Inspect the actual TMP character table, never its fallback chain or
        // merely the cmap of a separate source font file.
        bool currentFontSupportsCyrillic = SupportsCyrillic(original);
        if (!FontRenderPolicy.NeedsCyrillicFont(true, true, currentFontSupportsCyrillic)) return original;
        if (original.name.StartsWith("RTLC_Original_", StringComparison.Ordinal)) return original;
        if (Replacements.TryGetValue(original.GetInstanceID(), out var cached))
        {
            if (RuntimeFontLifetime.IsAlive(cached)) return cached;
            Replacements.Remove(original.GetInstanceID());
        }
        if (Unmatched.Contains(original.GetInstanceID())) return null;
        var key = Key(original.name);
        var file = GetSourceFiles().FirstOrDefault(p => key.Contains(Key(Path.GetFileNameWithoutExtension(p))));
        if (file == null) { Unmatched.Add(original.GetInstanceID()); return null; }
        var replacement = GetOrCreatePreparedFont(file);
        if (replacement == null) return null;
        TMP_FontAsset.UpdateAtlasTexturesForFontAssetsInQueue();
        Replacements.Add(original.GetInstanceID(), replacement);
        RussianLanguagePatches.LogDebug($"Complete original typography: {original.name} -> {Path.GetFileName(file)}, point size 90.");
        return replacement;
    }

    internal static bool Apply(TMP_Text text, out bool changed)
    {
        changed = false;
        if (text == null || text.font == null) return false;
        var replacement = Resolve(text.font);
        if (replacement == null) return false;
        changed = text.font != replacement;
        if (changed) text.font = replacement;

        bool managedReplacement = replacement.name.StartsWith("RTLC_Original_", StringComparison.Ordinal) ||
                                  replacement.name.StartsWith("Cyrillic_", StringComparison.Ordinal);
        if (managedReplacement)
        {
            var expected = replacement.material;
            var current = text.fontSharedMaterial;
            bool atlasMatches = expected != null && current != null && current.mainTexture == expected.mainTexture;
            if (expected != null && FontRenderPolicy.NeedsAtlasRepair(
                    true, true, managedReplacement, atlasMatches))
            {
                Material? compatible = null;
                try
                {
                    if (current != null)
                        compatible = TMP_MaterialManager.GetFallbackMaterial(current, expected);
                }
                catch
                {
                    // The base replacement material remains a safe fallback.
                }
                text.fontSharedMaterial = compatible ?? expected;
                text.UpdateMeshPadding();
                changed = true;
                if (MaterialRepairsLogged.Add(text.GetInstanceID()))
                    RussianLanguagePatches.LogDebug($"Repaired font atlas/material mismatch: {text.name}, {replacement.name}.");
            }
        }
        if (changed) text.SetAllDirty();
        return true;
    }
}
