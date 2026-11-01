using System;
using System.Collections.Generic;

namespace RTLC.Sable.RussianTranslate;

internal static class InkTranslationValidator
{
    internal static void Validate(string original, string translation) => Compare(SafeJson.Parse(original), SafeJson.Parse(translation), "$", 0);

    private static void Compare(SafeJson original, SafeJson translation, string path, int depth)
    {
        if (depth > 510 || original.Type != translation.Type) throw new FormatException($"Ink structure differs at {path}.");
        if (original.Type == SafeJson.Kind.Object)
        {
            if (original.Properties.Count != translation.Properties.Count) throw new FormatException($"Ink container size differs at {path}.");
            var right = new Dictionary<string, SafeJson>(StringComparer.Ordinal);
            foreach (var property in translation.Properties) right.Add(property.Key, property.Value);
            foreach (var property in original.Properties)
            {
                if (!right.TryGetValue(property.Key, out var value)) throw new FormatException($"Ink key differs at {path}.");
                Compare(property.Value, value, path + "/" + Escape(property.Key), depth + 1);
            }
            return;
        }
        if (original.Type == SafeJson.Kind.Array)
        {
            if (original.Items.Count != translation.Items.Count) throw new FormatException($"Ink container size differs at {path}.");
            for (int i = 0; i < original.Items.Count; i++) Compare(original.Items[i], translation.Items[i], path + "/" + i, depth + 1);
            return;
        }
        if (original.Text == translation.Text) return;
        if (original.Type == SafeJson.Kind.String && original.Text.StartsWith("^", StringComparison.Ordinal) && translation.Text.StartsWith("^", StringComparison.Ordinal)) return;
        throw new FormatException($"Ink control value differs at {path}.");
    }

    internal static string Escape(string key) => key.Replace("~", "~0").Replace("/", "~1");
}
