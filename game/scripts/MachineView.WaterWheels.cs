using Godot;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions;

/// <summary>
/// A water wheel, turned by the sim (<see cref="WaterWheel"/>) rather
/// than by Jolt: its axle runs along Z, the water runs along X. Fed from
/// above (overshot) it turns clockwise seen from +Z, the loaded buckets
/// going down the far side; pushed at the bottom by a race running +X
/// (undershot) it turns the other way. Buckets on the loaded arc show the
/// water in them; a millstone housing stands on the axle's end.
/// </summary>
public partial class MachineView
{
    private readonly List<(WaterWheel wheel, Node3D node, float sense, List<(Node3D bucket, MeshInstance3D water, float angle)> buckets)> _waterWheels = [];

    private void BuildWaterWheel(PartSpec part)
    {
        var wheel = Runtime.WaterWheels[part.Id];
        var wood = Surface(part.Material);
        var stone = Shapes.Mat(Shapes.Stone, roughness: 0.9f);
        var axle = V(part.At);
        float r = (float)wheel.Radius, w = (float)wheel.Width;
        bool overshot = wheel.Buckets > 0;

        // axle posts either side, and the millstone housing on the far one
        foreach (float side in new[] { -1f, 1f })
        {
            float z = side * (w / 2 + 0.15f);
            var post = Shapes.Box(new Vector3(0.15f, axle.Y, 0.15f), stone);
            post.Position = new Vector3(axle.X, axle.Y / 2, axle.Z + z);
            AddChild(post);
        }
        var housing = Shapes.Cylinder(0.35f, 0.25f, stone);
        housing.Position = new Vector3(axle.X, 0.125f, axle.Z + w / 2 + 0.6f);
        AddChild(housing);
        AddChild(Shapes.Rod(axle + new Vector3(0, 0, -w / 2 - 0.25f), axle + new Vector3(0, 0, w / 2 + 0.6f), 0.05f, wood));

        var node = new Node3D { Position = axle };
        AddChild(node);
        foreach (float side in new[] { -1f, 1f })
        {
            // a rim of short boards, and eight spokes
            int boards = 24;
            for (int i = 0; i < boards; i++)
            {
                float a = i * Mathf.Tau / boards;
                var board = Shapes.Box(new Vector3(Mathf.Tau * r / boards * 1.05f, 0.08f, 0.04f), wood);
                board.Position = new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r, side * w / 2);
                board.Rotation = new Vector3(0, 0, -a);
                node.AddChild(board);
            }
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.Tau / 8;
                node.AddChild(Shapes.Rod(new Vector3(0, 0, side * w / 2), new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r, side * w / 2), 0.03f, wood));
            }
        }

        var buckets = new List<(Node3D, MeshInstance3D, float)>();
        int n = overshot ? wheel.Buckets : 12;
        float depth = overshot ? Mathf.Clamp(Mathf.Sqrt((float)wheel.BucketVolume / w), 0.1f, 0.4f) : (float)wheel.PaddleDepth;
        var water = Shapes.Mat(Shapes.Water, roughness: 0.2f, alpha: 0.8f);
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.Tau / n;
            var holder = new Node3D { Position = new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r, 0), Rotation = new Vector3(0, 0, -a) };
            node.AddChild(holder);
            if (overshot)
            {
                // a trough open to the rim's outside: a floor and a back board
                var floor = Shapes.Box(new Vector3(0.03f, depth, w), wood);
                floor.Position = new Vector3(-depth / 3, depth / 2 - 0.04f, 0);
                holder.AddChild(floor);
                var fill = Shapes.Box(new Vector3(depth * 0.6f, 1, w * 0.95f), water);
                fill.Visible = false;
                holder.AddChild(fill);
                buckets.Add((holder, fill, a));
            }
            else
            {
                var paddle = Shapes.Box(new Vector3(0.04f, depth, w), wood);
                paddle.Position = new Vector3(0, depth / 2, 0);
                holder.AddChild(paddle);
            }
        }
        _waterWheels.Add((wheel, node, overshot ? -1 : 1, buckets));
        AddLabel(part.Id, axle + new Vector3(0, r + 0.35f, 0));
    }

    private void DrawWaterWheels()
    {
        foreach (var (wheel, node, sense, buckets) in _waterWheels)
        {
            node.Rotation = new Vector3(0, 0, sense * (float)wheel.Angle);
            // the loaded arc: buckets between the top and the spill point, on the descending side
            float full = wheel.Capacity > 0 ? Mathf.Clamp((float)(wheel.Water / wheel.Capacity), 0, 1) : 0;
            foreach (var (_, fill, a) in buckets)
            {
                float fromTop = Mathf.PosMod(a + (float)wheel.Angle, Mathf.Tau);   // turning down the +X side
                bool loaded = full > 0.01f && fromTop <= (float)wheel.SpillAngle;
                fill.Visible = loaded;
                if (!loaded) continue;
                float h = 0.25f * full + 0.02f;
                fill.Scale = new Vector3(1, h, 1);
                fill.Position = new Vector3(0.02f, h / 2, 0);
            }
        }
    }
}
