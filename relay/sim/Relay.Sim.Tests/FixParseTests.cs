using System;
using Relay.Sim;
using Xunit;
using Fix = FixMath.F64;

public class FixParseTests
{
    static Fix P(string raw) => FixParse.Decimal(raw, "test");

    // ------------------------------------------------------------------------
    // The happy path
    // ------------------------------------------------------------------------

    [Fact]
    public void ParsesTheNumbersTheMachineFilesActuallyContain()
    {
        // Every distinct number in m000, m001 and m002, checked against the Fix
        // the sim would have built for itself. If these drift, a machine stops
        // matching the hand-written tests it was supposed to reproduce.
        Assert.Equal(Fix.Zero.Raw, P("0.0").Raw);
        Assert.Equal(Fix.Ratio100(2).Raw, P("0.02").Raw);
        Assert.Equal(Fix.Ratio100(18).Raw, P("0.18").Raw);
        Assert.Equal(Fix.Ratio100(20).Raw, P("0.2").Raw);
        Assert.Equal(Fix.Ratio100(35).Raw, P("0.35").Raw);
        Assert.Equal(Fix.Ratio100(50).Raw, P("0.5").Raw);
        Assert.Equal(Fix.One.Raw, P("1.0").Raw);
        Assert.Equal(Fix.FromInt(3).Raw, P("3.0").Raw);
        Assert.Equal(Fix.FromInt(4).Raw, P("4.0").Raw);
        Assert.Equal(Fix.Ratio100(480).Raw, P("4.80").Raw);
        Assert.Equal(Fix.FromInt(8).Raw, P("8.0").Raw);
        Assert.Equal(Fix.Ratio100(835).Raw, P("8.35").Raw);
        Assert.Equal(Fix.FromInt(-20).Raw, P("-20.0").Raw);
        Assert.Equal(Fix.FromInt(-1).Raw, P("-1.0").Raw);
    }

    [Fact]
    public void AnIntegerNeedsNoDecimalPoint()
    {
        Assert.Equal(Fix.FromInt(7).Raw, P("7").Raw);
        Assert.Equal(Fix.Zero.Raw, P("0").Raw);
        Assert.Equal(Fix.FromInt(-7).Raw, P("-7").Raw);
    }

    [Fact]
    public void TrailingZerosChangeNothing()
    {
        // "0.5", "0.50" and "0.500000" are the same number, and must be the
        // same Fix. A scale that leaked into the result would make these differ.
        Assert.Equal(P("0.5").Raw, P("0.50").Raw);
        Assert.Equal(P("0.5").Raw, P("0.500000").Raw);
        Assert.Equal(P("4.8").Raw, P("4.80").Raw);
    }

    [Fact]
    public void NegativeZeroIsZero()
    {
        Assert.Equal(0L, P("-0.0").Raw);
        Assert.Equal(0L, P("-0").Raw);
    }

    [Theory]
    [InlineData("0.1")]
    [InlineData("0.12")]
    [InlineData("0.123")]
    [InlineData("0.1234")]
    [InlineData("0.12345")]
    [InlineData("0.123456")]
    public void UpToSixDecimalPlacesIsAccepted(string raw)
    {
        Fix v = P(raw);
        Assert.True(v.Raw > 0);
    }

    [Fact]
    public void SixDecimalPlacesLandsWhereItShould()
    {
        // 0.123456 in Q31.32 is 0.123456 * 2^32 = 530239482.49,
        // and Fix.Ratio truncates, so 530239482. Pinned because this is
        // the finest the format allows and it is where a rounding change
        // would show up first.
        Assert.Equal(530239482L, P("0.123456").Raw);
        Assert.Equal(
            Fix.Ratio(123456, 1000000).Raw,
            P("0.123456").Raw);
    }

    [Fact]
    public void ThePlainDecimalPathNeverTouchesFloatingPoint()
    {
        // The habit this protects: 0.1 is not representable in binary, so a
        // route through double would give a value that depends on the runtime's
        // parser. Integer maths gives 0.1 * 2^32 truncated, the same on every
        // platform.
        Assert.Equal(429496729L, P("0.1").Raw);
        Assert.Equal(
            Fix.Ratio(1, 10).Raw,
            P("0.1").Raw);
    }

    // ------------------------------------------------------------------------
    // Rejections
    // ------------------------------------------------------------------------

    [Theory]
    [InlineData("0.1234567", "more than 6 decimal places")]
    [InlineData("1e3", "trailing characters")]
    [InlineData("1E3", "trailing characters")]
    [InlineData("1.5e-3", "trailing characters")]
    [InlineData("+1.5", "digit before the decimal point")]
    [InlineData(".5", "digit before the decimal point")]
    [InlineData("1.", "decimal point with no digits")]
    [InlineData("NaN", "digit before the decimal point")]
    [InlineData("Infinity", "digit before the decimal point")]
    [InlineData("-", "just a sign")]
    [InlineData(" 1.0", "digit before the decimal point")]
    [InlineData("1.0 ", "trailing characters")]
    [InlineData("1,0", "trailing characters")]
    [InlineData("0x10", "trailing characters")]
    [InlineData("--1", "digit before the decimal point")]
    [InlineData("1.2.3", "trailing characters")]
    public void MalformedNumbersAreRejectedWithASayableReason(
        string raw,
        string expected)
    {
        var ex = Assert.Throws<MachineFormatException>(
            () => P(raw));

        Assert.Contains(expected, ex.Message);
        Assert.Contains(
            "test",
            ex.Message); // The field name is in there.
    }

    [Fact]
    public void AbsurdlyLargeNumbersAreRejectedRatherThanOverflowing()
    {
        // Fix.Ratio shifts left by 32. A mantissa near int.MaxValue would
        // overflow a long and wrap to a plausible-looking wrong answer -
        // the worst kind of bug.
        var ex = Assert.Throws<MachineFormatException>(
            () => P("2000000.0"));

        Assert.Contains(
            "larger than",
            ex.Message);
    }

    [Fact]
    public void TheLargestAllowedMagnitudeStillParses()
    {
        Assert.Equal(
            Fix.FromInt(FixParse.MaxMagnitude).Raw,
            P("1000000").Raw);
    }

    [Fact]
    public void EmptyAndNullAreRejected()
    {
        Assert.Throws<MachineFormatException>(
            () => P(""));

        Assert.Throws<MachineFormatException>(
            () => FixParse.Decimal(null!, "test"));
    }

    // ------------------------------------------------------------------------
    // Integers
    // ------------------------------------------------------------------------

    [Fact]
    public void IntegerParsesWholeNumbersOnly()
    {
        Assert.Equal(
            1,
            FixParse.Integer("1", "version"));

        Assert.Equal(
            1200,
            FixParse.Integer("1200", "ticks"));

        Assert.Equal(
            -5,
            FixParse.Integer("-5", "test"));

        Assert.Equal(
            0,
            FixParse.Integer("0", "test"));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1200.5")]
    [InlineData("1e3")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("99999999999999")]
    public void IntegerRejectsAnythingElse(string raw)
    {
        Assert.Throws<MachineFormatException>(
            () => FixParse.Integer(raw, "ticks"));
    }
}