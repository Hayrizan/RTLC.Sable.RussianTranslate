using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnhollowerRuntimeLib;

namespace RTLC.Sable.RussianTranslate;

// Event driven: no Update hook, periodic asset search or per-frame disk IO.
internal static class LogoTextureReplacement
{
    private sealed class SpritePair
    {
        internal Sprite Original = null!;
        internal Sprite Replacement = null!;
    }

    private static readonly Dictionary<string, Texture2D> Textures = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Failed = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, SpritePair> Sprites = new();
    private static readonly Dictionary<int, Sprite> OriginalSprites = new();
    private static readonly Dictionary<int, Texture> OriginalTextures = new();
    private static bool _refreshing;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.PropertySetter(typeof(Image), nameof(Image.sprite)),
            prefix: new HarmonyMethod(typeof(LogoTextureReplacement), nameof(SpritePrefix)));
        harmony.Patch(AccessTools.PropertySetter(typeof(Image), nameof(Image.overrideSprite)),
            prefix: new HarmonyMethod(typeof(LogoTextureReplacement), nameof(SpritePrefix)));
        harmony.Patch(AccessTools.PropertySetter(typeof(RawImage), nameof(RawImage.texture)),
            prefix: new HarmonyMethod(typeof(LogoTextureReplacement), nameof(TexturePrefix)));
        harmony.Patch(AccessTools.Method(typeof(Image), "OnEnable"),
            postfix: new HarmonyMethod(typeof(LogoTextureReplacement), nameof(ImageEnabled)));
        harmony.Patch(AccessTools.Method(typeof(Graphic), "OnEnable"),
            postfix: new HarmonyMethod(typeof(LogoTextureReplacement), nameof(GraphicEnabled)));
    }

    private static bool IsLogo(string name) => name == "SableLogo01" || name == "SableLogoWhite01";

    private static Texture2D? GetTexture(Texture original)
    {
        var name = original.name;
        if (!IsLogo(name) || Failed.Contains(name)) return null;
        if (Textures.TryGetValue(name, out var cached) && cached != null) return cached;
        Texture2D? texture = null;
        try
        {
            texture = BundledAssets.Texture(name);
            if (texture.width != original.width || texture.height != original.height)
                throw new InvalidDataException($"Expected {original.width}x{original.height}, got {texture.width}x{texture.height}.");
            texture.name = "RTLC_" + name;
            texture.filterMode = original.filterMode;
            texture.wrapMode = original.wrapMode;
            texture.anisoLevel = original.anisoLevel;
            texture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            Textures[name] = texture;
            original.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            OriginalTextures[texture.GetInstanceID()] = original;
            RussianLanguagePatches.Logger?.LogInfo($"Logo replacement loaded: {name}.png ({texture.width}x{texture.height}).");
            return texture;
        }
        catch (Exception ex)
        {
            Failed.Add(name);
            RussianLanguagePatches.Logger?.LogWarning($"Logo replacement {name}: {ex.Message}");
            return null;
        }
    }

    private static Sprite? Resolve(Sprite? sprite, bool active)
    {
        if (sprite == null) return sprite;
        if (OriginalSprites.TryGetValue(sprite.GetInstanceID(), out var original)) sprite = original;
        if (!active || sprite == null || sprite.texture == null || !IsLogo(sprite.texture.name)) return sprite;
        int id = sprite.GetInstanceID();
        if (Sprites.TryGetValue(id, out var pair) && pair.Replacement != null) return pair.Replacement;
        var texture = GetTexture(sprite.texture);
        if (texture == null) return sprite;
        // The two shipped logos are standalone, unpacked full-texture sprites.
        var rect = sprite.rect;
        var pivot = new Vector2(sprite.pivot.x / rect.width, sprite.pivot.y / rect.height);
        var replacement = Sprite.Create(texture, rect, pivot, sprite.pixelsPerUnit, 0,
            SpriteMeshType.FullRect, sprite.border);
        replacement.name = "RTLC_" + sprite.name;
        replacement.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        Sprites[id] = new SpritePair { Original = sprite, Replacement = replacement };
        OriginalSprites[replacement.GetInstanceID()] = sprite;
        return replacement;
    }

    private static Texture? Resolve(Texture? texture, bool active)
    {
        if (texture == null) return texture;
        if (OriginalTextures.TryGetValue(texture.GetInstanceID(), out var original)) texture = original;
        if (!active || texture == null || !IsLogo(texture.name)) return texture;
        return GetTexture(texture) ?? texture;
    }

    private static void SpritePrefix(ref Sprite __0)
    {
        if (_refreshing) return;
        try { __0 = Resolve(__0, RussianLanguagePatches.IsRussianActive)!; }
        catch (Exception ex) { RussianLanguagePatches.Logger?.LogWarning($"Logo sprite: {ex.Message}"); }
    }

    private static void TexturePrefix(ref Texture __0)
    {
        if (_refreshing) return;
        try { __0 = Resolve(__0, RussianLanguagePatches.IsRussianActive)!; }
        catch (Exception ex) { RussianLanguagePatches.Logger?.LogWarning($"Logo texture: {ex.Message}"); }
    }

    private static void Apply(Image image, bool active)
    {
        var sprite = Resolve(image.sprite, active);
        if (image.sprite != sprite) image.sprite = sprite;
        var overlay = Resolve(image.overrideSprite, active);
        if (image.overrideSprite != overlay) image.overrideSprite = overlay;
    }

    private static void ImageEnabled(Image __instance)
    {
        if (_refreshing || __instance == null) return;
        _refreshing = true;
        try { Apply(__instance, RussianLanguagePatches.IsRussianActive); }
        catch (Exception ex) { RussianLanguagePatches.Logger?.LogWarning($"Logo image: {ex.Message}"); }
        finally { _refreshing = false; }
    }

    private static void GraphicEnabled(Graphic __instance)
    {
        if (_refreshing || __instance == null) return;
        try
        {
            var raw = __instance.TryCast<RawImage>();
            if (raw != null) raw.texture = Resolve(raw.texture, RussianLanguagePatches.IsRussianActive);
        }
        catch (Exception ex) { RussianLanguagePatches.Logger?.LogWarning($"Logo graphic: {ex.Message}"); }
    }

    // Only at explicit language changes and title-screen setup; includes inactive UI.
    internal static void RefreshAll(bool active)
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            foreach (var obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Image>()))
            {
                var image = obj.TryCast<Image>();
                if (image != null) Apply(image, active);
            }
            foreach (var obj in Resources.FindObjectsOfTypeAll(Il2CppType.Of<RawImage>()))
            {
                var raw = obj.TryCast<RawImage>();
                if (raw != null) raw.texture = Resolve(raw.texture, active);
            }
        }
        catch (Exception ex) { RussianLanguagePatches.Logger?.LogWarning($"Logo refresh: {ex.Message}"); }
        finally { _refreshing = false; }
    }
}
