using Godot;
using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>
/// A float valve: the feed runs on past the tank's wall in a bronze spout
/// that turns down and ends in a seat, a hand's breadth above the level
/// the valve holds. A bronze float rides on the water under it, carrying a
/// rod and a conical plug up into the seat, so the plug sits in its seat
/// when the water stands at the shut level and hangs clear below it as the
/// level falls — the gap you see is the gap the water comes through, and
/// the stream falling from the seat onto the float thickens and thins
/// with the flow.
/// </summary>
public partial class MachineView
{
    private readonly List<(FloatValve valve, Func<double> flow, Node3D rider, MeshInstance3D stream, float seatY)> _floatValveViews = [];
    private const float FloatHeight = 0.04f;

    private void BuildFloatValves()
    {
        foreach (var (id, (valve, flow)) in Runtime.FloatValves)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            var tankPart = Runtime.Def.Part(valve.Tank.Name)!;
            float side = Mathf.Sqrt((float)tankPart.Number("area"));
            float floor = (float)valve.Tank.BaseElevation, top = floor + (float)tankPart.Number("height");
            float shutY = floor + (float)valve.ShutLevel;
            float floatR = Mathf.Min(0.07f, side * 0.18f);
            float rod = Mathf.Clamp(top - 0.03f - shutY - FloatHeight / 2, 0.03f, 0.12f);
            float seatY = shutY + FloatHeight / 2 + rod;
            var bronze = Surface(part.Material);

            // the feed's mouth: where its trough meets the wall, or the tank's top for a pipe
            var on = part.Symbol("on", "");
            var trough = _troughs.LastOrDefault(t => (t.source is not null && Runtime.Sources.GetValueOrDefault(on) == t.source)
                                                   || (t.channel is not null && Runtime.Channels.GetValueOrDefault(on) == t.channel)).trough;
            Vector3 at, mouth;
            if (trough is not null)
            {
                at = trough.FallAt + trough.Along * (floatR + 0.01f);
                mouth = new Vector3(trough.FallAt.X, trough.FallTop, trough.FallAt.Z) - trough.Along * 0.05f;
                trough.Into = null;   // it pours from the seat, drawn here, not off the trough's end
            }
            else
            {
                at = new Vector3((float)tankPart.At.X, 0, (float)tankPart.At.Z);
                mouth = at with { Y = top + 0.1f };
            }
            var elbow = new Vector3(at.X, mouth.Y, at.Z);
            if (mouth.DistanceTo(elbow) > 1e-3f) AddChild(Shapes.Rod(mouth, elbow, 0.018f, bronze));
            AddChild(Shapes.Rod(elbow, new Vector3(at.X, seatY, at.Z), 0.018f, bronze));
            var seat = Shapes.Cylinder(0.03f, 0.02f, bronze);
            seat.Position = new Vector3(at.X, seatY + 0.01f, at.Z);
            AddChild(seat);

            // the float, its rod and plug, moved as one with the water
            var rider = new Node3D { Position = new Vector3(at.X, shutY, at.Z) };
            AddChild(rider);
            rider.AddChild(Shapes.Cylinder(floatR, FloatHeight, bronze));
            const float plugH = 0.04f;
            rider.AddChild(Shapes.Rod(new Vector3(0, FloatHeight / 2, 0), new Vector3(0, FloatHeight / 2 + rod - plugH, 0), 0.005f, bronze));
            var plug = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.002f, BottomRadius = 0.022f, Height = plugH }, MaterialOverride = bronze };
            plug.Position = new Vector3(0, FloatHeight / 2 + rod - plugH / 2, 0);
            rider.AddChild(plug);

            var stream = Shapes.Cylinder(1, 1, Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.7f));
            stream.Visible = false;
            AddChild(stream);
            _floatValveViews.Add((valve, flow, rider, stream, seatY));
            AddLabel(id, new Vector3(at.X, seatY + 0.3f, at.Z));
        }
    }

    private void DrawFloatValves()
    {
        foreach (var (valve, flow, rider, stream, seatY) in _floatValveViews)
        {
            float surface = (float)valve.Tank.SurfaceElevation;
            rider.Position = rider.Position with { Y = surface };
            double q = flow();
            stream.Visible = q > 1e-6;
            if (!stream.Visible) continue;
            float r = Mathf.Clamp(Mathf.Sqrt((float)q) * 0.5f, 0.003f, 0.03f), h = Mathf.Max(0.001f, seatY - surface);
            stream.Scale = new Vector3(r, h, r);
            stream.Position = rider.Position with { Y = surface + h / 2 };
        }
    }
}
