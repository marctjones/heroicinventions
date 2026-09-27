namespace HeroicInventions.Sim.Parts;

/// <summary>What a connection point on a part can join to.</summary>
public enum PortKind
{
    Axle,        // rotating shaft end; joins a Bearing or another Axle (coupling)
    Bearing,     // hole that an axle turns in
    GearMesh,    // toothed rim; joins another GearMesh
    PipeFitting, // water/air/steam connection; joins another PipeFitting
    RopeAnchor,  // where a rope can be tied or wound
}

public sealed record PortDef(string Id, PortKind Kind, double X, double Y, double Z);

/// <summary>
/// A part in the build palette: a shape, what it is made of, and the ports
/// that other parts can snap onto. Geometry lives in the engine layer; the
/// sim core only needs dimensions to compute mass and strength.
/// </summary>
public sealed record PartDef(string Id, string Name, string MaterialId, double Volume, IReadOnlyList<PortDef> Ports);

public sealed record PartInstance(Guid Id, PartDef Def);

public sealed record Connection(PartInstance A, string PortA, PartInstance B, string PortB);

/// <summary>The graph the player builds. Run mode turns it into joints and network edges.</summary>
public sealed class Assembly
{
    private static readonly HashSet<(PortKind, PortKind)> Compatible =
    [
        (PortKind.Axle, PortKind.Bearing),
        (PortKind.Axle, PortKind.Axle),
        (PortKind.GearMesh, PortKind.GearMesh),
        (PortKind.PipeFitting, PortKind.PipeFitting),
        (PortKind.RopeAnchor, PortKind.RopeAnchor),
    ];

    public List<PartInstance> Parts { get; } = [];
    public List<Connection> Connections { get; } = [];

    public PartInstance Add(PartDef def)
    {
        var p = new PartInstance(Guid.NewGuid(), def);
        Parts.Add(p);
        return p;
    }

    public static bool CanConnect(PortKind a, PortKind b) =>
        Compatible.Contains((a, b)) || Compatible.Contains((b, a));

    public Connection Connect(PartInstance a, string portA, PartInstance b, string portB)
    {
        var ka = a.Def.Ports.Single(p => p.Id == portA).Kind;
        var kb = b.Def.Ports.Single(p => p.Id == portB).Kind;
        if (!CanConnect(ka, kb))
            throw new InvalidOperationException($"Cannot connect {ka} to {kb}");
        var c = new Connection(a, portA, b, portB);
        Connections.Add(c);
        return c;
    }
}
