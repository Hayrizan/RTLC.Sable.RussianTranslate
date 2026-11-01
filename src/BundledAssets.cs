using System;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Logging;
using UnityEngine;
using UnhollowerRuntimeLib;

namespace RTLC.Sable.RussianTranslate;

internal static class BundledAssets
{
    private static AssetBundle? _bundle;
    internal static bool IsLoaded => _bundle != null;

    internal static void Initialize(ManualLogSource log)
    {
        if (_bundle != null) return;
        var path = Path.Combine(Path.GetDirectoryName(typeof(BundledAssets).Assembly.Location)!, "rtlc.bundle");
        if (!File.Exists(path)) throw new FileNotFoundException("Required localization AssetBundle is missing.", path);
        _bundle = AssetBundle.LoadFromFile(path, 0);
        if (_bundle == null) throw new InvalidDataException("Unity could not load rtlc.bundle.");
        log.LogInfo($"Loaded rtlc.bundle: {_bundle.GetAllAssetNames().Length} assets.");
    }

    private static T Load<T>(string name) where T : UnityEngine.Object
    {
        if (_bundle == null) throw new InvalidOperationException("Localization bundle is not loaded.");
        var obj = _bundle.LoadAsset(name.ToLowerInvariant(), Il2CppType.Of<T>());
        var asset = obj == null ? null : obj.TryCast<T>();
        if (asset == null) throw new InvalidDataException("Missing bundle asset: " + name);
        asset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        return asset;
    }

    internal static string Translation => Encoding.UTF8.GetString(Load<TextAsset>("translation").bytes).TrimStart('\ufeff');

    internal static string[] OriginalFonts => _bundle!.GetAllAssetNames()
        .Where(name => name.StartsWith("fonts/original/", StringComparison.Ordinal)).ToArray();

    internal static Font Font(string source)
    {
        var normalized = source.Replace('\\', '/');
        string address = normalized.StartsWith("fonts/", StringComparison.OrdinalIgnoreCase)
            ? normalized : "fonts/" + Path.GetFileName(normalized);
        return Load<Font>(address);
    }

    internal static Texture2D Texture(string name) => Load<Texture2D>("textures/" + name);
}
