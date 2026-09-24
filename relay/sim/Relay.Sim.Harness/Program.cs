using System;
using System.Diagnostics;
using Relay.Sim;

// Runs one machine N times from a fresh armed state and compares the final hashes.
//
// Relay.Sim.Harness <machine.json> [--runs N] [--assert-identical] [--print-hash-only]
// Relay.Sim.Harness <machine.json> --per-tick-hash
//
// Exit codes: 0 ok, 1 runs disagreed (with --assert-identical), 2 bad arguments or machine.
//
// The harness is outside Relay.Sim, so Stopwatch is allowed here. It only times runs;
// nothing it measures ever feeds back into the sim.

const int ExitOk = 0, ExitMismatch = 1, ExitUsage = 2;

string? path = null;
int runs = 1;
bool assertIdentical = false, hashOnly = false, perTick = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--runs":
            if (i + 1 >= args.Length ||
                !int.TryParse(args[++i], out runs) ||
                runs < 1)
                return Usage("--runs needs a whole number of at least 1");
            break;

        case "--assert-identical":
            assertIdentical = true;
            break;

        case "--print-hash-only":
            hashOnly = true;
            break;

        case "--per-tick-hash":
            perTick = true;
            break;

        default:
            if (args[i].StartsWith("--"))
                return Usage($"unknown flag {args[i]}");

            if (path != null)
                return Usage("give exactly one machine file");

            path = args[i];
            break;
    }
}

if (path == null)
    return Usage("no machine file given");

if (perTick && (runs != 1 || assertIdentical || hashOnly))
    return Usage("--per-tick-hash traces one run and takes no other flags");

MachineDef m;

try
{
    m = MachineLoader.LoadFile(path);
}
catch (Exception e)
{
    Console.Error.WriteLine($"cannot load {path}: {e.Message}");
    return ExitUsage;
}

if (perTick)
{
    // One line per tick, "tick hash", from the armed state until the run stops moving.
    // Run it on two platforms and diff the output: the first differing line is the tick
    // where they diverged, which the final hash alone can never tell you.

    SimState s = m.ToState();
    Console.WriteLine($"{s.Tick} {s.Hash():X16}");

    for (int t = 0;
         t < m.Ticks &&
         s.Phase != SimPhase.Settled &&
         s.Phase != SimPhase.Failed;
         t++)
    {
        s = Simulation.Step(in s);
        Console.WriteLine($"{s.Tick} {s.Hash():X16}");
    }

    return ExitOk;
}

var hashes = new ulong[runs];
SimState last = default;
var clock = new Stopwatch();

for (int r = 0; r < runs; r++)
{
    // A fresh armed state every run. Reusing one would test nothing: the point is
    // that independent runs from the same file land on the same bits.
    clock.Start();
    last = Simulation.StepMany(m.ToState(), m.Ticks);
    clock.Stop();

    hashes[r] = last.Hash();
}

int firstBad = -1;

for (int r = 1; r < runs; r++)
{
    if (hashes[r] != hashes[0])
    {
        firstBad = r;
        break;
    }
}

if (hashOnly)
{
    Console.WriteLine($"{hashes[0]:X16}");
}
else
{
    string verdict = firstBad < 0 ? "identical" : "MISMATCH";
    double meanMs = clock.Elapsed.TotalMilliseconds / runs;

    Console.WriteLine(
        $"{m.Id} ticks={m.Ticks} lastPhase={last.Phase} at tick {last.Tick} " +
        $"hash=0x{hashes[0]:X16} runs={runs} {verdict} mean={meanMs:F2}ms");
}

if (firstBad >= 0)
{
    // Always reported, even with --print-hash-only: a mismatch must never be silent.
    Console.Error.WriteLine(
        $"run {firstBad} hashed 0x{hashes[firstBad]:X16}, " +
        $"run 0 hashed 0x{hashes[0]:X16}");

    if (assertIdentical)
        return ExitMismatch;
}

return ExitOk;

static int Usage(string problem)
{
    Console.Error.WriteLine(problem);
    Console.Error.WriteLine(
        "usage: Relay.Sim.Harness <machine.json> [--runs N] [--assert-identical] [--print-hash-only]\n" +
        "       Relay.Sim.Harness <machine.json> --per-tick-hash");

    return ExitUsage;
}