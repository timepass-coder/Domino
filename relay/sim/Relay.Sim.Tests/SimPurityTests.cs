using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Guards the determinism contract by scanning Relay.Sim's source.
///
/// Comments and string literals are stripped before matching - a comment that
/// mentions "double" is documentation, not a determinism bug.
///
/// A line may opt out with a trailing // PURITY-OK: &lt;reason&gt;
/// (checked against the raw line, so it survives stripping).
/// </summary>
public class SimPurityTests
{
    static readonly (string Pattern, string Why)[] Banned =
    {
        (@"\bUnityEngine\b", "the sim must never depend on the engine"),
        (@"\bUnityEditor\b", "the sim must never depend on the engine"),
        (@"\bfloat\b", "floating point is not bit-identical across CPUs"),
        (@"\bdouble\b", "floating point is not bit-identical across CPUs"),
        (@"\bdecimal\b", "use Fix instead"),
        (@"\bFromFloat\b", "lets a float into sim state"),
        (@"\bFromDouble\b", "lets a float into sim state"),
        (@"(?<![\w.])Math\.", "System.Math is double-based; use Fix"),
        (@"\bRandom\b", "randomness must be a seeded integer PRNG held in sim state"),
        (@"\bGuid\b", "not reproducible"),
        (@"\bDateTime\b", "wall-clock time must not reach sim state"),
        (@"\bStopwatch\b", "wall-clock time must not reach sim state"),
        (@"\bTickCount\b", "wall-clock time must not reach sim state"),
        (@"\bdeltaTime\b", "the sim is fixed-timestep; dt is a constant"),
        (@"\bDictionary\b", "iteration order is fragile; use arrays"),
        (@"\bHashSet\b", "iteration order is fragile; use arrays"),
    };

    [Fact]
    public void SimContainsNoForbiddenConstructs()
    {
        var failures = new List<string>();

        foreach (string file in SimSourceFiles())
        {
            foreach (var (lineNo, raw, code) in CodeLines(file))
            {
                foreach (var (pattern, why) in Banned)
                {
                    if (Regex.IsMatch(code, pattern))
                    {
                        failures.Add(
                            $"{Path.GetFileName(file)}:{lineNo}  {pattern} - {why}" +
                            $"\n        {raw.Trim()}");
                    }
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Sim purity violations ({failures.Count}):\n  " +
            string.Join("\n  ", failures));
    }

    [Fact]
    public void SimHasNoMutableStaticState()
    {
        var failures = new List<string>();

        foreach (string file in SimSourceFiles())
        {
            foreach (var (lineNo, raw, code) in CodeLines(file))
            {
                if (!Regex.IsMatch(code, @"\bstatic\b"))
                    continue;

                if (code.Contains("("))
                    continue; // method, not a field

                if (code.Contains("readonly") || code.Contains("const"))
                    continue;

                if (code.Contains("class") || code.Contains("struct"))
                    continue;

                failures.Add(
                    $"{Path.GetFileName(file)}:{lineNo}  {raw.Trim()}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "Mutable static state carries hidden state between runs:\n  " +
            string.Join("\n  ", failures));
    }

    [Fact]
    public void ScanActuallyFindsSourceFiles()
    {
        // A scan that silently finds zero files passes forever. Guard the guard.
        Assert.True(
            SimSourceFiles().Count >= 2,
            "Purity scan found almost no files - the source path is wrong.");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Line number, the raw line, and the line with comments and literals removed.
    /// </summary>
    static IEnumerable<(int LineNo, string Raw, string Code)> CodeLines(string file)
    {
        bool inBlockComment = false;
        string[] lines = File.ReadAllLines(file);

        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i];
            string code = StripNonCode(raw, ref inBlockComment);

            if (raw.Contains("PURITY-OK"))
                continue; // checked on the raw line, pre-strip

            yield return (i + 1, raw, code);
        }
    }

    /// <summary>
    /// Removes // line comments, /* block comments */, "strings" and 'chars'.
    /// Verbatim (@"") and raw ("""") strings are not special-cased - the sim has no
    /// need for them, and if that changes this needs revisiting.
    /// </summary>
    static string StripNonCode(string line, ref bool inBlockComment)
    {
        var sb = new StringBuilder(line.Length);
        bool inString = false;
        bool inChar = false;
        bool escape = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            char next = i + 1 < line.Length ? line[i + 1] : '\0';

            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString)
            {
                if (escape)
                    escape = false;
                else if (c == '\\')
                    escape = true;
                else if (c == '"')
                    inString = false;

                continue;
            }

            if (inChar)
            {
                if (escape)
                    escape = false;
                else if (c == '\\')
                    escape = true;
                else if (c == '\'')
                    inChar = false;

                continue;
            }

            if (c == '/' && next == '/')
                break; // line comment

            if (c == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '\'')
            {
                inChar = true;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    static List<string> SimSourceFiles()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null
               && !File.Exists(Path.Combine(dir.FullName, "Relay.slnx"))
               && !File.Exists(Path.Combine(dir.FullName, "Relay.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(
            dir != null,
            "Could not locate the repo root (no Relay.slnx or Relay.sln above the test binary).");

        string simDir = Path.Combine(dir!.FullName, "sim", "Relay.Sim");

        Assert.True(
            Directory.Exists(simDir),
            $"Expected sim source at {simDir}");

        return Directory.GetFiles(
                simDir,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains(Path.Combine("obj", "")) &&
                !f.Contains(Path.Combine("bin", "")))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }
}