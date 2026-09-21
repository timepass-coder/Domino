using System;
using System.Linq;
using System.Reflection;
using Relay.Sim;
using Xunit;
using Fix = FixMath.F64;
using Vec2 = FixMath.F64Vec2;

public class SimStateTests
{
    /// <summary>
    /// A fixed, non-trivial state. Every field holds a distinct value so that a
    /// swapped pair in the wire format shows up as a hash mismatch.
    /// </summary>
    static SimState Sample()
    {
        var balls = new[]
        {
            new Ball(
                0,
                Vec2.FromRaw(
                    Fix.Ratio100(150).Raw,
                    Fix.Ratio100(300).Raw),
                Vec2.FromRaw(
                    Fix.Ratio100(-25).Raw,
                    Fix.Ratio100(70).Raw),
                SimConstants.BallRadius,
                SimConstants.DefaultMass,
                Ball.Airborne, false),
        };

        var dominoes = new[]
        {
            new Domino(
                0,
                Vec2.FromRaw(
                    Fix.FromInt(2).Raw,
                    Fix.Zero.Raw),
                Fix.Zero,
                Fix.Zero,
                SimConstants.DominoHeight,
                SimConstants.DominoThickness,
                SimConstants.DefaultMass,
                DominoState.Standing),

            new Domino(
                1,
                Vec2.FromRaw(
                    Fix.FromInt(3).Raw,
                    Fix.Zero.Raw),
                Fix.Ratio100(40),
                Fix.Ratio100(-90),
                SimConstants.DominoHeight,
                SimConstants.DominoThickness,
                SimConstants.DefaultMass,
                DominoState.Toppling),
        };

        var surfaces = new[]
        {
            new Surface(
                0,
                Vec2.FromRaw(
                    Fix.FromInt(-5).Raw,
                    Fix.Zero.Raw),
                Vec2.FromRaw(
                    Fix.FromInt(10).Raw,
                    Fix.Zero.Raw),
                Fix.Ratio100(60),
                Fix.Ratio100(5)),
        };

        return new SimState(
            tick: 47,
            gravity: SimConstants.Gravity,
            balls: balls,
            dominoes: dominoes,
            surfaces: surfaces,
            chainCount: 1,
            phase: SimPhase.Running);
    }

    static SimState Empty() => new SimState(
        0,
        SimConstants.Gravity,
        Array.Empty<Ball>(),
        Array.Empty<Domino>(),
        Array.Empty<Surface>(),
        0,
        SimPhase.Ready);

    [Fact]
    public void RoundTripPreservesEveryField()
    {
        SimState a = Sample();
        SimState b = SimState.FromBytes(a.ToBytes());

        Assert.Equal(a.Tick, b.Tick);
        Assert.Equal(a.Gravity.Raw, b.Gravity.Raw);
        Assert.Equal(a.ChainCount, b.ChainCount);
        Assert.Equal(a.Phase, b.Phase);
        Assert.Equal(a.Balls.Length, b.Balls.Length);
        Assert.Equal(a.Dominoes.Length, b.Dominoes.Length);
        Assert.Equal(a.Surfaces.Length, b.Surfaces.Length);

        Assert.Equal(a.Balls[0].Pos.RawY, b.Balls[0].Pos.RawY);
        Assert.Equal(a.Balls[0].Vel.RawX, b.Balls[0].Vel.RawX);
        Assert.Equal(a.Dominoes[1].Theta.Raw, b.Dominoes[1].Theta.Raw);
        Assert.Equal(a.Dominoes[1].Omega.Raw, b.Dominoes[1].Omega.Raw);
        Assert.Equal(a.Dominoes[1].State, b.Dominoes[1].State);
        Assert.Equal(
            a.Surfaces[0].Restitution.Raw,
            b.Surfaces[0].Restitution.Raw);
    }

    [Fact]
    public void RoundTripPreservesHash()
        => Assert.Equal(
            Sample().Hash(),
            SimState.FromBytes(Sample().ToBytes()).Hash());

    [Fact]
    public void HashIsStableAcrossCalls()
        => Assert.Equal(Sample().Hash(), Sample().Hash());

    [Theory]
    [InlineData("tick")]
    [InlineData("chain")]
    [InlineData("phase")]
    [InlineData("gravity")]
    [InlineData("ball-pos")]
    [InlineData("domino-theta")]
    [InlineData("domino-state")]
    [InlineData("surface-friction")]
    [InlineData("ball-contact")]
    public void HashChangesWhenAnyFieldChanges(string what)
    {
        SimState s = Sample();
        SimState m = Mutate(s, what);

        Assert.NotEqual(s.Hash(), m.Hash());
    }

    static SimState Mutate(SimState s, string what)
    {
        var balls = (Ball[])s.Balls.Clone();
        var dominoes = (Domino[])s.Dominoes.Clone();
        var surfaces = (Surface[])s.Surfaces.Clone();

        int tick = s.Tick;
        int chain = s.ChainCount;
        SimPhase ph = s.Phase;
        Fix g = s.Gravity;

        switch (what)
        {
            case "tick":
                tick = s.Tick + 1;
                break;

            case "chain":
                chain = s.ChainCount + 1;
                break;

            case "phase":
                ph = SimPhase.Settled;
                break;

            case "gravity":
                g = Fix.FromRaw(s.Gravity.Raw + 1);
                break;

            case "ball-pos":
                balls[0] = new Ball(
                    balls[0].Id,
                    Vec2.FromRaw(
                        balls[0].Pos.RawX + 1,
                        balls[0].Pos.RawY),
                    balls[0].Vel,
                    balls[0].Radius,
                    balls[0].Mass,
                    balls[0].SurfaceIndex,
                    balls[0].Asleep);
                break;

            case "domino-theta":
                dominoes[1] = dominoes[1].WithMotion(
                    Fix.FromRaw(dominoes[1].Theta.Raw + 1),
                    dominoes[1].Omega,
                    dominoes[1].State);
                break;

            case "domino-state":
                dominoes[1] = dominoes[1].WithMotion(
                    dominoes[1].Theta,
                    dominoes[1].Omega,
                    DominoState.Fallen);
                break;

            case "ball-contact":
                balls[0] = balls[0].WithContact(
                    balls[0].Pos,
                    balls[0].Vel,
                    0,
                    false);
                break;

            case "surface-friction":
                surfaces[0] = new Surface(
                    surfaces[0].Id,
                    surfaces[0].A,
                    surfaces[0].B,
                    Fix.FromRaw(surfaces[0].Friction.Raw + 1),
                    surfaces[0].Restitution);
                break;

            default:
                throw new ArgumentException($"unknown mutation '{what}'");
        }

        return new SimState(
            tick,
            g,
            balls,
            dominoes,
            surfaces,
            chain,
            ph);
    }

    [Fact]
    public void SmallestPossibleChangeIsVisible()
    {
        // One raw unit is 2^-32 of a world unit. If the hash misses that, it
        // would also miss slow numeric drift between two machines.
        SimState a = Sample();
        SimState b = Mutate(a, "ball-pos");

        Assert.Equal(
            1L,
            b.Balls[0].Pos.RawX - a.Balls[0].Pos.RawX);

        Assert.NotEqual(a.Hash(), b.Hash());
    }

    [Fact]
    public void EmptyStateStillHashes()
    {
        SimState e = Empty();

        Assert.NotEqual(0UL, e.Hash());
        Assert.NotEqual(e.Hash(), Sample().Hash());
    }

    [Fact]
    public void SerialisedLengthIsExactlyTheDeclaredFormat()
    {
        // header: version + tick + chain (3 * 4) + gravity (8) + phase (1)
        //         + three array lengths (3 * 4) = 33
        // ball:    4 + 6*8 +4+ 1 = 57
        // domino:  4 + 7*8 + 1 = 61
        // surface: 4 + 6*8 = 52
        int expected =
            33 +
            (1 * 57) +
            (2 * 61) +
            (1 * 52);

        Assert.Equal(expected, Sample().ToBytes().Length);
    }

    [Theory]
    [InlineData(typeof(Ball), Ball.FieldCount)]
    [InlineData(typeof(Domino), Domino.FieldCount)]
    [InlineData(typeof(Surface), Surface.FieldCount)]
    public void BodyFieldCountsAreDeclared(Type t, int declared)
    {
        // Adding a field to a body without adding it to WriteTo would silently
        // drop it from every hash. This makes that a build-time failure instead.
        int actual =
            t.GetFields(
                BindingFlags.Public | BindingFlags.Instance).Length;

        Assert.Equal(declared, actual);
    }

    [Fact]
    public void SimStateFieldCountMatchesWireFormat()
    {
        int actual = typeof(SimState)
            .GetFields(
                BindingFlags.Public | BindingFlags.Instance)
            .Length;

        Assert.Equal(
            7,
            actual); // Tick, Gravity, Balls, Dominoes, Surfaces, ChainCount, Phase
    }

    [Fact]
    public void WrongFormatVersionIsRejected()
    {
        byte[] bytes = Sample().ToBytes();

        bytes[0] = 99; // corrupt the version int

        Assert.ThrowsAny<Exception>(
            () => SimState.FromBytes(bytes));
    }

    [Fact]
    public void TimeDerivesFromTick()
        => Assert.Equal(
            Fix.Ratio(47, 120).Raw,
            Sample().Time.Raw);

    [Fact]
    public void GoldenHashOfSampleState()
    {
        // Pinned on macOS ARM64, .NET 10.0.401. Fixed point means any CPU, any
        // runtime, any OS must produce this exact number. If this ever fails on
        // a new machine, determinism is broken and nothing downstream is
        // trustworthy - fix it before writing another line of physics.
        

        // It is expected to change whenever FormatVersion does, and only then.
        //
        // Format 1 (before Ball.SurfaceIndex) was:
        // 0x557213F417557233

        Assert.Equal(
            0x40413F78AEDD1278UL,
            Sample().Hash());
    }
}