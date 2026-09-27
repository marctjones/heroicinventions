using System.Globalization;
using System.Text;

namespace HeroicInventions.Sim.Machines;

/// <summary>
/// The subset of Racket's S-expression syntax that .machine files use:
/// lists, symbols, finite numbers, strings and booleans.
/// </summary>
public abstract record SExpr;

public sealed record SList(IReadOnlyList<SExpr> Items) : SExpr
{
    /// <summary>The list's first element when it is a symbol, as in (part …) or (at …).</summary>
    public string? Head => Items.Count > 0 && Items[0] is SSymbol s ? s.Name : null;

    /// <summary>The first sublist whose head is <paramref name="name"/>.</summary>
    public SList? Field(string name) => Items.OfType<SList>().FirstOrDefault(l => l.Head == name);

    public IEnumerable<SList> Fields(string name) => Items.OfType<SList>().Where(l => l.Head == name);
}

public sealed record SSymbol(string Name) : SExpr;
public sealed record SNumber(double Value) : SExpr;
public sealed record SString(string Value) : SExpr;
public sealed record SBool(bool Value) : SExpr;

public static class SExprReader
{
    public static IReadOnlyList<SExpr> ReadAll(string text)
    {
        var reader = new Reader(text);
        var result = new List<SExpr>();
        while (reader.SkipAtmosphere()) result.Add(reader.Read());
        return result;
    }

    private sealed class Reader(string text)
    {
        private int _pos;
        private int _line = 1;

        /// <summary>Skips whitespace and ; comments. Returns false at end of input.</summary>
        public bool SkipAtmosphere()
        {
            while (_pos < text.Length)
            {
                char c = text[_pos];
                if (c == '\n') { _line++; _pos++; }
                else if (char.IsWhiteSpace(c)) _pos++;
                else if (c == ';') { while (_pos < text.Length && text[_pos] != '\n') _pos++; }
                else return true;
            }
            return false;
        }

        public SExpr Read()
        {
            if (!SkipAtmosphere()) throw Error("unexpected end of input");
            char c = text[_pos];
            switch (c)
            {
                case '(' or '[':
                    return ReadList(c == '(' ? ')' : ']');
                case ')' or ']':
                    throw Error($"unexpected '{c}'");
                case '"':
                    return ReadString();
                case '|':
                    throw Error("|quoted| symbols are not supported in .machine files");
                case '\'' or '`' or ',':
                    throw Error($"quote syntax '{c}' is not supported in .machine files");
                default:
                    return ReadAtom();
            }
        }

        private SList ReadList(char close)
        {
            int startLine = _line;
            _pos++;
            var items = new List<SExpr>();
            while (true)
            {
                if (!SkipAtmosphere()) throw Error($"list opened on line {startLine} is never closed");
                char c = text[_pos];
                if (c == close) { _pos++; return new SList(items); }
                if (c is ')' or ']') throw Error($"'{c}' does not match the bracket opened on line {startLine}");
                items.Add(Read());
            }
        }

        private SString ReadString()
        {
            var sb = new StringBuilder();
            _pos++;
            while (true)
            {
                if (_pos >= text.Length) throw Error("string is never closed");
                char c = text[_pos++];
                if (c == '"') return new SString(sb.ToString());
                if (c == '\n') _line++;
                if (c != '\\') { sb.Append(c); continue; }
                if (_pos >= text.Length) throw Error("string ends with a lone backslash");
                char e = text[_pos++];
                sb.Append(e switch { 'n' => '\n', 't' => '\t', '\\' => '\\', '"' => '"', _ => throw Error($"unsupported escape \\{e}") });
            }
        }

        private SExpr ReadAtom()
        {
            int start = _pos;
            while (_pos < text.Length && !char.IsWhiteSpace(text[_pos]) && text[_pos] is not ('(' or ')' or '[' or ']' or '"' or ';'))
                _pos++;
            string token = text[start.._pos];
            return token switch
            {
                "#t" or "#true" => new SBool(true),
                "#f" or "#false" => new SBool(false),
                _ when double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d)
                    => new SNumber(d),
                _ => new SSymbol(token),
            };
        }

        private FormatException Error(string message) => new($"line {_line}: {message}");
    }
}
