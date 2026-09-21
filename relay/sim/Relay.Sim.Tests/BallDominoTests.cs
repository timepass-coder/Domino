using System;
using Relay.Sim;
using Xunit;
using Fix = FixMath.F64;
using Vec2 = FixMath.F64Vec2;

public class BallDominoContactTests
{
    static readonly Fix LowFriction = Fix.Ratio100(2); // as in m000

    static Surface Floor()
        => new Surface(
            0,
            new Vec2(Fix.FromInt(-5), Fix.Zero),
            new Vec2(Fix.FromInt(30), Fix.Zero),
            LowFriction,
            Fix.Zero);

    static Ball Rolling(Fix vx)
        => new Ball(
            0,
            new Vec2(Fix.Zero, SimConstants.BallRadius),
            new Vec2(vx, Fix.Zero),
            SimConstants.BallRadius,
            SimConstants.DefaultMass,
            0,
            false);

    static Domino Upright(int id, Fix x, Fix y)
        => new Domino(
            id,
            new Vec2(x, y),
            Fix.Zero,
            Fix.Zero,
            SimConstants.DominoHeight,
            SimConstants.DominoThickness,
            SimConstants.DefaultMass,
            DominoState.Standing);

    static SimState World(
        Ball[] balls,
        Domino[] dominoes,
        params Surface[] surfaces)
        => new SimState(
            0,
            SimConstants.Gravity,
            balls,
            dominoes,
            surfaces,
            0,
            SimPhase.Running);

    /// <summary>
    /// A ball rolling right into a single upright domino at x.
    /// </summary>
    static SimState Scene(Fix vx, Fix dominoX)
        => World(
            new[] { Rolling(vx) },
            new[] { Upright(0, dominoX, Fix.Zero) },
            Floor());

    /// <summary>
    /// Gravity off, ball airborne at a chosen height. Isolates one impulse at a known
    /// arm, with no friction decay and no falling to confuse the height.
    /// </summary>
    static SimState FlatShot(Fix height, Fix vx)
        => new SimState(
            0,
            Fix.Zero,
            new[]
            {
                new Ball(
                    0,
                    new Vec2(Fix.Zero, height),
                    new Vec2(vx, Fix.Zero),
                    SimConstants.BallRadius,
                    SimConstants.DefaultMass,
                    Ball.Airborne,
                    false)
            },
            new[]
            {
                Upright(0, Fix.FromInt(3), Fix.Zero)
            },
            Array.Empty<Surface>(),
            0,
            SimPhase.Running);

    /// <summary>
    /// Steps until the domino first moves, and returns that state.
    /// </summary>
    static SimState AtFirstContact(SimState s, int budget = 2400)
    {
        for (int i = 0; i < budget; i++)
        {
            s = Simulation.Step(in s);

            if (s.Dominoes[0].Omega.Raw != 0)
                return s;
        }

        Assert.Fail("the ball never reached the domino");
        return s;
    }

    // ------------------------------------------------------------------
    // The main case
    // ------------------------------------------------------------------

    [Fact]
    public void ABallRollingIntoADominoTopplesIt()
    {
        // m001_single_topple in miniature: 3 u/s into a domino 3 units away.
        // Every number here is measured, so a change to the transfer shows up as a
        // failure rather than as a level that quietly stops working.
        SimState s = AtFirstContact(
            Scene(Fix.FromInt(3), Fix.FromInt(3)));

        Assert.Equal(110, s.Tick);
        Assert.Equal(
            8483166336L,
            s.Dominoes[0].Omega.Raw); // 1.975141 rad/s
        Assert.Equal(
            2969108330L,
            s.Balls[0].Vel.X.Raw); // 3.0 -> 0.691299

        s = Simulation.StepMany(s, 2400);

        Assert.Equal(
            DominoState.Fallen,
            s.Dominoes[0].State);
        Assert.Equal(1, s.ChainCount);
        Assert.Equal(SimPhase.Settled, s.Phase);
    }

    [Fact]
    public void ABallTopplesADominoWhichTopplesTheRestOfTheChain()
    {
        // The two halves of Step 7 wired together: ball -> d0 -> d1..d4.
        var dominoes = new Domino[5];

        for (int i = 0; i < 5; i++)
        {
            dominoes[i] = Upright(
                i,
                Fix.FromInt(3) +
                    Fix.Ratio100(80) * Fix.FromInt(i),
                Fix.Zero);
        }

        SimState s = Simulation.StepMany(
            World(
                new[] { Rolling(Fix.FromInt(3)) },
                dominoes,
                Floor()),
            4000);

        Assert.Equal(5, s.ChainCount);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(
                DominoState.Fallen,
                s.Dominoes[i].State);
        }

        Assert.Equal(SimPhase.Settled, s.Phase);
        Assert.Equal(305, s.Tick); // the whole machine takes 2.5 s
    }

    [Fact]
    public void ABallCanToppleFromEitherSide()
    {
        var ball = new Ball(
            0,
            new Vec2(Fix.FromInt(6), SimConstants.BallRadius),
            new Vec2(-Fix.FromInt(3), Fix.Zero),
            SimConstants.BallRadius,
            SimConstants.DefaultMass,
            0,
            false);

        SimState s = Simulation.StepMany(
            World(
                new[] { ball },
                new[] { Upright(0, Fix.FromInt(3), Fix.Zero) },
                Floor()),
            2400);

        Assert.Equal(
            DominoState.Fallen,
            s.Dominoes[0].State);

        Assert.True(
            s.Dominoes[0].Theta.Raw < 0,
            "a leftward ball must topple it leftward");

        Assert.Equal(SimPhase.Settled, s.Phase);
    }

    // ------------------------------------------------------------------
    // The impulse
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(80)]
    [InlineData(100)]
    public void AfterTheImpulseTheBallMovesExactlyAsFastAsTheFaceItPushes(
        int armHundredths)
    {
        // This IS e = 0: the two contact velocities match afterwards.
        // The ball moves at vx, the face at that height moves at arm*omega,
        // and nothing separates.
        //
        // If this drifts, the collision has stopped being fully inelastic.
        Fix arm = Fix.Ratio100(armHundredths);

        SimState s = AtFirstContact(
            FlatShot(arm, Fix.FromInt(3)));

        Fix faceSpeed = arm * s.Dominoes[0].Omega;

        long diff = System.Math.Abs(
            s.Balls[0].Vel.X.Raw - faceSpeed.Raw);

        Assert.True(
            diff < Fix.Ratio1000(1).Raw,
            $"arm {arm.Double}: ball at {s.Balls[0].Vel.X.Double}, " +
            $"face at {faceSpeed.Double}");
    }

    [Fact]
    public void TheBallAlwaysPaysForWhatItDelivers()
    {
        // No energy from nowhere, and no reversal either: with e = 0 the ball is
        // slowed to the face's speed, never bounced back off it.
        SimState before = Scene(
            Fix.FromInt(3),
            Fix.FromInt(3));

        SimState after = AtFirstContact(before);

        Assert.True(
            after.Balls[0].Vel.X.Raw > 0,
            "e = 0 must never reverse the ball");

        Assert.True(
            after.Balls[0].Vel.X.Raw < Fix.FromInt(3).Raw,
            "the ball must slow down");

        Assert.True(
            after.Dominoes[0].Omega.Raw > 0,
            "the domino must gain spin");
    }

    [Fact]
    public void TheBallIsNeverDraggedBackwards()
    {
        // Guards the "centre has not crossed the far face" half of BallReaches.
        // Without it a ball that got through keeps being grabbed from behind.
        SimState s = Scene(
            Fix.FromInt(6),
            Fix.FromInt(3));

        for (int i = 0; i < 2400; i++)
        {
            s = Simulation.Step(in s);

            Assert.True(
                s.Balls[0].Vel.X.Raw >= 0,
                $"tick {s.Tick}: vx went negative " +
                $"({s.Balls[0].Vel.X.Double})");
        }
    }

    [Fact]
    public void HittingADominoTooHighIsAlmostAsBadAsTooLow()
    {
        // MEASURED, and not obvious. The transfer is arm/(I/m + arm^2),
        // which peaks at arm = sqrt(I/m) = 0.5866 and falls away on BOTH sides.
        //
        // A ball striking right at the top of a domino hands over less spin
        // than one striking at 0.6 - the lever is longer but the domino's
        // effective mass at the contact is worse.
        //
        // Relevant the moment Phase 1 lets a designer choose a ball size.
        Fix Delivered(int armHundredths)
            => AtFirstContact(
                    FlatShot(
                        Fix.Ratio100(armHundredths),
                        Fix.FromInt(3)))
                .Dominoes[0]
                .Omega;

        Fix low = Delivered(30);
        Fix best = Delivered(60);
        Fix high = Delivered(100);

        Assert.True(
            best.Raw > low.Raw,
            $"0.60 ({best.Double}) should beat 0.30 ({low.Double})");

        Assert.True(
            best.Raw > high.Raw,
            $"0.60 ({best.Double}) should beat 1.00 ({high.Double})");

        // And the peak really is at sqrt(I/m), not at the top of the domino.
        Fix root = Fix.Sqrt(
            Upright(
                0,
                Fix.Zero,
                Fix.Zero).Inertia);

        Assert.InRange(
            root.Raw,
            Fix.Ratio100(58).Raw,
            Fix.Ratio100(59).Raw);
    }

    // ------------------------------------------------------------------
    // Near misses
    // ------------------------------------------------------------------

    [Fact]
    public void ABallSailingOverTheTopMissesEntirely()
    {
        // arm > h. Gravity off so the height is unambiguous rather than a race
        // between the ball's flight and its fall.
        SimState s = Simulation.StepMany(
            FlatShot(
                Fix.Ratio100(150),
                Fix.FromInt(3)),
            400);

        Assert.Equal(
            0L,
            s.Dominoes[0].Omega.Raw);

        Assert.Equal(
            DominoState.Standing,
            s.Dominoes[0].State);

        Assert.Equal(
            Fix.FromInt(3).Raw,
            s.Balls[0].Vel.X.Raw); // not even slowed

        Assert.True(
            s.Balls[0].Pos.X.Raw > Fix.FromInt(3).Raw,
            "ball should be past it");
    }

    [Fact]
    public void ContactAtExactlyTheDominoHeightStillCounts()
    {
        // arm == h is inclusive: a ball clipping the very top corner topples it.
        // An exclusive test here would leave a one-raw-unit hole in the face.
        SimState s = Simulation.StepMany(
            FlatShot(
                SimConstants.DominoHeight,
                Fix.FromInt(3)),
            400);

        Assert.Equal(
            DominoState.Fallen,
            s.Dominoes[0].State);
    }

    [Fact]
    public void ADominoOnAHigherShelfIsNotHit()
    {
        // arm < 0. The ball rolls underneath as if the domino were not there -
        // and "as if" is exact: the same raw x as with no domino in the world at all.
        SimState with = Simulation.StepMany(
            World(
                new[] { Rolling(Fix.FromInt(3)) },
                new[]
                {
                    Upright(
                        0,
                        Fix.FromInt(3),
                        Fix.FromInt(4))
                },
                Floor()),
            2400);

        SimState without = Simulation.StepMany(
            World(
                new[] { Rolling(Fix.FromInt(3)) },
                Array.Empty<Domino>(),
                Floor()),
            2400);

        Assert.Equal(
            DominoState.Standing,
            with.Dominoes[0].State);

        Assert.Equal(
            48264696564L,
            without.Balls[0].Pos.X.Raw); // 11.2375

        Assert.Equal(
            without.Balls[0].Pos.X.Raw,
            with.Balls[0].Pos.X.Raw);
    }

    [Fact]
    public void AFallenDominoIsScenery()
    {
        // Nothing left to give: no lever, no impulse. The ball rolls straight over
        // it to exactly where it would have stopped on bare floor.
        var flat = new Domino(
            0,
            new Vec2(Fix.FromInt(3), Fix.Zero),
            Fix.Div(Fix.Pi, Fix.Two),
            Fix.Zero,
            SimConstants.DominoHeight,
            SimConstants.DominoThickness,
            SimConstants.DefaultMass,
            DominoState.Fallen);

        SimState s = Simulation.StepMany(
            World(
                new[] { Rolling(Fix.FromInt(3)) },
                new[] { flat },
                Floor()),
            2400);

        Assert.Equal(
            0L,
            s.Dominoes[0].Omega.Raw);

        Assert.Equal(
            48264696564L,
            s.Balls[0].Pos.X.Raw);
    }

    [Fact]
    public void ASleepingBallDoesNotPush()
    {
        // A ball parked against a domino must not nudge it awake forever.
        // Without the Asleep guard this is a world that never reaches Settled.
        var asleep = new Ball(
            0,
            new Vec2(
                Fix.Ratio100(60),
                SimConstants.BallRadius),
            Vec2.Zero,
            SimConstants.BallRadius,
            SimConstants.DefaultMass,
            0,
            true);

        SimState s = Simulation.StepMany(
            World(
                new[] { asleep },
                new[]
                {
                    Upright(
                        0,
                        Fix.One,
                        Fix.Zero)
                },
                Floor()),
            1200);

        Assert.Equal(
            0L,
            s.Dominoes[0].Omega.Raw);

        Assert.Equal(
            0L,
            s.Dominoes[0].Theta.Raw);

        Assert.Equal(
            SimPhase.Settled,
            s.Phase);
    }

    // ------------------------------------------------------------------
    // The threshold
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(138, false)]
    [InlineData(140, true)]
    public void ThereIsAMeasuredSpeedBelowWhichTheDominoSurvives(
        int vxHundredths,
        bool topples)
    {
        // MEASURED at a domino 1.0 away, so friction barely matters:
        // the cliff is between 1.38 and 1.40 u/s.
        //
        // Note this is well below the 1.318 a single impulse would need -
        // the ball is still touching next tick, and keeps pressing while it
        // is faster than the face.
        //
        // Sustained contact arrives on its own here, without the extra pass
        // domino-domino would need.
        SimState s = Simulation.StepMany(
            Scene(
                Fix.Ratio100(vxHundredths),
                Fix.One),
            2400);

        Assert.Equal(
            topples
                ? DominoState.Fallen
                : DominoState.Standing,
            s.Dominoes[0].State);

        Assert.Equal(
            SimPhase.Settled,
            s.Phase); // either way it must settle
    }

    [Fact]
    public void ABallThatFailsToTopplePressesIntoTheFaceRatherThanPassingThrough()
    {
        // The known Phase 0 limitation, pinned so it cannot quietly get worse.
        // There is no position correction in the ball-domino pass, so a ball that
        // stalls ends up overlapping the domino - measured at 0.0657 units,
        // about a third of the thickness.
        //
        // Step 10 owns the fix (position correction, or the sustained-contact pass);
        // this test just stops the overlap growing.
        SimState s = Simulation.StepMany(
            Scene(
                Fix.Ratio100(138),
                Fix.One),
            2400);

        Assert.Equal(
            DominoState.Standing,
            s.Dominoes[0].State);

        Assert.Equal(
            0L,
            s.Dominoes[0].Theta.Raw); // it really did come all the way back

        Assert.Equal(
            0L,
            s.Balls[0].Vel.X.Raw); // and the ball really did stop

        Fix edge =
            s.Balls[0].Pos.X +
            s.Balls[0].Radius;

        Fix face =
            s.Dominoes[0].StruckFaceX(1);

        Fix overlap = edge - face;

        Assert.True(
            overlap.Raw > 0,
            "it should be resting against the face");

        Assert.True(
            overlap.Raw < Fix.Ratio100(10).Raw,
            $"overlap grew to {overlap.Double} - " +
            "past 0.1 it is visible on screen");

        // It stopped short of the far face, so it is leaning on the domino,
        // not inside it.
        Assert.True(
            s.Balls[0].Pos.X.Raw < face.Raw,
            "the ball's centre must not have crossed into the domino");
    }

    // ------------------------------------------------------------------
    // Determinism
    // ------------------------------------------------------------------

    [Fact]
    public void BallAndDominoContactsStayDeterministicPerTick()
    {
        var dominoes = new Domino[4];

        for (int i = 0; i < 4; i++)
        {
            dominoes[i] = Upright(
                i,
                Fix.FromInt(3) +
                    Fix.Ratio100(80) * Fix.FromInt(i),
                Fix.Zero);
        }

        SimState a = World(
            new[] { Rolling(Fix.FromInt(3)) },
            dominoes,
            Floor());

        SimState b = World(
            new[] { Rolling(Fix.FromInt(3)) },
            dominoes,
            Floor());

        for (int i = 0; i < 2400; i++)
        {
            a = Simulation.Step(in a);
            b = Simulation.Step(in b);

            Assert.Equal(a.Hash(), b.Hash());
        }
    }

    [Fact]
    public void AFinishedMachineStaysFrozen()
    {
        var dominoes = new Domino[3];

        for (int i = 0; i < 3; i++)
        {
            dominoes[i] = Upright(
                i,
                Fix.FromInt(3) +
                    Fix.Ratio100(80) * Fix.FromInt(i),
                Fix.Zero);
        }

        SimState s = World(
            new[] { Rolling(Fix.FromInt(3)) },
            dominoes,
            Floor());

        int settledAt = -1;

        for (int i = 0; i < 4000 && settledAt < 0; i++)
        {
            s = Simulation.Step(in s);

            if (s.Phase == SimPhase.Settled)
                settledAt = s.Tick;
        }

        Assert.True(
            settledAt > 0,
            "ball plus three dominoes must settle");

        ulong h = s.Hash();

        s = Simulation.StepMany(s, 1200);

        Assert.Equal(h, s.Hash());
        Assert.Equal(settledAt, s.Tick);
    }
}