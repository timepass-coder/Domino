using System;
using Relay.Sim;
using Xunit;
using Fix = FixMath.F64;
using Vec2 = FixMath.F64Vec2;

public class BallSurfaceTests
{
    static Surface Floor(int id, Fix x0, Fix x1, Fix y, Fix friction, Fix restitution)
        => new Surface(id, new Vec2(x0, y), new Vec2(x1, y), friction, restitution);

    static Surface Wall(int id, Fix x, Fix y0, Fix y1, Fix restitution)
        => new Surface(
            id,
            new Vec2(x, y0),
            new Vec2(x, y1),
            Fix.Ratio100(2),
            restitution);

    static Ball Dropped(Fix x, Fix y, Fix vx)
        => new Ball(
            0,
            new Vec2(x, y),
            new Vec2(vx, Fix.Zero),
            SimConstants.BallRadius,
            SimConstants.DefaultMass,
            Ball.Airborne,
            false);

    static SimState World(Ball b, params Surface[] surfaces)
        => new SimState(
            0,
            SimConstants.Gravity,
            new[] { b },
            Array.Empty<Domino>(),
            surfaces,
            0,
            SimPhase.Ready);

    static readonly Fix NoFriction = Fix.Zero;
    static readonly Fix LowFriction = Fix.Ratio100(2); // as in m000

    // -------------------------------------------------------------------
    // Free fall
    // -------------------------------------------------------------------

    [Fact]
    public void LandingTickMatchesTheClosedFormSolution()
    {
        // Drop 4 units:
        // t = sqrt(2h/|g|) = sqrt(8/20) = 0.632456 s = 75.89 ticks.
        // Allow one tick either side - the ball lands on the tick it first
        // crosses, which is never exactly the continuous answer.

        Fix floorY = Fix.Zero;

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius + Fix.FromInt(4),
            Fix.Zero);

        SimState s = World(
            b,
            Floor(
                0,
                Fix.FromInt(-5),
                Fix.FromInt(5),
                floorY,
                NoFriction,
                Fix.Zero));

        int landed = -1;

        for (int i = 0; i < 400 && landed < 0; i++)
        {
            s = Simulation.Step(in s);

            if (s.Balls[0].IsRolling)
                landed = s.Tick;
        }

        Assert.InRange(landed, 75, 77);
    }

    [Fact]
    public void LandingSnapsTheCentreToExactlyRadiusAboveTheSurface()
    {
        Fix floorY = Fix.FromInt(3);

        Ball b = Dropped(
            Fix.Zero,
            floorY + Fix.FromInt(2),
            Fix.Zero);

        SimState s = Simulation.StepMany(
            World(
                b,
                Floor(
                    0,
                    Fix.FromInt(-5),
                    Fix.FromInt(5),
                    floorY,
                    NoFriction,
                    Fix.Zero)),
            200);

        // Exact, not approximate: the snap is an assignment, not a correction.
        Assert.Equal(
            (floorY + SimConstants.BallRadius).Raw,
            s.Balls[0].Pos.Y.Raw);

        Assert.Equal(0L, s.Balls[0].Vel.Y.Raw);
        Assert.Equal(0, s.Balls[0].SurfaceIndex);
    }

    [Fact]
    public void BallCannotFallThroughAFloorEvenAtMaxSpeed()
    {
        // The tunnelling case. Start just above the floor at terminal speed.
        Fix floorY = Fix.Zero;

        var b = new Ball(
            0,
            new Vec2(
                Fix.Zero,
                floorY + SimConstants.BallRadius + Fix.One),
            new Vec2(Fix.Zero, -SimConstants.MaxSpeed),
            SimConstants.BallRadius,
            SimConstants.DefaultMass,
            Ball.Airborne,
            false);

        SimState s = World(
            b,
            Floor(
                0,
                Fix.FromInt(-5),
                Fix.FromInt(5),
                floorY,
                NoFriction,
                Fix.Zero));

        for (int i = 0; i < 600; i++)
        {
            s = Simulation.Step(in s);

            Assert.True(
                s.Balls[0].Pos.Y.Raw >=
                    (floorY + SimConstants.BallRadius).Raw,
                $"tick {s.Tick}: ball centre sank to {s.Balls[0].Pos.Y.Raw}");
        }
    }

    // -------------------------------------------------------------------
    // Zero jitter
    // -------------------------------------------------------------------

    [Fact]
    public void BallAtRestIsBitIdenticalAtTick10AndTick1200()
    {
        // The build guide's exact check. A ball that jitters by one raw unit
        // would keep changing the hash forever, and a machine would never
        // look settled.

        Fix floorY = Fix.Zero;

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius,
            Fix.Zero);

        SimState s = World(
            b,
            Floor(
                0,
                Fix.FromInt(-5),
                Fix.FromInt(5),
                floorY,
                LowFriction,
                Fix.Zero));

        s = Simulation.StepMany(s, 10);

        long x10 = s.Balls[0].Pos.X.Raw;
        long y10 = s.Balls[0].Pos.Y.Raw;
        ulong h10 = s.Hash();

        s = Simulation.StepMany(s, 1190);

        Assert.Equal(x10, s.Balls[0].Pos.X.Raw);
        Assert.Equal(y10, s.Balls[0].Pos.Y.Raw);
        Assert.Equal(h10, s.Hash()); // tick is frozen too: Settled is terminal
    }

    [Fact]
    public void RestingBallFallsAsleepAndTheWorldSettles()
    {
        Fix floorY = Fix.Zero;

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius,
            Fix.Zero);

        SimState s = Simulation.StepMany(
            World(
                b,
                Floor(
                    0,
                    Fix.FromInt(-5),
                    Fix.FromInt(5),
                    floorY,
                    LowFriction,
                    Fix.Zero)),
            5);

        Assert.True(s.Balls[0].Asleep);
        Assert.Equal(SimPhase.Settled, s.Phase);
    }

    // -------------------------------------------------------------------
    // Friction
    // -------------------------------------------------------------------

    [Fact]
    public void RollingFrictionStopsTheBallWhereTheMathsSaysItShould()
    {
        // v0 = 3, mu = 0.02, |g| = 20 -> a = 0.4 u/s^2
        // stopping time = 3 / 0.4 = 7.5 s
        // stopping distance = v0^2 / (2a) = 9 / 0.8 = 11.25 units

        Fix floorY = Fix.Zero;

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius,
            Fix.FromInt(3));

        SimState s = World(
            b,
            Floor(
                0,
                Fix.FromInt(-5),
                Fix.FromInt(40),
                floorY,
                LowFriction,
                Fix.Zero));

        s = Simulation.StepMany(s, 1200); // 10 s, comfortably past 7.5

        Assert.True(s.Balls[0].Asleep, "ball should have stopped within 10 s");
        Assert.Equal(0L, s.Balls[0].Vel.X.Raw);

        // Discrete integration overshoots slightly - it moves at the old
        // velocity for a full tick after each decrement. Half a tick of v0
        // is the scale of the error, so 0.05 units of slack is generous but
        // not blind.

        Fix expected = Fix.Ratio100(1125);

        long diff = System.Math.Abs(
            s.Balls[0].Pos.X.Raw - expected.Raw);

        Assert.True(
            diff < Fix.Ratio100(5).Raw,
            $"stopped at raw {s.Balls[0].Pos.X.Raw}, expected near {expected.Raw}");
    }

    [Fact]
    public void FrictionNeverPushesTheBallBackwards()
    {
        // The jitter bug this guards: decrementing past zero flips the sign,
        // and the ball rocks back and forth by one raw unit forever.

        Fix floorY = Fix.Zero;

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius,
            Fix.Ratio1000(7));

        SimState s = World(
            b,
            Floor(
                0,
                Fix.FromInt(-5),
                Fix.FromInt(40),
                floorY,
                LowFriction,
                Fix.Zero));

        for (int i = 0; i < 600; i++)
        {
            s = Simulation.Step(in s);

            Assert.True(
                s.Balls[0].Vel.X.Raw >= 0,
                $"tick {s.Tick}: vx went negative ({s.Balls[0].Vel.X.Raw})");
        }
    }

    [Fact]
    public void FrictionlessBallKeepsItsSpeedExactly()
    {
        Fix floorY = Fix.Zero;

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius,
            Fix.FromInt(2));

        SimState s = World(
            b,
            Floor(
                0,
                Fix.FromInt(-100),
                Fix.FromInt(100),
                floorY,
                NoFriction,
                Fix.Zero));

        s = Simulation.StepMany(s, 600);

        Assert.Equal(Fix.FromInt(2).Raw, s.Balls[0].Vel.X.Raw);
        Assert.True(s.Balls[0].IsRolling);
    }

    [Fact]
    public void RollingBallMovesMonotonicallyInX()
    {
        Fix floorY = Fix.Zero;

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius,
            Fix.FromInt(3));

        SimState s = World(
            b,
            Floor(
                0,
                Fix.FromInt(-5),
                Fix.FromInt(40),
                floorY,
                LowFriction,
                Fix.Zero));

        long lastX = s.Balls[0].Pos.X.Raw;

        for (int i = 0; i < 1200; i++)
        {
            s = Simulation.Step(in s);

            long x = s.Balls[0].Pos.X.Raw;

            Assert.True(
                x >= lastX,
                $"tick {s.Tick}: x went backwards");

            lastX = x;
        }
    }

    // -------------------------------------------------------------------
    // Rolling off
    // -------------------------------------------------------------------

    [Fact]
    public void BallRollsOffTheEdgeAndBecomesAirborneAgain()
    {
        Fix shelfY = Fix.FromInt(8);

        Ball b = Dropped(
            Fix.Zero,
            shelfY + SimConstants.BallRadius,
            Fix.FromInt(3));

        SimState s = World(
            b,
            Floor(
                0,
                Fix.Zero,
                Fix.FromInt(4),
                shelfY,
                NoFriction,
                Fix.Zero));

        s = Simulation.StepMany(s, 200); // 3 u/s for 1.67 s clears x = 4

        Assert.False(s.Balls[0].IsRolling);

        Assert.True(
            s.Balls[0].Pos.Y.Raw <
                (shelfY + SimConstants.BallRadius).Raw,
            "ball should be falling below the shelf it left");

        Assert.True(
            s.Balls[0].Vel.Y.Raw < 0,
            "ball should be accelerating downwards");

        Assert.Equal(
            Fix.FromInt(3).Raw,
            s.Balls[0].Vel.X.Raw); // no friction: vx kept
    }

    [Fact]
    public void TwoShelvesBehaveLikeM000()
    {
        // The m000_roll_and_fall shape: roll right along the top shelf,
        // leave the edge, free-fall, land on the lower shelf, keep rolling.

        var surfaces = new[]
        {
            Floor(
                0,
                Fix.Zero,
                Fix.FromInt(9),
                Fix.FromInt(8),
                LowFriction,
                Fix.Zero),

            Floor(
                1,
                Fix.FromInt(6),
                Fix.FromInt(18),
                Fix.FromInt(4),
                LowFriction,
                Fix.Zero),
        };

        Ball b = Dropped(
            Fix.Ratio100(50),
            Fix.Ratio100(835),
            Fix.FromInt(3));

        SimState s = World(b, surfaces);

        Assert.Equal(
            0,
            Simulation.Step(in s).Balls[0].SurfaceIndex); // starts on the top

        s = Simulation.StepMany(s, 900);

        Assert.Equal(
            1,
            s.Balls[0].SurfaceIndex); // ends on the bottom

        Assert.Equal(
            (Fix.FromInt(4) + SimConstants.BallRadius).Raw,
            s.Balls[0].Pos.Y.Raw);

        Assert.True(
            s.Balls[0].Pos.X.Raw > Fix.FromInt(9).Raw,
            "ball should have travelled past the top shelf's edge");
    }

    // -------------------------------------------------------------------
    // Walls
    // -------------------------------------------------------------------

    [Fact]
    public void BallBouncesOffAWall()
    {
        Fix floorY = Fix.Zero;

        var surfaces = new[]
        {
            Floor(
                0,
                Fix.FromInt(-5),
                Fix.FromInt(10),
                floorY,
                NoFriction,
                Fix.Zero),

            Wall(
                1,
                Fix.FromInt(5),
                floorY,
                floorY + Fix.FromInt(3),
                Fix.Ratio100(50)),
        };

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius,
            Fix.FromInt(3));

        SimState s = World(b, surfaces);

        s = Simulation.StepMany(
            s,
            400); // 3 u/s reaches x = 4.65 in 1.55 s

        Assert.True(
            s.Balls[0].Vel.X.Raw < 0,
            "ball should be travelling left after the wall");

        Assert.True(
            s.Balls[0].Pos.X.Raw <=
                (Fix.FromInt(5) - SimConstants.BallRadius).Raw,
            "ball should never be past the wall face");
    }

    [Fact]
    public void BallNeverPassesThroughAWall()
    {
        Fix floorY = Fix.Zero;
        Fix wallX = Fix.FromInt(5);

        var surfaces = new[]
        {
            Floor(
                0,
                Fix.FromInt(-20),
                Fix.FromInt(20),
                floorY,
                NoFriction,
                Fix.Zero),

            Wall(
                1,
                wallX,
                floorY,
                floorY + Fix.FromInt(3),
                Fix.Ratio100(50)),
        };

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius,
            Fix.FromInt(20));

        SimState s = World(b, surfaces);

        for (int i = 0; i < 600; i++)
        {
            s = Simulation.Step(in s);

            Assert.True(
                s.Balls[0].Pos.X.Raw <=
                    (wallX - SimConstants.BallRadius).Raw,
                $"tick {s.Tick}: ball at {s.Balls[0].Pos.X.Raw} is past the wall");
        }
    }

    [Fact]
    public void WallOutsideTheBallsHeightIsIgnored()
    {
        Fix floorY = Fix.Zero;

        var surfaces = new[]
        {
            Floor(
                0,
                Fix.FromInt(-5),
                Fix.FromInt(20),
                floorY,
                NoFriction,
                Fix.Zero),

            // A wall that starts well above the rolling ball: it should pass under.
            Wall(
                1,
                Fix.FromInt(5),
                floorY + Fix.FromInt(3),
                floorY + Fix.FromInt(6),
                Fix.Ratio100(50)),
        };

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius,
            Fix.FromInt(3));

        SimState s = Simulation.StepMany(
            World(b, surfaces),
            400);

        Assert.True(
            s.Balls[0].Pos.X.Raw > Fix.FromInt(5).Raw,
            "ball should have passed under");

        Assert.Equal(
            Fix.FromInt(3).Raw,
            s.Balls[0].Vel.X.Raw);
    }

    // -------------------------------------------------------------------
    // Bouncing
    // -------------------------------------------------------------------

    [Fact]
    public void BouncingBallLosesHeightAndEventuallyRolls()
    {
        Fix floorY = Fix.Zero;

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius + Fix.FromInt(4),
            Fix.Zero);

        SimState s = World(
            b,
            Floor(
                0,
                Fix.FromInt(-5),
                Fix.FromInt(5),
                floorY,
                NoFriction,
                Fix.Ratio100(50)));

        // Half the speed back each bounce, so it must run out. If the
        // restitution path leaked energy the wrong way this would never terminate.

        s = Simulation.StepMany(s, 2400); // 20 s

        Assert.True(
            s.Balls[0].IsRolling,
            "a bouncing ball must settle onto the surface");

        Assert.Equal(
            (floorY + SimConstants.BallRadius).Raw,
            s.Balls[0].Pos.Y.Raw);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public void BounceThresholdClearsTheMicroBounceFixedPoint(int restitutionPercent)
    {
        // A discrete bounce does not decay to zero. Land snaps the ball to exactly
        // radius above the surface, so each impact carries one tick of gravity,
        // and the rebound converges to the fixed point of:
        //
        //     v* = e * (|g| * dt - v)
        //
        // Therefore:
        //
        //     v* = e * |g| * dt / (1 + e)
        //
        // BounceRestSpeed must sit above that for every restitution, or the ball
        // welds itself to the floor at v* forever and the world never settles.
        // Worst case is e = 1, where v* = |g| * dt / 2 - exactly half
        // the threshold.

        Fix e = Fix.Ratio100(restitutionPercent);
        Fix gravityPerTick =
            Fix.Abs(SimConstants.Gravity) * SimConstants.Dt;

        Fix fixedPoint =
            Fix.Div(e * gravityPerTick, Fix.One + e);

        Assert.True(
            fixedPoint.Raw < SimConstants.BounceRestSpeed.Raw,
            $"e={restitutionPercent}%: micro-bounce settles at {fixedPoint.Double} " +
            $"but the threshold is only {SimConstants.BounceRestSpeed.Double}");
    }

    // Settle times measured, not guessed. Bouncier floors take longer, and the
    // growth is steep at the top end: restitution 1.0 needs 49 s for a 4-unit drop.

    [Theory]
    [InlineData(0, 2)]
    [InlineData(25, 3)]
    [InlineData(50, 5)]
    [InlineData(75, 10)]
    [InlineData(100, 60)]
    public void ABouncingBallSettlesWhateverTheRestitution(
        int restitutionPercent,
        int budgetSeconds)
    {
        // The same claim as the theory above, made against the running sim rather
        // than the arithmetic. Includes a perfectly elastic floor: even with no
        // energy lost at the impact itself the ball must come to rest, because the
        // snap to contact height quietly discards a tick of gravity each bounce.

        Fix floorY = Fix.Zero;

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius + Fix.FromInt(4),
            Fix.Zero);

        SimState s = World(
            b,
            Floor(
                0,
                Fix.FromInt(-5),
                Fix.FromInt(5),
                floorY,
                NoFriction,
                Fix.Ratio100(restitutionPercent)));

        s = Simulation.StepMany(
            s,
            SimConstants.TicksPerSecond * budgetSeconds);

        Assert.True(
            s.Balls[0].IsRolling,
            $"e={restitutionPercent}%: still airborne after {budgetSeconds} s");

        Assert.Equal(SimPhase.Settled, s.Phase);
    }

    [Fact]
    public void PerfectlyElasticBounceDecaysByExactlyOneTickOfGravity()
    {
        // With restitution 1 nothing is lost at the impact itself, so the only
        // decay left is the snap in Land: it moves the ball back up to contact
        // height and throws away however far it had sunk that tick. That costs
        // |g|*dt of rebound speed per bounce - a fixed subtraction, so the decay
        // is LINEAR in bounce count, not geometric like every other restitution.
        //
        // Consequence for machine data: a perfectly elastic surface needs
        // v0/(|g|*dt) bounces to settle, here 12.67/0.1667 = 76, which is
        // 49 s of run time. Step 8's loader should keep restitution below 1.0;
        // the sim terminates either way, but a level that bounces for a minute
        // is unplayable.

        Fix floorY = Fix.Zero;

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius + Fix.FromInt(4),
            Fix.Zero);

        SimState s = World(
            b,
            Floor(
                0,
                Fix.FromInt(-5),
                Fix.FromInt(5),
                floorY,
                NoFriction,
                Fix.One));

        Fix previousRebound = Fix.Zero;
        int bounces = 0;
        int settledAt = 0;

        for (int i = 0; i < SimConstants.TicksPerSecond * 60; i++)
        {
            Fix before = s.Balls[0].Vel.Y;

            s = Simulation.Step(in s);

            Fix after = s.Balls[0].Vel.Y;

            if (before.Raw < 0 && after.Raw > 0)
            {
                bounces++;

                if (bounces > 1)
                {
                    // Each rebound is exactly one tick of gravity slower than the last.
                    Fix lost = previousRebound - after;

                    Assert.Equal(
                        SimConstants.BounceRestSpeed.Raw,
                        lost.Raw);
                }

                previousRebound = after;
            }

            if (s.Balls[0].IsRolling)
            {
                settledAt = s.Tick;
                break;
            }
        }

        Assert.Equal(75, bounces);
        Assert.Equal(5851, settledAt);
    }

    [Fact]
    public void PerfectlyElasticFloorStillDoesNotSinkTheBall()
    {
        Fix floorY = Fix.Zero;

        Ball b = Dropped(
            Fix.Zero,
            floorY + SimConstants.BallRadius + Fix.FromInt(2),
            Fix.Zero);

        SimState s = World(
            b,
            Floor(
                0,
                Fix.FromInt(-5),
                Fix.FromInt(5),
                floorY,
                NoFriction,
                Fix.One));

        for (int i = 0; i < 2400; i++)
        {
            s = Simulation.Step(in s);

            Assert.True(
                s.Balls[0].Pos.Y.Raw >=
                    (floorY + SimConstants.BallRadius).Raw,
                $"tick {s.Tick}: sank to {s.Balls[0].Pos.Y.Raw}");
        }
    }

    // -------------------------------------------------------------------
    // Determinism
    // -------------------------------------------------------------------

    [Fact]
    public void RollingAndBouncingStayDeterministicPerTick()
    {
        var surfaces = new[]
        {
            Floor(
                0,
                Fix.Zero,
                Fix.FromInt(9),
                Fix.FromInt(8),
                LowFriction,
                Fix.Zero),

            Floor(
                1,
                Fix.FromInt(6),
                Fix.FromInt(18),
                Fix.FromInt(4),
                LowFriction,
                Fix.Ratio100(30)),

            Wall(
                2,
                Fix.FromInt(18),
                Fix.FromInt(4),
                Fix.FromInt(6),
                Fix.Ratio100(20)),
        };

        SimState a = World(
            Dropped(
                Fix.Ratio100(50),
                Fix.Ratio100(835),
                Fix.FromInt(3)),
            surfaces);

        SimState b = World(
            Dropped(
                Fix.Ratio100(50),
                Fix.Ratio100(835),
                Fix.FromInt(3)),
            surfaces);

        for (int i = 0; i < 1200; i++)
        {
            a = Simulation.Step(in a);
            b = Simulation.Step(in b);

            Assert.Equal(a.Hash(), b.Hash());
        }
    }

    [Fact]
    public void SurfaceHelpersClassifyOrientationCorrectly()
    {
        Surface floor = Floor(
            0,
            Fix.Zero,
            Fix.FromInt(5),
            Fix.FromInt(2),
            NoFriction,
            Fix.Zero);

        Surface wall = Wall(
            1,
            Fix.FromInt(5),
            Fix.Zero,
            Fix.FromInt(3),
            Fix.Zero);

        Surface ramp = new Surface(
            2,
            new Vec2(Fix.Zero, Fix.Zero),
            new Vec2(Fix.FromInt(5), Fix.FromInt(2)),
            NoFriction,
            Fix.Zero);

        Assert.True(floor.IsHorizontal);
        Assert.True(wall.IsVertical);
        Assert.True(floor.IsAxisAligned);
        Assert.True(wall.IsAxisAligned);
        Assert.False(ramp.IsAxisAligned); // Step 8's loader must reject this
    }
}