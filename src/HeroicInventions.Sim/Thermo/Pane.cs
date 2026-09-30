using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Thermo;

/// <summary>
/// Glass panes in an enclosure's wall (issue #57): <see cref="Count"/> square
/// panes of side <see cref="Side"/> and <see cref="Thickness"/>, in a frame
/// facing <see cref="Facing"/>.
///
/// Light: the habitat's membranes are opaque, so glass is the only way light
/// gets in. A pane passes its glass's transmittance τ of the sun's beam on it,
/// τ·DNI·cos(incidence)·area, which warms the room (the glass holds the
/// room's own warmth in: the room loses heat only through its walls' UA), so
/// a glazed room settles at T_out + τ·I·A_glass/UA.
///
/// Strength: a square pane of side a and thickness t, simply supported in
/// its frame, under a pressure difference q, carries a peak bending stress
/// σ ≈ 0.29·q·(a/t)² (Roark). Past <see cref="Strength"/> (a design stress
/// of about 7 MPa for glass) it cracks, and so do all its identical fellows,
/// which carry the same stress: the enclosure then leaks through the holes.
/// </summary>
public sealed class Pane(string name, Enclosure room, Sun sun, double side, double thickness, int count, double transmittance, Vec3 facing)
{
    public const double RoarkCoefficient = 0.29;
    public const double DefaultStrength = 7e6;   // Pa, a design stress for glass

    public string Name { get; } = name;
    public Enclosure Room { get; } = room;
    public Sun Sun { get; } = sun;
    public double Side { get; } = side;                  // m
    public double Thickness { get; } = thickness;        // m
    public int Count { get; } = count;
    public double Transmittance { get; } = transmittance;
    /// <summary>A unit vector out of the room, the way the glass faces (up: a roof).</summary>
    public Vec3 Facing { get; } = facing;
    public double Strength { get; set; } = DefaultStrength;
    public bool Cracked { get; private set; }
    public double CrackTime { get; private set; } = double.NaN;
    private double _time;

    public double Area => Count * Side * Side;
    /// <summary>Pa: the pressure difference across it.</summary>
    public double Load => Math.Abs(Room.Pressure - Room.Outside.Pressure);
    /// <summary>Pa: its peak bending stress, 0.29·q·(a/t)².</summary>
    public double Stress => RoarkCoefficient * Load * Math.Pow(Side / Thickness, 2);
    /// <summary>Pa of pressure difference at which it cracks.</summary>
    public double CrackPressure => Strength / (RoarkCoefficient * Math.Pow(Side / Thickness, 2));

    /// <summary>cos of the sun's angle to the glass's face (0 with the sun behind it or down).</summary>
    public double Incidence
    {
        get
        {
            if (Sun.Elevation <= 0) return 0;
            var s = Sun.Direction;
            return Math.Max(0, s.X * Facing.X + s.Y * Facing.Y + s.Z * Facing.Z);
        }
    }

    /// <summary>W of sunlight through it into the room: τ·DNI·cos·area (a cracked pane's hole lets it all in).</summary>
    public double Gain => (Cracked ? 1 : Transmittance) * Sun.DirectNormal * Incidence * Area;

    public void Step(double dt)
    {
        _time += dt;
        if (Cracked || Stress <= Strength) return;
        Cracked = true;
        CrackTime = _time;
        Room.LeakArea += Area;
    }

    /// <summary>The direction a face named up, down, north, south, east or west looks (north −Z, east +X).</summary>
    public static Vec3 FacingNamed(string name) => name switch
    {
        "up" => new(0, 1, 0), "down" => new(0, -1, 0),
        "north" => new(0, 0, -1), "south" => new(0, 0, 1),
        "east" => new(1, 0, 0), "west" => new(-1, 0, 0),
        _ => throw new ArgumentException($"a pane faces up, down, north, south, east or west, not {name}"),
    };
}
