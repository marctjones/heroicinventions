using HeroicInventions.Sim.Fluids;
using HeroicInventions.Sim.Game;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// #243 and #244: the ground key and the soils' colours. The game draws a soil in <see cref="SoilLook.Drawn"/> of its table colour
/// (racket/heroic/materials.rktd), the map and its key read the same, so the swatch in the key is the ground's colour. The numbers are
/// worked out by hand below, from the table's hex and the 85% saturation rule, not read back from the code under test.
/// <para>
/// Hand arithmetic for the six soils of the Lonely Rover's crater (table hex, then the colour drawn: same hue and value, 0.85 of the
/// saturation, luminance 0.2126 R + 0.7152 G + 0.0722 B of the bytes). Basalt sand #474141 = (71, 65, 65): value 71, saturation
/// (71-65)/71 = 0.0845, drawn 0.0718, so green and blue are 71 x (1 - 0.0718) = 65.9: #474242, luminance 15.1 + 47.1 + 4.8 = 67.0.
/// The "narrowed" ranges of #244 are on these.
/// </para>
/// </summary>
public class SoilLookTests
{
    private static string MapText() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "maps", "victoria.map"));
    private static Terrain Victoria() => Terrain.Parse(MapText(), "victoria");

    private static (float R, float G, float B) Table(string material)
    {
        var hex = MaterialLibrary.LoadDefault()[material].Color!;
        return (Convert.ToInt32(hex[1..3], 16) / 255f, Convert.ToInt32(hex[3..5], 16) / 255f, Convert.ToInt32(hex[5..7], 16) / 255f);
    }

    private static string DrawnHex(string material)
    {
        var (r, g, b) = Table(material);
        var (dr, dg, db) = SoilLook.Drawn(r, g, b);
        return SoilLook.Hex(dr, dg, db);
    }

    /// <summary>Equal to within one byte a channel: a hand-worked value that lands on a half (173.5) may round either way.</summary>
    private static void AssertHex(string expected, string actual)
    {
        for (int c = 1; c < 7; c += 2)
            Assert.InRange(Convert.ToInt32(actual.Substring(c, 2), 16), Convert.ToInt32(expected.Substring(c, 2), 16) - 1, Convert.ToInt32(expected.Substring(c, 2), 16) + 1);
    }

    private static double DrawnLuminance(string material)
    {
        var (r, g, b) = Table(material);
        var (dr, dg, db) = SoilLook.Drawn(r, g, b);
        return SoilLook.Luminance(dr, dg, db);
    }

    [Fact]
    public void ASoilIsDrawnAtEightyFivePercentOfItsTableSaturationWithTheSameHueAndValue()
    {
        // basalt sand: worked in the class header by hand
        AssertHex("#474242", DrawnHex("basalt-sand"));
        // a grey has no saturation to take, and white and black stay as they are
        Assert.Equal((0.5f, 0.5f, 0.5f), SoilLook.Drawn(0.5f, 0.5f, 0.5f));
        // pure red (hue 0, saturation 1): value 1, drawn saturation 0.85, so green and blue are 0.15
        var red = SoilLook.Drawn(1, 0, 0);
        Assert.Equal(1f, red.R, 4); Assert.Equal(0.15f, red.G, 4); Assert.Equal(0.15f, red.B, 4);
        // pure blue (hue 240): the same, with blue on top
        var blue = SoilLook.Drawn(0, 0, 1);
        Assert.Equal(0.15f, blue.R, 4); Assert.Equal(0.15f, blue.G, 4); Assert.Equal(1f, blue.B, 4);
    }

    [Fact]
    public void TheCratersSoilsAreDrawnInTheColoursTheKeyShows()
    {
        // table (materials.rktd) -> drawn, each by hand as in the class header
        AssertHex("#8A6148", DrawnHex("regolith"));
        AssertHex("#756053", DrawnHex("bedrock"));
        AssertHex("#474242", DrawnHex("basalt-sand"));
        AssertHex("#BD7E59", DrawnHex("sublimed-regolith"));
        // ice, table #83726F = (131, 114, 111): value 131, saturation 20/131 = 0.1527, drawn 0.1298, so the lowest is 131 - 131 x 0.1298 = 114.0
        // and green 114.0 + 20 x 0.15 x 0.1298 / 0.1527... = 116.6: (131, 117, 114)
        AssertHex("#837572", DrawnHex("ice-cemented-regolith"));
        // silica, table #B6AC93 = (182, 172, 147): saturation 35/182 = 0.1923, drawn 0.1635, lowest 182 x (1 - 0.1635) = 152.2, green 152.2 + 25/35 x 29.8 = 173.5
        AssertHex("#B6AD98", DrawnHex("silica-sand"));
    }

    [Fact]
    public void TheSoilsBrightnessRangeIsNarrowedAndTheOrderIsKept()
    {
        // #244: before, the drawn luminances were basalt 57.8, bedrock 99.6, regolith 103.9, sublimed 136.7, ice 139.4 (it glints, so it
        // renders at 175), silica 226 (it renders at 198): 3.9 to 1. Now the darkest is lifted and the palest calmed.
        var lum = new[] { "basalt-sand", "bedrock", "regolith", "sublimed-regolith", "ice-cemented-regolith", "silica-sand" }.ToDictionary(m => m, DrawnLuminance);
        Assert.InRange(lum["basalt-sand"], 65, 70);
        Assert.InRange(lum["silica-sand"], 165, 175);
        Assert.InRange(lum["ice-cemented-regolith"], 115, 125);
        Assert.True(lum.Values.Max() / lum.Values.Min() < 2.7, $"{lum.Values.Max() / lum.Values.Min():0.00} to 1");
        // bedrock and regolith were left as they were (the crater's rim and wall; one is not the other's neighbour in brightness, only in hue)
        Assert.InRange(lum["bedrock"], 99, 100.5); Assert.InRange(lum["regolith"], 103.5, 104.5); Assert.InRange(lum["sublimed-regolith"], 136, 137.5);
    }

    [Fact]
    public void SoilsThatMeetOnTheMapStayApart()
    {
        // Every pair of soils that touch on the victoria map (4-neighbours): worked from the table's drawn colours. A pair that differed by 50 or
        // more in luminance before #244 (basalt and ice or silica; bedrock and silica or ice; regolith and ice) still does, and a pair that did not
        // (bedrock and regolith 5 apart in luminance, basalt and bedrock, bedrock and sublimed) still differs by 30 or more in luminance or by 20
        // or more bytes of colour. Bedrock and regolith are the same hue and differ in how grey they are: they were left as they were.
        var t = Victoria();
        var pairs = new HashSet<(string, string)>();
        for (int j = 0; j < t.Nz; j++)
            for (int i = 0; i < t.Nx; i++)
            {
                int k = i + j * t.Nx;
                foreach (int n in new[] { i + 1 < t.Nx ? k + 1 : -1, j + 1 < t.Nz ? k + t.Nx : -1 })
                    if (n >= 0 && t.Soil[n] != t.Soil[k])
                    {
                        var a = t.Soils[t.Soil[k]].Material; var b = t.Soils[t.Soil[n]].Material;
                        pairs.Add(string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a));
                    }
            }
        Assert.True(pairs.Count >= 7, string.Join(", ", pairs));
        double Distance(string a, string b)
        {
            var (ar, ag, ab) = SoilLook.Drawn(Table(a).R, Table(a).G, Table(a).B); var (br, bg, bb) = SoilLook.Drawn(Table(b).R, Table(b).G, Table(b).B);
            return 255 * Math.Sqrt((ar - br) * (ar - br) + (ag - bg) * (ag - bg) + (ab - bb) * (ab - bb));
        }
        // the luminances this table had before #244 (drawn): the pairs that were 50 apart or more
        var before = new Dictionary<string, double> { ["basalt-sand"] = 57.8, ["bedrock"] = 99.6, ["regolith"] = 103.9, ["sublimed-regolith"] = 136.7, ["ice-cemented-regolith"] = 139.4, ["silica-sand"] = 226 };
        foreach (var (a, b) in pairs)
        {
            double gap = Math.Abs(DrawnLuminance(a) - DrawnLuminance(b));
            Assert.True(gap >= 30 || Distance(a, b) >= 20, $"{a} / {b}: {gap:0} apart in luminance, {Distance(a, b):0} bytes of colour");
            if (a is "ice-cemented-regolith" or "silica-sand" || b is "ice-cemented-regolith" or "silica-sand") continue;   // these two render brighter or dimmer than their table colour: measured in docs/art-direction.md 12.17
            if (Math.Abs(before[a] - before[b]) >= 50) Assert.True(gap >= 50, $"{a} / {b}: {gap:0}");
        }
    }

    [Fact]
    public void TheKeyListsOnlyTheSoilsTheMapHasUnderACell()
    {
        var t = Victoria();
        Assert.Equal(6, SoilLook.SoilsPresent(t).Count);
        // a map whose soil list has one that no cell uses: the key leaves it out
        var m = new Terrain { Name = "m", X0 = 0, Z0 = 0, Cell = 1, Nx = 2, Nz = 2, Heights = new double[4], Soil = [1, 1, 1, 1],
                              Soils = [new SoilSpec("regolith", 0), new SoilSpec("clay", 0)] };
        Assert.Equal([1], SoilLook.SoilsPresent(m));
    }

    [Fact]
    public void TheWordsAreForAPlayerNotTheCode()
    {
        var ids = MaterialLibrary.LoadDefault().All.Select(m => m.Id).Where(id => id.Contains('-')).ToList();
        foreach (var s in Victoria().Soils)
        {
            var w = SoilLook.WordsFor(s.Material);
            Assert.DoesNotContain(w.Name, ids);   // not the table's id, which has hyphens in it
            foreach (var text in new[] { w.Name, w.Look, w.Note })
                Assert.DoesNotMatch(@"_|\d|[A-Z]", text);   // no field names, no numbers
            Assert.NotEmpty(w.Look); Assert.NotEmpty(w.Note);
        }
        // a soil with no words written is named from its id, spaced out
        Assert.Equal("fine clay", SoilLook.WordsFor("fine-clay").Name);
    }

    [Fact]
    public void TheSoilUnderThePointComesFromTheSameCellsTheTerrainIsDrawnFrom()
    {
        var t = Victoria();
        // the floor, the north wall, the east wall and the rim, as the opening's rover meets them (x, z in metres)
        foreach (var (x, z, soil, line) in new[] {
            (180.0, 140.0, "basalt-sand", "ground here: basalt sand (dark)"),
            (0.0, 345.0, "bedrock", "ground here: bedrock (grey-brown)"),
        })
        {
            var at = t.SoilAt(x, z)!.Value;
            Assert.Equal(soil, t.Soils[at.Soil].Material);
            Assert.Equal(t.Soil[t.CellAt(x, z)!.Value], at.Soil);
            Assert.Equal(line, SoilLook.GroundHere(t, x, z));
        }
        Assert.Equal("ground here: off the map", SoilLook.GroundHere(t, 1e6, 0));
        Assert.Null(t.SoilAt(1e6, 0));
    }

    [Fact]
    public void WhereTheRoverHasWorkedTheGroundTheSoilIsThePatchsOwn()
    {
        // 60 m square, regolith everywhere but bedrock under the four cells round the origin; the rover works a patch there and tips regolith on it:
        // the patch's node is loose regolith while the map's cell under it is still bedrock, and "ground here" says what is drawn.
        var soils = new[] { new SoilSpec("regolith", 0, Friction: 0.7, Density: 1500), new SoilSpec("bedrock", 0, Cohesion: 5e7, Friction: 0.6, Density: 2700) };
        var t = new Terrain { Name = "m", X0 = -30, Z0 = -30, Cell = 5, Nx = 12, Nz = 12, Heights = new double[144], Soil = new int[144], Soils = soils, OpenEdges = false };
        foreach (var k in new[] { 5 + 5 * 12, 6 + 5 * 12, 5 + 6 * 12, 6 + 6 * 12 }) t.Soil[k] = 1;
        Assert.Equal("ground here: bedrock (grey-brown)", SoilLook.GroundHere(t, 0.3, 0.3));
        var w = t.WorkAt(0, 0)!;
        Assert.NotNull(w.Pour(0.3, 0.3, 0.05, 0));
        Assert.Contains(t.Worked, p => ReferenceEquals(p, w));
        Assert.Equal("ground here: regolith (rust-brown), loose", SoilLook.GroundHere(t, 0.3, 0.3));
        Assert.Equal(1, t.Soil[t.CellAt(0.3, 0.3)!.Value]);   // the map's own cell is untouched until the patch is merged back
    }
}
