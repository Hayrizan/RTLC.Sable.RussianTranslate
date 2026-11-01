using TMPro;
using UnityEngine;

namespace RTLC.Sable.RussianTranslate;

internal static class RuntimeFontLifetime
{
    // Managed IL2CPP wrappers are not Unity asset roots. DontDestroyOnLoad does
    // not keep unused ScriptableObjects alive across Resources.UnloadUnusedAssets.
    // Only retain assets created by this plugin; native game fonts are untouched.
    internal static void Retain(TMP_FontAsset asset, Font source)
    {
        Keep(source);
        Keep(asset);
        // TMP may retain its own Font instance rather than the supplied wrapper.
        Keep(asset.sourceFontFile);
        Keep(asset.material);
        var atlases = asset.atlasTextures;
        if (atlases != null)
            for (int i = 0; i < atlases.Length; i++) Keep(atlases[i]);
    }

    internal static bool IsAlive(TMP_FontAsset? asset)
    {
        if (asset == null || asset.sourceFontFile == null ||
            asset.material == null || asset.material.mainTexture == null)
            return false;

        var atlases = asset.atlasTextures;
        if (atlases == null || atlases.Length == 0) return false;
        for (int i = 0; i < atlases.Length; i++)
            if (atlases[i] == null) return false;
        return true;
    }

    private static void Keep(Object? asset)
    {
        if (asset != null) asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
    }
}
