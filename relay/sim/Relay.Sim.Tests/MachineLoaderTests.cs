using System;
using System.IO;
using Relay.Sim;
using Xunit;
using Fix = FixMath.F64;
using Vec2 = FixMath.F64Vec2;
using System.Text;

public class MachineLoaderTests
{
    // ------------------------------------------------------------------
    // The real fixtures

    /// <summary>
    /// Walks up from the test binary looking for the machines directory.
    /// The tests must load the committed files, not copies: a fixture that
    /// drifts from what the golden hashes were blessed against is the exact
    /// failure Step 9 exists to catch.
    /// </summary>
    public static string MachinesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "machines");

            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "m000_roll_and_fall.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"no machines/ directory above {AppContext.BaseDirectory}");
    }

    static MachineDef Load(string id)
        => MachineLoader.LoadFile(Path.Combine(MachinesDir(), id + ".json"));

    [Theory]
    [InlineData("m000_roll_and_fall")]
    [InlineData("m001_single_topple")]
    [InlineData("m002_chain")]
    public void EveryCommittedMachineLoads(string id)
    {
        MachineDef m = Load(id);

        Assert.Equal(id, m.Id);
        Assert.Equal(MachineDef.SupportedVersion, m.Version);
        Assert.Equal(SimConstants.Gravity.Raw, m.Gravity.Raw);
        Assert.NotEmpty(m.Surfaces);
        Assert.Single(m.Balls);
    }

    [Fact]
    public void M000IsBallAndSurfacesOnly()
    {
        MachineDef m = Load("m000_roll_and_fall");

        Assert.Equal(3, m.Surfaces.Length);
        Assert.Equal(
            new[] { "shelf_top", "shelf_bottom", "backstop" },
            m.SurfaceNames);

        Assert.Empty(m.Dominoes);
        Assert.Equal(1080, m.Ticks);


        // Surface ids are array indices; the file's strings live only in SurfaceNames.
        for (int i = 0; i < m.Surfaces.Length; i++)
            Assert.Equal(i, m.Surfaces[i].Id);

        Assert.True(m.Surfaces[0].IsHorizontal);
        Assert.True(m.Surfaces[2].IsVertical);
        Assert.Equal(
            Fix.Ratio100(20).Raw,
            m.Surfaces[2].Restitution.Raw);
    }

    [Fact]
    public void TheSpawnedBallMatchesTheFileExactly()
    {
        MachineDef m = Load("m000_roll_and_fall");
        Ball b = m.Balls[0];

        Assert.Equal(new[] { "b0" }, m.BallNames);
        Assert.Equal(0, b.Id);
        Assert.Equal(Fix.Ratio100(50).Raw, b.Pos.X.Raw);
        Assert.Equal(Fix.Ratio100(835).Raw, b.Pos.Y.Raw);
        Assert.Equal(Fix.FromInt(4).Raw, b.Vel.X.Raw);
        Assert.Equal(0L, b.Vel.Y.Raw);
        Assert.Equal(SimConstants.BallRadius.Raw, b.Radius.Raw);
        Assert.Equal(Fix.One.Raw, b.Mass.Raw);
        Assert.False(b.Asleep);
    }

    [Fact]
    public void ABallSpawnsAirborneAndLetsTheFirstTicksFindItsSurface()
    {
        // The file says x and y, not which shelf. Inventing a SurfaceIndex
        // would be a guess; falling is a measurement. It costs a couple of
        // ticks and cannot be wrong.
        MachineDef m = Load("m000_roll_and_fall");

        Assert.Equal(Ball.Airborne, m.Balls[0].SurfaceIndex);

        SimState s = Simulation.StepMany(m.ToState(), 3);

        Assert.Equal(0, s.Balls[0].SurfaceIndex);
        Assert.True(s.Balls[0].IsRolling);
    }

    [Fact]
    public void DominoesTakeTheirBaseHeightFromTheNamedSurface()
    {
        // No y on a domino in the file. That is the point of naming a surface:
        // move the shelf and everything standing on it moves too, with nothing
        // to keep in sync.
        MachineDef m = Load("m001_single_topple");

        Assert.Single(m.Dominoes);

        Domino d = m.Dominoes[0];

        Assert.Equal(new[] { "d0" }, m.DominoNames);
        Assert.Equal(0, d.Id);
        Assert.Equal(Fix.FromInt(6).Raw, d.Base.X.Raw);
        Assert.Equal(Fix.FromInt(8).Raw, d.Base.Y.Raw);

        Assert.Equal(
            m.Surfaces[m.SurfaceIndex("shelf")].A.Y.Raw,
            d.Base.Y.Raw);

        Assert.Equal(0L, d.Theta.Raw);
        Assert.Equal(0L, d.Omega.Raw);
        Assert.Equal(DominoState.Standing, d.State);
        Assert.Equal(SimConstants.DominoHeight.Raw, d.Height.Raw);
        Assert.Equal(SimConstants.DominoThickness.Raw, d.Thickness.Raw);
    }

    [Fact]
    public void LeanIsRecordedAsMetadataAndDoesNotTiltAnything()
    {
        MachineDef m = Load("m001_single_topple");

        Assert.Equal(LeanHint.Right, m.DominoLeans[0]);
        Assert.Equal(0L, m.Dominoes[0].Theta.Raw);
    }

    [Fact]
    public void ChainSpacingSurvivesTheRoundTripToFixedPoint()
    {
        // 0.80 spacing, five dominoes. If a parse rounded differently the
        // chain would still look right and the measured wave speed in UNITS.md
        // would stop matching.
        MachineDef m = Load("m002_chain");

        Assert.Equal(5, m.Dominoes.Length);
        Assert.Equal(
            new[] { "d0", "d1", "d2", "d3", "d4" },
            m.DominoNames);

        Assert.Equal(1200, m.Ticks);

        var expected = new[] { 400, 480, 560, 640, 720 };

        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(
                Fix.Ratio100(expected[i]).Raw,
                m.Dominoes[i].Base.X.Raw);
        }

        // The gaps are 0.80 to within one raw unit, and NOT all identical:
        // 4.80 - 4.00 is 3435973836 raw but 5.60 - 4.80 is 3435973837.
        // Each x is truncated independently, so the difference of two
        // truncations is not the truncation of the difference.
        //
        // 2.3e-10 of a domino, and deterministic - but it means "evenly
        // spaced" is false in the last bit, and anything that assumes uniform
        // spacing rather than reading each x is wrong.
        Fix eight = Fix.Ratio100(80);

        for (int i = 1; i < m.Dominoes.Length; i++)
        {
            long gap =
                (m.Dominoes[i].Base.X - m.Dominoes[i - 1].Base.X).Raw;

            Assert.InRange(gap, eight.Raw, eight.Raw + 1);
        }
    }

    // ------------------------------------------------------------------
    // State and reset

    [Fact]
    public void ToStateProducesAReadyWorldAtTickZero()
    {
        MachineDef m = Load("m002_chain");
        SimState s = m.ToState();

        Assert.Equal(0, s.Tick);
        Assert.Equal(SimPhase.Ready, s.Phase);
        Assert.Equal(0, s.ChainCount);
        Assert.Equal(m.Gravity.Raw, s.Gravity.Raw);
        Assert.Equal(m.Surfaces.Length, s.Surfaces.Length);
    }
    [Fact]
    public void ToStateCarriesTheFilesOwnBoxAndNotTheConstantOne()
    {
        // world.bounds stopped being decorative at Step 8c: the state the machine hands
        // to Step is judged against the file's box, so a ball leaving a 21x15 playfield
        // fails immediately instead of drifting to the 100-unit safety net first.
        MachineDef m = Load("m000_roll_and_fall");
        SimState s = m.ToState();

        Assert.Equal(Fix.FromInt(-1).Raw, s.Bounds.X0.Raw);
        Assert.Equal(Fix.FromInt(-1).Raw, s.Bounds.Y0.Raw);
        Assert.Equal(Fix.FromInt(20).Raw, s.Bounds.X1.Raw);
        Assert.Equal(Fix.FromInt(14).Raw, s.Bounds.Y1.Raw);

        Assert.NotEqual(SimConstants.KillBoxMinX.Raw, s.Bounds.X0.Raw);

        // And it survives a tick, which is what stage 8 reads.
        Assert.Equal(s.Bounds.X1.Raw, Simulation.Step(s).Bounds.X1.Raw);
    }

    [Fact]
    public void ALoadedMachineFailsABallThatLeavesItsDeclaredWorld()
    {
        // m001's shelf ends at x = 16 and its world at x = 20. A ball fired hard enough
        // to run off the end must fail the run, and now does so at the machine's edge.
        MachineDef m = Load("m001_single_topple");
        SimState armed = m.ToState();

        var fast = new Ball(
            0,
            armed.Balls[0].Pos,
            new Vec2(Fix.FromInt(20), Fix.Zero),
            armed.Balls[0].Radius,
            armed.Balls[0].Mass,
            Ball.Airborne,
            false);

        SimState s = Simulation.StepMany(
            new SimState(
                0,
                SimConstants.Gravity,
                new[] { fast },
                m.Dominoes,
                m.Surfaces,
                0,
                SimPhase.Ready,
                m.Bounds),
            600);

        Assert.Equal(SimPhase.Failed, s.Phase);
        Assert.True(
            s.Balls[0].Pos.X.Raw > m.Bounds.X1.Raw,
            "it should have failed by leaving the right-hand edge");
    }

    [Fact]
    public void ToStateHashesTheSameEveryTime()
    {
        MachineDef m = Load("m002_chain");

        Assert.Equal(
            m.ToState().Hash(),
            m.ToState().Hash());
    }

    [Fact]
    public void ToStateHandsOutFreshArraysSoARunCannotSpoilTheNextOne()
    {
        // What reset() at Step 14 rests on. Step allocates its own arrays and
        // never writes into its input, so this is belt and braces - but the
        // belt is cheap and the failure it prevents is "the second attempt
        // at a level differs from the first".
        MachineDef m = Load("m002_chain");

        SimState a = m.ToState();
        SimState b = m.ToState();

        Assert.NotSame(a.Balls, b.Balls);
        Assert.NotSame(a.Dominoes, b.Dominoes);

        ulong armed = m.ToState().Hash();

        Simulation.StepMany(a, 600);

        Assert.Equal(armed, m.ToState().Hash());
    }

    [Fact]
    public void TheLoadedChainStillTopplesCompletely()
    {
        // The end-to-end claim: the file, not a hand-built state, drives all
        // five over.
        MachineDef m = Load("m002_chain");

        SimState s = Simulation.StepMany(
            m.ToState(),
            m.Ticks);

        Assert.Equal(5, s.ChainCount);
        Assert.Equal(SimPhase.Settled, s.Phase);

        for (int i = 0; i < s.Dominoes.Length; i++)
            Assert.Equal(DominoState.Fallen, s.Dominoes[i].State);
    }

    [Fact]
    public void TheLoadedSingleToppleTopplesAndTheBallOnlyMachineDoesNotFail()
    {
        MachineDef one = Load("m001_single_topple");

        SimState a = Simulation.StepMany(
            one.ToState(),
            one.Ticks);

        Assert.Equal(1, a.ChainCount);
        Assert.Equal(DominoState.Fallen, a.Dominoes[0].State);

        MachineDef zero = Load("m000_roll_and_fall");

        SimState b = Simulation.StepMany(
            zero.ToState(),
            zero.Ticks);

        Assert.NotEqual(SimPhase.Failed, b.Phase);
        Assert.Empty(b.Dominoes);
    }

    [Fact]
    public void EveryMachineSettlesInsideItsOwnDeclaredTickBudget()
    {
        // A file that declares fewer ticks than it needs is a level that ends mid-motion.
        // Step 9 blesses a hash at sim.ticks, so a machine still Running there would pin a
        // hash of an arbitrary intermediate frame - one that moves every time the physics
        // is touched, for no reason anyone could read off the file.
        //
        // Measured settle points: m000 tick 808, m001 393, m002 322. m000 used to be the
        // exception - it settled at 978 against a declared 900 - which is why the spawn
        // speed and the budget were both raised.
        foreach (string id in new[]
        {
            "m000_roll_and_fall",
            "m001_single_topple",
            "m002_chain"
        })
        {
            MachineDef m = Load(id);
            SimState atDeclared = Simulation.StepMany(m.ToState(), m.Ticks);
            Assert.Equal(SimPhase.Settled, atDeclared.Phase);
        }
    }

    [Fact]
    public void M000SettlesWithRoomToSpareRatherThanExactlyOnTheBudget()
    {
        // Settling one tick inside the budget would pass the test above and still be a
        // level balanced on a knife edge: any change to friction or restitution pushes it
        // back out. 808 against 1080 is 25% of headroom, which is enough to absorb a
        // tuning pass without re-authoring the file.
        MachineDef m = Load("m000_roll_and_fall");
        Assert.Equal(1080, m.Ticks);

        SimState s = Simulation.StepMany(m.ToState(), 808);
        Assert.Equal(SimPhase.Settled, s.Phase);

        // And not before 807 - so this is the real settle point, not a range.
        Assert.NotEqual(
            SimPhase.Settled,
            Simulation.StepMany(m.ToState(), 807).Phase);
    }

    [Fact]
    public void M000ReachesItsBackstopAndBouncesOffIt()
    {
        // The backstop is the only vertical surface in any fixture and carries the only
        // restitution above 0 anywhere, so until the spawn speed was raised to 4.0 no
        // fixture exercised the wall-bounce path at all. At 3.0 the ball ran out of speed
        // at x = 12.704, well short of the wall at 18.
        MachineDef m = Load("m000_roll_and_fall");

        Assert.Equal(Fix.FromInt(18).Raw, m.Surfaces[2].A.X.Raw);
        Assert.Equal(
            Fix.Ratio100(20).Raw,
            m.Surfaces[2].Restitution.Raw);

        // Contact is centre-to-wall, so the furthest the centre can get is 18 - radius.
        SimState s = Simulation.StepMany(m.ToState(), 808);
        Fix touching = Fix.FromInt(18) - SimConstants.BallRadius;

        Assert.Equal(
            touching.Raw,
            Simulation.StepMany(m.ToState(), 690).Balls[0].Pos.X.Raw);

        // Then it comes back. A ball that merely stopped against the wall would leave the
        // restitution untested, which is the whole point of coming out this far.
        Assert.True(
            s.Balls[0].Pos.X.Raw < touching.Raw,
            "the ball should have rebounded off the backstop, not parked against it");

        Assert.True(
            s.Balls[0].Pos.X.Raw > Fix.FromInt(17).Raw,
            "e = 0.2 is a small rebound; it should not have travelled far back");
    }

    [Fact]
    public void TheBackstopReboundScalesByItsOwnRestitution()
    {
        // Measured at tick 690: vx 1.960000 becomes ~0.991933, a ratio of 0.19966 against
        // the declared 0.2. Not exact because the same tick also applies rolling friction.
        // Pinned because a wall that returned MORE than it was given would make a machine
        // self-sustaining, and nothing else in the suite would notice.
        MachineDef m = Load("m000_roll_and_fall");

        SimState before = Simulation.StepMany(m.ToState(), 689);
        SimState after = Simulation.Step(before);

        Assert.True(
            before.Balls[0].Vel.X.Raw > 0,
            "tick 689 should still be rolling right");

        Assert.True(
            after.Balls[0].Vel.X.Raw < 0,
            "tick 690 is the rebound");

        Fix incoming = before.Balls[0].Vel.X;
        Fix outgoing = -after.Balls[0].Vel.X;

        Assert.True(
            outgoing.Raw < incoming.Raw,
            "a wall cannot return more speed than it was given");

        // Within 1% of the declared restitution, from below.
        Fix bound = incoming * m.Surfaces[2].Restitution;

        Assert.True(
            outgoing.Raw <= bound.Raw,
            "rebound exceeded e * incoming");

        Assert.True(
            outgoing.Raw > bound.Raw - bound.Raw / 100,
            "rebound fell more than 1% under e * incoming");
    }

    // ------------------------------------------------------------------
    // Rejections

    /// <summary>
    /// A machine that loads. Tests mutate one thing in it, so every rejection
    /// below is one edit away from a file that works - which is what a real
    /// typo looks like.
    /// </summary>
    /// /// <para>
    /// Wrap this however you like. The tests match against Good, the collapsed form, so
    /// reformatting the literal cannot break them.
    /// </para>
    /// </summary>
    const string GoodSource = @"{
      ""id"": ""t"",
      ""version"": 1,
      ""world"": {
        ""gravity"": -20.0,
        ""bounds"": {
          ""x0"": -1.0,
          ""y0"": -1.0,
          ""x1"": 20.0,
          ""y1"": 14.0
        }
      },
      ""surfaces"": [
        {
          ""id"": ""shelf"",
          ""x0"": 0.0,
          ""y0"": 8.0,
          ""x1"": 16.0,
          ""y1"": 8.0,
          ""friction"": 0.02,
          ""restitution"": 0.0
        }
      ],
      ""spawns"": [
        {
          ""type"": ""ball"",
          ""id"": ""b0"",
          ""x"": 0.5,
          ""y"": 8.35,
          ""vx"": 3.0,
          ""vy"": 0.0,
          ""radius"": 0.35,
          ""mass"": 1.0
        }
      ],
      ""fixed"": [
        {
          ""type"": ""domino"",
          ""id"": ""d0"",
          ""surface"": ""shelf"",
          ""x"": 6.0,
          ""height"": 1.0,
          ""thickness"": 0.18,
          ""mass"": 1.0,
          ""lean"": ""right""
        }
      ],
      ""gaps"": [],
      ""budget"": {},
      ""target"": null,
      ""sim"": {
        ""ticks"": 900
      }
    }";
    /// <summary>
    /// GoodSource with every run of whitespace collapsed to one space, which is what
    /// every fragment below is matched against.
    /// <para>
    /// Without this the tests depend on how the literal above happens to be wrapped: an
    /// editor reformatting it one-field-per-line breaks every multi-line fragment and
    /// five tests fail for a reason that has nothing to do with the loader. Whitespace
    /// between JSON tokens is insignificant, and no string value in this machine
    /// contains a space, so collapsing cannot change what the machine means.
    /// </para>
    /// </summary>
    static readonly string Good = Collapse(GoodSource);

    static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool gap = false;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                gap = true;
                continue;
            }

            if (gap && sb.Length > 0)
                sb.Append(' ');

            gap = false;
            sb.Append(c);
        }

        return sb.ToString();
    }

    static MachineDef P(string text)
        => MachineLoader.Parse(text, null);

    /// <summary>
    /// Replaces the first occurrence of a fragment. Fails loudly if it is absent.
    /// </summary>
    static string Swap(string from, string to)
    {
        int at = Good.IndexOf(from, StringComparison.Ordinal);

        Assert.True(
            at >= 0,
            $"the good machine does not contain \"{from}\"");

        return Good.Substring(0, at) +
               to +
               Good.Substring(at + from.Length);
    }

    static string Reject(string from, string to)
    {
        var ex = Assert.Throws<MachineFormatException>(
            () => P(Swap(from, to)));

        return ex.Message;
    }

    [Fact]
    public void TheUnmodifiedGoodMachineLoads()
    {
        // Without this the whole table below could be passing for the wrong reason.
        MachineDef m = P(Good);

        Assert.Equal("t", m.Id);
        Assert.Single(m.Dominoes);
    }

    [Fact]
    public void TheFragmentsBelowDoNotDependOnHowTheLiteralIsWrapped()
    {
        // The invariant every Swap rests on: one line, single spaces, so a fragment can
        // be written the obvious way and still match. Reformatting GoodSource one field
        // per line once broke five tests for a reason unrelated to the loader.
        Assert.DoesNotContain("\n", Good);
        Assert.DoesNotContain("\t", Good);
        Assert.DoesNotContain("  ", Good);

        // And collapsing does not change what the machine means, however it arrived.
        string rewrapped = GoodSource.Replace(", ", ",\n            ");
        Assert.Equal(Good, Collapse(rewrapped));
}

    [Theory]
    // Version and identity.
    [InlineData(@"""version"": 1", @"""version"": 2", "only understands 1")]
    [InlineData(@"""version"": 1", @"""version"": 0", "machine.version is 0")]
    [InlineData(@"""id"": ""t""", @"""id"": """"", "machine.id is empty")]

    // Unknown fields, rather than a silent default.
    [InlineData(
        @"""restitution"": 0.0",
        @"""restituton"": 0.0",
        "unknown field \"restituton\"")]

    [InlineData(
        @"""sim"": {",
        @"""simm"": {",
        "unknown field \"simm\"")]
    [InlineData(@"""gravity"": -20.0", @"""gravity"": 20.0", "must be negative")]
    [InlineData(@"""gravity"": -20.0", @"""gravity"": 0.0", "must be negative")]
    [InlineData(@"""x1"": 20.0", @"""x1"": -2.0", "x1 must be greater than x0")]
    [InlineData(@"""y1"": 14.0", @"""y1"": -2.0", "y1 must be greater than y0")]
    [InlineData(@"""x1"": 20.0", @"""x1"": 500.0", "outside the sim's kill box")]

    // Surfaces.
    [InlineData(@"""y1"": 8.0", @"""y1"": 9.0", "is diagonal")]
    [InlineData(@"""friction"": 0.02", @"""friction"": -0.02", "is negative")]
    [InlineData(@"""restitution"": 0.0", @"""restitution"": 1.0", "below 1")]
    [InlineData(@"""restitution"": 0.0", @"""restitution"": 1.5", "below 1")]
    [InlineData(@"""restitution"": 0.0", @"""restitution"": -0.1", "below 1")]
    [InlineData(@"""x1"": 16.0", @"""x1"": 0.0", "zero length")]
    [InlineData(@"""x1"": 16.0", @"""x1"": 40.0", "outside world.bounds")]

    // Spawns.
    [InlineData(
        @"""type"": ""ball""",
        @"""type"": ""cube""",
        "Phase 0 only spawns balls")]

    [InlineData(
        @"""radius"": 0.35",
        @"""radius"": 0.0",
        "radius must be positive")]

    [InlineData(
        @"""mass"": 1.0,",
        @"""mass"": -1.0,",
        "mass must be positive")]

    [InlineData(
        @"""vx"": 3.0",
        @"""vx"": 25.0",
        "tunnelling budget")]

    [InlineData(
        @"""x"": 0.5",
        @"""x"": 40.0",
        "outside world.bounds")]

    // Fixed.
    [InlineData(
        @"""type"": ""domino""",
        @"""type"": ""spring""",
        "Phase 0 only places dominoes")]

    [InlineData(
        @"""surface"": ""shelf""",
        @"""surface"": ""shelfx""",
        "not a surface in this machine")]

    [InlineData(
        @"""height"": 1.0",
        @"""height"": 0.0",
        "height must be positive")]

    [InlineData(
        @"""thickness"": 0.18",
        @"""thickness"": 0.0",
        "thickness must be positive")]

    [InlineData(
        @"""thickness"": 0.18",
        @"""thickness"": 1.0",
        "never topple")]

    [InlineData(
        @"""x"": 6.0",
        @"""x"": 20.0",
        "hangs off the end")]

    [InlineData(
        @"""lean"": ""right""",
        @"""lean"": ""Right""",
        "must be \"left\" or \"right\"")]

    [InlineData(
        @"""lean"": ""right""",
        @"""lean"": ""sideways""",
        "must be \"left\" or \"right\"")]

    // Phase 1 fields must be empty rather than ignored.
    [InlineData(
        @"""gaps"": []",
        @"""gaps"": [1]",
        "gaps are Phase 1")]

    [InlineData(
        @"""budget"": {}",
        @"""budget"": {""n"": 1}",
        "budgets are Phase 1")]

    [InlineData(
        @"""target"": null",
        @"""target"": 3",
        "targets are Phase 1")]

    // Ticks.
    [InlineData(@"""ticks"": 900", @"""ticks"": 0", "at least 1")]
    [InlineData(@"""ticks"": 900", @"""ticks"": -5", "at least 1")]
    [InlineData(@"""ticks"": 900", @"""ticks"": 999999", "10 minute")]
    [InlineData(@"""ticks"": 900", @"""ticks"": 900.0", "no decimal point")]

    // Wrong JSON type where a value was expected.
    [InlineData(
        @"""gravity"": -20.0",
        @"""gravity"": ""-20.0""",
        "expected a number, found String")]

    [InlineData(
        @"""id"": ""shelf""",
        @"""id"": 7",
        "expected a string, found Number")]
    public void AMalformedMachineSaysWhatIsWrongAndWhere(
        string from,
        string to,
        string expected)
    {
        Assert.Contains(expected, Reject(from, to));
    }

    [Fact]
    public void AMissingFieldIsNamedRatherThanDefaulted()
    {
        // The failure this whole loader exists to prevent: a dropped restitution
        // becoming 0.0 gives a level that runs, looks plausible and is not the one
        // its author wrote.
        string text = Swap(
            @", ""restitution"": 0.0",
            "");

        var ex = Assert.Throws<MachineFormatException>(
            () => P(text));

        Assert.Contains(
            "missing required field \"restitution\"",
            ex.Message);

        Assert.Contains("surfaces[0]", ex.Message);
    }

    [Fact]
    public void DuplicateBodyIdsAreRejected()
    {
        string text = Swap(
            @"""id"": ""d0""",
            @"""id"": ""b0""");

        Assert.Contains(
            "already used by another body",
            Assert.Throws<MachineFormatException>(
                () => P(text)).Message);
    }

    [Fact]
    public void DuplicateSurfaceNamesAreRejected()
    {
        // Fragments are written collapsed - single spaces, one line - because that is
// what Good holds. Matching is then independent of the literal's formatting.
string two = Swap(
    @"{ ""id"": ""shelf"", ""x0"": 0.0, ""y0"": 8.0, ""x1"": 16.0, ""y1"": 8.0, ""friction"": 0.02, ""restitution"": 0.0 }",
    @"{ ""id"": ""shelf"", ""x0"": 0.0, ""y0"": 8.0, ""x1"": 16.0, ""y1"": 8.0, ""friction"": 0.02, ""restitution"": 0.0 }, { ""id"": ""shelf"", ""x0"": 0.0, ""y0"": 4.0, ""x1"": 16.0, ""y1"": 4.0, ""friction"": 0.02, ""restitution"": 0.0 }");

        Assert.Contains(
            "already used by surfaces[0]",
            Assert.Throws<MachineFormatException>(
                () => P(two)).Message);
    }

    [Fact]
    public void OverlappingDominoesAreRejected()
    {
        // Two dominoes in the same place would not crash the sim - it would
        // resolve them as a permanent mutual contact and the chain would behave
        // inexplicably.
        string text = Swap(
        @"{ ""type"": ""domino"", ""id"": ""d0"", ""surface"": ""shelf"", ""x"": 6.0, ""height"": 1.0, ""thickness"": 0.18, ""mass"": 1.0, ""lean"": ""right"" }",
        @"{ ""type"": ""domino"", ""id"": ""d0"", ""surface"": ""shelf"", ""x"": 6.0, ""height"": 1.0, ""thickness"": 0.18, ""mass"": 1.0, ""lean"": ""right"" }, { ""type"": ""domino"", ""id"": ""d1"", ""surface"": ""shelf"", ""x"": 6.1, ""height"": 1.0, ""thickness"": 0.18, ""mass"": 1.0, ""lean"": ""right"" }");

        Assert.Contains(
            "overlap",
            Assert.Throws<MachineFormatException>(
                () => P(text)).Message);
    }

    [Fact]
    public void ADominoTouchingExactlyIsAllowed()
    {
        // The boundary of the overlap rule: faces flush at one thickness apart.
        // Legal, because a chain packed that tightly is a design choice, not a
        // broken file.
        string text = Swap(
        @"""x"": 6.0, ""height"": 1.0, ""thickness"": 0.18, ""mass"": 1.0, ""lean"": ""right"" }",
        @"""x"": 6.0, ""height"": 1.0, ""thickness"": 0.18, ""mass"": 1.0, ""lean"": ""right"" }, { ""type"": ""domino"", ""id"": ""d1"", ""surface"": ""shelf"", ""x"": 6.18, ""height"": 1.0, ""thickness"": 0.18, ""mass"": 1.0, ""lean"": ""right"" }");

        Assert.Equal(2, P(text).Dominoes.Length);
    }

    [Fact]
    public void ADominoCannotStandOnAWall()
    {
        string text = Swap(
            @"""x1"": 16.0, ""y1"": 8.0",
            @"""x1"": 0.0, ""y1"": 10.0");

        Assert.Contains(
            "which is vertical",
            Assert.Throws<MachineFormatException>(
                () => P(text)).Message);
    }

    [Fact]
    public void AMachineWithNoSurfacesIsRejected()
    {
        var ex = Assert.Throws<MachineFormatException>(
            () => MachineLoader.Parse(
                @"{
                    ""id"": ""t"",
                    ""version"": 1,
                    ""world"": {
                        ""gravity"": -20.0,
                        ""bounds"": {
                            ""x0"": 0.0,
                            ""y0"": 0.0,
                            ""x1"": 9.0,
                            ""y1"": 16.0
                        }
                    },
                    ""surfaces"": [],
                    ""spawns"": [],
                    ""fixed"": [],
                    ""gaps"": [],
                    ""budget"": {},
                    ""target"": null,
                    ""sim"": {
                        ""ticks"": 60
                    }
                }",
                null));

        Assert.Contains("something to stand on", ex.Message);
    }

    [Fact]
    public void NotesAreOptionalAndIgnored()
    {
        // The one field allowed to say anything. Never hashed, never validated.
        Assert.Equal("", P(Good).Notes);

        string text = Swap(
            @"""version"": 1,",
            @"""version"": 1, ""notes"": ""anything at all"",");

        Assert.Equal("anything at all", P(text).Notes);
    }

    [Fact]
    public void AFileWhoseIdDisagreesWithItsNameIsRejected()
    {
        // The golden hashes are keyed by id. A file named one thing and claiming
        // another would bless the wrong machine, and the regression test would
        // pass forever.
        var ex = Assert.Throws<MachineFormatException>(
            () => MachineLoader.Parse(
                Good,
                "m999_something_else"));

        Assert.Contains(
            "machine.id is \"t\"",
            ex.Message);

        Assert.Contains(
            "m999_something_else",
            ex.Message);
    }

    [Fact]
    public void AMissingFileSaysSoRatherThanThrowingAnIOException()
    {
        var ex = Assert.Throws<MachineFormatException>(
            () => MachineLoader.LoadFile(
                Path.Combine(
                    MachinesDir(),
                    "m999_nope.json")));

        Assert.Contains(
            "no machine file at",
            ex.Message);
    }

    [Fact]
    public void BadJsonComesBackAsAMachineFormatException()
    {
        // One exception type for the whole loading path, so a caller has one
        // thing to catch whether the file was unparseable or merely wrong.
        Assert.Throws<MachineFormatException>(
            () => P(@"{ ""id"": ""t"", "));

        Assert.Throws<MachineFormatException>(
            () => P(@"[ 1, 2 ]"));

        Assert.Throws<MachineFormatException>(
            () => P(""));
    }

    [Fact]
    public void ABomAtTheStartOfAFileIsTolerated()
    {
        // Windows editors add one without asking. Two files differing only in
        // their BOM must load identically, or the mirror and the real copy could
        // disagree.
        string path = Path.Combine(
            Path.GetTempPath(),
            "t.json");

        File.WriteAllText(
            path,
            Good,
            new System.Text.UTF8Encoding(true));

        try
        {
            Assert.Equal(
                "t",
                MachineLoader.LoadFile(path).Id);
        }
        finally
        {
            File.Delete(path);
        }
    }
}