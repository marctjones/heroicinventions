namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// The grade of the ground ahead of a vehicle, for the rover's panel: the steepest one-metre run of ground along its heading over
/// the next few metres, uphill positive, in degrees. The steepest run, not the mean: a 1 m bank at 45 degrees averages only 18
/// degrees over 3 m, but it is the part the tyres meet, and the part that stops the rover.
/// </summary>
public static class GroundGrade
{
    public const double Run = 1.0;     // m: the horizontal length one grade is measured over
    public const double Reach = 3.0;   // m: how far ahead the runs start
    public const double Step = 0.5;    // m: between one run's start and the next's

    /// <summary>The steepest run's grade (degrees, up positive) from (x, z) along the horizontal unit heading (fx, fz).</summary>
    public static double Ahead(Func<double, double, double> heightAt, double x, double z, double fx, double fz, double reach = Reach)
    {
        double worst = 0;
        for (double d = 0; d + Run <= reach + 1e-9; d += Step)
        {
            double rise = heightAt(x + fx * (d + Run), z + fz * (d + Run)) - heightAt(x + fx * d, z + fz * d);
            double deg = Math.Atan2(rise, Run) * 180 / Math.PI;
            if (Math.Abs(deg) > Math.Abs(worst)) worst = deg;
        }
        return worst;
    }
}
