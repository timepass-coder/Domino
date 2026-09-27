using System;
using System.IO;
using Relay.Sim;
using Xunit;
using Fix = FixMath.F64;

/// <summary>
/// Phase 1 Step 1: gaps, budget and target. Same approach as MachineLoaderTests - one
/// level that loads, and every rejection one edit away from it.
/// </summary>
public class MachineLoaderLevelTests
{
    /// <summary>
    /// A playable level: two fixed dominoes, a gap after them, a budget, a goal. Wrap it
    /// however you like; the tests match against Level, the collapsed form.
    /// </summary>
    const string LevelSource = @"{
  ""id"": ""m"",
  ""version"": 1,
  ""world"": { ""gravity"": -20.0,
    ""bounds"": { ""x0"": -1.0, ""y0"": -1.0, ""x1"": 20.0, ""y1"": 14.0 } },
  ""surfaces"": [
    { ""id"": ""shelf"", ""x0"": 0.0, ""y0"": 8.0, ""x1"": 16.0, ""y1"": 8.0,
      ""friction"": 0.02, ""restitution"": 0.0 }
  ],
  ""spawns"": [
    { ""type"": ""ball"", ""id"": ""bb0"", ""x"": 0.5, ""y"": 8.35,
      ""vx"": 3.0, ""vy"": 0.0, ""radius"": 0.35, ""mass"": 1.0 }
  ],
  ""fixed"": [
    { ""type"": ""domino"", ""id"": ""d0"", ""surface"": ""shelf"", ""x"": 4.0,
      ""height"": 1.0, ""thickness"": 0.18, ""mass"": 1.0, ""lean"": ""right"" },
    { ""type"": ""domino"", ""id"": ""d1"", ""surface"": ""shelf"", ""x"": 4.8,
      ""height"": 1.0, ""thickness"": 0.18, ""mass"": 1.0, ""lean"": ""right"" }
  ],
  ""gaps"": [ { ""id"": ""g0"", ""surface"": ""shelf"", ""x0"": 5.2, ""x1"": 7.6 } ],
  ""budget"": { ""domino"": 3 },
  ""target"": { ""type"": ""goal"", ""surface"": ""shelf"", ""x"": 9.0,
    ""width"": 0.4, ""height"": 0.6 },
  ""sim"": { ""ticks"": 1200 }
}";

    static readonly string Level = MachineLoaderTests.Collapse(LevelSource);

    static MachineDef P(string text) => MachineLoader.Parse(text, null);

    static string Swap(string from, string to)
    {
        int at = Level.IndexOf(from, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the level does not contain \"{from}\"");
        return Level.Substring(0, at) + to + Level.Substring(at + from.Length);
    }

    static void AssertClose(Fix expected, Fix actual)
        => Assert.True(Math.Abs((actual - expected).Raw) <= 4,
            $"expected raw {expected.Raw}, got raw {actual.Raw}");

    static string Reject(string text)
        => Assert.Throws<MachineFormatException>(() => P(text)).Message;

    // Fragments, written collapsed.
    const string D1 = @"{ ""type"": ""domino"", ""id"": ""d1"", ""surface"": ""shelf"", ""x"": 4.8, ""height"": 1.0, ""thickness"": 0.18, ""mass"": 1.0, ""lean"": ""right"" }";
    const string G0 = @"{ ""id"": ""g0"", ""surface"": ""shelf"", ""x0"": 5.2, ""x1"": 7.6 }";

    // ------------------------------------------------------------ what loads

    [Fact]
    public void TheLevelLoadsWithItsGapBudgetAndGoal()
    {
        MachineDef m = P(Level);

        Assert.Single(m.Gaps);
        Assert.Equal(new[] { "g0" }, m.GapNames);
        Assert.Equal(0, m.GapIndex("g0"));
        Assert.Equal(-1, m.GapIndex("g1"));
        Assert.Equal(0, m.Gaps[0].Surface);
        Assert.Equal(Fix.Ratio100(520).Raw, m.Gaps[0].X0.Raw);
        Assert.Equal(Fix.Ratio100(760).Raw, m.Gaps[0].X1.Raw);

        Assert.Equal(3, m.DominoBudget);

        Assert.True(m.Target.HasValue);
        Goal g = m.Target.Value;
        Assert.Equal(0, g.Surface);
        Assert.Equal(Fix.FromInt(9).Raw, g.Base.X.Raw);
        Assert.Equal(Fix.FromInt(8).Raw, g.Base.Y.Raw); // the shelf's y
        Assert.Equal(Fix.Ratio100(40).Raw, g.Width.Raw);
        Assert.Equal(Fix.Ratio100(60).Raw, g.Height.Raw);

        // 9.0 +/- 0.4/2 in fixed-point lands a couple of raw units off the literals
        // 8.80 and 9.20, which is 5e-10 of a unit. Close is the right assertion here.
        AssertClose(Fix.Ratio100(880), g.MinX);
        AssertClose(Fix.Ratio100(920), g.MaxX);
    }

    [Fact]
    public void GapsBudgetAndTargetDoNotChangeTheArmedState()
    {
        // Step 1 is format only. The sim sees none of it until placements (Step 2) and
        // the goal test (Step 4) arrive, so the armed hash must not move.
        string bare = Swap(G0, "");
        bare = bare.Replace(@"""domino"": 3", "");
        bare = bare.Replace(
            @"{ ""type"": ""goal"", ""surface"": ""shelf"", ""x"": 9.0, ""width"": 0.4, ""height"": 0.6 }",
            "null");

        MachineDef plain = P(bare);
        Assert.Empty(plain.Gaps);
        Assert.Null(plain.Target);

        Assert.Equal(plain.ToState().Hash(), P(Level).ToState().Hash());
    }

    [Theory]
    [InlineData("m000_roll_and_fall")]
    [InlineData("m001_single_topple")]
    [InlineData("m002_chain")]
    public void ThePhase0FixturesHaveNothingToPlaceAndNoTarget(string id)
    {
        MachineDef m = MachineLoader.LoadFile(
            Path.Combine(MachineLoaderTests.MachinesDir(), id + ".json"));

        Assert.Empty(m.Gaps);
        Assert.Empty(m.GapNames);
        Assert.Equal(0, m.DominoBudget);
        Assert.Null(m.Target);
    }

    [Fact]
    public void AGoalWithNoGapsLoads()
    {
        // A machine that should win untouched. Step 4 needs one to test the goal.
        string text = Swap(G0, "").Replace(@"{ ""domino"": 3 }", "{}");
        MachineDef m = P(text);

        Assert.Empty(m.Gaps);
        Assert.Equal(0, m.DominoBudget);
        Assert.True(m.Target.HasValue);
    }

    // ------------------------------------------------------------ rejections

    [Theory]
    // Gaps.
    [InlineData(@"""x1"": 7.6", @"""x1"": 5.2", "x1 must be greater than x0")]
    [InlineData(@"""x1"": 7.6", @"""x1"": 5.3", "narrower than one domino")]
    [InlineData(@"""x1"": 7.6", @"""x1"": 17.0", "runs off the end of \"shelf\"")]
    [InlineData(@"""x0"": 5.2", @"""x0"": 4.5", "has fixed domino \"d1\" standing inside it")]
    [InlineData(@"""id"": ""g0""", @"""id"": ""d0""", "already used by another body or gap")]
    [InlineData(@"""id"": ""g0""", @"""id"": """"", "gaps[0].id is empty")]
    [InlineData(@"""x1"": 7.6 }", @"""x1"": 7.6, ""lean"": ""right"" }", "gaps[0]: unknown field \"lean\"")]
    [InlineData(@"""surface"": ""shelf"", ""x0"": 5.2",
                @"""surface"": ""shelfx"", ""x0"": 5.2",
                "not a surface in this machine")]
    // Budget.
    [InlineData(@"""domino"": 3", @"""domino"": -1", "budget.domino is negative")]
    [InlineData(@"""domino"": 3", @"""domino"": 1.5", "no decimal point")]
    [InlineData(@"""domino"": 3", @"""domino"": 3, ""spring"": 1",
                "budget: unknown field \"spring\"")]
    [InlineData(@"""domino"": 3", @"""domino"": 0", "nothing to place in them")]
    // Target.
    [InlineData(@"""type"": ""goal""", @"""type"": ""flag""", "Phase 1 only has \"goal\"")]
    [InlineData(@"""width"": 0.4", @"""width"": 0.0", "target.width must be positive")]
    [InlineData(@"""height"": 0.6", @"""height"": -1.0", "target.height must be positive")]
    [InlineData(@"""x"": 9.0", @"""x"": 15.9", "hangs off the end of \"shelf\"")]
    [InlineData(@"""height"": 0.6", @"""height"": 7.0", "target reaches outside world.bounds")]
    [InlineData(@"""x"": 9.0", @"""x"": 4.8", "target overlaps fixed domino \"d1\"")]
    [InlineData(@"""x"": 9.0", @"""x"": 6.0", "target overlaps gaps[0] \"g0\"")]
    [InlineData(@"""height"": 0.6 }",
                @"""height"": 0.6, ""id"": ""goal"" }",
                "target: unknown field \"id\"")]
    public void MalformedLevelSaysWhatIsWrongAndWhere(
        string from,
        string to,
        string expected)
    {
        Assert.Contains(expected, Reject(Swap(from, to)));
    }

    [Fact]
    public void ATargetThatIsNotAnObjectIsRejected()
    {
        string text = Swap(
            @"{ ""type"": ""goal"", ""surface"": ""shelf"", ""x"": 9.0, ""width"": 0.4, ""height"": 0.6 }",
            "3");

        Assert.Contains("expected an object or null, found Number", Reject(text));
    }

    [Fact]
    public void AGapThatIsNotAnObjectIsRejected()
        => Assert.Contains(
            "gaps[0]: expected an object, found Number",
            Reject(Swap(G0, "1")));

    [Fact]
    public void OverlappingGapsAreRejected()
    {
        string text = Swap(
            G0 ,
            G0 + @", { ""id"": ""g1"", ""surface"": ""shelf"", ""x0"": 7.0, ""x1"": 8.0 }");

        Assert.Contains(
            @"gaps[1] overlaps gaps[0] ""g0""",
            Reject(text));
    }

    [Fact]
    public void GapsThatOnlyTouchAreAllowed()
    {
        string text = Swap(
            G0 ,
            G0 + @", { ""id"": ""g1"", ""surface"": ""shelf"", ""x0"": 7.6, ""x1"": 8.0 }");

        Assert.Equal(2, P(text).Gaps.Length);
    }

    [Fact]
    public void AGapOnAWallIsRejected()
    {
        string text = Swap(
            @"""restitution"": 0.0 }",
            @"""restitution"": 0.0 }, { ""id"": ""wall"", ""x0"": 16.0, ""y0"": 8.0, ""x1"": 16.0, ""y1"": 10.0, ""friction"": 0.02, ""restitution"": 0.0 }");

        text = text.Replace(
            G0,
            @"{ ""id"": ""g0"", ""surface"": ""wall"", ""x0"": 5.2, ""x1"": 7.6 }");

        Assert.Contains(
            "which is vertical - a gap needs a floor",
            Reject(text));
    }

    [Fact]
    public void GapsWithoutAGoalAreRejected()
    {
        string text = Swap(
            @"{ ""type"": ""goal"", ""surface"": ""shelf"", ""x"": 9.0, ""width"": 0.4, ""height"": 0.6 }",
            "null");

        Assert.Contains("gaps but no target", Reject(text));
    }

    [Fact]
    public void ABudgetWithoutGapsIsRejected()
        => Assert.Contains(
            "no gaps to place them in",
            Reject(Swap(G0, "")));

    // ------------------------------------------------------------ start-up rule

    static string WithD1At(string x, string gap)
        => Swap(D1, D1.Replace(@"""x"": 4.8", $@"""x"": {x}"))
            .Replace(G0, gap);

    [Fact]
    public void TwoFixedDominoesTooCloseToStartAChainAreRejected()
    {
        // 0.30 apart: not touching (0.18), but under the 0.38 start-up limit. The gap is
        // after both, so nothing the player places can come first.
        string message = Reject(WithD1At("4.3", G0));

        Assert.Contains(
            @"""d0"" and ""d1"" start a chain only 0.300 apart",
            message);
        Assert.Contains("below 0.380", message);
    }

    [Fact]
    public void ExactlyTheStartUpLimitIsAllowed()
        => Assert.Equal(
            2,
            P(WithD1At("4.38", G0)).Dominoes.Length);

    [Fact]
    public void TheStartUpRuleAlsoHoldsWithNoGaps()
    {
        // A Phase 0 style machine: same rule, the chain still has to start.
        string text = WithD1At("4.3", G0)
            .Replace(G0, "")
            .Replace(@"{ ""domino"": 3 }", "{}");

        Assert.Contains(
            "start a chain only 0.300 apart",
            Reject(text));
    }

    [Fact]
    public void TheLoaderLeavesTheStartUpRuleToPlacementWhenAPieceCouldGoFirst()
    {
        // A gap in front of d1 means a placed domino could lead, so the fixed pair may
        // not be the first pair. The placement check (Step 2) judges the real first pair.
        string before =
            @"{ ""id"": ""g0"", ""surface"": ""shelf"", ""x0"": 1.5, ""x1"": 3.5 }";

        Assert.Equal(
            2,
            P(WithD1At("4.3", before)).Dominoes.Length);
    }
}