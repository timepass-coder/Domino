using System;
using Relay.Sim;
using Xunit;
using Xunit.Abstractions;
using Fix = FixMath.F64;
using Vec2 = FixMath.F64Vec2;

/// <summary>
/// Step 10: measures R, the largest base-to-base spacing a chain survives, and the
/// smallest push that starts a chain at each spacing. Run with
/// <c>--logger "console;verbosity=detailed"</c> to see the tables.
/// </summary>
public class ReachDerivationTests
{
    readonly ITestOutputHelper _out;

    public ReachDerivationTests(ITestOutputHelper output) => _out = output;

    // The smallest omega that tips one domino on its own in this integrator
    // (SimulationTests.MeasuredTipOmegaRaw), and the usual kick: that plus a margin.
    static readonly Fix LoneTip = Fix.FromRaw(4245447016L);
    static readonly Fix Kick = LoneTip + Fix.Ratio100(20);

    static Fix T => SimConstants.DominoThickness;
    static Fix RGeom => SimConstants.DominoThickness + SimConstants.DominoHeight;

    const int MaxTicks = SimConstants.TicksPerSecond * 60;

    /// <summary>
    /// n dominoes on y=0, evenly spaced base to base, only the first one kicked.
    /// </summary>
    static SimState Chain(int n, Fix spacing, Fix kick)
    {
        var ds = new Domino[n];

        for (int i = 0; i < n; i++)
        {
            ds[i] = new Domino(
                i,
                new Vec2(spacing * Fix.FromInt(i), Fix.Zero),
                Fix.Zero,
                i == 0 ? kick : Fix.Zero,
                SimConstants.DominoHeight,
                SimConstants.DominoThickness,
                SimConstants.DefaultMass,
                DominoState.Standing);
        }

        return new SimState(
            0,
            SimConstants.Gravity,
            Array.Empty<Ball>(),
            ds,
            Array.Empty<Surface>(),
            0,
            SimPhase.Running);
    }

    /// <summary>
    /// Runs to rest and reports whether every domino fell. Runs to Settled rather than a
    /// fixed tick count, so a slow chain is not cut off mid-wave and misread as broken.
    /// </summary>
    static bool FullyTopples(int n, Fix spacing, Fix kick)
    {
        SimState s = Chain(n, spacing, kick);

        for (int i = 0; i < MaxTicks && s.Phase == SimPhase.Running; i++)
            s = Simulation.Step(in s);

        Assert.True(
            s.Phase == SimPhase.Settled,
            $"n={n} spacing={spacing.Double}: {s.Phase} after {MaxTicks} ticks");

        for (int i = 0; i < s.Dominoes.Length; i++)
            if (s.Dominoes[i].State != DominoState.Fallen)
                return false;

        return true;
    }

    /// <summary>
    /// 30 rounds of bisection on raw fixed-point. good passes, bad fails.
    /// </summary>
    static Fix Bisect(Fix good, Fix bad, Func<Fix, bool> passes)
    {
        Assert.True(passes(good), $"{good.Double} should pass");
        Assert.False(passes(bad), $"{bad.Double} should fail");

        for (int i = 0; i < 30; i++)
        {
            Fix mid = Fix.FromRaw(
                good.Raw + (bad.Raw - good.Raw) / 2);

            if (passes(mid))
                good = mid;
            else
                bad = mid;
        }

        return good;
    }

    // ------------------------------------------------------------------------- R

    /// <summary>
    /// The build guide's lower bound was t + 0.01, "certain to work". It is not: below
    /// about 0.38 the first domino reaches the second before passing its own tipping
    /// point, and the usual kick is no longer enough to start the chain. That is a
    /// start-up limit, measured separately below, not a reach limit - so R is searched
    /// from 0.40, where the usual kick starts every chain.
    /// </summary>
    static readonly Fix ReachLo = Fix.Ratio100(40);

    static Fix MaxSpacing(int n) =>
        Bisect(ReachLo, RGeom, sp => FullyTopples(n, sp, Kick));

    [Fact]
    public void DeriveR()
    {
        Fix g = Fix.Ratio10(50); // G = 5.0, the table's reference gap
        int[] lengths = { 2, 5, 10 };

        _out.WriteLine(
            $"t = {T.Double:F4}   h = {SimConstants.DominoHeight.Double:F4}   " +
            $"R_geom = t + h = {RGeom.Double:F4}");

        _out.WriteLine("| Chain length | Max spacing | slack at G = 5.0 |");
        _out.WriteLine("|---|---|---|");

        foreach (int n in lengths)
        {
            Fix r = MaxSpacing(n);
            Fix slack = r - Fix.Div(g, Fix.FromInt(n + 1));

            _out.WriteLine(
                $"| {n} | {r.Double:F6} | {slack.Double:F6} |");

            // Pinned to the current model, which reaches right up to the geometric
            // limit. If Step 10's decision changes the transfer, this is meant to fail.
            Assert.True(
                r.Raw >= Fix.Ratio1000(1175).Raw &&
                r.Raw < RGeom.Raw,
                $"n={n}: R = {r.Double}, expected in [1.175, {RGeom.Double})");
        }
    }

    // ---------------------------------------------------------------- start-up push

    /// <summary>
    /// The edge is not clean. Contact is tested once per tick, so near the threshold a
    /// slightly harder push can fail where a softer one worked: at 0.35, kicks of 1.410
    /// and 1.415 start the chain, 1.420 and 1.425 do not, 1.430 does. Read the table as
    /// good to about +-0.02, and only assert what holds well clear of that.
    /// </summary>
    [Fact]
    public void SmallestPushThatStartsAChain()
    {
        int[] spacingsMilli = { 190, 250, 300, 350, 380, 500, 800, 1100 };
        Fix hard = LoneTip * Fix.FromInt(8); // starts a chain at every spacing here

        _out.WriteLine("| Spacing | Smallest start push | x lone tip |");
        _out.WriteLine("|---|---|---|");

        Fix previous = hard;

        foreach (int ms in spacingsMilli)
        {
            Fix sp = Fix.Ratio1000(ms);

            // Bisect wants good below bad, so search on (hard - kick): bigger is weaker.
            Fix spare = Bisect(
                Fix.Zero,
                hard,
                d => FullyTopples(5, sp, hard - d));

            Fix min = hard - spare;
            Fix ratio = Fix.Div(min, LoneTip);

            _out.WriteLine(
                $"| {sp.Double:F3} | {min.Double:F4} | x{ratio.Double:F2} |");

            // Wider spacing never needs a harder push.
            Assert.True(
                min.Raw <= previous.Raw,
                $"spacing {sp.Double}: needs {min.Double}, more than {previous.Double} closer in");

            previous = min;

            // At 0.38 and beyond, contact comes after the first domino has passed its
            // own tipping point, so a push that tips one domino starts the whole chain.
            if (ms >= 380)
            {
                Assert.True(
                    min.Raw <= Kick.Raw,
                    $"spacing {sp.Double}: needs {min.Double}, the usual kick is {Kick.Double}");
            }

            // The close end, pinned so it cannot quietly get worse (or better).
            if (ms == 190)
            {
                Assert.True(
                    ratio.Raw > Fix.FromInt(4).Raw &&
                    ratio.Raw < Fix.FromInt(5).Raw,
                    $"spacing 0.19 needs x{ratio.Double} the lone tip push, expected x4-x5");
            }
        }
    }
}