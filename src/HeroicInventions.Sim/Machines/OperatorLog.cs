using System.Globalization;
using System.Text;

namespace HeroicInventions.Sim.Machines;

/// <summary>One thing a person's hand did: at <see cref="At"/> sim-seconds, set <c>Target.Field</c> to <see cref="Value"/>.</summary>
public sealed record OperatorAction(double At, string Target, string Field, double Value);

/// <summary>
/// The operator log (issue #153): every action on a running machine as <c>(at t (part field value))</c>, one per line.
/// It is the timed-setting form tests already use (<c>(part field value t)</c>), written so a person can read it, so one
/// record serves a test, a replay, a save file and a blueprint's demo operator.
/// </summary>
public static class OperatorLog
{
    /// <summary>Reads <c>(at t (part field value))</c> forms; anything else is an error naming what was wrong, never a skipped line.</summary>
    public static List<OperatorAction> Parse(string text)
    {
        var actions = new List<OperatorAction>();
        foreach (var form in SExprReader.ReadAll(text))
            actions.Add(FromForm(form) ?? throw new FormatException($"expected (at seconds (part field value)), got {SExprWriter.Print(form)}"));
        return actions;
    }

    /// <summary>One <c>(at t (part field value))</c> form as an action, or null when it is not one.</summary>
    public static OperatorAction? FromForm(SExpr form) =>
        form is SList { Items: [SSymbol { Name: "at" }, SNumber t, SList { Items: [SSymbol target, SSymbol field, SNumber v] }] }
            ? new OperatorAction(t.Value, target.Name, field.Name, v.Value)
            : null;

    public static SList ToForm(OperatorAction a) =>
        new([new SSymbol("at"), new SNumber(a.At), new SList([new SSymbol(a.Target), new SSymbol(a.Field), new SNumber(a.Value)])]);

    /// <summary>The log as a file: one action to a line.</summary>
    public static string ToText(IEnumerable<OperatorAction> actions) =>
        string.Concat(actions.Select(a => Line(a) + "\n"));

    /// <summary>One action as the console shows it.</summary>
    public static string Line(OperatorAction a) => $"(at {Num(a.At)} ({a.Target} {a.Field} {Num(a.Value)}))";

    /// <summary>
    /// "Copy as test": the log as a Racket form that runs the same machine under <c>simulate</c> and hand-applies the same
    /// settings. <paramref name="seconds"/> is how long the run lasted (rounded up to a whole second).
    /// </summary>
    public static string ToSimulateForm(string machine, double seconds, IEnumerable<OperatorAction> actions)
    {
        var sb = new StringBuilder();
        sb.Append($"(simulate '{machine} #:seconds {Math.Max(1, Math.Ceiling(seconds)).ToString(CultureInfo.InvariantCulture)} #:sample-dt 1\n          #:set '(");
        sb.Append(string.Join("\n                  ", actions.Select(a => $"({a.Target} {a.Field} {Num(a.Value)} {Num(a.At)})")));
        sb.Append("))");
        return sb.ToString();
    }

    // shortest text that reads back as the same double, with a lowercase exponent marker Racket's reader accepts
    private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e");
}
