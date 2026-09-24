using System;
using System.IO;
using Relay.Sim;
using Xunit;

public class GoldenHashTests
{
    // ----------------------------------------------------------------------- armed state

    /// <summary>
    /// The tick-0 hash of each fixture, pinned. The settled hash says "the physics has not
    /// moved"; this one says "the level is still the level we blessed against". A fixture
    /// edit that nudged a spawn point would otherwise only show up as a settled-hash change,
    /// and look exactly like a physics regression.
    /// </summary>
    [Theory]
    [InlineData("m000_roll_and_fall", 0x06496AAFE1FFBBD4UL)]
    [InlineData("m001_single_topple", 0x613E94345B634FB9UL)]
    [InlineData("m002_chain", 0xF6F79058B8E5019BUL)]
    public void ArmedHashIsPinned(string id, ulong expected)
    {
        MachineDef m = MachineLoader.LoadFile(
            Path.Combine(MachineLoaderTests.MachinesDir(), id + ".json"));

        ulong actual = m.ToState().Hash();

        Assert.True(
            actual == expected,
            $"{id} armed hash is 0x{actual:X16}, pinned 0x{expected:X16}. " +
            "The fixture file changed - if on purpose, re-pin and say why.");
    }
    // ----------------------------------------------------------------------- settled state

    /// <summary>
    /// Set RELAY_BLESS=1 to rewrite the golden files from the current physics. Blessing
    /// still fails the run, so the variable cannot be left set by accident: a suite that
    /// re-blesses itself on every run would pass through any regression.
    /// </summary>
    const string BlessVar = "RELAY_BLESS";

    static string GoldenDir() => Path.Combine(MachineLoaderTests.MachinesDir(), "golden");

    static string Format(ulong h) => $"0x{h:X16}";

    static ulong ReadGolden(string path)
    {
        string text = File.ReadAllText(path).Trim();

        Assert.True(
            text.Length == 18 && text.StartsWith("0x", StringComparison.Ordinal),
            $"{path} should hold one hash like 0xB23456789ABCDEF, got \"{text}\"");

        return ulong.Parse(
            text.Substring(2),
            System.Globalization.NumberStyles.AllowHexSpecifier);
    }

    [Theory]
    [InlineData("m000_roll_and_fall")]
    [InlineData("m001_single_topple")]
    [InlineData("m002_chain")]
    public void SettledHashMatchesGolden(string id)
    {
        MachineDef m = MachineLoader.LoadFile(
            Path.Combine(MachineLoaderTests.MachinesDir(), id + ".json"));

        SimState s = Simulation.StepMany(m.ToState(), m.Ticks);

        // A hash of a still-moving frame would change whenever anything upstream is
        // touched, for no reason readable off the file. Only bless settled worlds.
        Assert.True(
            s.Phase == SimPhase.Settled,
            $"{id} is {s.Phase} at tick {s.Tick} of {m.Ticks} - not settled, so not blessable");

        ulong actual = s.Hash();
        string path = Path.Combine(GoldenDir(), id + ".txt");

        if (Environment.GetEnvironmentVariable(BlessVar) == "1")
        {
            Directory.CreateDirectory(GoldenDir());
            File.WriteAllText(path, Format(actual) + "\n");

            Assert.Fail(
                $"Blessed {id} = {Format(actual)} settled at tick {s.Tick}. " +
                $"Unset {BlessVar} and run again.");
        }

        Assert.True(
            File.Exists(path),
            $"no golden file {path} - run once with {BlessVar}=1");

        ulong expected = ReadGolden(path);

        Assert.True(
            actual == expected,
            $"{id} settled hash is {Format(actual)}, golden {Format(expected)}. " +
            "The physics changed. If on purpose, re-bless and say why in the commit.");
    }

    [Fact]
    public void EveryMachineHasAGoldenFileAndNoGoldenFileIsOrphaned()
    {
        string[] machines =
            Directory.GetFiles(MachineLoaderTests.MachinesDir(), "*.json");

        string[] goldens =
            Directory.Exists(GoldenDir())
                ? Directory.GetFiles(GoldenDir(), "*.txt")
                : Array.Empty<string>();

        foreach (string json in machines)
        {
            string id = Path.GetFileNameWithoutExtension(json);

            Assert.True(
                File.Exists(Path.Combine(GoldenDir(), id + ".txt")),
                $"{id}.json has no golden hash");
        }

        foreach (string txt in goldens)
        {
            string id = Path.GetFileNameWithoutExtension(txt);

            Assert.True(
                File.Exists(Path.Combine(MachineLoaderTests.MachinesDir(), id + ".json")),
                $"golden/{id}.txt has no machine");
        }
    }
}