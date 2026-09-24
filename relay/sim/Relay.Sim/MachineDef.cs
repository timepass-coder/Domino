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

        /// <summary>
        /// How long the harness should run this machine for.
        /// </summary>
        public readonly int Ticks;

        public MachineDef(
            string id,
            int version,
            string notes,
            Fix gravity,
            WorldBounds bounds,
            Surface[] surfaces,
            string[] surfaceNames,
            Ball[] balls,
            string[] ballNames,
            Domino[] dominoes,
            string[] dominoNames,
            LeanHint[] dominoLeans,
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