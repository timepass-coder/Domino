using System.IO;

namespace Relay.Sim
{
    public enum SimPhase : byte
    {
        Ready = 0,      // built, not started
        Running = 1,    // chain in progress
        Settled = 2,    // nothing is moving any more
        Failed = 3,     // a body left the kill box; the run is over
    }

    /// <summary>
    /// The box a run happens inside. A ball outside it has left the machine and the run
    /// has failed.
    /// <para>
    /// Part of SimState rather than a constant, because it is per-machine: a level
    /// authored 9 units wide should fail a ball that leaves it, not one that eventually
    /// wanders 100 units away. <c>SimConstants.DefaultBounds</c> is the fallback for
    /// states built in code rather than loaded from a file.
    /// </para>
    /// </summary>
    public readonly struct WorldBounds
    {
        public readonly Fix X0, Y0, X1, Y1;

        public WorldBounds(Fix x0, Fix y0, Fix x1, Fix y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        public bool Contains(Vec2 p)
            => p.X.Raw >= X0.Raw && p.X.Raw <= X1.Raw
            && p.Y.Raw >= Y0.Raw && p.Y.Raw <= Y1.Raw;

        public void WriteTo(BinaryWriter w)
        {
            w.Write(X0.Raw);
            w.Write(Y0.Raw);
            w.Write(X1.Raw);
            w.Write(Y1.Raw);
        }

        public static WorldBounds ReadFrom(BinaryReader r)
            => new WorldBounds(
                Fix.FromRaw(r.ReadInt64()),
                Fix.FromRaw(r.ReadInt64()),
                Fix.FromRaw(r.ReadInt64()),
                Fix.FromRaw(r.ReadInt64()));
    }

    /// <summary>
    /// The entire simulation at one instant. Everything the next tick needs is
    /// here and nowhere else - no statics, no engine objects, no wall clock.
    /// That is what makes Step(state) a pure function and replays exact.
    /// <para>
    /// Field order in WriteTo is the wire format. Changing it changes every
    /// golden hash, so bump FormatVersion when you do.
    /// </para>
    /// </summary>
    public readonly struct SimState
    {
        public const int FormatVersion = 3;   // 3: world bounds; 2: Ball.SurfaceIndex

        public readonly int Tick;
        public readonly Fix Gravity;
        public readonly WorldBounds Bounds;
        public readonly Ball[] Balls;
        public readonly Domino[] Dominoes;
        public readonly Surface[] Surfaces;
        public readonly int ChainCount;
        public readonly SimPhase Phase;

        public SimState(
            int tick,
            Fix gravity,
            Ball[] balls,
            Domino[] dominoes,
            Surface[] surfaces,
            int chainCount,
            SimPhase phase,
            WorldBounds bounds)
        {
            Tick = tick;
            Gravity = gravity;
            Bounds = bounds;
            Balls = balls;
            Dominoes = dominoes;
            Surfaces = surfaces;
            ChainCount = chainCount;
            Phase = phase;
        }

        /// <summary>
        /// A state in the default kill box, for worlds built in code rather than loaded
        /// from a machine file.
        /// <para>
        /// An overload rather than an optional parameter, because the only compile-time
        /// default a struct can have is all-zeroes - and an all-zeroes WorldBounds is a
        /// box of no size, which would fail every run on the first tick. Better to have
        /// no silent default at all than a lethal one.
        /// </para>
        /// </summary>
        public SimState(
            int tick,
            Fix gravity,
            Ball[] balls,
            Domino[] dominoes,
            Surface[] surfaces,
            int chainCount,
            SimPhase phase)
            : this(
                tick,
                gravity,
                balls,
                dominoes,
                surfaces,
                chainCount,
                phase,
                SimConstants.DefaultBounds)
        {
        }

        /// <summary>Seconds of simulated time elapsed. Derived, never stored.</summary>
        public Fix Time => Fix.Ratio(Tick, SimConstants.TicksPerSecond);

        // ------------------------------------------------------------- wire format

        public void WriteTo(BinaryWriter w)
        {
            w.Write(FormatVersion);
            w.Write(Tick);
            w.Write(Gravity.Raw);
            Bounds.WriteTo(w);
            w.Write(ChainCount);
            w.Write((byte)Phase);

            w.Write(Balls.Length);
            for (int i = 0; i < Balls.Length; i++)
                Balls[i].WriteTo(w);

            w.Write(Dominoes.Length);
            for (int i = 0; i < Dominoes.Length; i++)
                Dominoes[i].WriteTo(w);

            w.Write(Surfaces.Length);
            for (int i = 0; i < Surfaces.Length; i++)
                Surfaces[i].WriteTo(w);
        }

        public static SimState ReadFrom(BinaryReader r)
        {
            int version = r.ReadInt32();

            if (version != FormatVersion)
                throw new InvalidDataException(
                    $"SimState format {version}, expected {FormatVersion}.");

            int tick = r.ReadInt32();
            Fix gravity = Fix.FromRaw(r.ReadInt64());
            var bounds = WorldBounds.ReadFrom(r);
            int chain = r.ReadInt32();
            var phase = (SimPhase)r.ReadByte();

            var balls = new Ball[r.ReadInt32()];
            for (int i = 0; i < balls.Length; i++)
                balls[i] = Ball.ReadFrom(r);

            var dominoes = new Domino[r.ReadInt32()];
            for (int i = 0; i < dominoes.Length; i++)
                dominoes[i] = Domino.ReadFrom(r);

            var surfaces = new Surface[r.ReadInt32()];
            for (int i = 0; i < surfaces.Length; i++)
                surfaces[i] = Surface.ReadFrom(r);

            return new SimState(
                tick,
                gravity,
                balls,
                dominoes,
                surfaces,
                chain,
                phase,
                bounds);
        }

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms))
                    WriteTo(w);

                return ms.ToArray();
            }
        }

        public static SimState FromBytes(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes))
            using (var r = new BinaryReader(ms))
                return ReadFrom(r);
        }

        /// <summary>
        /// Determinism fingerprint. Hashing the serialised bytes rather than the
        /// fields directly means the hash and the wire format can never drift
        /// apart - a field left out of WriteTo is a field left out of the hash,
        /// and the round-trip test catches it.
        /// </summary>
        public ulong Hash()
        {
            byte[] bytes = ToBytes();
            return Fnv1a64.Hash(bytes, bytes.Length);
        }
    }
}