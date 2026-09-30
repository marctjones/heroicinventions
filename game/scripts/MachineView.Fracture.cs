using Godot;
using HeroicInventions.Sim;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// Fracture (issue #43): a post built #:breakable snaps when a blow bends it
/// past its strength. Each strike on it (#27's impact records) is taken as a
/// mass m — the striker's effective mass along the blow, so a swinging ram's
/// is its moment of inertia over its lever arm squared — arriving at speed v
/// at height h above the post's foot. The post bends like a cantilever
/// spring, k = 3 E I / h³, and stops the blow with a peak force
/// F = v √(k m); the moment F h at its foot bends it to a stress
/// σ = F h c / I. Past its material's tensile strength (along the grain, for
/// a wooden post: its fibres run up it) it breaks at the foot.
///
/// Jolt has already bounced the striker off the post in that step, so a
/// break gives it back its velocity from before the blow, less what the
/// bending took: the strain energy at the moment of breaking, F_b² / 2k with
/// F_b the force that reaches the strength. That energy is the fracture's
/// (splintering, sound, heat). The post above the break becomes loose
/// pieces: the shaft and a few splinters for wood, three blocky shards for
/// stone, left standing where they were for the striker to carry on into.
/// </summary>
public partial class MachineView
{
    private sealed class BreakablePost
    {
        public required PartSpec Part;
        public required StaticBody3D Body;
        public required Label3D Label;
        public bool Broken;
        public float PeakStress, BreakSpeed, EnergyTaken;   // Pa, m/s, J
    }

    private readonly Dictionary<StaticBody3D, BreakablePost> _breakable = [];

    private void RegisterBreakable(PartSpec part, StaticBody3D body)
    {
        if (part.Props.GetValueOrDefault("breakable") is not SBool { Value: true }) return;
        float h = (float)part.Number("size-y");
        var label = new Label3D
        {
            Position = V(part.At) + new Vector3(0, h + 0.3f, 0),
            FontSize = 24, OutlineSize = 6, PixelSize = 0.003f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
        };
        AddChild(label);
        _breakable[body] = new BreakablePost { Part = part, Body = body, Label = label };
    }

    private void BuildFracture()
    {
        if (_breakable.Count > 0) Struck += StrikePost;
    }

    private void StrikePost(Impact impact)
    {
        if (impact.Other is not StaticBody3D sb || !_breakable.TryGetValue(sb, out var post) || post.Broken) return;
        var striker = impact.Body;
        var across = impact.Normal with { Y = 0 };
        if (across.Length() < 0.3f) return;          // landing on its top, not a blow from the side
        var dir = across.Normalized();
        float v = impact.Speed * across.Length();

        var part = post.Part;
        var mat = _materials[part.Material];
        float sx = (float)part.Number("size-x"), sy = (float)part.Number("size-y"), sz = (float)part.Number("size-z");
        bool round = part.Props.GetValueOrDefault("round") is SBool { Value: true };
        var foot = V(part.At);
        float h = Mathf.Clamp(impact.Point.Y - foot.Y, 0.01f, sy);
        // the section's second moment and outer fibre, bending away from the blow
        double I = round ? Math.PI * Math.Pow(sx, 4) / 64
                         : sz * Math.Pow(sx, 3) / 12 * dir.X * dir.X + sx * Math.Pow(sz, 3) / 12 * dir.Z * dir.Z;
        double c = round ? sx / 2 : sx / 2 * Mathf.Abs(dir.X) + sz / 2 * Mathf.Abs(dir.Z);
        double k = 3 * mat.YoungsModulus * 1e9 * I / Math.Pow(h, 3);
        double inverseMass = InverseMassAlong(striker, impact.Point, dir);
        if (inverseMass <= 0) return;
        double m = 1 / inverseMass;
        double force = v * Math.Sqrt(k * m);
        double stress = force * h * c / I;
        post.PeakStress = Mathf.Max(post.PeakStress, (float)stress);
        double strength = mat.TensileStrength * 1e6;
        if (stress <= strength) return;

        // it breaks: the striker goes on with what the bending didn't take
        double breakingForce = strength * I / (c * h);
        double taken = breakingForce * breakingForce / (2 * k);
        var (vin, win) = IntoCollision(striker);
        double before = KineticEnergy(striker, vin, win);
        float keep = (float)Math.Sqrt(Math.Max(0, 1 - taken / before));
        striker.LinearVelocity = vin * keep;
        striker.AngularVelocity = win * keep;
        post.Broken = true;
        post.BreakSpeed = v;
        post.EnergyTaken = (float)Math.Min(taken, before);
        _impactOverride = impact with { EnergyLost = post.EnergyTaken };   // the strike took the break's energy, not a bounce's
        GD.Print($"{part.Id} broke: {stress / 1e6:F0} MPa against {mat.TensileStrength:F0}, struck at {v:F2} m/s; the break took {post.EnergyTaken:F0} J");
        Shatter(post, sx, sy, sz, round);
    }

    /// <summary>Swaps the standing post for a stump and loose pieces.</summary>
    private void Shatter(BreakablePost post, float sx, float sy, float sz, bool round)
    {
        var part = post.Part;
        var foot = V(part.At);
        var mat = _materials[part.Material];
        float stump = Mathf.Min(0.05f, sy * 0.1f);
        // out of the world now, not at the end of the frame: the striker
        // would meet it again in this very step and bounce back
        RemoveChild(post.Body);
        post.Body.QueueFree();
        var stub = new StaticBody3D { Position = foot + new Vector3(0, stump / 2, 0), PhysicsMaterialOverride = new PhysicsMaterial { Friction = (float)mat.Friction } };
        stub.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(sx, stump, sz) } });
        stub.AddChild(Shapes.Box(new Vector3(sx, stump, sz), Surface(part.Material)));
        AddChild(stub);

        var pieces = new List<(Vector3 Size, Vector3 Centre)>();
        float rest = sy - stump;
        if (mat.Category == HeroicInventions.Sim.Materials.MaterialCategory.Wood)
        {
            // the shaft, and splinters torn from its foot
            pieces.Add((new Vector3(sx * 0.8f, rest, sz * 0.8f), foot + new Vector3(0, stump + rest / 2, 0)));
            foreach (var (dx, dz) in new[] { (0.45f, 0f), (-0.45f, 0f), (0f, 0.45f) })
                pieces.Add((new Vector3(sx * 0.1f, rest * 0.3f, sz * 0.1f), foot + new Vector3(dx * sx, stump + rest * 0.15f, dz * sz)));
        }
        else
        {
            // blocky shards, the height split in three
            for (int i = 0; i < 3; i++)
                pieces.Add((new Vector3(sx * 0.95f, rest / 3 * 0.95f, sz * 0.95f), foot + new Vector3(0, stump + rest / 3 * (i + 0.5f), 0)));
        }
        int n = 0;
        foreach (var (size, centre) in pieces)
        {
            var piece = new MaterialBlock(mat, size, Shapes.ColorFor(part.Material))
            {
                Name = $"{part.Id}-piece-{++n}", Position = centre,
                // it must feel the ram as the ram feels it: in Godot's Jolt a body
                // whose mask misses the other's layer treats it as immovable,
                // and a 134 kg ram bounced off a 3 kg splinter
                CollisionMask = 1 | PendulumLayer | SprungArmLayer | AxleLayer,
            };
            AddChild(piece);
            _freezable.Add(piece);
            _bodiesById[piece.Name] = piece;
            piece.ContactMonitor = true;
            piece.MaxContactsReported = 8;
            _impactRecords[piece] = new ImpactRecord();
            _touching[piece] = [];
            // the post held this height as a fixture; loose, it counts, and the
            // machine's starting energy with it, or "energy retained" would jump
            _initialMechanicalEnergy += piece.Mass * Physics.Gravity * centre.Y;
        }
    }

    private void DrawFracture()
    {
        foreach (var post in _breakable.Values)
        {
            double strength = _materials[post.Part.Material].TensileStrength;
            post.Label.Text = post.Broken
                ? $"{post.Part.Id}: broke at {post.BreakSpeed:0.00} m/s, {post.PeakStress / 1e6:0} MPa of {strength:0}"
                : post.PeakStress > 0 ? $"{post.Part.Id}: {post.PeakStress / 1e6:0} MPa of {strength:0}" : "";
        }
    }
}
