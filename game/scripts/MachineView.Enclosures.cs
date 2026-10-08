using Godot;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions;

/// <summary>
/// An enclosure (issue #39): a pale membrane room you can see into, with its
/// state on it, not only in the panel.
/// <list type="bullet">
/// <item>A pressure dial on its front wall (MachineView.Gauges.cs): gauge pressure
/// over the air outside, 270° to 100 kPa, so a punctured module's needle visibly sinks.</item>
/// <item>A membrane is only taut while the air inside pushes harder than the
/// air outside: as the gauge pressure falls below about 1 kPa the room
/// sags towards the floor, and empty it lies nearly flat.</item>
/// <item>Gas leaving through a hole hisses out as a plume from the +x wall,
/// as strong as the flow; gas drawn in shows none (it is going in).</item>
/// <item>Below freezing its walls frost; a heater inside glows while on.</item>
/// </list>
/// The room has no collision shape: parts stand inside it untouched.
/// </summary>
public partial class MachineView
{
    private sealed record EnclosureView(Enclosure Room, Node3D Walls, StandardMaterial3D Skin,
                                        GpuParticles3D Hiss, MeshInstance3D? Heater, Label3D Label, double FullFlow, float Height, bool Membrane, Color Wall);
    private readonly List<EnclosureView> _enclosureViews = [];
    private static readonly Color Membrane = new(0.93f, 0.9f, 0.82f);

    private void BuildEnclosures()
    {
        foreach (var (id, room) in Runtime.Enclosures)
        {
            _building = id;
            var part = Runtime.Def.Part(id)!;
            float w = (float)part.Number("size-x"), h = (float)part.Number("size-y"), d = (float)part.Number("size-z");
            var at = V(part.At);

            // the walls, scaled about the floor so a slack room sags down onto it
            var walls = new Node3D { Position = at };
            AddChild(walls);
            var wall = part.Material == "hemp" ? Membrane : Shapes.ColorFor(part.Material);
            var skin = Shapes.Mat(wall, roughness: 0.9f, alpha: 0.22f);
            skin.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            var box = Shapes.Box(new Vector3(w, h, d), skin);
            box.Position = new Vector3(0, h / 2, 0);
            walls.AddChild(box);

            // its pressure dial is built with the others (MachineView.Gauges.cs), on this front wall and riding its billow

            var hiss = SteamCloud(at + new Vector3(w / 2 + 0.02f, h / 2, 0), amount: 60, radius: 0.02f, lifetime: 0.8f);
            hiss.RotationDegrees = new Vector3(0, 0, -90);   // out of the +x wall

            MeshInstance3D? heater = null;
            if (room.Heater > 0 || part.Number("heater", 0) > 0)
            {
                heater = Shapes.Box(new Vector3(0.3f, 0.08f, 0.3f), Shapes.Mat(new Color(0.3f, 0.25f, 0.22f), metallic: 0.5f));
                heater.Position = at + new Vector3(-w / 2 + 0.3f, 0.04f, d / 2 - 0.3f);
                AddChild(heater);
            }

            var label = new Label3D
            {
                Position = at + new Vector3(0, h + 0.25f, 0), FontSize = 28, OutlineSize = 8, PixelSize = 0.008f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true,
            };
            AddChild(label);
            // the flow a hole this size passes when the room is as full as it starts: what a full plume is
            double full = room.LeakArea > 0
                ? Enclosure.OrificeFlow(room.Cd, room.LeakArea, Math.Max(room.Pressure, room.Outside.Pressure),
                                        HeroicInventions.Sim.Physics.ToKelvin(room.Temperature), Math.Min(room.Pressure, room.Outside.Pressure), 1.4, 287)
                : 1;
                        // a fabric room stands only while blown up; one of wood, stone or metal keeps its shape
            _enclosureViews.Add(new EnclosureView(room, walls, skin, hiss, heater, label, Math.Max(1e-9, full), h,
                                                  part.Material == "hemp", wall));
        }
    }

    private void DrawEnclosures()
    {
        foreach (var v in _enclosureViews)
        {
            var room = v.Room;
            double p = room.Pressure, gauge = room.GaugePressure;
            // a membrane room stands for its gauge pressure (Skins.Billow): slack and nearly flat at nothing over the outside,
            // full height by 1 kPa, then billowing out to 8% as it fills. The needle is the dial's (Gauges.cs)
            if (v.Membrane) { var (up, out_) = Skins.Billow(gauge); v.Walls.Scale = new Vector3(out_, up, out_); }
            float frost = (float)Math.Clamp(-room.Temperature / 5, 0, 1);
            v.Skin.AlbedoColor = Skins.Warmed(v.Wall.Lerp(new Color(0.97f, 0.98f, 1f), frost), room.Temperature) with { A = 0.22f + 0.2f * frost };   // frost whitens it, warmth washes it (#169)
            v.Hiss.Emitting = room.Flow > 0;
            if (room.Flow > 0)
            {
                v.Hiss.AmountRatio = (float)Math.Clamp(room.Flow / v.FullFlow, 0.1, 1);
                if (v.Hiss.ProcessMaterial is ParticleProcessMaterial hm2)   // thrown as hard as the pressure difference pushes (Skins.JetSpeed)
                {
                    float speed = Skins.JetSpeed(room.Pressure - room.Outside.Pressure);
                    hm2.InitialVelocityMin = speed * 0.8f; hm2.InitialVelocityMax = speed * 1.2f;
                }
            }
            if (v.Heater?.MaterialOverride is StandardMaterial3D hm)
            {
                Skins.Warm(hm, room.Heater > 0 ? 1100 : room.Temperature);   // an unlit heater is as warm as the room
                Skins.Glow(hm, room.Heater > 0 ? 1100 : room.Temperature);   // a lit heater glows bright orange, as iron does at 1,100 °C
            }
            string o2 = room.TotalMoles > 0 ? $"{room.Moles[0] / room.TotalMoles * 100:0.#}% O₂" : "empty";
            if (room.TotalMoles > 0 && room.Moles[2] / room.TotalMoles > 0.01) o2 += $", {room.Moles[2] / room.TotalMoles * 100:0.#}% CO₂";
            v.Label.Text = $"{room.Name}: {p / 1000:0.##} kPa, {room.Temperature:0.#} °C, {o2}" +
                           (room.Flow > 0 ? $"\nleaking {room.Flow * 1000:0.##} g/s{(room.Choked ? " (choked)" : "")}" : "");
        }
    }
}
