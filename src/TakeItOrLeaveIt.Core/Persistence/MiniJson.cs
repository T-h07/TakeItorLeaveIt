using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TakeItOrLeaveIt.Core.Persistence;

/// <summary>
/// Minimal JSON reader/writer for the mod's own small settings files. Core has no package
/// dependencies and must load in both MelonLoader runtimes, so it does not use System.Text.Json.
/// Values: <see cref="Dictionary{TKey,TValue}"/> (object), <see cref="List{T}"/> (array), string,
/// double, bool, null.
/// </summary>
public static class MiniJson
{
    private const int MaxDepth = 32;

    public static object? Parse(string text)
    {
        if (text is null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var reader = new Reader(text);
        var value = reader.ReadValue(0);
        reader.SkipWhitespace();
        if (!reader.AtEnd)
        {
            throw reader.Error("Unexpected trailing characters");
        }

        return value;
    }

    public static string Write(object? value)
    {
        var sb = new StringBuilder();
        WriteValue(sb, value, 0);
        sb.Append('\n');
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, object? value, int indent)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case string s:
                WriteString(sb, s);
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case int i:
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                break;
            case long l:
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
                break;
            case double d:
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                break;
            case IDictionary<string, object?> map:
                if (map.Count == 0)
                {
                    sb.Append("{}");
                    break;
                }

                sb.Append("{\n");
                var first = true;
                foreach (var pair in map)
                {
                    if (!first)
                    {
                        sb.Append(",\n");
                    }

                    first = false;
                    sb.Append(' ', (indent + 1) * 2);
                    WriteString(sb, pair.Key);
                    sb.Append(": ");
                    WriteValue(sb, pair.Value, indent + 1);
                }

                sb.Append('\n').Append(' ', indent * 2).Append('}');
                break;
            case IEnumerable<object?> list:
                sb.Append('[');
                var firstItem = true;
                foreach (var item in list)
                {
                    if (!firstItem)
                    {
                        sb.Append(", ");
                    }

                    firstItem = false;
                    WriteValue(sb, item, indent + 1);
                }

                sb.Append(']');
                break;
            default:
                throw new ArgumentException($"Unsupported JSON value type {value.GetType().Name}.");
        }
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }

    private sealed class Reader
    {
        private readonly string _s;
        private int _i;

        public Reader(string s) => _s = s;

        public bool AtEnd => _i >= _s.Length;

        public FormatException Error(string message) => new($"{message} at position {_i}.");

        public void SkipWhitespace()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
            {
                _i++;
            }
        }

        public object? ReadValue(int depth)
        {
            if (depth > MaxDepth)
            {
                throw Error("Nesting too deep");
            }

            SkipWhitespace();
            if (AtEnd)
            {
                throw Error("Unexpected end of input");
            }

            var c = _s[_i];
            switch (c)
            {
                case '{': return ReadObject(depth);
                case '[': return ReadArray(depth);
                case '"': return ReadString();
                case 't': Expect("true"); return true;
                case 'f': Expect("false"); return false;
                case 'n': Expect("null"); return null;
                default:
                    if (c == '-' || char.IsDigit(c))
                    {
                        return ReadNumber();
                    }

                    throw Error($"Unexpected character '{c}'");
            }
        }

        private Dictionary<string, object?> ReadObject(int depth)
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            _i++;
            SkipWhitespace();
            if (Peek('}'))
            {
                _i++;
                return map;
            }

            while (true)
            {
                SkipWhitespace();
                if (!Peek('"'))
                {
                    throw Error("Expected property name");
                }

                var key = ReadString();
                SkipWhitespace();
                Consume(':');
                var value = ReadValue(depth + 1);
                if (map.ContainsKey(key))
                {
                    throw Error($"Duplicate property '{key}'");
                }

                map[key] = value;
                SkipWhitespace();
                if (Peek(','))
                {
                    _i++;
                    continue;
                }

                Consume('}');
                return map;
            }
        }

        private List<object?> ReadArray(int depth)
        {
            var list = new List<object?>();
            _i++;
            SkipWhitespace();
            if (Peek(']'))
            {
                _i++;
                return list;
            }

            while (true)
            {
                list.Add(ReadValue(depth + 1));
                SkipWhitespace();
                if (Peek(','))
                {
                    _i++;
                    continue;
                }

                Consume(']');
                return list;
            }
        }

        private string ReadString()
        {
            Consume('"');
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd)
                {
                    throw Error("Unterminated string");
                }

                var c = _s[_i++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    if (c < 0x20)
                    {
                        throw Error("Control character in string");
                    }

                    sb.Append(c);
                    continue;
                }

                if (AtEnd)
                {
                    throw Error("Unterminated escape");
                }

                var e = _s[_i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_i + 4 > _s.Length ||
                            !int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            throw Error("Invalid \\u escape");
                        }

                        sb.Append((char)code);
                        _i += 4;
                        break;
                    default:
                        throw Error($"Invalid escape '\\{e}'");
                }
            }
        }

        private double ReadNumber()
        {
            var start = _i;
            while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0)
            {
                _i++;
            }

            var token = _s.Substring(start, _i - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                double.IsNaN(value) || double.IsInfinity(value))
            {
                throw Error($"Invalid number '{token}'");
            }

            return value;
        }

        private void Expect(string literal)
        {
            if (string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
            {
                throw Error($"Expected '{literal}'");
            }

            _i += literal.Length;
        }

        private bool Peek(char c) => _i < _s.Length && _s[_i] == c;

        private void Consume(char c)
        {
            SkipWhitespace();
            if (!Peek(c))
            {
                throw Error($"Expected '{c}'");
            }

            _i++;
        }
    }
}
