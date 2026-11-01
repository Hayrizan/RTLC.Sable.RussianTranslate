namespace RTLC.Sable.RussianTranslate;

internal static class FontRenderPolicy
{
    // TMP's lookup cache also stores fallback characters and missing-glyph squares.
    internal static bool IsOwnCharacter(uint requested, uint actual, bool belongsToFont) =>
        requested == actual && belongsToFont;

    internal static bool NeedsCompleteFont(
        bool russianSelected,
        bool containsCyrillic,
        bool currentFontCoversText) =>
        russianSelected && containsCyrillic && !currentFontCoversText;

    internal static bool NeedsCyrillicFont(
        bool russianSelected,
        bool containsCyrillic,
        bool currentFontSupportsCyrillic) =>
        russianSelected && containsCyrillic && !currentFontSupportsCyrillic;

    internal static bool NeedsAtlasRepair(
        bool russianSelected,
        bool containsCyrillic,
        bool replacementFontActive,
        bool atlasMatches) =>
        russianSelected && containsCyrillic && replacementFontActive && !atlasMatches;

    internal static bool NeedsImmediateMeshRefresh(
        bool russianSelected,
        bool containsCyrillic,
        bool fontOrMaterialChanged,
        bool calledDuringPreRender) =>
        russianSelected && containsCyrillic && fontOrMaterialChanged && !calledDuringPreRender;

    internal static bool NeedsOriginalFontPrewarm(
        bool russianSelected,
        bool alreadyPrewarmed,
        int sourceFontCount) =>
        russianSelected && !alreadyPrewarmed && sourceFontCount > 0;

    internal static bool NeedsCanvasRendererAtlasSync(
        bool russianSelected,
        bool containsCyrillic,
        bool replacementFontActive,
        bool fontOrMaterialChanged,
        bool rendererAtlasMatches) =>
        russianSelected && containsCyrillic && replacementFontActive &&
        (fontOrMaterialChanged || !rendererAtlasMatches);
}
