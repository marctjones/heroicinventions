using HeroicInventions.Sim.Fluids;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #188: a slide's finish (<see cref="Terrain.Relax"/> after boulders are taken) scanned the whole map for each
/// of its passes, 78 ms in the one tick a crater slide ended; it now scans, after the first pass, only the box round
/// what moved. That must be the same ground bit for bit: the checksums below were taken from the whole-map scan
/// (commit 45ed32b) and any change of them means a different slide.
/// </summary>
public class SlideFinishTests
{
    private static string Dir(string f) => Path.Combine(AppContext.BaseDirectory, "maps", f);

    /// <summary>A checksum of every height's exact bits, in order.</summary>
    private static ulong Checksum(Terrain t)
    {
        ulong h = 14695981039346656037UL;
        foreach (double v in t.Heights) h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(v)) * 1099511628211UL;
        return h;
    }

    [Theory]
    [InlineData("talus.map", 9.81, 12299171980080648275UL)]
    [InlineData("victoria.map", 3.71, 13621414887412942932UL)]
    public void SettlingGivesTheSameGroundBitForBit(string map, double gravity, ulong expected)
    {
        var t = Terrain.Parse(File.ReadAllText(Dir(map)), map);
        var (failures, _) = t.Settle(gravity);
        Assert.True(failures > 0);
        Assert.Equal(expected, Checksum(t));
    }
}
