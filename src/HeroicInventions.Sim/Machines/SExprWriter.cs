using System.Globalization;
using System.Text;

namespace HeroicInventions.Sim.Machines;

/// <summary>
/// Prints <see cref="SExpr"/> trees back into the small Racket-readable
/// syntax <see cref="SExprReader"/> parses: the inverse of that reader, and
/// the same subset racket/heroic/emit.rkt writes. Kept separate from the
/// reader so both directions of the .machine format sit next to each other.
/// </summary>
public static class SExprWriter
{
    public static string Print(SExpr e)
    {
        var sb = new StringBuilder();
        Write(e, sb);
        return sb.ToString();
    }

    private static void Write(SExpr e, StringBuilder sb)
    {
        switch (e)
        {
            case SList l:
                sb.Append('(');
                for (int i = 0; i < l.Items.Count; i++)
                {
                    if (i > 0) sb.Append(' ');
                    Write(l.Items[i], sb);
                }
                sb.Append(')');
                break;
            case SSymbol s:
                sb.Append(s.Name);
                break;
            case SBool b:
                sb.Append(b.Value ? "#t" : "#f");
                break;
            case SString s:
                sb.Append('"');
                foreach (char c in s.Value)
                    sb.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\t' => "\\t", _ => c.ToString() });
                sb.Append('"');
                break;
            case SNumber n:
                sb.Append(Number(n.Value));
                break;
            default:
                throw new ArgumentException($"unhandled SExpr: {e}");
        }
    }

    /// <summary>
    /// A finite double, formatted so <see cref="SExprReader"/> parses back
    /// the identical value (.NET's default double.ToString has round-tripped
    /// exactly since .NET Core 3.0). Mirrors emit.rkt's `num`, which refuses
    /// NaN and infinities — SExprReader's atom reader would otherwise read
    /// "+inf.0" as a bare symbol, silently losing the number.
    /// </summary>
    public static string Number(double v)
    {
        if (!double.IsFinite(v))
            throw new ArgumentException($"a .machine file needs a finite number, got {v}");
        string s = v.ToString(CultureInfo.InvariantCulture);
        // A bare integer like "3" still reads back as a number, but writing
        // it as "3.0" keeps every emitted number visibly a flonum, matching
        // emit.rkt's exact->inexact and the racket-built .machine files.
        return s.IndexOfAny(['.', 'e', 'E']) < 0 ? s + ".0" : s;
    }
}
