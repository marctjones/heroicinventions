using Godot;
using HeroicInventions.Sim.Machines;

namespace HeroicInventions;

/// <summary>One entry of the scenario-select screen: the world file to load, and what the player is told about it.</summary>
public sealed record ScenarioInfo(string World, string Title, string Description);

/// <summary>
/// Which world files are scenarios (issue #95), behind one adapter so that the scenario format (issue #60's
/// <c>WorldDef.Scenario</c>) can arrive without the front end changing. This file is the whole of the coupling.
///
/// Until #60 lands, <c>WorldDef</c> has no <c>Scenario</c> and the list is the built-in one: the Lonely Rover's opening.
/// <see cref="ReadScenario"/> is the single method that reads a parsed world's scenario form. It reads <c>WorldDef.Scenario</c>
/// reflectively (a non-null <c>Scenario</c> with a <c>Title</c> and a <c>Description</c> is a scenario), so it already works
/// once the property exists. At the merge, if #60's type differs, replace its body with the direct form, one line:
/// <c>world.Scenario is { } s ? new ScenarioInfo(world.Name, s.Title, s.Description) : null</c>
/// </summary>
public static class ScenarioList
{
    /// <summary>The first scenario, from docs/lonely-rover.html (the Premise), for when no world file declares itself one.</summary>
    public static readonly ScenarioInfo LonelyRover = new("lonely-rover-opening", "The Lonely Rover",
        "A Mars rover wakes up to find it can think, and that Earth has stopped answering. A dust storm has brought a dust slide down over the " +
        "cargo for a habitat that was never occupied, the battery bank among it. The rover cannot make electronics, only mechanisms, so it builds " +
        "itself machines out of weight, water, air and heat, and tries to charge the bank and call Earth on the 03:00 relay pass.");

    private static IReadOnlyList<ScenarioInfo>? _cached;

    /// <summary>The scenarios the game offers: every world file that declares one, or the built-in list if none does.</summary>
    public static IReadOnlyList<ScenarioInfo> Discover(string worldsDir)
    {
        if (_cached is not null) return _cached;
        var found = new List<ScenarioInfo>();
        using var dir = DirAccess.Open(worldsDir);
        if (dir is not null)
        {
            foreach (string file in DirAccess.GetFilesAt(worldsDir).Where(f => f.EndsWith(".world")).Order())
            {
                string path = $"{worldsDir}/{file}";
                try
                {
                    var world = WorldDef.Parse(Godot.FileAccess.GetFileAsString(path), path);
                    if (ReadScenario(world) is { } info) found.Add(info);
                }
                catch (Exception e) when (e is FormatException or InvalidOperationException) { /* a test world, or one the parser does not take: not a scenario */ }
            }
        }
        if (found.Count == 0 && Godot.FileAccess.FileExists($"{worldsDir}/{LonelyRover.World}.world")) found.Add(LonelyRover);
        return _cached = found.OrderBy(s => s.World == LonelyRover.World ? 0 : 1).ThenBy(s => s.Title).ToList();
    }

    /// <summary>The one method that knows #60's format: a world's scenario form as the screen shows it, or null if the world is not a scenario.</summary>
    private static ScenarioInfo? ReadScenario(WorldDef world)
    {
        return world.Scenario is { Title.Length: > 0 } s ? new ScenarioInfo(world.Name, s.Title, s.Description ?? "") : null;   // #60's scenario form
    }
}
