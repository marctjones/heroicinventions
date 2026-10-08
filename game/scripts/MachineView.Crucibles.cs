using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// Crucibles (issue #56): a stone bowl of sand whose surface shows its heat
/// the way hot things do, dark until about 500 °C, then a dull red, orange
/// and at last yellow-white; and as it melts the sand turns to glass, dark
/// for basalt, clear for silica, from the middle out.
/// </summary>
public partial class MachineView
{
    private readonly List<(Crucible Pot, StandardMaterial3D Sand, MeshInstance3D Glass, StandardMaterial3D GlassMat, float Radius)> _crucibleViews = [];

    private void BuildCrucibles()
    {
        foreach (var (id, pot) in Runtime.Crucibles)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            // the bowl: a charge of sand about 1,500 kg/m³, 15 cm deep, wide enough to hold it
            float radius = Mathf.Clamp(Mathf.Sqrt((float)(pot.Charge / 1500 / 0.15) / Mathf.Pi), 0.08f, 0.6f);
            var bowl = Shapes.Cylinder(radius * 1.25f, 0.2f, Surface(part.Material));
            bowl.Position = V(part.At) + new Vector3(0, 0.1f, 0);
            AddChild(bowl);
            var sandMat = Shapes.Mat(pot.Sand.Name == "silica" ? new Color(0.9f, 0.88f, 0.8f) : new Color(0.3f, 0.26f, 0.24f), roughness: 1);
            sandMat.EmissionEnabled = true;
            var sand = Shapes.Cylinder(radius, 0.02f, sandMat);
            sand.Position = V(part.At) + new Vector3(0, 0.205f, 0);
            AddChild(sand);
            var glassMat = Shapes.Mat(pot.Sand.Name == "silica" ? new Color(0.85f, 0.95f, 1f) : new Color(0.08f, 0.07f, 0.07f),
                                      metallic: 0.2f, roughness: 0.05f, alpha: pot.Sand.Name == "silica" ? 0.5f : 0.95f);
            glassMat.EmissionEnabled = true;
            var glass = Shapes.Cylinder(radius, 0.025f, glassMat);
            glass.Position = sand.Position + new Vector3(0, 0.005f, 0);
            glass.Visible = false;
            AddChild(glass);
            AddLabel(id, V(part.At) + new Vector3(0, 0.45f, 0));
            _crucibleViews.Add((pot, sandMat, glass, glassMat, radius));
        }
    }

    /// <summary>The colour and strength of a surface glowing at <paramref name="celsius"/>: nothing below 500 °C.</summary>
    private void DrawCrucibles()
    {
        foreach (var (pot, sandMat, glass, glassMat, radius) in _crucibleViews)
        {
            Skins.Glow(sandMat, pot.Temperature);
            Skins.Glow(glassMat, pot.Temperature);
            double share = pot.Charge > 0 ? pot.Melted / pot.Charge : 0;
            glass.Visible = share > 0;
            float r = radius * Mathf.Sqrt((float)share);                 // the melt spreads from the middle out
            glass.Scale = new Vector3(r / radius, 1, r / radius);
        }
    }
}
