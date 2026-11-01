using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RTLC.Sable.RussianTranslate;

internal sealed class UnifiedTranslationCatalog
{
    internal sealed class Entry
    {
        internal string Id { get; }
        internal string English { get; }
        internal string Russian { get; }
        internal Entry(string id, string english, string russian)
        { Id = id; English = english; Russian = russian; }
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _uniqueText = new(StringComparer.OrdinalIgnoreCase);
    internal IEnumerable<Entry> Entries => _entries.Values;
    internal int Count => _entries.Count;

    internal static UnifiedTranslationCatalog Parse(string text)
    {
        var result = new UnifiedTranslationCatalog();
        string? pendingId = null;
        int lineNumber = 0;
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            if (line.Length == 0) continue;
            if (line.StartsWith("#@", StringComparison.Ordinal))
            {
                if (pendingId != null) throw Error(lineNumber, "Previous context has no translation pair.");
                pendingId = line.Substring(2).Trim();
                if (!(pendingId.StartsWith("ui:", StringComparison.Ordinal) ||
                      pendingId.StartsWith("item:", StringComparison.Ordinal) ||
                      pendingId.StartsWith("ink:/", StringComparison.Ordinal) ||
                      pendingId.StartsWith("direct:", StringComparison.Ordinal)))
                    throw Error(lineNumber, "Unknown context identifier.");
                continue;
            }
            if (line.StartsWith("#", StringComparison.Ordinal)) continue;
            // Human-readable labels used to group dialogue lines. They are
            // intentionally metadata only and must not become translations.
            if ((line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)) ||
                (line.StartsWith("==", StringComparison.Ordinal) && line.EndsWith("==", StringComparison.Ordinal)))
                continue;
            int separator = -1;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '\\') { i++; continue; }
                if (line[i] == '=') { separator = i; break; }
            }
            // Free-form notes are allowed between entries so translators can
            // organize a large hand-edited file. A context line still requires
            // its English=Russian pair and remains strictly validated.
            if (separator < 0)
            {
                if (pendingId == null) continue;
                throw Error(lineNumber, "Expected English=Russian.");
            }
            var english = Unescape(line.Substring(0, separator), lineNumber);
            var russian = Unescape(line.Substring(separator + 1), lineNumber);
            var id = pendingId ?? "text:" + english;
            if (result._entries.ContainsKey(id)) throw Error(lineNumber, "Duplicate context: " + id);
            result._entries.Add(id, new Entry(id, english, russian));
            pendingId = null;
        }
        if (pendingId != null) throw Error(lineNumber, "Context has no translation pair.");
        if (result.Count == 0) throw Error(lineNumber, "Translation file is empty.");

        // An explicit context-free pair is the canonical fallback even when a
        // few keyed UI contexts intentionally use a different wording.
        foreach (var entry in result.Entries.Where(e => e.Id.StartsWith("text:", StringComparison.Ordinal)))
            result._uniqueText[entry.English] = entry.Russian;

        // Infer a context-free caption only when no explicit common pair exists
        // and every keyed UI occurrence has the same translation. Narrative and
        // item homonyms must never override an unrelated button caption.
        foreach (var group in result.Entries.Where(e => e.Id.StartsWith("ui:", StringComparison.Ordinal) ||
                     e.Id.StartsWith("text:", StringComparison.Ordinal)).GroupBy(e => e.English, StringComparer.OrdinalIgnoreCase))
        {
            if (result._uniqueText.ContainsKey(group.Key)) continue;
            var translations = group.Select(e => e.Russian).Distinct(StringComparer.Ordinal).ToArray();
            if (translations.Length == 1) result._uniqueText[group.Key] = translations[0];
        }
        return result;
    }

    internal bool TryGet(string id, out string russian)
    {
        if (_entries.TryGetValue(id, out var entry)) { russian = entry.Russian; return true; }
        russian = string.Empty;
        return false;
    }

    internal bool TryGetCaption(string english, out string russian) =>
        _uniqueText.TryGetValue(english, out russian!) || TryGet("direct:" + english, out russian);

    internal string BuildInk(string englishJson)
    {
        var root = SafeJson.Parse(englishJson);
        var itemIds = new HashSet<string>(Entries
            .Where(e => e.Id.StartsWith("item:", StringComparison.Ordinal))
            .Select(e => e.Id.Substring(5, e.Id.LastIndexOf(':') - 5)), StringComparer.Ordinal);
        int matched = 0;
        void Walk(SafeJson node, string path)
        {
            if (node.Type == SafeJson.Kind.Object)
            {
                foreach (var child in node.Properties)
                    Walk(child.Value, path + "/" + InkTranslationValidator.Escape(child.Key));
            }
            else if (node.Type == SafeJson.Kind.Array)
            {
                int index = 0;
                foreach (var child in node.Items) Walk(child, path + "/" + index++);
            }
            else if (node.Type == SafeJson.Kind.String && node.Text.StartsWith("^", StringComparison.Ordinal))
            {
                if (_entries.TryGetValue("ink:" + path, out var entry))
                {
                    if (!EnglishMatches(node.Text.Substring(1), entry.English))
                        throw new FormatException("English Ink source differs at " + path);
                    // Ink's ^ strings include inventory API arguments, not only
                    // visible dialogue. Translating an item ID breaks quest conditions.
                    if (!itemIds.Contains(node.Text.Substring(1)))
                        node.Text = "^" + entry.Russian;
                    matched++;
                }
                else if (!itemIds.Contains(node.Text.Substring(1)) && TryGet("text:" + node.Text.Substring(1), out var translated))
                    node.Text = "^" + translated;
            }
        }
        Walk(root, string.Empty);
        // The game can add/remove Ink nodes between releases while the hand-
        // edited catalog still contains older contexts. Do not discard the
        // entire valid translation in that case: unmatched catalog entries are
        // harmless, and the structural validator below still protects every
        // control value in the current story.
        var json = root.Write();
        InkTranslationValidator.Validate(englishJson, json);
        return json;
    }

    private static FormatException Error(int line, string message) =>
        new FormatException($"translation.txt, line {line}: {message}");

    private static bool EnglishMatches(string gameText, string catalogText)
    {
        if (string.Equals(gameText, catalogText, StringComparison.Ordinal)) return true;
        return string.Equals(RemoveItalicTags(gameText), RemoveItalicTags(catalogText), StringComparison.Ordinal);
    }

    private static string RemoveItalicTags(string value) =>
        value.Replace("<i>", string.Empty).Replace("</i>", string.Empty);

    private static string Unescape(string value, int line)
    {
        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\') { result.Append(value[i]); continue; }
            if (++i == value.Length) throw Error(line, "Incomplete escape sequence.");
            switch (value[i])
            {
                case 'n': result.Append('\n'); break;
                case 'r': result.Append('\r'); break;
                case 't': result.Append('\t'); break;
                case '\\': result.Append('\\'); break;
                case '=': result.Append('='); break;
                case '#': result.Append('#'); break;
                default: throw Error(line, "Unknown escape sequence: \\" + value[i]);
            }
        }
        return result.ToString();
    }
}
