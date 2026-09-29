using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// One standard part from racket/heroic/geometry/catalogue.rkt, as
/// racket/build.rkt writes it to game/meshes/catalogue/catalogue.rktd: a
/// pre-generated mesh (a gear, screw, pulley, drum, treadwheel, noria or
/// catapult frame) with the exact numbers a `wheel`, `screw` or `fixture`
/// part needs — see emit.rkt's prop->items, which is where a machine's own
/// shaped parts get the same fields from.
/// </summary>
public sealed record CatalogueEntry(
    string Id,
    string Description,
    string ShapeKind,   // gear | pulley | drum | treadwheel | noria | screw | catapult-frame
    string MeshStem,
    double Volume,
    Vec3 Inertia,        // per unit density, about local x/y/z (see shape.rkt)
    IReadOnlyDictionary<string, SExpr> ShapeProps)
{
    /// <summary>Wheels ride on an axle and turn; screws lift; everything else is a fixed fixture — see shape.rkt's wheel-kinds.</summary>
    private static readonly HashSet<string> WheelKinds = ["gear", "pulley", "drum", "treadwheel", "noria"];

    /// <summary>The part clause kind (wheel/screw/fixture) this entry becomes when placed — see machine.rkt's shaped-part.</summary>
    public string PartKind => ShapeKind switch
    {
        "screw" => "screw",
        _ when WheelKinds.Contains(ShapeKind) => "wheel",
        _ => "fixture",
    };
}

public static class CatalogueReader
{
    /// <summary>
    /// Parses a catalogue.rktd file: one (entry id description kind mesh-stem
    /// volume (ix iy iz) ((prop value) ...)) form per catalogue part. Returns
    /// an empty list, not an error, when the catalogue hasn't been built
    /// (game/meshes/catalogue/ is gitignored — racket/build.rkt regenerates
    /// it) — the editor still offers the primitive part kinds without it.
    /// </summary>
    public static IReadOnlyList<CatalogueEntry> Parse(string text)
    {
        var forms = SExprReader.ReadAll(text);
        var root = forms.OfType<SList>().FirstOrDefault();
        if (root is null) return [];

        var entries = new List<CatalogueEntry>();
        foreach (var e in root.Items.OfType<SList>().Where(l => l.Head == "entry"))
        {
            string id = ((SSymbol)e.Items[1]).Name;
            string description = ((SString)e.Items[2]).Value;
            string kind = ((SSymbol)e.Items[3]).Name;
            string mesh = ((SString)e.Items[4]).Value;
            double volume = ((SNumber)e.Items[5]).Value;
            var inertia = (SList)e.Items[6];
            var vec = new Vec3(((SNumber)inertia.Items[0]).Value, ((SNumber)inertia.Items[1]).Value, ((SNumber)inertia.Items[2]).Value);
            var props = new Dictionary<string, SExpr>();
            if (e.Items.Count > 7 && e.Items[7] is SList propList)
                foreach (var kv in propList.Items.OfType<SList>())
                    props[((SSymbol)kv.Items[0]).Name] = kv.Items[1];
            entries.Add(new CatalogueEntry(id, description, kind, mesh, volume, vec, props));
        }
        return entries;
    }

    public static IReadOnlyList<CatalogueEntry> ReadFile(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : [];
}
