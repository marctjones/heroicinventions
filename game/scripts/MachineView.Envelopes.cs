using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Hot-air envelopes (issue #111): a paper lantern standing on its mouth, a rigid body in Jolt with the weight of its
/// skin and burner, lifted each tick by (ρ_out − ρ_in)·g·V from the sim's two air densities and slowed by the same air
/// drag as any body that names a #:drag-coefficient. The state shows in the scene: the paper glows warmer as the air
/// inside heats (nothing at the air's own temperature, full amber at 50 K above it), the burner shows a flame while it
/// burns, and a label gives the temperature inside and the lift against the weight, so you can watch it come to the
/// point where the one passes the other.
/// </summary>
public partial class MachineView
{
    private sealed record EnvelopeView(Envelope Envelope, RigidBody3D Body, StandardMaterial3D Paper, MeshInstance3D Flame, StandardMaterial3D FlameMat, Label3D Label);
    private readonly List<EnvelopeView> _envelopeViews = [];
    private const float PaperGlowKelvin = 50;   // the warmth over the air at which the paper is fully lit

    private void BuildEnvelopes()
    {
        foreach (var (id, env) in Runtime.Envelopes)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            float h = (float)env.Height, r = (float)env.Radius;
            var body = new RigidBody3D
            {
                Name = id,
                Mass = (float)env.Mass,
                Position = V(part.At) + new Vector3(0, h / 2, 0),
                Freeze = true,
                CanSleep = false,                       // it sits cold for half a minute and then must answer a force
                ContinuousCd = true,
                LinearDampMode = RigidBody3D.DampMode.Replace, LinearDamp = 0,       // nothing slows it but the air's drag
                AngularDampMode = RigidBody3D.DampMode.Replace, AngularDamp = 0.5f,
                PhysicsMaterialOverride = ContactFor(part.Material),
            };
            body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = r, Height = h } });

            // the paper: pale, and lit from inside as it warms
            var paper = Shapes.Mat(Shapes.ColorFor(part.Material).Lightened(0.35f), roughness: 0.9f, alpha: 0.8f);
            paper.EmissionEnabled = true;
            paper.EmissionEnergyMultiplier = 0;
            paper.CullMode = BaseMaterial3D.CullModeEnum.Disabled;   // the inside shows through the mouth
            body.AddChild(new MeshInstance3D { Mesh = LanternMesh(r, h), MaterialOverride = paper });
            AddLanternRibs(body, r, h);

            // the burner: a small dark dish at the mouth, and the flame over it
            var dish = Shapes.Cylinder(r * 0.22f, 0.03f, Shapes.Mat(new Color(0.3f, 0.25f, 0.2f), metallic: 0.3f));
            dish.Position = new Vector3(0, -h / 2 + 0.05f, 0);
            body.AddChild(dish);
            var flameMat = Shapes.Mat(new Color(1f, 0.75f, 0.3f), outline: false);
            flameMat.EmissionEnabled = true;
            flameMat.Emission = new Color(1f, 0.6f, 0.15f);
            flameMat.EmissionEnergyMultiplier = 3;
            var flame = Shapes.Sphere(0.04f, flameMat);
            flame.Position = new Vector3(0, -h / 2 + 0.11f, 0);
            flame.Scale = new Vector3(1, 1.8f, 1);
            body.AddChild(flame);

            var label = new Label3D
            {
                Position = new Vector3(0, h / 2 + 0.3f, 0), FontSize = 28, OutlineSize = 8, PixelSize = 0.0065f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            };
            body.AddChild(label);
            AddChild(body);
            _freezable.Add(body);
            _bodiesById[id] = body;
            // drag on the footprint the air meets as it climbs: π r² = V / h, as a box (s, h, s) with s² = π r²
            float side = r * Mathf.Sqrt(Mathf.Pi);
            RegisterDrag(part, body, new Vector3(side, h, side), false);
            _envelopeViews.Add(new EnvelopeView(env, body, paper, flame, flameMat, label));
        }
    }

    /// <summary>Radius (as a share of the lantern's) at each height (as a share of its height from the mouth): a paper lantern, narrow at the mouth, fullest low, rounding in to a small top.</summary>
    private static readonly (float Up, float Out)[] LanternProfile = [(0f, 0.55f), (0.1f, 0.84f), (0.3f, 1f), (0.55f, 0.97f), (0.78f, 0.76f), (0.93f, 0.46f), (1f, 0.3f)];

    /// <summary>The paper skin as a surface of revolution about the lantern's axis, open at the mouth (#176), centred on the body.</summary>
    private static ArrayMesh LanternMesh(float r, float h)
    {
        const int around = 28;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 At(int ring, int k) =>
            new(r * LanternProfile[ring].Out * Mathf.Cos(Mathf.Tau * k / around), -h / 2 + h * LanternProfile[ring].Up, r * LanternProfile[ring].Out * Mathf.Sin(Mathf.Tau * k / around));
        for (int ring = 0; ring + 1 < LanternProfile.Length; ring++)
            for (int k = 0; k < around; k++)
            {
                var a = At(ring, k); var b = At(ring, k + 1); var c = At(ring + 1, k + 1); var d = At(ring + 1, k);
                st.AddVertex(a); st.AddVertex(c); st.AddVertex(b);
                st.AddVertex(a); st.AddVertex(d); st.AddVertex(c);
            }
        var cap = new Vector3(0, h / 2, 0);   // the closed top
        for (int k = 0; k < around; k++)
        {
            st.AddVertex(At(LanternProfile.Length - 1, k)); st.AddVertex(cap); st.AddVertex(At(LanternProfile.Length - 1, k + 1));
        }
        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>The frame a paper lantern is pasted over: a hoop at the mouth, one at the belly and one at the shoulder, and eight ribs between them.</summary>
    private void AddLanternRibs(Node3D body, float r, float h)
    {
        var cane = Shapes.Mat(new Color(0.45f, 0.3f, 0.15f), roughness: 0.9f);
        foreach (int ring in new[] { 0, 2, 4 })
        {
            var hoop = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = r * LanternProfile[ring].Out - 0.012f, OuterRadius = r * LanternProfile[ring].Out + 0.012f, Rings = 24, RingSegments = 6 },
                MaterialOverride = cane, Position = new Vector3(0, -h / 2 + h * LanternProfile[ring].Up, 0),
            };
            body.AddChild(hoop);
        }
        for (int k = 0; k < 8; k++)
        {
            float a = Mathf.Tau * k / 8;
            for (int ring = 0; ring + 1 < LanternProfile.Length; ring++)
            {
                Vector3 P(int i) => new(r * LanternProfile[i].Out * 1.005f * Mathf.Cos(a), -h / 2 + h * LanternProfile[i].Up, r * LanternProfile[i].Out * 1.005f * Mathf.Sin(a));
                body.AddChild(Shapes.Rod(P(ring), P(ring + 1), 0.006f, cane));
            }
        }
    }

    /// <summary>Each tick, before the physics step: the net lift of the air outside over the air inside, on every free envelope.</summary>
    private void ApplyLift()
    {
        foreach (var v in _envelopeViews)
        {
            if (!IsInstanceValid(v.Body) || v.Body.Freeze) continue;
            // the warm air's own weight is not in the body's mass, so what acts is the buoyancy less that weight, (ρ_out − ρ_in) g V
            v.Body.ApplyCentralForce(new Vector3(0, (float)v.Envelope.Lift, 0));
        }
    }

    private void DrawEnvelopes()
    {
        foreach (var v in _envelopeViews)
        {
            var env = v.Envelope;
            float warm = Mathf.Clamp((float)(env.Temperature - env.Zone.Temperature) / PaperGlowKelvin, 0, 1);
            v.Paper.Emission = new Color(1f, 0.55f, 0.18f);
            v.Paper.EmissionEnergyMultiplier = warm * 1.6f;
            v.FlameMat.EmissionEnergyMultiplier = 3;
            v.Flame.Visible = env.Lit;
            v.Flame.Scale = new Vector3(1, 1.8f * (0.9f + 0.1f * Mathf.Sin((float)Runtime.Time * 17f)), 1);
            v.Label.Text = $"{env.Temperature:0.0} °C inside\nlift {env.Lift:0.00} / weight {env.Weight:0.00} N";
        }
    }
}
