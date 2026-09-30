using System.Globalization;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// Parses a number token from a BuildSession command into an SI value. The
/// command language is not a general evaluator — see BuildSession's class
/// comment — so the only units it understands are this fixed whitelist, the
/// same units the design doc's editor commands are meant to read comfortably
/// in (a tank's area in m² or cm², a flow in L/s, a boiler's fire in W or
/// kW) without silently accepting anything else typed after a number.
/// </summary>
public static class Units
{
    /// <summary>True when the token reads as a number (with or without a unit); a plain name such as <c>coal</c> or <c>y</c> is not.</summary>
    public static bool LooksNumeric(string token) =>
        token.Length > 0 && (char.IsAsciiDigit(token[0]) ||
            token.Length > 1 && token[0] is '.' or '-' or '+' && (char.IsAsciiDigit(token[1]) || token[1] == '.'));

    public static double Parse(string token, string context)
    {
        int split = 0;
        while (split < token.Length && (char.IsAsciiDigit(token[split]) || token[split] is '.' or '-' or '+'))
            split++;
        // An exponent (1e-3) has a digit after the e/E; a bare unit never does.
        if (split < token.Length && token[split] is 'e' or 'E' && split + 1 < token.Length &&
            (char.IsAsciiDigit(token[split + 1]) || token[split + 1] is '-' or '+'))
        {
            split++;
            while (split < token.Length && (char.IsAsciiDigit(token[split]) || token[split] is '-' or '+')) split++;
        }
        string numberPart = token[..split];
        string unit = token[split..];
        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
            throw new FormatException($"{context}: '{token}' is not a number, optionally followed by a unit");
        if (unit.Length == 0) return n;
        return unit switch
        {
            "m" => n,
            "cm" => n / 100,
            "mm" => n / 1000,
            "m2" => n,
            "cm2" => n / 100 / 100,
            "L" => n / 1000,          // -> m^3
            "L/s" => n / 1000,        // -> m^3/s
            "kg" => n,
            "g" => n / 1000,
            "W" => n,
            "kW" => n * 1000,
            _ => throw new FormatException(
                $"{context}: unknown unit '{unit}' in '{token}' — allowed units are m, cm, mm, m2, cm2, L, L/s, kg, g, W, kW"),
        };
    }
}
