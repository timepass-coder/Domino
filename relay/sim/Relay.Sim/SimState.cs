using System.IO;

namespace Relay.Sim
{
    public enum SimPhase : byte
    {
        Ready = 0,   // built, not started
        Running = 1, // chain in progress
        Settled = 2, // nothing is moving any more
        Failed = 3,  // a body left the killbox; the run is over.
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
        public const int FormatVersion = 2; // 2: Ball gained SurfaceIndex

        public readonly int Tick;
        public readonly Fix Gravity;
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
            SimPhase phase)
        {
            Tick = tick;
            Gravity = gravity;
            Balls = balls;
            Dominoes = dominoes;
            Surfaces = surfaces;
            ChainCount = chainCount;
            Phase = phase;
        }

        /// <summary>
        /// Seconds of simulated time elapsed. Derived, never stored.
        /// </summary>
        public Fix Time =>
            Fix.Ratio(Tick, SimConstants.TicksPerSecond);

        // ------------------------------------------------------------- wire format

        public void WriteTo(BinaryWriter w)
        {
            w.Write(FormatVersion);
            w.Write(Tick);
            w.Write(Gravity.Raw);
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
            {
                throw new InvalidDataException(
                    $"SimState format {version}, expected {FormatVersion}.");
            }

            int tick = r.ReadInt32();
            Fix gravity = Fix.FromRaw(r.ReadInt64());
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
                phase);
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