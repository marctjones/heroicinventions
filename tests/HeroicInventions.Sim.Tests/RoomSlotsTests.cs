using HeroicInventions.Sim.Editor;
using HeroicInventions.Sim.Machines;
using HeroicInventions.Sim.Materials;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Building phase 5 (#254, "Inside a selected room"): the room a part goes into, its floor, and which blank references (`?`) a new part
/// can be given by default. The vault: a 0.5 m room at (254.5, -44.4, 137.9) (its #:at the middle of its floor), a rock (heat store) on its
/// floor, a bin that holds the rock, a bimetal strip that senses the room and drives the bin.
/// </summary>
public class RoomSlotsTests
{
    private static readonly MaterialLibrary Materials = MaterialLibrary.LoadDefault();

    private static BuildSession Session(params string[] commands)
    {
        string dir = Path.Combine(Path.GetTempPath(), "heroic-room-slots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var s = new BuildSession(Materials, [], dir);
        foreach (var c in commands) s.Execute(c);
        return s;
    }

    private const string Room = "(enclosure enclosure_1 #:at (254.5 -44.4 137.9) #:size-x 0.5 #:size-y 0.5 #:size-z 0.5)";
    private const string Rock = "(heat-store heat_store_2 #:at (254.62 -44.4 137.9))";
    private static PartSpec Probe(string kind) => PartTemplates.Create(kind, "probe", new Vec3(0, 0, 0), "bronze");

    [Fact]
    public void TheFloorIsTheRoomsAtAndHalfItsSizes()
    {
        var s = Session(Room);
        var f = RoomSlots.FloorOf(s.Document, "enclosure_1")!.Value;
        Assert.Equal(-44.4, f.Y, 9);
        Assert.Equal(254.5, f.CentreX, 9);
        Assert.Equal(137.9, f.CentreZ, 9);
        Assert.Equal(0.25, f.HalfX, 9);
        Assert.True(f.Holds(254.62, 137.9));
        Assert.False(f.Holds(254.8, 137.9));
        Assert.Null(RoomSlots.FloorOf(s.Document, "nothing"));
    }

    [Fact]
    public void ARoomIsItsOwnRoomAndAPartInsideItHasThatRoomEvenWhenSelected()
    {
        var s = Session(Room, Rock);
        Assert.Equal("enclosure_1", RoomSlots.RoomOf(s.Document, "enclosure_1"));
        Assert.Equal("enclosure_1", RoomSlots.RoomOf(s.Document, "heat_store_2"));   // the rock is selected after it is placed
        Assert.Null(RoomSlots.RoomOf(s.Document, null));
    }

    [Fact]
    public void APartOutsideTheBoxHasNoRoomAndIsNotACandidate()
    {
        var s = Session(Room, "(heat-store far #:at (260 -44.4 137.9))", "(heat-store above #:at (254.5 -43.8 137.9))");
        Assert.Null(RoomSlots.RoomOf(s.Document, "far"));
        Assert.Null(RoomSlots.RoomOf(s.Document, "above"));   // its box is 0.5 m tall: 0.6 m up is outside
        Assert.Empty(RoomSlots.Candidates(s.Document, "enclosure_1", "holds"));
    }

    [Fact]
    public void TheInnermostRoomIsTheOneAPartIsIn()
    {
        var s = Session("(enclosure big #:at (0 0 0) #:size-x 4 #:size-y 4 #:size-z 4)", "(enclosure small #:at (0 0 0) #:size-x 1 #:size-y 1 #:size-z 1)", "(heat-store rock #:at (0.1 0 0))");
        Assert.Equal("small", RoomSlots.RoomOf(s.Document, "rock"));
    }

    [Fact]
    public void WithOneStoreInTheRoomABinHoldsItAndOneUndoTakesBothBack()
    {
        var s = Session(Room, Rock);
        var plan = RoomSlots.Defaults(s.Document, "enclosure_1", Probe("heat-bin"));
        Assert.Equal([("holds", "heat_store_2")], plan.Chosen);
        Assert.Empty(plan.Open);
        string command = RoomSlots.Command("(heat-bin heat_bin_3 #:at (254.62 -44.4 137.9) #:material bronze)", "heat_bin_3", plan);
        Assert.Equal("(begin (heat-bin heat_bin_3 #:at (254.62 -44.4 137.9) #:material bronze) (set heat_bin_3 #:holds heat_store_2))", command);
        s.Execute(command);
        Assert.Equal("heat_store_2", s.Document.Parts["heat_bin_3"].Symbol("holds", ""));
        s.Execute("(undo)");
        Assert.False(s.Document.Parts.ContainsKey("heat_bin_3"));
    }

    [Fact]
    public void WithTwoStoresTheBinsHoldsStaysBlankAndNamesBothCandidates()
    {
        var s = Session(Room, Rock, "(heat-store heat_store_4 #:at (254.4 -44.4 137.9))");
        var plan = RoomSlots.Defaults(s.Document, "enclosure_1", Probe("heat-bin"));
        Assert.Empty(plan.Chosen);
        var open = Assert.Single(plan.Open);
        Assert.Equal("holds", open.Key);
        Assert.Equal(["heat_store_2", "heat_store_4"], open.Candidates);
        Assert.Equal("(heat-bin b)", RoomSlots.Command("(heat-bin b)", "b", plan));   // the bare command: nothing added
    }

    [Fact]
    public void AStoreAlreadyInABinIsNotACandidateForAnother()
    {
        var s = Session(Room, Rock, "(heat-bin heat_bin_3 #:at (254.62 -44.4 137.9) #:holds heat_store_2)");
        Assert.Empty(RoomSlots.Candidates(s.Document, "enclosure_1", "holds"));
        var plan = RoomSlots.Defaults(s.Document, "enclosure_1", Probe("heat-bin"));
        Assert.Empty(plan.Chosen);
        Assert.Empty(Assert.Single(plan.Open).Candidates);
    }

    [Fact]
    public void AStripSensesTheRoomAndDrivesTheOnlyBin()
    {
        var s = Session(Room, Rock, "(heat-bin heat_bin_3 #:at (254.62 -44.4 137.9) #:holds heat_store_2)");
        var plan = RoomSlots.Defaults(s.Document, "enclosure_1", Probe("bimetal"));
        Assert.Equal([("senses", "enclosure_1"), ("drives", "heat_bin_3")], plan.Chosen);
        Assert.Equal(["enclosure_1", "heat_store_2"], RoomSlots.Candidates(s.Document, "enclosure_1", "senses"));   // the room first
        string command = RoomSlots.Command("(bimetal bimetal_4 #:at (254.31 -44.4 137.9))", "bimetal_4", plan);
        s.Execute(command);
        Assert.Equal("enclosure_1", s.Document.Parts["bimetal_4"].Symbol("senses", ""));
        Assert.Equal("heat_bin_3", s.Document.Parts["bimetal_4"].Symbol("drives", ""));
    }

    [Fact]
    public void ABinWithAStripOnItsLidIsNotACandidateForAnotherStrip()
    {
        var s = Session(Room, Rock, "(heat-bin heat_bin_3 #:at (254.62 -44.4 137.9) #:holds heat_store_2)",
                        "(bimetal bimetal_4 #:at (254.31 -44.4 137.9) #:senses enclosure_1 #:drives heat_bin_3)");
        Assert.Empty(RoomSlots.Candidates(s.Document, "enclosure_1", "drives"));
    }

    [Fact]
    public void AKindWithNoBlankReferencesGetsTheBareCommand()
    {
        var s = Session(Room);
        var plan = RoomSlots.Defaults(s.Document, "enclosure_1", Probe("block"));
        Assert.Empty(plan.Chosen);
        Assert.Empty(plan.Open);
    }
}
