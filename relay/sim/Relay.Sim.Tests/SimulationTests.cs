using System;
using System.Collections.Generic;
using Relay.Sim;
using Xunit;
using Fix = FixMath.F64;
using Vec2 = FixMath.F64Vec2;

public class SimulationTests
{
    static Ball BallAt(int id, Fix x, Fix y, Fix vx, Fix vy)
        => new Ball(
            id,
            new Vec2(x, y),
            new Vec2(vx, vy),
            SimConstants.BallRadius,
            SimConstants.DefaultMass,
            Ball.Airborne,
            false);

    static Domino Upright(int id, Fix x)
        => new Domino(
            id,
            new Vec2(x, Fix.Zero),
            Fix.Zero,
            Fix.Zero,
            SimConstants.DominoHeight,
            SimConstants.DominoThickness,
            SimConstants.DefaultMass,
            DominoState.Standing);

    /// <summary>
    /// A domino leaning by theta, with an initial push omega.
    /// </summary>
    static Domino Leaning(int id, Fix x, Fix theta, Fix omega)
        => new Domino(
            id,
            new Vec2(x, Fix.Zero),
            theta,
            omega,
            SimConstants.DominoHeight,
            SimConstants.DominoThickness,
            SimConstants.DefaultMass,
            DominoState.Standing);

    static SimState World(Ball[] balls, Domino[] dominoes)
        => new SimState(
            0,
            SimConstants.Gravity,
            balls,
            dominoes,
            Array.Empty<Surface>(),
            0,
            SimPhase.Ready);
    static SimState WorldIn(WorldBounds bounds, Ball[] balls)
    => new SimState(
        0,
        SimConstants.Gravity,
        balls,
        Array.Empty<Domino>(),
        Array.Empty<Surface>(),
        0,
        SimPhase.Ready,
        bounds);

    // ------------------------------------------------------------------ determinism

    [Fact]
    public void TwoIndependentRunsAgreeAtEveryTick()
    {
        // The exit criterion is per-tick agreement, not just a matching final
        // hash. A divergence that cancels out by the end is still a divergence.
        SimState a = Scene();
        SimState b = Scene();

        for (int tick = 1; tick <= 1200; tick++)
        {
            a = Simulation.Step(in a);
            b = Simulation.Step(in b);

            Assert.Equal(a.Hash(), b.Hash());
        }
    }

    [Fact]
    public void ReplayFromSerialisedStateMatchesTheOriginal()
    {
        // Save at tick 300, reload, run on. This is what a replay actually is.
        SimState live = Simulation.StepMany(Scene(), 300);
        SimState reloaded = SimState.FromBytes(live.ToBytes());

        for (int i = 0; i < 300; i++)
        {
            live = Simulation.Step(in live);
            reloaded = Simulation.Step(in reloaded);

            Assert.Equal(live.Hash(), reloaded.Hash());
        }
    }

    [Fact]
    public void StepDoesNotMutateItsInput()
    {
        SimState before = Scene();
        ulong hashBefore = before.Hash();

        Simulation.Step(in before);

        Assert.Equal(hashBefore, before.Hash());
    }

    [Fact]
    public void NoTwoTicksOfAMovingWorldShareAHash()
    {
        // Guards against a Step that silently does nothing.
        var seen = new HashSet<ulong>();
        SimState s = Scene();

        for (int i = 0; i < 60; i++)
        {
            Assert.True(
                seen.Add(s.Hash()),
                $"tick {s.Tick} repeated an earlier hash");

            s = Simulation.Step(in s);
        }
    }

    /// <summary>
    /// One falling ball plus four dominoes: one upright, one nudged below
    /// theta_crit, one pushed past it, one already flat.
    /// </summary>
    static SimState Scene()
        => World(
            new[]
            {
                BallAt(
                    0,
                    Fix.Zero,
                    Fix.FromInt(5),
                    Fix.Ratio100(120),
                    Fix.Zero)
            },
            new[]
            {
                Upright(0, Fix.FromInt(1)),

                Leaning(
                    1,
                    Fix.FromInt(2),
                    Fix.Ratio1000(50),
                    Fix.Zero),

                Leaning(
                    2,
                    Fix.FromInt(3),
                    Fix.Ratio1000(300),
                    Fix.Ratio100(50)),

                new Domino(
                    3,
                    new Vec2(Fix.FromInt(4), Fix.Zero),
                    SimConstants.DominoFallenAngle,
                    Fix.Zero,
                    SimConstants.DominoHeight,
                    SimConstants.DominoThickness,
                    SimConstants.DefaultMass,
                    DominoState.Fallen)
            });

    // ---------------------------------------------------------------------- the tick

    [Fact]
    public void TickAdvancesByExactlyOne()
    {
        SimState s = Scene();

        Assert.Equal(
            1,
            Simulation.Step(in s).Tick);
    }

    [Fact]
    public void TimeAfterOneSecondIsExactlyOneSecond()
    {
        SimState s = Simulation.StepMany(
            Scene(),
            SimConstants.TicksPerSecond);

        Assert.Equal(
            Fix.One.Raw,
            s.Time.Raw);
    }

    // ------------------------------------------------------------------------ gravity

    [Fact]
    public void DtIsOneOver120RoundedDownNotExact()
    {
        // 1/120 is not representable in binary. In Q31.32 it truncates to
        // 35791394 raw, so 120 ticks come to 0.999999996 s, not 1. The shortfall
        // is 16 raw units - about 4e-9 - and it is truncation, not noise, so it
        // is identical on every machine. Determinism does not need exactness.
        // Written down here because it is easy to assume otherwise.
        Assert.Equal(35791394L, SimConstants.Dt.Raw);

        Assert.Equal(
            Fix.One.Raw - 16L,
            (SimConstants.Dt * Fix.FromInt(120)).Raw);
    }

    [Fact]
    public void BallInFreeFallGainsGtPerSecond()
    {
        SimState s = World(
            new[]
            {
                BallAt(
                    0,
                    Fix.Zero,
                    Fix.FromInt(100),
                    Fix.Zero,
                    Fix.Zero)
            },
            Array.Empty<Domino>());

        s = Simulation.StepMany(
            s,
            SimConstants.TicksPerSecond);

        // Not exactly g, because Dt is not exactly 1/120 (see above). 320 raw
        // units short, or 7.5e-8 u/s per second of fall. Pinned rather than
        // approximated, so the number is also a determinism check.
        Assert.Equal(
            SimConstants.Gravity.Raw + 320L,
            s.Balls[0].Vel.Y.Raw);

        long error = System.Math.Abs(
            s.Balls[0].Vel.Y.Raw - SimConstants.Gravity.Raw);

        Assert.True(
            error < Fix.Ratio1000(1).Raw,
            "drift grew beyond 0.001 u/s in one second");
    }

    [Fact]
    public void BallSpeedNeverExceedsMaxSpeed()
    {
        SimState s = World(
            new[]
            {
                BallAt(
                    0,
                    Fix.Zero,
                    Fix.FromInt(190),
                    Fix.Zero,
                    Fix.Zero)
            },
            Array.Empty<Domino>());

        for (int i = 0; i < 1200; i++)
        {
            s = Simulation.Step(in s);

            Fix speed = Vec2.Length(s.Balls[0].Vel);

            Assert.True(
                speed.Raw <= SimConstants.MaxSpeed.Raw,
                $"tick {s.Tick}: speed {speed.Raw} exceeded MaxSpeed");
        }
    }

    [Fact]
    public void ClampedSpeedStillCannotTunnelThroughADomino()
    {
        Fix perTick = SimConstants.MaxSpeed * SimConstants.Dt;

        Assert.True(
            perTick.Raw < SimConstants.DominoThickness.Raw);
    }

    [Fact]
    public void AsleepBallDoesNotMove()
    {
        var b = new Ball(
            0,
            new Vec2(Fix.One, Fix.One),
            Vec2.Zero,
            SimConstants.BallRadius,
            SimConstants.DefaultMass,
            Ball.Airborne,
            true);

        SimState s = Simulation.StepMany(
            World(
                new[] { b },
                Array.Empty<Domino>()),
            240);

        Assert.Equal(
            Fix.One.Raw,
            s.Balls[0].Pos.Y.Raw);

        Assert.Equal(
            SimPhase.Settled,
            s.Phase);
    }

    // --------------------------------------------------------------- domino physics

    [Fact]
    public void UndisturbedUprightDominoNeverMoves()
    {
        SimState s = Simulation.StepMany(
            World(
                Array.Empty<Ball>(),
                new[] { Upright(0, Fix.Zero) }),
            1200);

        Assert.Equal(
            0L,
            s.Dominoes[0].Theta.Raw);

        Assert.Equal(
            0L,
            s.Dominoes[0].Omega.Raw);

        Assert.Equal(
            DominoState.Standing,
            s.Dominoes[0].State);

        Assert.Equal(
            0,
            s.ChainCount);
    }

    [Fact]
    public void DominoLeanedBelowThetaCritFallsBackUpright()
    {
        // theta_crit is 0.1781 rad. Lean to 0.10 and let go: gravity should push
        // it back down, not over. This is the whole point of the negative-torque
        // region - a nudge is not a topple.
        Fix lean = Fix.Ratio100(10);

        Assert.True(
            lean.Raw < SimConstants.DominoThetaCrit.Raw,
            "test lean must be sub-critical");

        SimState s = Simulation.StepMany(
            World(
                Array.Empty<Ball>(),
                new[] { Leaning(0, Fix.Zero, lean, Fix.Zero) }),
            600);

        Assert.Equal(
            DominoState.Standing,
            s.Dominoes[0].State);

        Assert.Equal(
            0L,
            s.Dominoes[0].Theta.Raw);
    }

    [Fact]
    public void DominoPushedPastThetaCritGoesAllTheWayOver()
    {
        Fix lean = Fix.Ratio100(20);

        Assert.True(
            lean.Raw > SimConstants.DominoThetaCrit.Raw,
            "test lean must be super-critical");

        SimState s = Simulation.StepMany(
            World(
                Array.Empty<Ball>(),
                new[] { Leaning(0, Fix.Zero, lean, Fix.Zero) }),
            600);

        Assert.Equal(
            DominoState.Fallen,
            s.Dominoes[0].State);

        Assert.Equal(
            SimConstants.DominoFallenAngle.Raw,
            s.Dominoes[0].Theta.Raw);

        Assert.Equal(
            0L,
            s.Dominoes[0].Omega.Raw);

        Assert.Equal(
            1,
            s.ChainCount);
    }

    [Fact]
    public void ExactlyAtThetaCritTorqueIsEssentiallyZero()
    {
        // The balance point. Not a test of behaviour so much as a check that the
        // sign flip happens where UNITS.md says it does.
        SimState s = World(
            Array.Empty<Ball>(),
            new[]
            {
                Leaning(
                    0,
                    Fix.Zero,
                    SimConstants.DominoThetaCrit,
                    Fix.Zero)
            });

        s = Simulation.Step(in s);

        Assert.True(
            Fix.Abs(s.Dominoes[0].Omega).Raw < Fix.Ratio1000(1).Raw,
            $"omega after one tick at theta_crit was {s.Dominoes[0].Omega.Raw}");
    }

    /// <summary>
    /// Raw omega, measured by bisection, at which this integrator topples.
    /// </summary>
    const long MeasuredTipOmegaRaw = 4245447016L; // 0.988470161 rad/s

    [Fact]
    public void MeasuredTippingThresholdIsPinned()
    {
        // Bisected against the real integrator, not derived. Pinning it means a
        // change to the integration scheme cannot quietly move the difficulty of
        // every domino in the game.
        Fix just = Fix.FromRaw(MeasuredTipOmegaRaw);

        Assert.True(
            Topples(just),
            "at the threshold it must go over");

        Assert.False(
            Topples(Fix.FromRaw(MeasuredTipOmegaRaw - 4096L)),
            "just under it must fall back");
    }

    [Fact]
    public void DiscreteThresholdExceedsTheAnalyticBound()
    {
        // DominoTipOmega is the continuous-time energy barrier: sqrt(2E/I). A
        // semi-implicit Euler step evaluates torque at the start of the tick,
        // and at theta = 0 that torque is at its most restoring, so the tick
        // over-brakes. The discrete threshold is therefore HIGHER - here by
        // 2.3%. If it ever came out LOWER, the integrator would be creating
        // energy, which is a bug and would make chains self-sustaining.
        Assert.True(
            MeasuredTipOmegaRaw > SimConstants.DominoTipOmega.Raw,
            "discrete threshold fell below the analytic bound - integrator gains energy");

        long excess =
            MeasuredTipOmegaRaw - SimConstants.DominoTipOmega.Raw;

        Assert.True(
            excess * 100 / SimConstants.DominoTipOmega.Raw < 5,
            "discrete threshold drifted more than 5% from theory");
    }

    [Fact]
    public void ComfortablyAboveThresholdTopples()
    {
        Assert.True(
            Topples(
                SimConstants.DominoTipOmega + Fix.Ratio100(10)));
    }

    static bool Topples(Fix omega)
    {
        SimState s = Simulation.StepMany(
            World(
                Array.Empty<Ball>(),
                new[]
                {
                    Leaning(
                        0,
                        Fix.Zero,
                        Fix.Zero,
                        omega)
                }),
            2000);

        return s.Dominoes[0].State == DominoState.Fallen;
    }

    [Fact]
    public void JustUnderTipOmegaFallsBack()
    {
        Fix push =
            SimConstants.DominoTipOmega - Fix.Ratio100(10);

        SimState s = Simulation.StepMany(
            World(
                Array.Empty<Ball>(),
                new[]
                {
                    Leaning(
                        0,
                        Fix.Zero,
                        Fix.Zero,
                        push)
                }),
            600);

        Assert.Equal(
            DominoState.Standing,
            s.Dominoes[0].State);

        Assert.Equal(
            0L,
            s.Dominoes[0].Theta.Raw);
    }

    [Fact]
    public void DominoesFallBothWays()
    {
        Fix lean = Fix.Ratio100(20);

        SimState right = Simulation.StepMany(
            World(
                Array.Empty<Ball>(),
                new[]
                {
                    Leaning(
                        0,
                        Fix.Zero,
                        lean,
                        Fix.Zero)
                }),
            600);

        SimState left = Simulation.StepMany(
            World(
                Array.Empty<Ball>(),
                new[]
                {
                    Leaning(
                        0,
                        Fix.Zero,
                        -lean,
                        Fix.Zero)
                }),
            600);

        Assert.Equal(
            DominoState.Fallen,
            right.Dominoes[0].State);

        Assert.Equal(
            DominoState.Fallen,
            left.Dominoes[0].State);

        Assert.Equal(
            right.Dominoes[0].Theta.Raw,
            -left.Dominoes[0].Theta.Raw);
    }

    [Fact]
    public void FallenDominoStaysFallen()
    {
        var flat = new Domino(
            0,
            Vec2.Zero,
            SimConstants.DominoFallenAngle,
            Fix.Zero,
            SimConstants.DominoHeight,
            SimConstants.DominoThickness,
            SimConstants.DefaultMass,
            DominoState.Fallen);

        SimState s = Simulation.StepMany(
            World(
                Array.Empty<Ball>(),
                new[] { flat }),
            600);

        Assert.Equal(
            DominoState.Fallen,
            s.Dominoes[0].State);

        Assert.Equal(
            SimConstants.DominoFallenAngle.Raw,
            s.Dominoes[0].Theta.Raw);
    }

    // ---------------------------------------------------------------- phase and kill box

    [Fact]
    public void BallLeavingTheKillBoxFailsTheRun()
    {
        SimState s = World(
            new[]
            {
                BallAt(
                    0,
                    Fix.Zero,
                    SimConstants.KillBoxMinY + Fix.One,
                    Fix.Zero,
                    Fix.FromInt(-5))
            },
            Array.Empty<Domino>());

        s = Simulation.StepMany(s, 120);

        Assert.Equal(
            SimPhase.Failed,
            s.Phase);
    }

    [Fact]
    public void TheMachinesOwnBoxFailsARunTheConstantBoxWouldHaveAllowed()
    {
        // The whole point of Step 8c. This ball falls from y = 0 and is still well
        // inside the constant kill box at y = -50 for four full seconds; a machine that
        // says its world stops at y = -2 should have failed it long before.
        var tight = new WorldBounds(
            Fix.FromInt(-9),
            Fix.FromInt(-2),
            Fix.FromInt(9),
            Fix.FromInt(16));

        SimState s = Simulation.StepMany(
            WorldIn(
                tight,
                new[] { BallAt(0, Fix.Zero, Fix.Zero, Fix.Zero, Fix.Zero) }),
            120);

        Assert.Equal(SimPhase.Failed, s.Phase);

        // Same ball, same ticks, default box: still running, nowhere near the net.
        SimState wide = Simulation.StepMany(
            World(
                new[] { BallAt(0, Fix.Zero, Fix.Zero, Fix.Zero, Fix.Zero) },
                Array.Empty<Domino>()),
                120);

        Assert.NotEqual(SimPhase.Failed, wide.Phase);
        Assert.True(
            wide.Balls[0].Pos.Y.Raw > SimConstants.KillBoxMinY.Raw);
    }

    [Fact]
    public void TheBoxTravelsWithTheStateSoEveryTickAgrees()
    {
        // Bounds are carried, not looked up. If Step read them from anywhere else a
        // replay could be judged against a different box than the run that produced it.
        var tight = new WorldBounds(
            Fix.FromInt(-9),
            Fix.FromInt(-2),
            Fix.FromInt(9),
            Fix.FromInt(16));

        SimState s = WorldIn(
            tight,
            new[] { BallAt(0, Fix.Zero, Fix.FromInt(4), Fix.Zero, Fix.Zero) });

        for (int i = 0; i < 60; i++)
        {
            s = Simulation.Step(s);

            Assert.Equal(tight.X0.Raw, s.Bounds.X0.Raw);
            Assert.Equal(tight.Y0.Raw, s.Bounds.Y0.Raw);
            Assert.Equal(tight.X1.Raw, s.Bounds.X1.Raw);
            Assert.Equal(tight.Y1.Raw, s.Bounds.Y1.Raw);
        }
    }

    [Fact]
    public void TerminalPhasesAreFrozen()
    {
        SimState s = World(
            new[]
            {
                BallAt(
                    0,
                    Fix.Zero,
                    SimConstants.KillBoxMinY + Fix.One,
                    Fix.Zero,
                    Fix.FromInt(-5))
            },
            Array.Empty<Domino>());

        s = Simulation.StepMany(s, 120);

        Assert.Equal(
            SimPhase.Failed,
            s.Phase);

        ulong frozen = s.Hash();
        int tick = s.Tick;

        s = Simulation.StepMany(s, 500);

        Assert.Equal(
            frozen,
            s.Hash());

        Assert.Equal(
            tick,
            s.Tick); // a terminal tick does not advance
    }

    [Fact]
    public void EmptyWorldSettlesImmediately()
    {
        SimState empty = World(
            Array.Empty<Ball>(),
            Array.Empty<Domino>());

        SimState s = Simulation.Step(in empty);

        Assert.Equal(
            SimPhase.Settled,
            s.Phase);

        Assert.Equal(
            1,
            s.Tick);
    }

    [Fact]
    public void ChainCountTracksToppledDominoes()
    {
        Fix lean = Fix.Ratio100(20);

        SimState s = World(
            Array.Empty<Ball>(),
            new[]
            {
                Upright(0, Fix.Zero),
                Leaning(1, Fix.One, lean, Fix.Zero),
                Leaning(2, Fix.Two, lean, Fix.Zero)
            });

        Assert.Equal(
            0,
            s.ChainCount);

        s = Simulation.StepMany(s, 600);

        Assert.Equal(
            2,
            s.ChainCount); // dominoes 1 and 2; 0 was never touched
    }
}
