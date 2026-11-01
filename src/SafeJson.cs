using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RTLC.Sable.RussianTranslate;

internal sealed class SafeJson
{
    internal enum Kind { Object, Array, String, Number, True, False, Null }
    internal Kind Type;
    internal string Text = string.Empty;
    internal readonly List<KeyValuePair<string, SafeJson>> Properties = new();
    internal readonly List<SafeJson> Items = new();

    internal static SafeJson Parse(string json)
    {
        var parser = new Parser(json.TrimStart('\uFEFF'));
        var value = parser.Value(0);
        parser.White();
        if (!parser.End) throw new FormatException("Trailing data after Ink JSON.");
        return value;
    }

    internal string Write() { var output = new StringBuilder(); WriteTo(output); return output.ToString(); }
    private void WriteTo(StringBuilder output)
    {
        switch (Type)
        {
            case Kind.Object:
                output.Append('{');
                for (int i = 0; i < Properties.Count; i++) { if (i != 0) output.Append(','); Quote(output, Properties[i].Key); output.Append(':'); Properties[i].Value.WriteTo(output); }
                output.Append('}'); break;
            case Kind.Array:
                output.Append('['); for (int i = 0; i < Items.Count; i++) { if (i != 0) output.Append(','); Items[i].WriteTo(output); } output.Append(']'); break;
            case Kind.String: Quote(output, Text); break;
            case Kind.Number: output.Append(Text); break;
            case Kind.True: output.Append("true"); break;
            case Kind.False: output.Append("false"); break;
            default: output.Append("null"); break;
        }
    }

    private static void Quote(StringBuilder output, string value)
    {
        output.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': output.Append("\\\""); break; case '\\': output.Append("\\\\"); break;
                case '\b': output.Append("\\b"); break; case '\f': output.Append("\\f"); break;
                case '\n': output.Append("\\n"); break; case '\r': output.Append("\\r"); break; case '\t': output.Append("\\t"); break;
                default: if (c < 0x20) output.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); else output.Append(c); break;
            }
        }
        output.Append('"');
    }

    private sealed class Parser
    {
        private readonly string _json; private int _at;
        internal Parser(string json) { _json = json; }
        internal bool End => _at == _json.Length;
        internal void White() { while (_at < _json.Length && char.IsWhiteSpace(_json[_at])) _at++; }
        internal SafeJson Value(int depth)
        {
            if (depth > 512) throw Error("JSON nesting is too deep."); White(); if (End) throw Error("Incomplete Ink JSON.");
            switch (_json[_at])
            {
                case '{': return Object(depth + 1); case '[': return Array(depth + 1);
                case '"': return new SafeJson { Type = Kind.String, Text = String() };
                case 't': Literal("true"); return new SafeJson { Type = Kind.True };
                case 'f': Literal("false"); return new SafeJson { Type = Kind.False };
                case 'n': Literal("null"); return new SafeJson { Type = Kind.Null };
                default: return Number();
            }
        }
        private SafeJson Object(int depth)
        {
            _at++; var result = new SafeJson { Type = Kind.Object }; var keys = new HashSet<string>(StringComparer.Ordinal); White(); if (Take('}')) return result;
            while (true) { White(); if (End || _json[_at] != '"') throw Error("Expected JSON object key."); string key = String(); if (!keys.Add(key)) throw Error("Duplicate Ink key: " + key); White(); if (!Take(':')) throw Error("Expected ':' after JSON object key."); result.Properties.Add(new KeyValuePair<string, SafeJson>(key, Value(depth))); White(); if (Take('}')) return result; if (!Take(',')) throw Error("Expected ',' in JSON object."); }
        }
        private SafeJson Array(int depth)
        {
            _at++; var result = new SafeJson { Type = Kind.Array }; White(); if (Take(']')) return result;
            while (true) { result.Items.Add(Value(depth)); White(); if (Take(']')) return result; if (!Take(',')) throw Error("Expected ',' in JSON array."); }
        }
        private SafeJson Number()
        {
            int start = _at; Take('-'); if (_at >= _json.Length) throw Error("Invalid JSON number.");
            if (_json[_at] == '0') _at++; else { if (!Digit(_json[_at])) throw Error("Unexpected JSON token."); while (_at < _json.Length && Digit(_json[_at])) _at++; }
            if (Take('.')) { int fraction = _at; while (_at < _json.Length && Digit(_json[_at])) _at++; if (_at == fraction) throw Error("Invalid JSON number."); }
            if (_at < _json.Length && (_json[_at] == 'e' || _json[_at] == 'E')) { _at++; if (_at < _json.Length && (_json[_at] == '+' || _json[_at] == '-')) _at++; int exponent = _at; while (_at < _json.Length && Digit(_json[_at])) _at++; if (_at == exponent) throw Error("Invalid JSON number."); }
            return new SafeJson { Type = Kind.Number, Text = _json.Substring(start, _at - start) };
        }
        private string String()
        {
            _at++; var value = new StringBuilder();
            while (_at < _json.Length) { char c = _json[_at++]; if (c == '"') return value.ToString(); if (c < 0x20) throw Error("Control character in JSON string."); if (c != '\\') { value.Append(c); continue; } if (_at >= _json.Length) throw Error("Incomplete JSON escape."); c = _json[_at++]; switch (c) { case '"': case '\\': case '/': value.Append(c); break; case 'b': value.Append('\b'); break; case 'f': value.Append('\f'); break; case 'n': value.Append('\n'); break; case 'r': value.Append('\r'); break; case 't': value.Append('\t'); break; case 'u': value.Append(Unicode()); break; default: throw Error("Unknown JSON escape."); } }
            throw Error("Unterminated JSON string.");
        }
        private char Unicode()
        {
            if (_at + 4 > _json.Length) throw Error("Incomplete Unicode escape."); int value = 0;
            for (int i = 0; i < 4; i++) { char c = _json[_at++]; int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; if (digit < 0) throw Error("Invalid Unicode escape."); value = value * 16 + digit; }
            return (char)value;
        }
        private void Literal(string value) { if (_at + value.Length > _json.Length || string.CompareOrdinal(_json, _at, value, 0, value.Length) != 0) throw Error("Unexpected JSON token."); _at += value.Length; }
        private bool Take(char c) { if (_at < _json.Length && _json[_at] == c) { _at++; return true; } return false; }
        private static bool Digit(char c) => c >= '0' && c <= '9';
        private FormatException Error(string message) => new FormatException(message + " Position " + _at + ".");
    }
}
