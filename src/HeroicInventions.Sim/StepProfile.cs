using System.Diagnostics;

namespace HeroicInventions.Sim;

/// <summary>
/// Timing hooks for finding what a physics tick spends (issue #188). Off unless a host sets <see cref="Sink"/>
/// (the game does, under HEROIC_TICK_PROFILE=1): then <see cref="Start"/> and <see cref="Stop"/> report a named
/// piece's wall time in ms; otherwise they cost one null check. Nothing here touches the simulation's state.
/// </summary>
public static class StepProfile
{
    public static Action<string, double>? Sink { get; set; }

    public static long Start() => Sink is null ? 0 : Stopwatch.GetTimestamp();

    public static void Stop(string name, long start)
    {
        if (Sink is { } sink) sink(name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
}
