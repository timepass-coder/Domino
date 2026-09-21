using System;
using Relay.Sim;
using Xunit;
using Fix = FixMath.F64;
using Vec2 = FixMath.F64Vec2;

public class DominoContactTests
{
    // The smallest push that actually topples this integrator, plus a margin.
    // Taken from SimulationTests.MeasuredTipOmegaRaw - the analytic
    // DominoTipOmega is 2.3% too low and a kick at that value falls back.
    static readonly Fix Kick = Fix.FromRaw(4245447016L) + Fix.Ratio100(20);

    static readonly Fix Typical = Fix.Ratio100(80); // working chain spacing

    static Fix RGeom =>
        SimConstants.DominoThickness + SimConstants.DominoHeight;

    static Domino At(int id, Fix x, Fix y, Fix omega)
        => new Domino(
            id,
            new Vec2(x, y),
            Fix.Zero,
            omega,
            SimConstants.DominoHeight,
            SimConstants.DominoThickness,
            SimConstants.DefaultMass,
            DominoState.Standing);

    static SimState World(params Domino[] dominoes)
        => new SimState(
            0,
            SimConstants.Gravity,
            Array.Empty<Ball>(),
            dominoes,
            Array.Empty<Surface>(),
            0,
            SimPhase.Running);

    /// <summary>
    /// n dominoes on y=0, evenly spaced, only the first one kicked.
    /// </summary>
    static SimState Chain(int n, Fix spacing, Fix kick)
    {
        var ds = new Domino[n];

        for (int i = 0; i < n; i++)
        {
            ds[i] = At(
                i,
                spacing * Fix.FromInt(i),
                Fix.Zero,
                i == 0 ? kick : Fix.Zero);
        }

        return World(ds);
    }

    static int CountFallen(SimState s)
    {
        int n = 0;

        for (int i = 0; i < s.Dominoes.Length; i++)
        {
            if (s.Dominoes[i].State == DominoState.Fallen)
                n++;
        }

        return n;
    }

    // ------------------------------------------------------------------- geometry

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(600)]
    [InlineData(1200)]
    public void ArmIsBothTheContactHeightAndTheMomentArm(int thetaMilli)
    {
        // These are the same quantity - h*cos(theta) - which is why the impulse
        // only needs one of them. If they ever diverge the transfer is wrong.
        Fix theta = Fix.Ratio1000(thetaMilli);

        var d = new Domino(
            0,
            Vec2.Zero,
            theta,
            Fix.Zero,
            SimConstants.DominoHeight,
            SimConstants.DominoThickness,
            SimConstants.DefaultMass,
            DominoState.Toppling);

        Assert.Equal(
            (SimConstants.DominoHeight * Fix.Cos(theta)).Raw,
            d.Arm.Raw);

        // The leading corner starts at (0, h) from the pivot and rotates to
        // (h sin theta, h cos theta): the y of that is exactly Arm.
        Fix cornerOffset =
            d.LeadingCornerX(1) - d.PivotX(1);

        Assert.Equal(
            (SimConstants.DominoHeight * Fix.Sin(theta)).Raw,
            cornerOffset.Raw);
    }

    [Fact]
    public void PivotAndStruckFaceSitOnOppositeSidesOfTheBase()
    {
        var d = At(0, Fix.FromInt(3), Fix.Zero, Fix.Zero);

        // Falling right: it turns about its right corner, and it is hit on its left.
        Assert.Equal(
            (Fix.FromInt(3) + d.HalfThickness).Raw,
            d.PivotX(1).Raw);

        Assert.Equal(
            (Fix.FromInt(3) - d.HalfThickness).Raw,
            d.StruckFaceX(1).Raw);

        // Falling left: both mirror.
        Assert.Equal(
            (Fix.FromInt(3) - d.HalfThickness).Raw,
            d.PivotX(-1).Raw);

        Assert.Equal(
            (Fix.FromInt(3) + d.HalfThickness).Raw,
            d.StruckFaceX(-1).Raw);
    }

    [Fact]
    public void InertiaComesFromTheDominosOwnDimensions()
    {
        // Not from SimConstants: Phase 3's tall piece must work without editing
        // the contact code. (h^2 + t^2)/3 per unit mass.
        Fix h = Fix.Ratio100(160);
        Fix t = Fix.Ratio100(30);

        var tall = new Domino(
            0,
            Vec2.Zero,
            Fix.Zero,
            Fix.Zero,
            h,
            t,
            Fix.FromInt(2),
            DominoState.Standing);

        Fix expected =
            Fix.Div(
                h * h + t * t,
                Fix.FromInt(3));

        Assert.Equal(
            expected.Raw,
            tall.InertiaPerMass.Raw);

        Assert.Equal(
            (Fix.FromInt(2) * expected).Raw,
            tall.Inertia.Raw);
    }

    // --------------------------------------------------------------------- a pair

    [Fact]
    public void TwoDominoesAtTypicalSpacingBothFall()
    {
        SimState s =
            Simulation.StepMany(
                Chain(2, Typical, Kick),
                1200);

        Assert.Equal(
            DominoState.Fallen,
            s.Dominoes[0].State);

        Assert.Equal(
            DominoState.Fallen,
            s.Dominoes[1].State);

        Assert.Equal(2, s.ChainCount);
        Assert.Equal(SimPhase.Settled, s.Phase);
    }

    [Fact]
    public void TheStruckDominoDoesNotMoveBeforeTheCornerArrives()
    {
        // No spooky action at a distance. Until the leading corner actually
        // reaches the back face, the second domino is bit-for-bit untouched.
        SimState s = Chain(2, Typical, Kick);
        int contactTick = -1;

        for (int i = 0; i < 400 && contactTick < 0; i++)
        {
            s = Simulation.Step(in s);

            if (s.Dominoes[1].Omega.Raw != 0)
            {
                contactTick = s.Tick;
            }
            else
            {
                Assert.Equal(
                    0L,
                    s.Dominoes[1].Theta.Raw);

                Assert.Equal(
                    DominoState.Standing,
                    s.Dominoes[1].State);
            }
        }

        Assert.Equal(73, contactTick);

        // At the moment it is first touched it has not rotated yet - the impulse
        // changes omega this tick, theta only next tick.
        Assert.Equal(
            0L,
            s.Dominoes[1].Theta.Raw);

        // And the geometry agrees: corner has reached face.
        Fix corner =
            s.Dominoes[0].LeadingCornerX(1);

        Fix face =
            s.Dominoes[1].StruckFaceX(1);

        Assert.True(
            corner.Raw >= face.Raw,
            $"corner {corner.Double} should have reached face {face.Double}");
    }

    [Theory]
    [InlineData(50)]  // early contact, arm still long
    [InlineData(80)]
    [InlineData(115)] // very late contact, arm nearly gone
    public void AnEqualMassHitSplitsTheAngularVelocityEvenly(
        int spacingHundredths)
    {
        // Fully inelastic between identical dominoes: the contact points must end
        // up moving together, and with equal arms and equal inertia that means
        // equal omega. Half the pair's kinetic energy is destroyed, as e = 0 demands.
        //
        // Note this holds at EVERY spacing. The moment arm cancels out of the
        // angular transfer - it scales the closing speed down and the lever on the
        // struck domino down by the same factor - so a glancing blow at the end of
        // the fall hands over just as much omega as a square hit. That is the
        // headline limitation of impulse-only transfer; see UNITS.md.
        SimState s =
            Chain(
                2,
                Fix.Ratio100(spacingHundredths),
                Kick);

        for (int i = 0; i < 600; i++)
        {
            s = Simulation.Step(in s);

            if (s.Dominoes[1].Omega.Raw == 0)
                continue;

            long w0 = s.Dominoes[0].Omega.Raw;
            long w1 = s.Dominoes[1].Omega.Raw;

            // Not bit-identical: the two deltas are divided out separately and
            // round independently. 1e-6 rad/s of disagreement, on values near 5.
            Assert.True(
                System.Math.Abs(w0 - w1) < Fix.Ratio1000(1).Raw,
                $"spacing {spacingHundredths / 100.0}: omega split {w0} vs {w1}");

            return;
        }

        Assert.Fail(
            $"spacing {spacingHundredths / 100.0}: contact never happened");
    }

    [Fact]
    public void AChainCanFallLeftwardsToo()
    {
        // Mirror of the pair test. dir is derived from the sign of theta/omega, so
        // a sign error here shows up as a chain that only works one way.
        SimState s =
            Simulation.StepMany(
                World(
                    At(0, Fix.Zero, Fix.Zero, -Kick),
                    At(1, -Typical, Fix.Zero, Fix.Zero)),
                1200);

        Assert.Equal(
            DominoState.Fallen,
            s.Dominoes[1].State);

        Assert.True(
            s.Dominoes[1].Theta.Raw < 0,
            "second domino should lean left");

        Assert.Equal(
            SimPhase.Settled,
            s.Phase);
    }

    [Fact]
    public void ADominoFallingAwayNeverTouchesTheNeighbourBehindIt()
    {
        // d0 is kicked to the LEFT with d1 sitting to its right. Without the
        // "only what lies ahead" test in InContact, d1 would topple backwards.
        SimState s =
            Simulation.StepMany(
                World(
                    At(0, Fix.Zero, Fix.Zero, -Kick),
                    At(1, Typical, Fix.Zero, Fix.Zero)),
                1200);

        Assert.Equal(
            DominoState.Fallen,
            s.Dominoes[0].State);

        Assert.Equal(
            DominoState.Standing,
            s.Dominoes[1].State);

        Assert.Equal(
            0L,
            s.Dominoes[1].Theta.Raw);

        Assert.Equal(
            1,
            s.ChainCount);
    }

    [Fact]
    public void NothingHappensWithoutAKick()
    {
        SimState s =
            Simulation.StepMany(
                Chain(5, Typical, Fix.Zero),
                1200);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(
                0L,
                s.Dominoes[i].Theta.Raw);

            Assert.Equal(
                DominoState.Standing,
                s.Dominoes[i].State);
        }

        Assert.Equal(0, s.ChainCount);
        Assert.Equal(SimPhase.Settled, s.Phase);
    }

    [Fact]
    public void DominoesOnDifferentLevelsDoNotInteract()
    {
        // Phase 0 keeps a chain on one shelf. Without the Base.Y test a domino
        // would topple something on a shelf metres below it.
        SimState s =
            Simulation.StepMany(
                World(
                    At(0, Fix.Zero, Fix.Zero, Kick),
                    At(1, Typical, Fix.FromInt(4), Fix.Zero)),
                1200);

        Assert.Equal(
            DominoState.Fallen,
            s.Dominoes[0].State);

        Assert.Equal(
            DominoState.Standing,
            s.Dominoes[1].State);

        Assert.Equal(1, s.ChainCount);
    }

    // ---------------------------------------------------------------------- reach

    [Fact]
    public void TheMeasuredReachIsJustUnderTheGeometricLimit()
    {
        // MEASURED, and the number is a surprise: UNITS.md predicted 0.85-1.10 on
        // the grounds that a near-horizontal corner strikes with almost no moment
        // arm. It does - but the arm cancels out of the transfer (see the even-split
        // test), so the chain keeps working right up to R_geom.
        //
        // At exactly R_geom the corner only arrives when the domino is flat, where
        // Arm is 0 and TransferImpulse bails out. So the reach is the last spacing
        // strictly below it. Step 10 still owns the real R: it has to decide whether
        // a model that propagates at the geometric limit is acceptable, because real
        // dominoes fail well before it.
        Assert.Equal(
            Fix.Ratio100(118).Raw,
            RGeom.Raw);

        Assert.Equal(
            5,
            CountFallen(
                Simulation.StepMany(
                    Chain(5, Fix.Ratio1000(1175), Kick),
                    4000)));

        Assert.Equal(
            1,
            CountFallen(
                Simulation.StepMany(
                    Chain(5, Fix.Ratio1000(1180), Kick),
                    4000)));
    }

    [Theory]
    [InlineData(1180)] // exactly R_geom: corner arrives with no arm left
    [InlineData(1300)]
    [InlineData(2000)]
    public void SpacingAtOrBeyondTheGeometricLimitBreaksTheChain(
        int spacingMilli)
    {
        SimState s =
            Simulation.StepMany(
                Chain(
                    5,
                    Fix.Ratio1000(spacingMilli),
                    Kick),
                4000);

        Assert.Equal(1, CountFallen(s));
        Assert.Equal(1, s.ChainCount);

        // A broken chain must still settle.
        Assert.Equal(
            SimPhase.Settled,
            s.Phase);
    }

    // ---------------------------------------------------------------------- chains

    [Fact]
    public void AChainOfFiveTopplesInIndexOrder()
    {
        SimState s =
            Chain(5, Typical, Kick);

        var firstMotion = new int[5];

        for (int i = 0; i < 5; i++)
            firstMotion[i] = -1;

        for (int i = 0; i < 2400; i++)
        {
            s = Simulation.Step(in s);

            for (int k = 0; k < 5; k++)
            {
                if (firstMotion[k] < 0 &&
                    s.Dominoes[k].Omega.Raw != 0)
                {
                    firstMotion[k] = s.Tick;
                }
            }
        }

        Assert.Equal(5, CountFallen(s));
        Assert.Equal(5, s.ChainCount);

        for (int k = 1; k < 5; k++)
        {
            Assert.True(
                firstMotion[k] > firstMotion[k - 1],
                $"d{k} started at {firstMotion[k]}, " +
                $"d{k - 1} at {firstMotion[k - 1]}");
        }
    }

    [Fact]
    public void TheTopplingWaveReachesASteadySpeed()
    {
        // A chain is a travelling wave, and the interesting question is whether it
        // converges or runs away. Measured: gaps of 72, 33, 21, 19, then a flat 18
        // ticks for the rest - 0.15 s per domino, 5.3 dominoes/s at 0.8 spacing.
        // A gap that kept shrinking would mean the impulse was manufacturing energy.
        const int N = 14;

        SimState s =
            Chain(N, Typical, Kick);

        var firstMotion = new int[N];

        for (int i = 0; i < N; i++)
            firstMotion[i] = -1;

        for (int i = 0; i < 6000; i++)
        {
            s = Simulation.Step(in s);

            for (int k = 0; k < N; k++)
            {
                if (firstMotion[k] < 0 &&
                    s.Dominoes[k].Omega.Raw != 0)
                {
                    firstMotion[k] = s.Tick;
                }
            }
        }

        Assert.Equal(N, CountFallen(s));

        for (int k = 5; k < N; k++)
        {
            Assert.Equal(
                18,
                firstMotion[k] - firstMotion[k - 1]);
        }
    }

    [Fact]
    public void NoInteriorDominoSpinsFasterThanOneFallingAlone()
    {
        // The no-energy-from-nowhere check. Every interior domino gives half its
        // spin away when it lands on its neighbour, so none of them should ever
        // exceed what a single kicked domino reaches under gravity alone.
        //
        // The LAST domino legitimately does exceed it: it arrives with a boost,
        // then falls its full height with nothing to hand energy on to. Contact is
        // also sustained - the pair keeps re-impulsing while the faller lies across
        // it - so the end of a chain is the loudest part. Hence "interior".
        Fix lonePeak = Fix.Zero;

        SimState lone =
            Chain(1, Typical, Kick);

        for (int i = 0; i < 1200; i++)
        {
            lone = Simulation.Step(in lone);

            Fix w =
                Fix.Abs(lone.Dominoes[0].Omega);

            if (w.Raw > lonePeak.Raw)
                lonePeak = w;
        }

        Assert.InRange(
            lonePeak.Raw,
            Fix.Ratio100(676).Raw,
            Fix.Ratio100(677).Raw);

        const int N = 12;

        SimState s =
            Chain(N, Typical, Kick);

        var peak = new Fix[N];

        for (int i = 0; i < 6000; i++)
        {
            s = Simulation.Step(in s);

            for (int k = 0; k < N; k++)
            {
                Fix w =
                    Fix.Abs(s.Dominoes[k].Omega);

                if (w.Raw > peak[k].Raw)
                    peak[k] = w;
            }
        }

        for (int k = 0; k < N - 2; k++)
        {
            Assert.True(
                peak[k].Raw < lonePeak.Raw,
                $"d{k} peaked at {peak[k].Double:F4}, " +
                $"above the lone {lonePeak.Double:F4}");
        }
    }

    // ------------------------------------------------------------------ determinism

    [Fact]
    public void AChainIsBitIdenticalTickByTickAcrossRuns()
    {
        // Contacts are the easiest place to lose determinism: one Dictionary, one
        // sort by distance, one parallel loop, and this fails.
        SimState a =
            Chain(8, Typical, Kick);

        SimState b =
            Chain(8, Typical, Kick);

        for (int i = 0; i < 2400; i++)
        {
            a = Simulation.Step(in a);
            b = Simulation.Step(in b);

            Assert.Equal(
                a.Hash(),
                b.Hash());
        }
    }

    [Fact]
    public void ADeclarationOrderChangeDoesNotChangeTheOutcome()
    {
        // Same eight dominoes, handed to the sim back to front. The physics must
        // not care which index a domino happens to hold - only the geometry.
        // (Index order is the tiebreak WITHIN a tick; it must not alter the result.)
        var forward = new Domino[8];
        var reverse = new Domino[8];

        for (int i = 0; i < 8; i++)
        {
            Fix x =
                Typical * Fix.FromInt(i);

            Fix w =
                i == 0 ? Kick : Fix.Zero;

            forward[i] =
                At(i, x, Fix.Zero, w);

            reverse[7 - i] =
                At(7 - i, x, Fix.Zero, w);
        }

        SimState f =
            Simulation.StepMany(
                World(forward),
                2400);

        SimState r =
            Simulation.StepMany(
                World(reverse),
                2400);

        Assert.Equal(
            8,
            CountFallen(f));

        Assert.Equal(
            8,
            CountFallen(r));

        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(
                f.Dominoes[i].Theta.Raw,
                r.Dominoes[7 - i].Theta.Raw);
        }
    }

    [Fact]
    public void AFallenChainStaysFrozenOnceSettled()
    {
        SimState s =
            Chain(5, Typical, Kick);

        int settledAt = -1;

        for (int i = 0; i < 4000 && settledAt < 0; i++)
        {
            s = Simulation.Step(in s);

            if (s.Phase == SimPhase.Settled)
                settledAt = s.Tick;
        }

        Assert.True(
            settledAt > 0,
            "a five-domino chain must settle");

        ulong h = s.Hash();

        s =
            Simulation.StepMany(
                s,
                1200);

        Assert.Equal(
            h,
            s.Hash()); // Settled is terminal, tick included

        Assert.Equal(
            settledAt,
            s.Tick);
    }
}