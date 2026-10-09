using HeroicInventions.Sim.Electrics;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Mechanics;
using HeroicInventions.Sim.Thermo;

namespace HeroicInventions.Sim.Game;

public enum GoalKind
{
    /// <summary>A step of the path to the call (docs/lonely-rover.html#path), shown as a checklist.</summary>
    Path,
    /// <summary>A concept achievement: a machine the engine models did its job (Heron's catalogue, docs#routes).</summary>
    Concept,
    /// <summary>A route achievement: how the bank was charged, read from its per-source record (#64).</summary>
    Route,
}

/// <summary>One goal or achievement: its id, how it is shown, the exact trigger in words (the field it reads), and the line the rover log carries.</summary>
public sealed record GoalDef(string Id, GoalKind Kind, string Title, string Trigger, string Story);

/// <summary>When a goal was earned: seconds of the first scene's clock, and the sol.</summary>
public sealed record Earned(string Id, double Time, int Sol);

/// <summary>
/// What the view knows of the battery bank's crate in the opening world, which the sim does not (the buried crate is a rigid body that the
/// game's ground holds): metres of ground over its top (more than a quarter of <see cref="Size"/> holds it, Burial.Held; zero or less, its
/// top is clear) and where it lies on the ground.
/// </summary>
public sealed record CrateReading(double Cover, double Size, double X, double Z);

/// <summary>
/// Goals and achievements (issue #68), evaluated from readable state each time it is called, never from a script: the phases of the path to
/// the call as a checklist, the concept achievements of the design doc's table, and the route achievements read from the bank's per-source
/// record. Every trigger is a comparison of fields the sim already keeps, named in <see cref="Catalogue"/>; an achievement for which the
/// engine has no field (Tantalus cup: no siphon; Torsion: no boulder fracture; Undermined, lever, siege, hushing: nothing records why a
/// boulder moved) is left out rather than invented.
///
/// Durations ("a sol", "an hour", "a night") are measured on the scene's own clock, by the change in <c>time</c> between calls, so a sleep
/// that runs ahead counts as much as watching; a restart, which sets the clock back, drops the half-counted ones (never an earned one).
/// Earned goals persist in the world save (<see cref="ToForm"/>, <see cref="Restore"/>; WorldSave.Goals).
/// </summary>
public sealed class GoalTracker
{
    // ---- the catalogue ----
    public static IReadOnlyList<GoalDef> Catalogue { get; } =
    [
        new("find-bank", GoalKind.Path, "Find the bank", "the bank's crate, once the ground has settled, has its top clear of the ground (CrateReading.Cover <= 0); where a scene has a battery bank built into it, it is found",
            "The storm's rubble is off the crate: the battery bank is in sight."),
        new("free-bank", GoalKind.Path, "Free it", "the crate has been moved 2 m or more along the ground from where it was found, and its top is clear (CrateReading X, Z, Cover)",
            "The battery bank is out from the slide, hauled clear of the boulders."),
        new("keep-warm", GoalKind.Path, "Keep it warm", "a bank's temperature stayed between its charging limits (0 to 45 C: <bank>.temperature, <bank>.accepting's range) through a whole night, from the sun going under the horizon to it rising (scene.sun-elevation < 0)",
            "The bank rode out a night between 0 and 45 C."),
        new("charge-bank", GoalKind.Path, "Charge it", "a bank is full (<bank>.full) and has been charged by a generator (its per-source record is above 0)",
            "The bank is full."),
        new("the-call", GoalKind.Path, "The call", "a bank made the call on the relay pass (scene.won)",
            "The call went out. Earth has heard the rover."),

        new("gear-up", GoalKind.Concept, "Gear up", "a generator charging a bank (<generator>.charging) above its cut-in (<generator>.rpm > <generator>.cut-in-rpm) turns at 100 times or more the speed of the prime mover it is geared to (rotor rad/s over the windmill's, water wheel's, jet wheel's or Stirling engine's)",
            "A 100:1 train brought a slow shaft up to the motor's cut-in: Step-up gearbox."),
        new("thermostat", GoalKind.Concept, "Thermostat", "for one hour of the scene's clock a bank held between 0 and 45 C while the heat store in the bin its lid works ran above 100 C and the lid not wide open (<strip>.open < 1: the strip, or the sense switch, reading the bank's store)",
            "A thermostat kept the bank under 45 C beside a store above 100 C: Lidded heat-bin."),
        new("harrison", GoalKind.Concept, "Harrison", "the thermostat of Thermostat was a bimetallic strip (the bimetal part on the lid), not the ideal sense switch",
            "A bimetallic strip worked the lid, as in Harrison's H3 clock of 1759."),
        new("archimedes-screw", GoalKind.Concept, "Archimedes' screw", "a lift worked by a screw has carried up half a store's capacity and the store above it is full (lift Moved, To.WaterVolume, Head > 0)",
            "A screw filled a raised store: Wind-driven screw."),
        new("noria", GoalKind.Concept, "Noria", "a lift worked by a noria wheel has carried up half a store's capacity and the store above it is full",
            "A bucket wheel filled a raised store: Noria."),
        new("ctesibius", GoalKind.Concept, "Ctesibius", "a force pump (a piston lift) has carried up a litre or more to a head above the suction limit of its air for 20 C water (LiftPump.SuctionLimit(20, zone) < lift Head)",
            "A force pump lifted water higher than suction can: Force pump."),
        new("constant-head", GoalKind.Concept, "Constant head", "a float valve kept its store's level (Tank.Level) inside its band, from the shut level down to Travel below it, with the feed flowing, without a break for one sol of the planet's day (planet Sol seconds)",
            "A float valve held a store's level for a sol: Float valve."),
        new("safety-valve", GoalKind.Concept, "Safety valve", "a safety valve has been lifted for 10 s or more in all (<valve>.opening > 0) on a boiler that has not burst (<boiler>.burst = 0)",
            "The valve lifted and the boiler lived: Safe boiler."),
        new("boiler-burst", GoalKind.Concept, "Boiler burst", "a boiler burst (<boiler>.burst = 1)",
            "A boiler burst. This is why Papin added the safety valve in 1679."),
        new("measure-mars", GoalKind.Concept, "Measure Mars", "a pendulum's timed period (two turning points apart, doubled) gives g = 4 pi^2 L / T^2 within 2% of 3.71 m/s2, from at least 4 turning points",
            "A pendulum measured Mars: g within 2% of 3.71."),
        new("newcomens-mistake", GoalKind.Concept, "Newcomen's mistake", "a Newcomen (atmospheric) cylinder made a stroke (<cylinder>.strokes >= 1) in air under 10 kPa",
            "Newcomen's engine ran on Mars, pushed by almost no air: why engines went to high pressure."),

        new("route-windmiller", GoalKind.Route, "The windmiller", "at the call, 90% or more of the bank's charge came from wind (<bank>.from-wind over the sum of its sources)",
            "Won by windmills alone: Windmill-generator."),
        new("route-hot-air", GoalKind.Route, "Hot air", "at the call, half or more of the bank's charge came from a Stirling engine (<bank>.from-stirling)",
            "Won on hot air: Stirling-generator."),
        new("route-water-wheel", GoalKind.Route, "The water-wheel trap", "a water wheel has fed a generator (<bank>.from-water-wheel > 0)",
            "A water wheel fed the generator; the log shows how little it gave."),
        new("route-gravity-battery", GoalKind.Route, "The gravity battery", "falling weights have fed a generator (<bank>.from-falling-weight > 0)",
            "A falling weight fed the generator: Counterweight store."),
        new("route-heron-purist", GoalKind.Route, "Heron purist", "an aeolipile has fed a generator (<bank>.from-aeolipile > 0, a generator named #:driven-by aeolipile)",
            "Heron's aeolipile fed the generator, and hardly at all."),
        new("route-three-sols", GoalKind.Route, "Three sols to Earth", "the call was made within 4 sols of the scene's start (clock <= 4 x the planet's Sol)",
            "The call went out inside four sols."),
    ];

    private static readonly Dictionary<string, GoalDef> ById = Catalogue.ToDictionary(g => g.Id);
    public static GoalDef Def(string id) => ById[id];

    // ---- state ----
    private readonly Dictionary<string, Earned> _earned = [];
    private readonly Dictionary<string, double> _holds = [];       // seconds accumulated toward a duration trigger, by key
    private readonly Dictionary<string, int> _nights = [];         // per bank: 0 day, 1 a night kept in range, 2 a night broken
    private (double X, double Z)? _foundAt;
    private double _last = double.NaN;
    private bool _routed;

    /// <summary>What the view knows of the bank's crate (null: the scene has none to read; a battery bank built into the scene then stands for it).</summary>
    public Func<CrateReading?>? BankCrate { get; set; }
    /// <summary>The ground has finished settling (the rim's collapse is over), so what is on it is where it will lie. True for a scene with no ground.</summary>
    public Func<bool> GroundSettled { get; set; } = () => true;

    public IReadOnlyCollection<Earned> EarnedGoals => _earned.Values;
    public bool Has(string id) => _earned.ContainsKey(id);
    public Earned? Get(string id) => _earned.GetValueOrDefault(id);

    /// <summary>Forgets a half-counted run (a restart): earned achievements stay, the path's steps and the running counts start over.</summary>
    public void RestartRun()
    {
        foreach (var g in Catalogue.Where(g => g.Kind == GoalKind.Path)) _earned.Remove(g.Id);
        _holds.Clear(); _nights.Clear(); _foundAt = null; _last = double.NaN; _routed = false;
    }

    private const double HourS = 3600;
    /// <summary>The longest gap of the scene's clock between two calls that still counts as watched, s: a sleep calls every 0.5 s, a frame at high speed under 2 s.</summary>
    public const double MaxGap = 30;

    private void ClearRuns()
    {
        foreach (var k in _holds.Keys.Where(k => !k.StartsWith("valve/")).ToList()) _holds.Remove(k);   // a safety valve's lifted time is a total, not a run
        foreach (var k in _nights.Keys.ToList()) _nights[k] = _nights[k] == 0 ? 0 : 2;       // a night in progress is no longer whole
    }

    /// <summary>
    /// Looks at the scene (every machine of a world) at <paramref name="time"/> s of its clock and returns what was newly earned. Call it every
    /// frame, and between the steps of a sleep.
    /// </summary>
    public IReadOnlyList<GoalDef> Update(IReadOnlyList<MachineRuntime> runtimes, double time)
    {
        var fresh = new List<GoalDef>();
        if (runtimes.Count == 0) return fresh;
        double dt = double.IsNaN(_last) ? 0 : time - _last;
        if (dt < 0) { ClearRuns(); dt = 0; }                         // the clock went back: a restart or a load
        else if (dt > MaxGap) { ClearRuns(); dt = 0; }               // not watched across the gap (the caller must look as often as a sleep steps): a duration or a night cannot be claimed from two samples
        _last = time;
        int sol = runtimes.Max(r => r.Sun.SolNumber);
        void Earn(string id)
        {
            if (_earned.ContainsKey(id)) return;
            _earned[id] = new Earned(id, time, sol);
            fresh.Add(ById[id]);
        }
        // accumulate a duration while a condition holds (reset when it does not); true once it has held for needed s
        bool Held(string key, bool holds, double needed)
        {
            if (!holds) { _holds.Remove(key); return false; }
            double h = _holds.GetValueOrDefault(key) + dt;
            _holds[key] = h;
            return h >= needed;
        }

        var banks = runtimes.SelectMany((rt, i) => rt.Banks.Values.Select(b => (Rt: rt, Index: i, Bank: b))).ToList();

        // ---------------- the path ----------------
        var crate = BankCrate?.Invoke();
        if (!Has("find-bank"))
        {
            bool found = crate is { } c ? GroundSettled() && c.Cover <= 0 : banks.Count > 0;
            if (found) { Earn("find-bank"); if (crate is { } c2) _foundAt = (c2.X, c2.Z); }
        }
        if (Has("find-bank") && !Has("free-bank"))
        {
            if (crate is { } c)
            {
                _foundAt ??= (c.X, c.Z);     // a save made after the find, loaded: measure from here
                var (fx, fz) = _foundAt.Value;
                if (c.Cover <= 0 && Math.Sqrt((c.X - fx) * (c.X - fx) + (c.Z - fz) * (c.Z - fz)) >= 2) Earn("free-bank");
            }
            else if (banks.Count > 0) Earn("free-bank");
        }
        foreach (var (rt, i, bank) in banks)
        {
            string key = $"{i}/{bank.Name}";
            bool night = rt.FieldGetters.TryGetValue("scene.sun-elevation", out var elevation) && elevation() < 0;
            int state = _nights.GetValueOrDefault(key);
            if (night)
            {
                if (!_nights.ContainsKey(key)) state = 2;          // a night begun before it was watched is not whole
                else if (state == 0) state = bank.InRange ? 1 : 2;
                else if (state == 1 && !bank.InRange) state = 2;
            }
            else
            {
                if (state == 1) Earn("keep-warm");
                state = 0;
            }
            _nights[key] = state;
            if (bank.Full && bank.Sources.Values.Sum() > 0) Earn("charge-bank");
        }
        if (runtimes.Any(r => r.Won)) Earn("the-call");

        // ---------------- concepts ----------------
        for (int i = 0; i < runtimes.Count; i++)
        {
            var rt = runtimes[i];
            foreach (var gen in rt.Generators.Values)
                if (gen.Delivered > 0 && gen.Omega > gen.CutInOmega && gen.TrainRatio is { } ratio && ratio >= 100) Earn("gear-up");

            foreach (var strip in rt.Bimetals.Values)
            {
                var bin = strip.Bin;
                if (bin is null) continue;
                var bank = rt.Banks.Values.FirstOrDefault(b => b.InName == strip.SensesName);
                bool holds = bank is { InRange: true } && bin.Store is { Temperature: > 100 } && bin.Open < 1;     // the lid not wide open: the strip is holding the heat back
                if (Held($"thermostat/{i}/{strip.Name}", holds, HourS)) { Earn("thermostat"); Earn("harrison"); }
            }
            foreach (var bin in rt.HeatBins.Values.Where(b => b.Sense is not null))
            {
                var bank = rt.Banks.Values.FirstOrDefault(b => b.InName == bin.Sense!.Name);
                bool holds = bank is { InRange: true } && bin.Store is { Temperature: > 100 } && bin.Openings >= 1;
                if (Held($"switch/{i}/{bin.Name}", holds, HourS)) Earn("thermostat");
            }

            foreach (var spec in rt.Def.Lifts)
            {
                if (!rt.Lifts.TryGetValue(spec.Id, out var lift)) continue;
                var by = rt.Def.Part(spec.By);
                if (by is null) continue;
                bool raisedAndFull = lift.Head > 0 && lift.Moved >= 0.5 * lift.To.Capacity && lift.To.WaterVolume >= 0.99 * lift.To.Capacity;
                if (by.Kind == "screw" && raisedAndFull) Earn("archimedes-screw");
                if (by.Kind == "wheel" && by.Symbol("shape", "") == "noria" && raisedAndFull) Earn("noria");
                if (lift.IsPump && lift.Moved >= 0.001 && lift.Head > LiftPump.SuctionLimit(20, lift.Zone)) Earn("ctesibius");
            }

            double sol_s = rt.Def.Planet.Sol;
            foreach (var (id, fv) in rt.FloatValves)
            {
                var v = fv.Valve;
                bool inBand = v.Tank.Level >= v.ShutLevel - v.Travel && v.Tank.Level <= v.ShutLevel + 1e-9;
                if (Held($"head/{i}/{id}", inBand && (fv.Flow() > 0 || v.Opening > 0), sol_s)) Earn("constant-head");
            }
            foreach (var (id, sv) in rt.SafetyValves)
            {
                if (sv.Valve.Opening > 0 && !sv.Boiler.Burst) _holds[$"valve/{i}/{id}"] = _holds.GetValueOrDefault($"valve/{i}/{id}") + dt;
                if (_holds.GetValueOrDefault($"valve/{i}/{id}") >= 10 && !sv.Boiler.Burst) Earn("safety-valve");
            }
            if (rt.Boilers.Values.Any(b => b.Burst)) Earn("boiler-burst");

            foreach (var p in rt.Pendulums.Values)
            {
                if (p.Swings < 4 || double.IsNaN(p.HalfPeriod) || p.HalfPeriod <= 0) continue;
                double period = 2 * p.HalfPeriod, g = 4 * Math.PI * Math.PI * p.Length / (period * period);
                if (Math.Abs(g - MarsGravity) <= 0.02 * MarsGravity) Earn("measure-mars");
            }
            foreach (var cyl in rt.Cylinders.Values)
                if (cyl is AtmosphericCylinder { Strokes: >= 1 } ac && ac.Zone.Pressure < 10_000) Earn("newcomens-mistake");
        }

        // ---------------- routes: the bank's record of where its charge came from ----------------
        foreach (var (rt, i, bank) in banks)
        {
            double Wh(string source) => bank.Sources.GetValueOrDefault(source);
            if (Wh("water-wheel") > 0) Earn("route-water-wheel");
            if (Wh("falling-weight") > 0) Earn("route-gravity-battery");
            if (Wh("aeolipile") > 0) Earn("route-heron-purist");
            if (bank.Won && !_routed)
            {
                double total = bank.Sources.Values.Sum();
                if (total > 0)
                {
                    _routed = true;
                    if (Wh("wind") / total >= 0.9) Earn("route-windmiller");
                    if (Wh("stirling") / total >= 0.5) Earn("route-hot-air");
                    if (rt.Time <= 4 * rt.Def.Planet.Sol) Earn("route-three-sols");
                }
            }
        }
        return fresh;
    }

    public const double MarsGravity = 3.71;

    // ---- the world save ----
    /// <summary>The earned goals as <c>(goals 1 (found X Z) (earned id time sol) …)</c>.</summary>
    public SList ToForm()
    {
        var items = new List<SExpr> { new SSymbol("goals"), new SNumber(1) };
        if (_foundAt is { } f) items.Add(new SList([new SSymbol("found"), new SNumber(f.X), new SNumber(f.Z)]));
        foreach (var e in _earned.Values)
            items.Add(new SList([new SSymbol("earned"), new SSymbol(e.Id), new SNumber(e.Time), new SNumber(e.Sol)]));
        return new SList(items);
    }

    /// <summary>Takes up a save's goals: what it earned is earned (added to what is already, since an achievement is not taken back).</summary>
    public void Restore(SList form)
    {
        foreach (var e in form.Fields("earned"))
            if (e.Items is [_, SSymbol id, SNumber t, SNumber s] && ById.ContainsKey(id.Name) && !_earned.ContainsKey(id.Name))
                _earned[id.Name] = new Earned(id.Name, t.Value, (int)s.Value);
        if (form.Field("found") is { Items: [_, SNumber x, SNumber z] }) _foundAt = (x.Value, z.Value);
        _last = double.NaN;
        _holds.Clear(); _nights.Clear();
    }
}
