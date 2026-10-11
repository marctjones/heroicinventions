using HeroicInventions.Sim.Machines;

namespace HeroicInventions.Sim.Editor;

/// <summary>
/// The live ratio (building plan, phase 3): for each prime mover in a design (a windmill, water wheel, jet wheel or Stirling engine), the
/// gear train its arbors and meshes reach (a mesh that doesn't turn its partner, not touching or jammed, is no step of it) and the ratio from it to the train's last gear, from the tooth counts (as the sim's
/// <see cref="MachineRuntime.TrainOf"/> and the gear view: a mesh turns its partner at teeth / teeth, an arbor at 1, the sense dropped).
/// Over the editor's document, so build mode shows it as the train is built. Belts are left out (a belt's ratio is its drums' radii).
/// </summary>
public static class TrainRatio
{
    /// <summary>The parts that drive a train.</summary>
    public static readonly IReadOnlyList<string> PrimeMoverKinds = ["windmill", "waterwheel", "jetwheel", "stirling"];

    /// <summary>
    /// A train from <paramref name="Mover"/> to <paramref name="Last"/>: the last gear turns <paramref name="Num"/>:<paramref name="Den"/>
    /// (lowest terms) times as fast as the mover, across <paramref name="Meshes"/> meshes.
    /// </summary>
    public sealed record Train(string Mover, string Last, long Num, long Den, int Meshes)
    {
        public double Ratio => (double)Num / Den;

        /// <summary>"256:1" for four 72:18 stages, "1:4" for one geared down, "3:2" for a 36 driving a 24.</summary>
        public string RatioText => $"{Num}:{Den}";

        /// <summary>"sails -> pinion_e: 256:1".</summary>
        public string Text => $"{Mover} -> {Last}: {RatioText}";
    }

    /// <summary>
    /// The trains in <paramref name="doc"/>, one for each prime mover whose train crosses at least one mesh. The last gear: the one most
    /// meshes from the mover; among those the fastest; among those the last reached.
    /// </summary>
    public static IReadOnlyList<Train> Of(EditorDocument doc)
    {
        var trains = new List<Train>();
        foreach (var mover in doc.Parts.Values.Where(p => PrimeMoverKinds.Contains(p.Kind)).OrderBy(p => p.Id, StringComparer.Ordinal))
            if (From(doc, mover.Id) is { } t) trains.Add(t);
        return trains;
    }

    /// <summary>The train from part <paramref name="start"/>, or null when it crosses no mesh.</summary>
    public static Train? From(EditorDocument doc, string start)
    {
        // ω_part / ω_start as num/den, and the meshes crossed to get there
        var seen = new Dictionary<string, (long Num, long Den, int Meshes)> { [start] = (1, 1, 0) };
        var order = new List<string> { start };
        var queue = new Queue<string>([start]);
        while (queue.Count > 0)
        {
            string here = queue.Dequeue();
            var (num, den, meshes) = seen[here];
            var next = doc.Arbors.Where(a => a.Parts.Contains(here)).SelectMany(a => a.Parts).Select(n => (n, 1L, 1L, 0))
                .Concat(doc.Meshes.Where(m => m.A == here && Turns(doc, m)).Select(m => (m.B, Size(doc, here), Size(doc, m.B), 1)))
                .Concat(doc.Meshes.Where(m => m.B == here && Turns(doc, m)).Select(m => (m.A, Size(doc, here), Size(doc, m.A), 1)));
            foreach (var (n, top, bottom, k) in next)
            {
                if (seen.ContainsKey(n)) continue;
                long nn = num * Math.Max(top, 1), dd = den * Math.Max(bottom, 1);
                long g = Gcd(nn, dd);
                seen[n] = (nn / g, dd / g, meshes + k);
                order.Add(n);
                queue.Enqueue(n);
            }
        }
        var gears = order.Where(id => seen[id].Meshes > 0).ToList();
        if (gears.Count == 0) return null;
        string last = gears.Select((id, i) => (id, i))
            .OrderByDescending(x => seen[x.id].Meshes).ThenByDescending(x => (double)seen[x.id].Num / seen[x.id].Den).ThenByDescending(x => x.i)
            .First().id;
        var (lNum, lDen, lMeshes) = seen[last];
        return new Train(start, last, lNum, lDen, lMeshes);
    }

    /// <summary>Whether a mesh turns its partner (<see cref="LinkGestures.MeshFit.Engaged"/>): one not touching passes nothing, a jammed one locks.</summary>
    private static bool Turns(EditorDocument doc, MeshSpec m) =>
        !doc.Parts.TryGetValue(m.A, out var a) || !doc.Parts.TryGetValue(m.B, out var b) || LinkGestures.FitOf(a, b).Engaged;

    /// <summary>A wheel's size for a mesh's step: its teeth, else its pitch radius (or radius) in micrometres, so the ratio stays whole numbers.</summary>
    private static long Size(EditorDocument doc, string id)
    {
        if (!doc.Parts.TryGetValue(id, out var p)) return 1;
        if (p.Number("teeth", 0) is > 0 and var t) return (long)Math.Round(t);
        return Math.Max(1, (long)Math.Round(LinkGestures.PitchRadius(p) * 1e6));
    }

    private static long Gcd(long a, long b) { while (b != 0) (a, b) = (b, a % b); return Math.Max(1, Math.Abs(a)); }
}
