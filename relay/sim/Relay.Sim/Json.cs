using System;
using System.Collections.Generic;
using System.Text;

namespace Relay.Sim
{
    /// <summary>
    /// Thrown for anything wrong with a machine file: bad JSON, a missing field, a
    /// number the sim cannot represent, a surface that isn't axis-aligned. Never
    /// swallowed and never defaulted around - a malformed machine must fail loudly,
    /// because the alternative is a level that silently plays differently.
    /// </summary>
    public sealed class MachineFormatException : Exception
    {
        public MachineFormatException(string message) : base(message) { }
    }

    public enum JsonKind
    {
        Object,
        Array,
        String,
        Number,
        Bool,
        Null
    }

    /// <summary>
    /// A parsed JSON value.
    /// <para>
    /// Hand-rolled rather than System.Text.Json, for three reasons. Relay.Sim is
    /// netstandard2.1 with no package references, and keeping it that way is what
    /// makes it droppable into Unity without assembly-version fights. The sim must
    /// parse decimals into fixed-point with integer maths (see FixParse), so the
    /// library's number handling is unusable here anyway - all we ever want is the
    /// raw token text. And a machine file is 30 lines of the simplest possible JSON.
    /// </para>
    /// <para>
    /// Objects keep their members in parallel name/value arrays rather than a
    /// Dictionary: file order is the only order worth having, and a Dictionary's
    /// iteration order is exactly the kind of thing that makes two machines load
    /// differently.
    /// </para>
    /// </summary>
    public sealed class JsonValue
    {
        public readonly JsonKind Kind;

        readonly string[] _names;      // Object only
        readonly JsonValue[] _items;   // Object values, or Array items
        readonly string _text;         // String text, or Number raw token
        readonly bool _bool;

        JsonValue(
            JsonKind kind,
            string[] names,
            JsonValue[] items,
            string text,
            bool b)
        {
            Kind = kind;
            _names = names;
            _items = items;
            _text = text;
            _bool = b;
        }

        static readonly string[] NoNames = new string[0];
        static readonly JsonValue[] NoItems = new JsonValue[0];

        internal static JsonValue Object(
            string[] names,
            JsonValue[] values)
            => new JsonValue(
                JsonKind.Object,
                names,
                values,
                "",
                false);

        internal static JsonValue Array(JsonValue[] items)
            => new JsonValue(
                JsonKind.Array,
                NoNames,
                items,
                "",
                false);

        internal static JsonValue String(string text)
            => new JsonValue(
                JsonKind.String,
                NoNames,
                NoItems,
                text,
                false);

        internal static JsonValue Number(string raw)
            => new JsonValue(
                JsonKind.Number,
                NoNames,
                NoItems,
                raw,
                false);

        internal static JsonValue Bool(bool b)
            => new JsonValue(
                JsonKind.Bool,
                NoNames,
                NoItems,
                "",
                b);

        internal static readonly JsonValue Null =
            new JsonValue(
                JsonKind.Null,
                NoNames,
                NoItems,
                "",
                false);

        public bool IsNull => Kind == JsonKind.Null;

        public bool BoolValue =>
            Kind == JsonKind.Bool
                ? _bool
                : throw Wrong("a bool");

        /// <summary>
        /// The string's contents, escapes already resolved.
        /// </summary>
        public string Text =>
            Kind == JsonKind.String
                ? _text
                : throw Wrong("a string");

        /// <summary>
        /// The number exactly as it was written. Deliberately not converted here -
        /// FixParse turns it into a Fix with integer maths.
        /// </summary>
        public string RawNumber =>
            Kind == JsonKind.Number
                ? _text
                : throw Wrong("a number");

        public int Count =>
            Kind == JsonKind.Array || Kind == JsonKind.Object
                ? _items.Length
                : throw Wrong("an array or object");

        public JsonValue this[int i]
        {
            get
            {
                if (Kind != JsonKind.Array)
                    throw Wrong("an array");

                if (i < 0 || i >= _items.Length)
                {
                    throw new MachineFormatException(
                        $"array index {i} is outside 0..{_items.Length - 1}");
                }

                return _items[i];
            }
        }

        /// <summary>
        /// Member lookup by name. Linear, over file order - see the class note.
        /// </summary>
        public bool TryGet(string name, out JsonValue value)
        {
            if (Kind != JsonKind.Object)
                throw Wrong("an object");

            for (int i = 0; i < _names.Length; i++)
            {
                if (string.Equals(
                    _names[i],
                    name,
                    StringComparison.Ordinal))
                {
                    value = _items[i];
                    return true;
                }
            }

            value = Null;
            return false;
        }

        /// <summary>
        /// Member lookup that refuses to invent a default.
        /// </summary>
        public JsonValue Get(string name, string where)
        {
            if (TryGet(name, out JsonValue v))
                return v;

            throw new MachineFormatException(
                $"{where}: missing required field \"{name}\"");
        }

        /// <summary>
        /// Member names in file order. For rejecting unknown fields.
        /// </summary>
        public string[] Names =>
            Kind == JsonKind.Object
                ? _names
                : throw Wrong("an object");

        MachineFormatException Wrong(string expected)
            => new MachineFormatException(
                $"expected {expected}, found {Kind}");
    }

    /// <summary>
    /// Enough of a JSON parser for a machine file, and no more. No comments, no
    /// trailing commas, no duplicate member names - all three are rejected rather
    /// than tolerated, because a file the loader half-understands is worse than one
    /// it refuses.
    /// </summary>
    public static class JsonReader
    {
        public static JsonValue Parse(string text)
        {
            if (text == null)
                throw new MachineFormatException(
                    "no JSON text to parse");

            int i = 0;

            JsonValue root = ParseValue(text, ref i);

            SkipWhitespace(text, ref i);

            if (i != text.Length)
            {
                throw Fail(
                    text,
                    i,
                    "trailing content after the top-level value");
            }

            return root;
        }

        static JsonValue ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);

            if (i >= s.Length)
                throw Fail(s, i, "unexpected end of input");

            char c = s[i];

            if (c == '{')
                return ParseObject(s, ref i);

            if (c == '[')
                return ParseArray(s, ref i);

            if (c == '"')
                return JsonValue.String(ParseString(s, ref i));

            if (c == '-' || (c >= '0' && c <= '9'))
                return JsonValue.Number(ParseNumber(s, ref i));

            if (Literal(s, ref i, "true"))
                return JsonValue.Bool(true);

            if (Literal(s, ref i, "false"))
                return JsonValue.Bool(false);

            if (Literal(s, ref i, "null"))
                return JsonValue.Null;

            throw Fail(
                s,
                i,
                $"unexpected character '{c}'");
        }

        static JsonValue ParseObject(string s, ref int i)
        {
            i++; // past '{'

            var names = new List<string>();
            var values = new List<JsonValue>();

            SkipWhitespace(s, ref i);

            if (Peek(s, i) == '}')
            {
                i++;

                return JsonValue.Object(
                    names.ToArray(),
                    values.ToArray());
            }

            while (true)
            {
                SkipWhitespace(s, ref i);

                if (Peek(s, i) != '"')
                {
                    throw Fail(
                        s,
                        i,
                        "expected a quoted member name");
                }

                string name = ParseString(s, ref i);

                // A duplicate member is ambiguous: last-one-wins is a silent choice,
                // and silent choices are how two platforms end up disagreeing.
                for (int n = 0; n < names.Count; n++)
                {
                    if (string.Equals(
                        names[n],
                        name,
                        StringComparison.Ordinal))
                    {
                        throw Fail(
                            s,
                            i,
                            $"duplicate member \"{name}\"");
                    }
                }

                SkipWhitespace(s, ref i);

                if (Peek(s, i) != ':')
                {
                    throw Fail(
                        s,
                        i,
                        $"expected ':' after \"{name}\"");
                }

                i++;

                names.Add(name);
                values.Add(ParseValue(s, ref i));

                SkipWhitespace(s, ref i);

                char c = Peek(s, i);

                if (c == ',')
                {
                    i++;
                    continue;
                }

                if (c == '}')
                {
                    i++;
                    break;
                }

                throw Fail(
                    s,
                    i,
                    "expected ',' or '}'");
            }

            return JsonValue.Object(
                names.ToArray(),
                values.ToArray());
        }

        static JsonValue ParseArray(string s, ref int i)
        {
            i++; // past '['

            var items = new List<JsonValue>();

            SkipWhitespace(s, ref i);

            if (Peek(s, i) == ']')
            {
                i++;

                return JsonValue.Array(
                    items.ToArray());
            }

            while (true)
            {
                items.Add(ParseValue(s, ref i));

                SkipWhitespace(s, ref i);

                char c = Peek(s, i);

                if (c == ',')
                {
                    i++;
                    continue;
                }

                if (c == ']')
                {
                    i++;
                    break;
                }

                throw Fail(
                    s,
                    i,
                    "expected ',' or ']'");
            }

            return JsonValue.Array(
                items.ToArray());
        }

        static string ParseString(string s, ref int i)
        {
            i++; // past opening quote

            var sb = new StringBuilder();

            while (true)
            {
                if (i >= s.Length)
                    throw Fail(
                        s,
                        i,
                        "unterminated string");

                char c = s[i++];

                if (c == '"')
                    break;

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i >= s.Length)
                    throw Fail(
                        s,
                        i,
                        "unterminated escape");

                char e = s[i++];

                switch (e)
                {
                    case '"':
                        sb.Append('"');
                        break;

                    case '\\':
                        sb.Append('\\');
                        break;

                    case '/':
                        sb.Append('/');
                        break;

                    case 'b':
                        sb.Append('\b');
                        break;

                    case 'f':
                        sb.Append('\f');
                        break;

                    case 'n':
                        sb.Append('\n');
                        break;

                    case 'r':
                        sb.Append('\r');
                        break;

                    case 't':
                        sb.Append('\t');
                        break;

                    case 'u':
                        sb.Append(ParseHex4(s, ref i));
                        break;

                    default:
                        throw Fail(
                            s,
                            i,
                            $"unknown escape '\\{e}'");
                }
            }

            return sb.ToString();
        }

        static char ParseHex4(string s, ref int i)
        {
            if (i + 4 > s.Length)
            {
                throw Fail(
                    s,
                    i,
                    "truncated \\u escape");
            }

            int v = 0;

            for (int k = 0; k < 4; k++)
            {
                char c = s[i + k];

                int d =
                    c >= '0' && c <= '9'
                        ? c - '0'
                        : c >= 'a' && c <= 'f'
                            ? c - 'a' + 10
                            : c >= 'A' && c <= 'F'
                                ? c - 'A' + 10
                                : -1;

                if (d < 0)
                {
                    throw Fail(
                        s,
                        i + k,
                        $"'{c}' is not a hex digit");
                }

                v = v * 16 + d;
            }

            i += 4;

            return (char)v;
        }

        /// <summary>
        /// Captures the number's text without interpreting it. The grammar is
        /// deliberately narrower than JSON's: no exponents, and no leading '+'.
        /// FixParse applies the rest of the rules (digit count, range).
        /// </summary>
        static string ParseNumber(string s, ref int i)
        {
            int start = i;

            if (Peek(s, i) == '-')
                i++;

            int intDigits = 0;

            while (
                i < s.Length &&
                s[i] >= '0' &&
                s[i] <= '9')
            {
                i++;
                intDigits++;
            }

            if (intDigits == 0)
            {
                throw Fail(
                    s,
                    start,
                    "a number needs at least one digit");
            }

            if (Peek(s, i) == '.')
            {
                i++;

                int fracDigits = 0;

                while (
                    i < s.Length &&
                    s[i] >= '0' &&
                    s[i] <= '9')
                {
                    i++;
                    fracDigits++;
                }

                if (fracDigits == 0)
                {
                    throw Fail(
                        s,
                        i,
                        "a decimal point needs digits after it");
                }
            }

            // Exponents are banned outright rather than parsed: "1e-3" in a
            // machine file is a number someone did not mean to write by hand.
            char c = Peek(s, i);

            if (c == 'e' || c == 'E')
            {
                throw Fail(
                    s,
                    i,
                    "exponent notation is not allowed in a machine file");
            }

            return s.Substring(
                start,
                i - start);
        }

        static bool Literal(
            string s,
            ref int i,
            string word)
        {
            if (i + word.Length > s.Length)
                return false;

            if (string.CompareOrdinal(
                s,
                i,
                word,
                0,
                word.Length) != 0)
            {
                return false;
            }

            i += word.Length;
            return true;
        }

        static char Peek(string s, int i)
            => i < s.Length ? s[i] : '\0';

        static void SkipWhitespace(
            string s,
            ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];

                if (
                    c == ' ' ||
                    c == '\t' ||
                    c == '\n' ||
                    c == '\r')
                {
                    i++;
                    continue;
                }

                // Not tolerated, but worth a better message than "unexpected '/'".
                if (c == '/')
                    throw Fail(
                        s,
                        i,
                        "comments are not valid JSON");

                break;
            }
        }

        /// <summary>
        /// Error with a line and column, because "invalid JSON" helps nobody.
        /// </summary>
        static MachineFormatException Fail(
            string s,
            int index,
            string message)
        {
            int line = 1;
            int col = 1;

            for (int k = 0; k < index && k < s.Length; k++)
            {
                if (s[k] == '\n')
                {
                    line++;
                    col = 1;
                }
                else
                {
                    col++;
                }
            }

            return new MachineFormatException(
                $"line {line}, column {col}: {message}");
        }
    }
}