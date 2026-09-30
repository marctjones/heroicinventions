namespace HeroicInventions.Sim.Fluids;

/// <summary>
/// The pieces of a shallow-water (Saint-Venant) solver, written once for
/// the 1-D channel (issue #36) and meant for the 2-D ground water (#37) too.
/// Water is a depth h and a discharge per unit width q = h·u. Cells trade
/// water through their faces by the HLL approximate Riemann flux; where the
/// bed steps between cells, each side's depth is first cut to what stands
/// above the higher bed (Audusse's hydrostatic reconstruction, 2004), so a
/// still pond on a sloping bed stays still and a wet cell never hands on
/// more than it holds, letting a front run out over dry ground. Friction is
/// Manning's, applied semi-implicitly so a thin sheet can't reverse. The
/// boundary helpers give the water's state where a channel meets a tank:
/// from the tank's head and the characteristic arriving from inside
/// (Bernoulli into the channel, critical at a free brink, the tank's level
/// where it is drowned).
/// </summary>
public static class ShallowWater
{
    /// <summary>Depths below this count as dry, m.</summary>
    public const double Dry = 1e-6;

    public static double Velocity(double h, double q) => h > Dry ? q / h : 0;

    /// <summary>The HLL flux (mass, momentum) across a face between two states on one bed level.</summary>
    public static (double Mass, double Momentum) Hll(double hL, double uL, double hR, double uR, double g)
    {
        bool dryL = hL <= Dry, dryR = hR <= Dry;
        if (dryL && dryR) return (0, 0);
        if (dryL) uL = 0;
        if (dryR) uR = 0;
        double cL = Math.Sqrt(g * Math.Max(0, hL)), cR = Math.Sqrt(g * Math.Max(0, hR));
        double sL = dryL ? uR - 2 * cR : Math.Min(uL - cL, uR - cR);
        double sR = dryR ? uL + 2 * cL : Math.Max(uL + cL, uR + cR);
        double mL = hL * uL, mR = hR * uR;
        double pL = hL * uL * uL + g * hL * hL / 2, pR = hR * uR * uR + g * hR * hR / 2;
        if (sL >= 0) return (mL, pL);
        if (sR <= 0) return (mR, pR);
        double d = sR - sL;
        return ((sR * mL - sL * mR + sL * sR * (hR - hL)) / d,
                (sR * pL - sL * pR + sL * sR * (mR - mL)) / d);
    }

    /// <summary>
    /// The flux across a face between cells on beds zL and zR, by hydrostatic
    /// reconstruction: the mass flux, and the momentum flux as each side sees
    /// it (they differ by the push of the step in the bed).
    /// </summary>
    public static (double Mass, double MomentumLeft, double MomentumRight) Face(
        double zL, double hL, double uL, double zR, double hR, double uR, double g)
    {
        double z = Math.Max(zL, zR);
        double hl = Math.Max(0, hL + zL - z), hr = Math.Max(0, hR + zR - z);
        var (m, p) = Hll(hl, uL, hr, uR, g);
        return (m, p + g / 2 * (hL * hL - hl * hl), p + g / 2 * (hR * hR - hr * hr));
    }

    /// <summary>
    /// Manning friction over dt, semi-implicitly: q / (1 + dt·g·n²·|u| / R^(4/3)),
    /// R the hydraulic radius of a rectangular section <paramref name="width"/>
    /// wide (0: a wide sheet, R = h). Manning's n is fitted under Earth's
    /// gravity; elsewhere the speed a slope gives goes as √g, so n scales as
    /// 1/√(g/9.81).
    /// </summary>
    public static double Friction(double h, double q, double dt, double n, double width, double g)
    {
        if (h <= Dry) return 0;
        double u = q / h;
        double r = width > 0 ? width * h / (width + 2 * h) : h;
        double n2 = n * n * Physics.Gravity / g;
        return q / (1 + dt * g * n2 * Math.Abs(u) / Math.Pow(r, 4.0 / 3));
    }

    /// <summary>
    /// Where water runs out of a reservoir into a channel: the depth and
    /// speed at the mouth, given the reservoir's surface <paramref name="head"/>
    /// m above the mouth's bed and the water just inside (h, u, u positive
    /// into the channel). Energy is kept from the still reservoir to the
    /// mouth, h_b + u_b²/2g = head, and the characteristic u − 2√(gh) arrives
    /// from inside unchanged. If that would pass faster than the waves, the
    /// mouth runs critical, h_b = ⅔·head: the broad-crested weir,
    /// q = (⅔)^1.5·√g·head^1.5. A negative speed means the channel is
    /// pushing back: water runs the other way (see <see cref="Outlet"/>).
    /// </summary>
    public static (double H, double U) Inlet(double head, double h, double u, double g)
    {
        if (head <= 0) return (0, 0);
        double hc = 2 * head / 3, uc = Math.Sqrt(g * hc);
        if (h <= Dry) return (hc, uc);
        double w = u - 2 * Math.Sqrt(g * h);
        double disc = 48 * g * head - 8 * w * w;
        if (disc < 0) return (hc, w < 0 ? -1 : uc);            // pushed back hard: caller runs it the other way
        double cb = (-4 * w + Math.Sqrt(disc)) / 12;
        if (cb <= 0) return (0, -1);
        double ub = w + 2 * cb;
        if (ub >= cb) return (hc, uc);                          // free: critical at the mouth
        return (cb * cb / g, ub);
    }

    /// <summary>
    /// Where water leaves a channel over its end into a reservoir (or off the
    /// scene): the depth and outward speed at the brink, given the water just
    /// inside (h, v, v outward) and the reservoir's surface
    /// <paramref name="downstream"/> m above the brink's bed (negative or −∞:
    /// below it, a free fall). Free, a subcritical stream goes critical at the
    /// brink, h_b = (v + 2√(gh))² / 9g; a supercritical one leaves as it is.
    /// Drowned, the brink stands at the reservoir's level and the outgoing
    /// characteristic sets the speed; if that comes out negative the
    /// reservoir runs back in (<see cref="Inlet"/>), returned as a negative speed.
    /// </summary>
    public static (double H, double V) Outlet(double h, double v, double downstream, double g)
    {
        if (h <= Dry)
        {
            if (downstream <= Dry) return (0, 0);
            var (hi, ui) = Inlet(downstream, 0, 0, g);
            return (hi, -ui);
        }
        double c = Math.Sqrt(g * h), j = v + 2 * c;
        double hb, vb;
        if (v >= c) (hb, vb) = (h, v);
        else if (j <= 0) (hb, vb) = (0, 0);
        else { hb = j * j / (9 * g); vb = Math.Sqrt(g * hb); }
        if (downstream > hb)
        {
            double back = j - 2 * Math.Sqrt(g * downstream);
            if (back >= 0) return (downstream, back);
            var (hi, ui) = Inlet(downstream, h, -v, g);
            return ui > 0 ? (hi, -ui) : (downstream, 0);
        }
        return (hb, vb);
    }
}
