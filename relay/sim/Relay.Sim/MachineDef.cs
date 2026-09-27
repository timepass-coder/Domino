using System;

namespace Relay.Sim
{
    /// <summary>
    /// Which way a domino is meant to go over. Metadata only: it does not tilt the
    /// domino, and the sim never reads it. The validator and the Step 12 reporter use
    /// it to say "d3 fell left, the machine wanted right", which is the difference
    /// between a useful failure and a screenshot.
    /// </summary>
    public enum LeanHint : byte
    {
        None = 0,
        Left = 1,
        Right = 2
    }

    /// <summary>
    /// A stretch of one surface the player may place into. Placement data, not a body:
    /// the sim never sees a gap, only the dominoes placed in it.
    /// </summary>
    public readonly struct Gap
    {
        public readonly int Surface; // index into MachineDef.Surfaces
        public readonly Fix X0;      // a placed piece's whole footprint must fit
        public readonly Fix X1;      // inside [X0, X1]

        public Gap(int surface, Fix x0, Fix x1)
        {
            Surface = surface;
            X0 = x0;
            X1 = x1;
        }
    }

    /// <summary>
    /// What the machine is for: a box standing on a surface. The run is won the moment
    /// anything - ball or domino - touches it. Defined here; the touching is Phase 1
    /// Step 4.
    /// </summary>
    public readonly struct Goal
    {
        public readonly int Surface; // index into MachineDef.Surfaces
        public readonly Vec2 Base;   // centre of the bottom edge, on the surface
        public readonly Fix Width;
        public readonly Fix Height;

        public Goal(int surface, Vec2 baseCentre, Fix width, Fix height)
        {
            Surface = surface;
            Base = baseCentre;
            Width = width;
            Height = height;
        }

        public Fix MinX => Base.X - Fix.Div(Width, Fix.Two);
        public Fix MaxX => Base.X + Fix.Div(Width, Fix.Two);
    }

    /// <summary>
    /// The machine's own kill box, from <c>world.bounds</c>. A body outside it has
    /// left the machine and the run has failed.
    /// <para>
    /// Not yet what the sim tests against - stage 8 still uses SimConstants' box,
    /// because moving this into SimState is a wire-format bump. Validated here so the
    /// two cannot contradict each other in the meantime.
    /// </para>
    /// </summary>

    /// <summary>
    /// A validated machine file. The bridge between text and simulation, and the only
    /// thing that knows about names.
    /// <para>
    /// The sim identifies bodies by array index, because index order is the collision
    /// tiebreak and must never change. Files identify them by string, because "d3" is
    /// what a human can debug. So every body array here has a parallel name array in
    /// the same order - file order - and the index into both is the sim's id. Nothing
    /// is sorted, ever; a Dictionary would have thrown the ordering away.
    /// </para>
    /// <para>
    /// Separate from SimState on purpose. This holds what the file said, including the
    /// parts the sim has no field for (names, lean hints, notes, tick budget); SimState
    /// holds only what the tick function needs. ToState builds one from the other.
    /// </para>
    /// </summary>
    public sealed class MachineDef
    {
        /// <summary>
        /// The only machine-format version this loader understands.
        /// </summary>
        public const int SupportedVersion = 1;

        public readonly string Id;
        public readonly int Version;
        public readonly string Notes; // ignored by the sim, never hashed
        public readonly Fix Gravity;
        public readonly WorldBounds Bounds;

        public readonly Surface[] Surfaces;
        public readonly string[] SurfaceNames; // parallel to Surfaces

        public readonly Ball[] Balls;
        public readonly string[] BallNames; // parallel to Balls

        public readonly Domino[] Dominoes;
        public readonly string[] DominoNames; // parallel to Dominoes
        public readonly LeanHint[] DominoLeans; // parallel to Dominoes

        public readonly Gap[] Gaps;
        public readonly string[] GapNames; // parallel to Gaps

        /// <summary>How many dominoes the player may place. 0 in a Phase 0 machine.</summary>
        public readonly int DominoBudget;

        /// <summary>The goal, or null for a machine that just runs (Phase 0).</summary>
        public readonly Goal? Target;

        /// <summary>How long the harness should run this machine for.</summary>
        public readonly int Ticks;

        public MachineDef(
            string id, int version, string notes, Fix gravity,
            WorldBounds bounds,
            Surface[] surfaces, string[] surfaceNames,
            Ball[] balls, string[] ballNames,
            Domino[] dominoes, string[] dominoNames, LeanHint[] dominoLeans,
            Gap[] gaps, string[] gapNames, int dominoBudget, Goal? target,
            int ticks)
        {
            Id = id;
            Version = version;
            Notes = notes;
            Gravity = gravity;
            Bounds = bounds;

            Surfaces = surfaces;
            SurfaceNames = surfaceNames;

            Balls = balls;
            BallNames = ballNames;

            Dominoes = dominoes;
            DominoNames = dominoNames;
            DominoLeans = dominoLeans;

            Gaps = gaps;
            GapNames = gapNames;
            DominoBudget = dominoBudget;
            Target = target;

            Ticks = ticks;
        }

        /// <summary>
        /// The machine armed and ready, at tick 0, phase Ready.
        /// <para>
        /// Fresh body arrays every call. Step never mutates the state it is given, so
        /// sharing would be safe today - but reset() at Step 14 hands the same def out
        /// repeatedly, and a run that could scribble on the starting position would
        /// make the second attempt differ from the first.
        /// </para>
        /// </summary>
        public SimState ToState()
        {
            var balls = new Ball[Balls.Length];

            for (int i = 0; i < balls.Length; i++)
                balls[i] = Balls[i];

            var dominoes = new Domino[Dominoes.Length];

            for (int i = 0; i < dominoes.Length; i++)
                dominoes[i] = Dominoes[i];

            // Surfaces are shared rather than copied, exactly as Step shares them from
            // one tick to the next: nothing in the sim can move a surface.
            return new SimState(
                0,
                Gravity,
                balls,
                dominoes,
                Surfaces,
                0,
                SimPhase.Ready, Bounds);
        }

        /// <summary>
        /// Index of a named surface, or -1. Linear over file order.
        /// </summary>
        public int SurfaceIndex(string name)
        {
            for (int i = 0; i < SurfaceNames.Length; i++)
            {
                if (string.Equals(
                    SurfaceNames[i],
                    name,
                    StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Index of a named gap, or -1. Linear over file order.</summary>
        public int GapIndex(string name)
        {
            for (int i = 0; i < GapNames.Length; i++)
            {
                if (string.Equals(GapNames[i], name, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Index of a named domino, or -1. For tests and the reporter.
        /// </summary>
        public int DominoIndex(string name)
        {
            for (int i = 0; i < DominoNames.Length; i++)
            {
                if (string.Equals(
                    DominoNames[i],
                    name,
                    StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}