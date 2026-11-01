using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class BundleBuilder
{
    public static void Build()
    {
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var assets = Directory.GetFiles("Assets/RTLC", "*", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            foreach (var path in assets)
            {
                var importer = AssetImporter.GetAtPath(path);
                if (importer is TrueTypeFontImporter font)
                {
                    font.fontTextureCase = FontTextureCase.Dynamic;
                    font.includeFontData = true;
                    font.fontSize = 90;
                    font.SaveAndReimport();
                }
                else if (importer is TextureImporter texture)
                {
                    texture.textureType = TextureImporterType.Default;
                    texture.npotScale = TextureImporterNPOTScale.None;
                    texture.mipmapEnabled = false;
                    texture.alphaIsTransparency = false;
                    texture.sRGBTexture = true;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Bilinear;
                    texture.maxTextureSize = 4096;
                    texture.textureCompression = TextureImporterCompression.Uncompressed;
                    var platform = texture.GetPlatformTextureSettings("Standalone");
                    platform.overridden = true;
                    platform.maxTextureSize = 4096;
                    platform.format = TextureImporterFormat.RGBA32;
                    platform.textureCompression = TextureImporterCompression.Uncompressed;
                    texture.SetPlatformTextureSettings(platform);
                    texture.SaveAndReimport();
                }
            }
            var names = assets.Select(p =>
            {
                var name = p.Substring("Assets/RTLC/".Length).ToLowerInvariant();
                if (name == "translation.txt") return "translation";
                if (name.StartsWith("textures/")) return name.Substring(0, name.Length - 4);
                return name;
            }).ToArray();
            var output = Environment.GetEnvironmentVariable("SABLE_BUNDLE_OUTPUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("SABLE_BUNDLE_OUTPUT missing.");
            Directory.CreateDirectory(output);
            var definition = new AssetBundleBuild { assetBundleName = "rtlc.bundle", assetNames = assets, addressableNames = names };
            var manifest = BuildPipeline.BuildAssetBundles(output, new[] { definition },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode |
                BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new InvalidOperationException("Bundle build failed.");
            var bundle = AssetBundle.LoadFromFile(Path.Combine(output, "rtlc.bundle"));
            if (bundle == null || bundle.GetAllAssetNames().Length != assets.Length)
                throw new InvalidOperationException("Bundle asset inventory mismatch.");
            foreach (var name in names)
            {
                var asset = bundle.LoadAsset(name);
                if (asset == null) throw new InvalidOperationException("Cannot load " + name);
                if (name.StartsWith("fonts/") && (!(asset is Font f) || !f.dynamic))
                    throw new InvalidOperationException("Expected dynamic Font: " + name);
                if (name.StartsWith("textures/") && (!(asset is Texture2D t) || t.width != 2048 || t.height != 999))
                    throw new InvalidOperationException("Incorrect logo: " + name);
            }
            var translation = bundle.LoadAsset<TextAsset>("translation");
            if (!translation.bytes.SequenceEqual(File.ReadAllBytes("Assets/RTLC/translation.txt")))
                throw new InvalidOperationException("Translation bytes changed.");
            Debug.Log("SABLE_BUNDLE_OK: " + names.Length + " assets loaded; translation bytes exact.");
            bundle.Unload(true);
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
}
