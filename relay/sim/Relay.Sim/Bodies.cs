using System.IO;

namespace Relay.Sim
{
    public enum DominoState : byte
    {
        Standing = 0,
        Toppling = 1,
        Fallen = 2,
    }

    /// <summary>A rolling body. Position is the centre.</summary>
    public readonly struct Ball
{
    public const int FieldCount = 7;

    /// <summary>
    /// SurfaceIndex when the ball is touching nothing.
    /// </summary>
    public const int Airborne = -1;

    public readonly int Id;
    public readonly Vec2 Pos;
    public readonly Vec2 Vel;
    public readonly Fix Radius;
    public readonly Fix Mass;

    /// <summary>
    /// Index into SimState.Surfaces of the surface this ball is rolling on,
    /// or Airborne. Stored rather than re-derived each tick: a ball pinned to
    /// a named surface cannot drift off it through rounding, and "am I still
    /// touching?" stops being a threshold question that can flip either way.
    /// </summary>
    public readonly int SurfaceIndex;

    public readonly bool Asleep;

    public Ball(
        int id,
        Vec2 pos,
        Vec2 vel,
        Fix radius,
        Fix mass,
        int surfaceIndex,
        bool asleep)
    {
        Id = id;
        Pos = pos;
        Vel = vel;
        Radius = radius;
        Mass = mass;
        SurfaceIndex = surfaceIndex;
        Asleep = asleep;
    }

    public bool IsRolling => SurfaceIndex != Airborne;

    /// <summary>
    /// Moves the ball, leaving its contact unchanged.
    /// </summary>
    public Ball WithMotion(Vec2 pos, Vec2 vel, bool asleep)
        => new Ball(
            Id,
            pos,
            vel,
            Radius,
            Mass,
            SurfaceIndex,
            asleep);

    /// <summary>
    /// Moves the ball and changes what it is touching.
    /// </summary>
    public Ball WithContact(
        Vec2 pos,
        Vec2 vel,
        int surfaceIndex,
        bool asleep)
        => new Ball(
            Id,
            pos,
            vel,
            Radius,
            Mass,
            surfaceIndex,
            asleep);

    public void WriteTo(BinaryWriter w)
    {
        w.Write(Id);
        w.Write(Pos.RawX);
        w.Write(Pos.RawY);
        w.Write(Vel.RawX);
        w.Write(Vel.RawY);
        w.Write(Radius.Raw);
        w.Write(Mass.Raw);
        w.Write(SurfaceIndex);
        w.Write(Asleep);
    }

    public static Ball ReadFrom(BinaryReader r)
    {
        // Read into locals in wire order. Never nest reads inside a call -
        // that leans on argument evaluation order to stay correct.
        int id = r.ReadInt32();

        long posX = r.ReadInt64();
        long posY = r.ReadInt64();

        long velX = r.ReadInt64();
        long velY = r.ReadInt64();

        long rad = r.ReadInt64();
        long mass = r.ReadInt64();

        int surf = r.ReadInt32();
        bool slp = r.ReadBoolean();

        return new Ball(
            id,
            Vec2.FromRaw(posX, posY),
            Vec2.FromRaw(velX, velY),
            Fix.FromRaw(rad),
            Fix.FromRaw(mass),
            surf,
            slp);
        }
    }

    /// <summary>
    /// A domino, modelled as a rigid rectangle pivoting about a base corner.
    /// <para>
    /// Base is the midpoint of the bottom edge. Theta is signed: positive means
    /// leaning right and pivoting about the right base corner, negative means
    /// leaning left about the left corner. One signed angle therefore covers
    /// both fall directions without a separate direction field.
    /// </para>
    /// </summary>
    public readonly struct Domino
    {
        public const int FieldCount = 8; // bump this and WriteTo/ReadFrom together

        public readonly int Id;
        public readonly Vec2 Base;
        public readonly Fix Theta;
        public readonly Fix Omega;
        public readonly Fix Height;
        public readonly Fix Thickness;
        public readonly Fix Mass;
        public readonly DominoState State;

        public Domino(
            int id,
            Vec2 basePos,
            Fix theta,
            Fix omega,
            Fix height,
            Fix thickness,
            Fix mass,
            DominoState state)
        {
            Id = id;
            Base = basePos;
            Theta = theta;
            Omega = omega;
            Height = height;
            Thickness = thickness;
            Mass = mass;
            State = state;
        }

        public Domino WithMotion(Fix theta, Fix omega, DominoState state)
            => new Domino(
                Id,
                Base,
                theta,
                omega,
                Height,
                Thickness,
                Mass,
                state);

        public void WriteTo(BinaryWriter w)
        {
            w.Write(Id);
            w.Write(Base.RawX);
            w.Write(Base.RawY);
            w.Write(Theta.Raw);
            w.Write(Omega.Raw);
            w.Write(Height.Raw);
            w.Write(Thickness.Raw);
            w.Write(Mass.Raw);
            w.Write((byte)State);
        }

        public static Domino ReadFrom(BinaryReader r)
        {
            int id = r.ReadInt32();
            long baseX = r.ReadInt64();
            long baseY = r.ReadInt64();
            long theta = r.ReadInt64();
            long omega = r.ReadInt64();
            long h = r.ReadInt64();
            long t = r.ReadInt64();
            long mass = r.ReadInt64();
            byte state = r.ReadByte();

            return new Domino(
                id,
                Vec2.FromRaw(baseX, baseY),
                Fix.FromRaw(theta),
                Fix.FromRaw(omega),
                Fix.FromRaw(h),
                Fix.FromRaw(t),
                Fix.FromRaw(mass),
                (DominoState)state);
        }
    }

    /// <summary>A static line segment: floor, wall or ramp. Never moves.</summary>
    public readonly struct Surface
    {
        public const int FieldCount = 5; // bump this and WriteTo/ReadFrom together

        public readonly int Id;
        public readonly Vec2 A;
        public readonly Vec2 B;
        public readonly Fix Friction;
        public readonly Fix Restitution;

        public Surface(
            int id,
            Vec2 a,
            Vec2 b,
            Fix friction,
            Fix restitution)
        {
            Id = id;
            A = a;
            B = b;
            Friction = friction;
            Restitution = restitution;
        }

        // Phase 0 surfaces are axis-aligned only. Ramps need tangential gravity
// along the surface, which is Phase 1 work; the loader rejects anything
// diagonal rather than letting the sim quietly ignore it.
public bool IsHorizontal => A.Y.Raw == B.Y.Raw;
public bool IsVertical => A.X.Raw == B.X.Raw;
public bool IsAxisAligned => IsHorizontal || IsVertical;

public Fix MinX => Fix.Min(A.X, B.X);
public Fix MaxX => Fix.Max(A.X, B.X);
public Fix MinY => Fix.Min(A.Y, B.Y);
public Fix MaxY => Fix.Max(A.Y, B.Y);

public bool SpansX(Fix x) => x.Raw >= MinX.Raw && x.Raw <= MaxX.Raw;
public bool SpansY(Fix y) => y.Raw >= MinY.Raw && y.Raw <= MaxY.Raw;

        public void WriteTo(BinaryWriter w)
        {
            w.Write(Id);
            w.Write(A.RawX);
            w.Write(A.RawY);
            w.Write(B.RawX);
            w.Write(B.RawY);
            w.Write(Friction.Raw);
            w.Write(Restitution.Raw);
        }

        public static Surface ReadFrom(BinaryReader r)
        {
            int id = r.ReadInt32();
            long ax = r.ReadInt64();
            long ay = r.ReadInt64();
            long bx = r.ReadInt64();
            long by = r.ReadInt64();
            long fric = r.ReadInt64();
            long rest = r.ReadInt64();

            return new Surface(
                id,
                Vec2.FromRaw(ax, ay),
                Vec2.FromRaw(bx, by),
                Fix.FromRaw(fric),
                Fix.FromRaw(rest));
        }
    }
}