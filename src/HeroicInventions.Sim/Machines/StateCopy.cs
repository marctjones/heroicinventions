using System.Reflection;

namespace HeroicInventions.Sim.Machines;

/// <summary>
/// Carries a part's running state from one object to its rebuilt twin
/// (issue #75: editing a machine while it runs), by a three-way comparison
/// field by field:
/// <list type="bullet">
/// <item>where the edited part, freshly built, differs from the original
/// part freshly built, the edit changed that setting (a wheel's load, a
/// fire's power), and the new value stays;</item>
/// <item>everywhere else the running value is carried over: a tank's water,
/// a boiler's temperature, a fire's remaining fuel, a wheel's speed.</item>
/// </list>
/// That tells settings from state without depending on how each property is
/// declared (a fire's fuel is publicly settable but also burns down). Only
/// plain values are copied (numbers, booleans, enums, structs of those, and
/// same-length arrays of them); references to other parts are the new
/// machine's own wiring and are never touched.
/// </summary>
public static class StateCopy
{
    /// <param name="running">the original part, as it is now</param>
    /// <param name="baseline">the original part as freshly built</param>
    /// <param name="edited">the edited part, freshly built: receives the running state</param>
    public static void Carry(object running, object baseline, object edited)
    {
        if (running.GetType() != edited.GetType() || baseline.GetType() != edited.GetType()) return;
        for (var type = edited.GetType(); type is not null && type != typeof(object); type = type.BaseType)
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (f.IsInitOnly) continue;                                   // fixed at construction: a setting
                if (IsPlain(f.FieldType))
                {
                    if (Equals(f.GetValue(baseline), f.GetValue(edited))) f.SetValue(edited, f.GetValue(running));
                }
                else if (f.FieldType.IsArray && IsPlain(f.FieldType.GetElementType()!)
                         && f.GetValue(running) is Array src && f.GetValue(edited) is Array dst && src.Length == dst.Length
                         && f.GetValue(baseline) is Array b && b.Length == dst.Length)
                    Array.Copy(src, dst, src.Length);
            }
    }

    private static bool IsPlain(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(decimal)
        || (t.IsValueType && !t.IsGenericType && t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(f => IsPlain(f.FieldType)));
}
