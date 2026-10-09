using System.Collections;
using System.Reflection;

namespace HeroicInventions.Sim.Machines;

/// <summary>
/// A running machine's state written down and read back (issue #67): every tank's water, boiler's heat, hopper's
/// grain, cam's follower, pawl's valley, the sun's clock, the air in each room, the time — everything plain the
/// simulation core keeps as it runs, found by walking the runtime the way <see cref="StateCopy"/> does when a machine
/// is edited while it runs. It is written as <c>(path value)</c> pairs, one per number, flag or short list, where a
/// path names where it lives (<c>_tanks/cistern/_water</c>): no part has to know how to save itself, and a part added
/// tomorrow is saved by the same walk.
///
/// What is written is the running state; the machine's definition stays in its own file, which the save names. Loading
/// rebuilds the machine from that file and lays the state back on it, so a machine saved and loaded goes on exactly as
/// if it had never stopped (to within float rounding of the text). What it cannot hold: anything the rigid-body
/// engine keeps (poses, velocities, a rope's wound length): the game saves those beside it.
/// </summary>
public static class RuntimeState
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>The running state as <c>(state (path value) …)</c>.</summary>
    public static SList Capture(MachineRuntime runtime)
    {
        var items = new List<SExpr> { new SSymbol("state") };
        foreach (var (path, holder) in Roots(runtime))
            Walk(path, holder, [], 0, (p, v) => items.Add(new SList([new SSymbol(p), v])));
        return new SList(items);
    }

    /// <summary>Lays a captured state back on a freshly built runtime of the same machine. Returns the paths that found nothing to set (the machine has changed since).</summary>
    public static IReadOnlyList<string> Restore(MachineRuntime runtime, SList state)
    {
        var unused = RestoreFrom(Roots(runtime), state);
        runtime.ReseatHeatStores();   // a rock that was in a bin when it was saved is in it again (issue #206)
        return unused;
    }

    private static IReadOnlyList<string> RestoreFrom(IEnumerable<(string Path, object Holder)> roots, SList state)
    {
        var saved = state.Items.Skip(1).OfType<SList>().Where(l => l.Items is [SSymbol, _])
            .ToDictionary(l => ((SSymbol)l.Items[0]).Name, l => l.Items[1]);
        var used = new HashSet<string>();
        foreach (var (path, holder) in roots)
            Walk(path, holder, [], 0, (p, _) => { },
                (p, set) => { if (saved.TryGetValue(p, out var v) && set(v)) used.Add(p); },
                (p, dict, valueType) =>
                {
                    // a name-keyed dictionary of plain values fills as the sim runs: put back the entries the save has and this run has not made yet
                    foreach (var (key, sx) in saved.Where(e => e.Key.StartsWith(p + "/") && !e.Key[(p.Length + 1)..].Contains('/')).ToList())
                    {
                        string name = key[(p.Length + 1)..];
                        if (dict.Contains(name) || FromSExpr(valueType, sx) is not { } value) continue;
                        dict[name] = value;
                        used.Add(key);
                    }
                });
        return saved.Keys.Where(k => !used.Contains(k)).ToList();
    }

    /// <summary>The state of the ground a world stands on: the map's heights and soils, the water lying on it, the sand it has moved.</summary>
    public static SList CaptureGround(WorldGround ground)
    {
        var items = new List<SExpr> { new SSymbol("state") };
        foreach (var (path, holder) in GroundRoots(ground))
            Walk(path, holder, [], 0, (p, v) => items.Add(new SList([new SSymbol(p), v])));
        return new SList(items);
    }

    public static IReadOnlyList<string> RestoreGround(WorldGround ground, SList state) => RestoreFrom(GroundRoots(ground), state);

    private static IEnumerable<(string Path, object Holder)> GroundRoots(WorldGround ground)
    {
        yield return ("ground", ground);      // reaches the terrain and the water through its own fields
    }

    /// <summary>The objects a walk starts from, by name, in a fixed order.</summary>
    private static IEnumerable<(string Path, object Holder)> Roots(MachineRuntime runtime)
    {
        yield return ("runtime", runtime);
        yield return ("sun", runtime.Sun);
        yield return ("outside", runtime.Outside);
        yield return ("fluids", runtime.Fluids);
    }

    private static bool IsPlain(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(decimal)
        || (t.IsValueType && !t.IsGenericType && t.GetFields(Fields).All(f => IsPlain(f.FieldType)));

    private static bool IsPlainArray(Type t) => t.IsArray && IsPlain(t.GetElementType()!);

    private static bool Skippable(Type t) =>
        typeof(Delegate).IsAssignableFrom(t) || t == typeof(string) || t.Namespace?.StartsWith("System.Reflection") == true;

    /// <summary>
    /// Visits every plain field reachable from <paramref name="holder"/>: its own, those of the objects its fields
    /// hold when they belong to the simulation, and those of the values in its string- or index-keyed collections. The
    /// same walk, in the same order, runs on save and load, so a path names the same field both times.
    /// </summary>
    private static void Walk(string path, object holder, HashSet<object> visited, int depth, Action<string, SExpr> visit, Action<string, Func<SExpr, bool>>? restore = null, Action<string, IDictionary, Type>? named = null)
    {
        if (depth > 6 || !visited.Add(holder) && depth > 0) return;
        var seen = visited;
        if (seen.Count == 0) seen.Add(holder);
        for (var type = holder.GetType(); type is not null && type != typeof(object); type = type.BaseType)
            foreach (var f in type.GetFields(Fields | BindingFlags.DeclaredOnly).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (f.IsStatic || f.IsNotSerialized || Skippable(f.FieldType) || f.Name is "_materials") continue;   // [NonSerialized]: saved its own way (a map's boulders)
                string p = $"{path}/{Clean(f.Name)}";
                object? value = f.GetValue(holder);
                if (IsPlain(f.FieldType) || IsPlainArray(f.FieldType))
                {
                    // a plain value fixed at construction (a setting) is not state; a readonly array's contents (a map's heights) are, and so is
                    // a readonly reference to a mutable object, below
                    if (f.IsInitOnly && !IsPlainArray(f.FieldType)) continue;
                    if (value is null)
                    {
                        // a lazily made array not made yet here (the ground's loose-soil flags) can still be filled from a save that has it
                        if (!f.IsInitOnly) restore?.Invoke(p, sx => TrySet(f, holder, sx));
                        continue;
                    }
                    visit(p, ToSExpr(value));
                    restore?.Invoke(p, sx => TrySet(f, holder, sx));
                }
                else if (value is IDictionary dict && f.FieldType.IsGenericType)
                {
                    var args = f.FieldType.GetGenericArguments();
                    bool byName = args[0] == typeof(string);
                    if (byName && IsPlain(args[1])) named?.Invoke(p, dict, args[1]);      // entries a fresh runtime has not made yet
                    int i = 0;
                    foreach (DictionaryEntry e in dict)
                    {
                        string key = byName ? (string)e.Key : (i++).ToString();
                        string ep = $"{p}/{key}";
                        object? v = e.Value;
                        if (v is System.Runtime.CompilerServices.ITuple tuple && tuple.Length > 0) v = tuple[0];
                        if (v is null) continue;
                        if (IsPlain(v.GetType()))
                        {
                            visit(ep, ToSExpr(v));
                            if (restore is not null && dict is not null)
                            {
                                var boxedKey = e.Key;
                                restore(ep, sx => { var b = FromSExpr(v.GetType(), sx); if (b is null) return false; dict[boxedKey] = b; return true; });
                            }
                        }
                        else if (v.GetType().IsClass && Sim(v.GetType())) Walk(ep, v, seen, depth + 1, visit, restore, named);
                    }
                }
                else if (value is IList list && value is not Array && f.FieldType.IsGenericType)
                {
                    for (int i = 0; i < list.Count; i++)
                        if (list[i] is { } item && item.GetType().IsClass && Sim(item.GetType())) Walk($"{p}/{i}", item, seen, depth + 1, visit, restore, named);
                }
                else if (value is not null && f.FieldType.IsClass && Sim(f.FieldType) && !typeof(Delegate).IsAssignableFrom(f.FieldType))
                    Walk(p, value, seen, depth + 1, visit, restore, named);
            }
    }

    private static bool Sim(Type t) => t.Namespace?.StartsWith("HeroicInventions.Sim") == true && !typeof(MachineRuntime).IsAssignableFrom(t);

    /// <summary>Auto-property backing fields read <c>&lt;Time&gt;k__BackingField</c>: keep the name.</summary>
    private static string Clean(string name) => name.StartsWith('<') ? name[1..name.IndexOf('>')] : name;

    // ------------------------------------------------------------------ values

    private static SExpr ToSExpr(object v) => v switch
    {
        bool b => new SBool(b),
        Enum e => new SSymbol(e.ToString()),
        double d => Number(d),
        float f => Number(f),
        int i => new SNumber(i),
        long l => new SNumber(l),
        short s => new SNumber(s),
        byte y => new SNumber(y),
        uint u => new SNumber(u),
        char c => new SNumber(c),
        Array a => new SList(a.Cast<object?>().Select(x => x is null ? new SBool(false) : ToSExpr(x)).ToList()),
        decimal m => new SNumber((double)m),
        _ => new SList(v.GetType().GetFields(Fields).Select(f => ToSExpr(f.GetValue(v)!)).ToList()),   // a struct of plain values, field by field
    };

    /// <summary>A finite number as itself; NaN and the infinities, which the file format cannot hold as numbers, as the symbols nan, inf and -inf.</summary>
    private static SExpr Number(double d) =>
        double.IsFinite(d) ? new SNumber(d) : new SSymbol(double.IsNaN(d) ? "nan" : d > 0 ? "inf" : "-inf");

    private static double? ReadNumber(SExpr sx) => sx switch
    {
        SNumber n => n.Value,
        SSymbol { Name: "nan" } => double.NaN,
        SSymbol { Name: "inf" } => double.PositiveInfinity,
        SSymbol { Name: "-inf" } => double.NegativeInfinity,
        _ => null,
    };

    private static bool TrySet(FieldInfo f, object holder, SExpr sx)
    {
        var value = FromSExpr(f.FieldType, sx);
        if (value is null) return false;
        if (f.IsInitOnly && f.GetValue(holder) is Array existing && value is Array incoming)
        {
            if (existing.Length != incoming.Length) return false;         // a map of another size is not this map
            Array.Copy(incoming, existing, incoming.Length);              // the array stays where it is (others hold it); its contents come back
            return true;
        }
        f.SetValue(holder, value);
        return true;
    }

    private static object? FromSExpr(Type t, SExpr sx)
    {
        try
        {
            if (t == typeof(bool)) return sx is SBool b ? b.Value : null;
            if (t.IsEnum) return sx is SSymbol s && Enum.TryParse(t, s.Name, out var e) ? e : null;
            if (t == typeof(double)) return ReadNumber(sx);
            if (t == typeof(float)) return ReadNumber(sx) is { } f ? (float)f : null;
            if (t == typeof(int)) return sx is SNumber n2 ? (int)Math.Round(n2.Value) : null;
            if (t == typeof(long)) return sx is SNumber n3 ? (long)Math.Round(n3.Value) : null;
            if (t == typeof(short)) return sx is SNumber n4 ? (short)Math.Round(n4.Value) : null;
            if (t == typeof(byte)) return sx is SNumber n5 ? (byte)Math.Round(n5.Value) : null;
            if (t == typeof(uint)) return sx is SNumber n6 ? (uint)Math.Round(n6.Value) : null;
            if (t == typeof(char)) return sx is SNumber n7 ? (char)Math.Round(n7.Value) : null;
            if (t == typeof(decimal)) return sx is SNumber n8 ? (decimal)n8.Value : null;
            if (t.IsArray)
            {
                if (sx is not SList l) return null;
                var element = t.GetElementType()!;
                var array = Array.CreateInstance(element, l.Items.Count);
                for (int i = 0; i < l.Items.Count; i++)
                {
                    var item = FromSExpr(element, l.Items[i]);
                    if (item is null) return null;
                    array.SetValue(item, i);
                }
                return array;
            }
            if (t.IsValueType)
            {
                if (sx is not SList l) return null;
                var fs = t.GetFields(Fields);
                if (fs.Length != l.Items.Count) return null;
                object boxed = Activator.CreateInstance(t)!;
                for (int i = 0; i < fs.Length; i++)
                {
                    var item = FromSExpr(fs[i].FieldType, l.Items[i]);
                    if (item is null) return null;
                    fs[i].SetValue(boxed, item);
                }
                return boxed;
            }
        }
        catch (Exception e) when (e is OverflowException or InvalidCastException or ArgumentException) { }
        return null;
    }
}
