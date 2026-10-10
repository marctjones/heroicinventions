using System.Text.RegularExpressions;
using HeroicInventions.Sim.Game;

namespace HeroicInventions.Sim.Tests;

/// <summary>
/// Issue #249: the goals panel is written for the player. Title, Plain and Story are shown (Trigger only behind HEROIC_GOALS_DEV=1), so none of
/// them may carry a developer's field name: no angle brackets (<c>&lt;bank&gt;.temperature</c>), no Type.Field (<c>CrateReading.Cover</c>), no
/// dotted lower-case field (<c>scene.won</c>), no comparison written as code (<c>&lt;=</c>), no hash-keyword (<c>#:driven-by</c>).
/// </summary>
public class GoalsTextTests
{
    private static readonly Regex Dev = new(@"[<>]|\b[A-Z][a-z]+(?:[A-Z][a-z]+)*\.[A-Za-z]|\b[a-z][a-z-]*\.[a-z][a-z-]*\b|#:|=", RegexOptions.Compiled);

    public static IEnumerable<object[]> Goals() => GoalTracker.Catalogue.Select(g => new object[] { g.Id });

    [Theory, MemberData(nameof(Goals))]
    public void The_player_text_of_a_goal_has_no_field_names(string id)
    {
        var g = GoalTracker.Def(id);
        foreach (var (what, text) in new[] { ("Title", g.Title), ("Plain", g.Plain), ("Story", g.Story) })
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"{id}: {what} is empty");
            Assert.False(Dev.IsMatch(text), $"{id}: {what} reads like a developer trigger: {text}");
        }
    }

    [Fact]
    public void The_detector_catches_the_old_panel_text()
    {
        foreach (var bad in new[] { "a bank's temperature stayed (<bank>.temperature)", "top is clear (CrateReading.Cover <= 0)", "a bank made the call (scene.won)", "(<bank>.from-wind over the sum)" })
            Assert.True(Dev.IsMatch(bad), bad);
    }

    [Fact]
    public void The_developer_trigger_is_kept_and_is_not_the_plain_text()
    {
        foreach (var g in GoalTracker.Catalogue)
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Trigger), g.Id);
            Assert.NotEqual(g.Trigger, g.Plain);
        }
        Assert.Contains(GoalTracker.Catalogue, g => g.Trigger.Contains("CrateReading.Cover"));   // the developer text is still there, for the docs and HEROIC_GOALS_DEV
    }
}
