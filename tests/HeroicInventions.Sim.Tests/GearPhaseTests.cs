using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;
using HeroicInventions.Sim.Mechanics;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #85: a gear in mesh stands turned so its teeth fall in its partner's
/// gaps. The misalignment is measured as degrees of the driven gear's turn
/// from where it would interleave: 0 is tooth in gap, ±180/z is tooth on
/// tooth (10° for an 18-tooth gear).
/// </summary>
public class GearPhaseTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    // an 18- and a 24-tooth involute gear of module 10 mm, as build.rkt writes them into the catalogue
    private const string Catalogue = """
        (catalogue
         (entry involute-gear-m10-18 "Involute spur gear, 18 teeth, module 10 mm" gear "gear-4355620147" 0.0019 (5e-6 5e-6 8e-6) ((teeth 18.0) (module 0.01) (profile involute) (width 0.08) (pitch-radius 0.09)))
         (entry involute-gear-m10-24 "Involute spur gear, 24 teeth, module 10 mm" gear "gear-24" 0.003 (9e-6 9e-6 1.6e-5) ((teeth 24.0) (module 0.01) (profile involute) (width 0.08) (pitch-radius 0.12)))
         (entry involute-gear-m10-19 "Involute spur gear, 19 teeth, module 10 mm" gear "gear-19" 0.002 (5e-6 5e-6 8e-6) ((teeth 19.0) (module 0.01) (profile involute) (width 0.08) (pitch-radius 0.095))))
        """;

    private static BuildSession NewSession()
    {
        string dir = Path.Combine(Path.GetTempPath(), "heroic-gear-phase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new BuildSession(Materials, CatalogueReader.Parse(Catalogue), dir);
    }

    private static MachineDef Shipped(string name) =>
        MachineDef.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machines", name + ".machine")));

    private static double Error(MachineDef def, MeshSpec m) => GearPhase.MeshErrorDegrees(def.Part(m.A)!, def.Part(m.B)!);

    private static double Error(EditorDocument doc, string a, string b) => GearPhase.MeshErrorDegrees(doc.Parts[a], doc.Parts[b]);

    [Fact]
    public void TheCSharpPhaseIsGearRktsMateAngle()
    {
        // geared-brake.rkt: (mate-angle 100 0 10 0) → 198°, as its .machine carries for the pinion
        Assert.Equal(198, GearPhase.MateAngle(100, 0, 10, 0) * 180 / Math.PI, 9);
        // the general form, for two gears in one frame, is mate-angle exactly
        foreach (var (za, ta, zb, line) in new[] { (64.0, 0.3, 38.0, 0.0), (48, 1.1, 24, 60 * Math.PI / 180), (127, -0.4, 32, 150 * Math.PI / 180) })
            Assert.Equal(GearPhase.MateAngle(za, ta, zb, line), GearPhase.Mate(za, ta, line, zb, line + Math.PI, 1), 9);
    }

    [Fact]
    public void ToothOnToothIsHalfAToothOff()
    {
        // two 18-tooth gears unturned, side by side on +X: A's tooth on +X meets B's tooth on its −X
        Assert.Equal(10, Math.Abs(GearPhase.ErrorDegrees(18, 0, 0, 18, 0, Math.PI, 1)), 9);
        // a 19-tooth partner unturned has a gap on its −X (19 is odd): it meshes by accident
        Assert.Equal(0, GearPhase.ErrorDegrees(18, 0, 0, 19, 0, Math.PI, 1), 9);
    }

    public static IEnumerable<object[]> GearedMachines() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "machines"), "*.machine")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(n => Shipped(n).Meshes.Count > 0)
            .Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(GearedMachines))]
    public void EveryShippedMeshInterleaves(string name)
    {
        // antikythera-lunar-train and geared-brake phase their gears with mate-angle; turned to any heading they still do
        var def = Shipped(name);
        // a machine with a part built along the axes (generator-train's windmill) can't stand at a heading at all
        MachineDef? atHeading;
        try { atHeading = def.Turned(53); } catch (MachineFormatException) { atHeading = null; }
        foreach (var turned in atHeading is null ? new[] { def } : new[] { def, atHeading })
            foreach (var m in turned.Meshes)
                Assert.True(Math.Abs(Error(turned, m)) < 1e-6, $"{name}: {m.B} stands {Error(turned, m):F4}° off {m.A}'s gaps");
        // and the editor's phasing leaves them as they are
        Assert.Empty(GearPhase.Rephase(def.Parts.ToDictionary(p => p.Id), def.Meshes));
    }

    [Fact]
    public void AGearMeshedInTheEditorIsTurnedToFallInItsPartnersGaps()
    {
        var session = NewSession();
        // 18 and 24 teeth of module 10 mm: pitch radii 9 and 12 cm, so 21 cm apart, here on a slant
        double line = 35 * Math.PI / 180, d = 0.21;
        session.Execute("(wheel a #:catalogue involute-gear-m10-18 #:at (0 1 0))");
        session.Execute($"(wheel b #:catalogue involute-gear-m10-24 #:at ({d * Math.Cos(line)} {1 + d * Math.Sin(line)} 0))");
        double before = Error(session.Document, "a", "b");
        // mate-angle: 35 + 180 + (180 − 18·(0 − 35))/24 = 248.75° = 16 teeth of 15° and 8.75°; B stands at 0,
        // 6.25° past the nearest such angle (−6.25°), most of the 7.5° that is tooth on tooth
        Assert.Equal(6.25, before, 6);
        var a = session.Document.Parts["a"];
        session.Execute("(mesh a b)");
        Assert.Equal(0, Error(session.Document, "a", "b"), 6);
        // moved by less than half a tooth; nothing but the angle changed, so the ratio and torque are the teeth's as before
        Assert.Equal(360 - 6.25, session.Document.Parts["b"].Number("angle-deg"), 6);
        Assert.Same(a, session.Document.Parts["a"]);
        Assert.Equal(24, session.Document.Parts["b"].Number("teeth"));
        // one undo takes the mesh and the phase back together
        session.Execute("(undo)");
        Assert.Equal(0, session.Document.Parts["b"].Number("angle-deg"));
    }

    [Fact]
    public void TwoEighteensSideBySideAreTurnedHalfAToothAndStayMeshedWhenMovedOrTurned()
    {
        var session = NewSession();
        session.Execute("(wheel a #:catalogue involute-gear-m10-18 #:at (0 1 0))");
        session.Execute("(wheel b #:catalogue involute-gear-m10-18 #:at (0.18 1 0))");
        Assert.Equal(10, Math.Abs(Error(session.Document, "a", "b")), 6);   // tooth on tooth
        session.Execute("(mesh a b)");
        Assert.Equal(0, Error(session.Document, "a", "b"), 6);
        // b dragged round a to stand above it: rephased from a
        session.Execute("(move b (0 1.18 0))");
        Assert.Equal(0, Error(session.Document, "a", "b"), 6);
        // a third gear meshed with b is phased from b, and b keeps its place
        double bAngle = session.Document.Parts["b"].Number("angle-deg");
        session.Execute("(wheel c #:catalogue involute-gear-m10-19 #:at (0.185 1.18 0))");
        session.Execute("(mesh b c)");
        Assert.Equal(bAngle, session.Document.Parts["b"].Number("angle-deg"), 9);
        Assert.Equal(0, Error(session.Document, "a", "b"), 6);
        Assert.Equal(0, Error(session.Document, "b", "c"), 6);
        // a and b, one above the other, turned to a heading about the vertical their centres share: their axles
        // stay parallel and square to the line of centres, so they still mesh, and stay interleaved
        session.Execute("(unmesh b c)");
        foreach (var g in new[] { "a", "b" }) session.Execute($"(turn {g} 40)");
        Assert.Equal(40, session.Document.Parts["b"].Number("heading-deg"), 9);
        Assert.Equal(0, Error(session.Document, "a", "b"), 6);
    }

    [Fact]
    public void ACrankedGearKeepsItsAngleAndItsPartnerIsPhasedFromIt()
    {
        var session = NewSession();
        session.Execute("(wheel a #:catalogue involute-gear-m10-18 #:at (0 1 0))");
        session.Execute("(wheel b #:catalogue involute-gear-m10-18 #:at (0.18 1 0))");
        session.Execute("(set b #:drive-rpm 2)");
        session.Execute("(set b #:angle-deg 7)");
        session.Execute("(mesh a b)");
        Assert.Equal(7, session.Document.Parts["b"].Number("angle-deg"), 9);
        Assert.Equal(0, Error(session.Document, "b", "a"), 6);
    }
}
