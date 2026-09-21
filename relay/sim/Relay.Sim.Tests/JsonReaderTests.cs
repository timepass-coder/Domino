using System;
using Relay.Sim;
using Xunit;

public class JsonReaderTests
{
    // ------------------------------------------------------------------------
    // Structure
    // ------------------------------------------------------------------------

    [Fact]
    public void ReadsAMachineShapedDocument()
    {
        JsonValue root = JsonReader.Parse(@"
        {
          ""id"": ""m001_single_topple"",
          ""version"": 1,
          ""world"": { ""gravity"": -20.0 },
          ""surfaces"": [
            { ""id"": ""shelf"", ""y0"": 8.0 }
          ],
          ""fixed"": [],
          ""target"": null
        }");

        Assert.Equal(JsonKind.Object, root.Kind);
        Assert.Equal(
            "m001_single_topple",
            root.Get("id", "root").Text);

        Assert.Equal(
            "1",
            root.Get("version", "root").RawNumber);

        Assert.Equal(
            "-20.0",
            root.Get("world", "root")
                .Get("gravity", "world")
                .RawNumber);

        JsonValue surfaces = root.Get("surfaces", "root");

        Assert.Equal(1, surfaces.Count);

        Assert.Equal(
            "shelf",
            surfaces[0]
                .Get("id", "surface 0")
                .Text);

        Assert.Equal(
            0,
            root.Get("fixed", "root").Count);

        Assert.True(
            root.Get("target", "root").IsNull);
    }

    [Fact]
    public void MembersKeepFileOrder()
    {
        // Not a Dictionary. Order is stable and it is the order in the file,
        // which is what makes "the third surface" a meaningful thing to say
        // in an error.
        JsonValue root =
            JsonReader.Parse(@"{ ""z"": 1, ""a"": 2, ""m"": 3 }");

        Assert.Equal(
            new[] { "z", "a", "m" },
            root.Names);
    }

    [Fact]
    public void NumbersArriveAsTextNotAsValues()
    {
        // The whole reason for this reader. "0.18" must reach FixParse with
        // its digits intact, never having been through a floating-point
        // conversion.
        JsonValue root =
            JsonReader.Parse(
                @"{ ""t"": 0.180, ""g"": -20.0, ""n"": 0 }");

        Assert.Equal(
            "0.180",
            root.Get("t", "r").RawNumber);

        Assert.Equal(
            "-20.0",
            root.Get("g", "r").RawNumber);

        Assert.Equal(
            "0",
            root.Get("n", "r").RawNumber);
    }

    [Fact]
    public void HandlesEmptyContainersAndNesting()
    {
        JsonValue root =
            JsonReader.Parse(
                @"{ ""a"": [], ""b"": {}, ""c"": [[1], [2, 3]] }");

        Assert.Equal(
            0,
            root.Get("a", "r").Count);

        Assert.Equal(
            0,
            root.Get("b", "r").Count);

        JsonValue c = root.Get("c", "r");

        Assert.Equal(2, c.Count);

        Assert.Equal(
            "1",
            c[0][0].RawNumber);

        Assert.Equal(
            "3",
            c[1][1].RawNumber);
    }

    [Fact]
    public void ReadsBoolsAndStringEscapes()
    {
        JsonValue root =
            JsonReader.Parse(
                "{ \"yes\": true, \"no\": false, \"s\": \"a\\\"b\\\\c\\nd\\u0041\" }");

        Assert.True(
            root.Get("yes", "r").BoolValue);

        Assert.False(
            root.Get("no", "r").BoolValue);

        Assert.Equal(
            "a\"b\\c\ndA",
            root.Get("s", "r").Text);
    }

    [Fact]
    public void WhitespaceAnywhereIsFine()
    {
        Assert.Equal(
            "1",
            JsonReader.Parse(
                "\r\n\t {\n \"a\"\t:\r 1 \n} \t")
                .Get("a", "r")
                .RawNumber);
    }

    // ------------------------------------------------------------------------
    // Rejections
    // ------------------------------------------------------------------------

    [Theory]
    [InlineData(@"{ ""a"": 1 } extra", "trailing content")]
    [InlineData(@"{ ""a"": 1,  }", "expected a quoted member name")]
    [InlineData(@"[ 1, 2, ]", "unexpected character ']'")]
    [InlineData(@"{ ""a"" 1 }", "expected ':'")]
    [InlineData(@"{ ""a"": 1 ""b"": 2 }", "expected ',' or '}'")]
    [InlineData(@"{ ""a"": }", "unexpected character '}'")]
    [InlineData(@"{ ""a"": 1", "expected ',' or '}'")]
    [InlineData(@"[ 1", "expected ',' or ']'")]
    [InlineData(@"{ ""a"": ""x }", "unterminated string")]
    [InlineData(@"{ ""a"": tru }", "unexpected character 't'")]
    [InlineData("", "unexpected end of input")]
    [InlineData(@"{ ""a"": 01.5.2 }", "expected ',' or '}'")]
    public void MalformedJsonIsRejected(
        string text,
        string expected)
    {
        var ex =
            Assert.Throws<MachineFormatException>(
                () => JsonReader.Parse(text));

        Assert.Contains(
            expected,
            ex.Message);
    }

    [Fact]
    public void CommentsAreRejectedWithAnHonestMessage()
    {
        // JSON has no comments. Saying so beats "unexpected character '/'",
        // because the author's next question is always "why not?".
        var ex =
            Assert.Throws<MachineFormatException>(
                () => JsonReader.Parse(
                    "{ \"a\": 1 } // why not"));

        Assert.Contains(
            "comments are not valid JSON",
            ex.Message);
    }

    [Fact]
    public void DuplicateMembersAreRejectedRatherThanResolved()
    {
        // Last-one-wins is a silent choice. A file with two "gravity" keys
        // is a file whose author does not know what it says, and neither
        // would we.
        var ex =
            Assert.Throws<MachineFormatException>(
                () => JsonReader.Parse(
                    @"{ ""gravity"": -20.0, ""gravity"": -9.81 }"));

        Assert.Contains(
            "duplicate member \"gravity\"",
            ex.Message);
    }

    [Fact]
    public void ExponentsAreRejectedAtTheTokeniser()
    {
        // Caught here as well as in FixParse: a machine file with 1e-3 in it
        // was not written by hand, and the loader should say so before the
        // value matters.
        var ex =
            Assert.Throws<MachineFormatException>(
                () => JsonReader.Parse(
                    @"{ ""x"": 1.5e-3 }"));

        Assert.Contains(
            "exponent notation is not allowed",
            ex.Message);
    }

    [Fact]
    public void ErrorsCarryALineAndColumn()
    {
        var ex =
            Assert.Throws<MachineFormatException>(
                () => JsonReader.Parse(
                    "{\n  \"a\": 1,\n  \"b\" 2\n}"));

        Assert.Contains(
            "line 3",
            ex.Message);

        Assert.Contains(
            "expected ':'",
            ex.Message);
    }

    // ------------------------------------------------------------------------
    // Access mistakes
    // ------------------------------------------------------------------------

    [Fact]
    public void AskingForTheWrongTypeThrowsRatherThanReturningADefault()
    {
        JsonValue root =
            JsonReader.Parse(
                @"{ ""s"": ""text"", ""n"": 1 }");

        Assert.Throws<MachineFormatException>(
            () => root.Get("s", "r").RawNumber);

        Assert.Throws<MachineFormatException>(
            () => root.Get("n", "r").Text);

        Assert.Throws<MachineFormatException>(
            () => root.Get("n", "r").Count);

        Assert.Throws<MachineFormatException>(
            () => root[0]);
    }

    [Fact]
    public void AMissingFieldNamesItselfAndItsContext()
    {
        JsonValue root =
            JsonReader.Parse(
                @"{ ""a"": 1 }");

        var ex =
            Assert.Throws<MachineFormatException>(
                () => root.Get("gravity", "world"));

        Assert.Contains(
            "world",
            ex.Message);

        Assert.Contains(
            "gravity",
            ex.Message);
    }

    [Fact]
    public void TryGetReportsAbsenceWithoutThrowing()
    {
        // For genuinely optional fields. Distinct from Get, which never
        // invents one.
        JsonValue root =
            JsonReader.Parse(
                @"{ ""a"": 1 }");

        Assert.True(
            root.TryGet("a", out JsonValue a));

        Assert.Equal(
            "1",
            a.RawNumber);

        Assert.False(
            root.TryGet("notes", out JsonValue missing));

        Assert.True(
            missing.IsNull);
    }

    [Fact]
    public void AnIndexOutsideAnArraySaysSo()
    {
        JsonValue root =
            JsonReader.Parse(
                @"[ 1, 2 ]");

        var ex =
            Assert.Throws<MachineFormatException>(
                () => root[5]);

        Assert.Contains(
            "outside 0..1",
            ex.Message);
    }
}