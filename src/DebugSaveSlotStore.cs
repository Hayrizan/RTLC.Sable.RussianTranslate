using System;
using System.IO;
using System.Linq;

namespace RTLC.Sable.RussianTranslate;

internal static class DebugSaveSlotStore
{
    internal static string MakeSafeName(string value)
    {
        var invalid = new string(Path.GetInvalidFileNameChars());
        var safe = new string((value ?? string.Empty).Trim().Where(c => !invalid.Contains(c)).ToArray()).TrimEnd('.', ' ');
        if (safe.Length == 0) throw new ArgumentException("Введите название слота.");
        var truncated = safe.Length > 80 ? safe.Substring(0, 80).TrimEnd('.', ' ') : safe;
        if (truncated.Length == 0) throw new ArgumentException("Введите название слота.");
        return truncated;
    }

    internal static string[] List(string root) => Directory.Exists(root)
        ? Directory.GetFiles(root, "*.save").Select(Path.GetFileNameWithoutExtension).Where(n => n != null).Select(n => n!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray()
        : Array.Empty<string>();
}
