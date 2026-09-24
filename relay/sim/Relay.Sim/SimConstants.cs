namespace Relay.Sim
{
    /// <summary>
    /// Every number the simulation depends on. Must match sim/UNITS.md exactly.
    /// Built from exact integer ratios - never from a decimal literal, because that
    /// would mean parsing a float to produce a "deterministic" constant.
    /// </summary>
    public static class SimConstants
    {
        public const int TicksPerSecond = 120;

        public static readonly Fix Dt =
            Fix.Ratio(1, TicksPerSecond);

        public static readonly Fix Gravity =
            Fix.FromInt(-20);

        public static readonly Fix DominoHeight =
            Fix.One;

        public static readonly Fix DominoThickness =
            Fix.Ratio100(18); // 0.18

        public static readonly Fix BallRadius =
            Fix.Ratio100(35); // 0.35

        public static readonly Fix DefaultMass =
            Fix.One;

        // Tunnelling guard: max_speed * dt must stay under DominoThickness.
        public static readonly Fix MaxSpeed =
            Fix.Ratio10(216); // 21.6

        // Derived. Computed, never hand-typed,
    // so they cannot drift.
    // Declaration order matters: static initialisers run top to bottom.

    public static readonly Fix DominoThetaCrit =
        Fix.Atan2(DominoThickness, DominoHeight);

    public static readonly Fix DominoDiag =
        Fix.Sqrt(DominoHeight * DominoHeight +
                DominoThickness * DominoThickness);

    public static readonly Fix DominoComRadius =
        Fix.Div(DominoDiag, Fix.Two);

    /// <summary>
    /// Inertia about the base edge, PER UNIT MASS: (h^2 + t^2) / 3
    /// </summary>
    public static readonly Fix DominoInertiaPerMass =
        Fix.Div(DominoDiag * DominoDiag, Fix.FromInt(3));

    /// <summary>
    /// Energy to lift the centre of mass from h/2 to the balance point d_com,
    /// PER UNIT MASS. Equivalent to |g| * d_com * (1 - cos(theta_crit)).
    /// </summary>
    public static readonly Fix DominoTipEnergyPerMass =
        Fix.Abs(Gravity) *
        (DominoComRadius - Fix.Div(DominoHeight, Fix.Two));

    /// <summary>
    /// Minimum angular velocity that puts a domino over: sqrt(2E/I).
    /// Mass-independent - E and I both scale with m, so it cancels.
    /// </summary>
    public static readonly Fix DominoTipOmega =
        Fix.Sqrt(
            Fix.Div(
                Fix.Two * DominoTipEnergyPerMass,
                DominoInertiaPerMass));
        
        //
    // Rest thresholds. Below these a body snaps to exact rest, which is what
    // stops a chain jittering forever and keeps hashes settling.
    //

    /// <summary>Ball speed under which it is treated as stopped.</summary>
    public static readonly Fix RestSpeed = Fix.Ratio1000(5);
    // 0.005 u/s

    /// <summary>
/// Rebound speed under which a bounce becomes a roll.
///Derived from the tick rate, not chosen: a discrete bounce cannot
/// decay to zero. It decays to the fixed point of
/// v = e * (|g| * dt - v), which is e * |g| * dt / (1 + e).
///<para>
/// This is largest at e = 1, where it is |g| * dt / 2, so
/// |g| * dt clears every restitution in [0, 1] with a factor of
/// two to spare.
///<para>
/// An absolute threshold cannot work here. With one of 0.005 and
/// e = 0.5, a ball welds itself to the floor at a permanent
/// 0.0556 u/s, never sleeps, and the world never reaches Settled.
///<para>
/// The height thrown away is v^2 / (2 * |g|) = 0.0007 units -
/// under a thousandth of a domino, invisible on screen.
/// </summary>
    public static readonly Fix BounceRestSpeed = Fix.Abs(Gravity) * Dt;
// 0.1667 u/s

    /// <summary>Domino angular speed under which it is treated as stopped.</summary>
    public static readonly Fix RestOmega = Fix.Ratio1000(1);
    // 0.001 rad/s

    /// <summary>Lean under which an upright domino snaps back to exactly zero.</summary>
    public static readonly Fix RestAngle = Fix.Ratio(1, 10000);
    // 0.0001 rad

    /// <summary>A domino is flat on its face at 90 degrees from upright.</summary>
    public static readonly Fix DominoFallenAngle = Fix.PiHalf;

    // ---------------------------------------------------------------------------
    // --- Kill box. The outer limit of anywhere a body may be.
    // --- Generous on purpose - a backstop, not a level boundary. A machine
    // --- file names its own, tighter box in world.bounds, and the loader requires
    // --- that box to sit inside this one so the two cannot contradict each other.

    public static readonly Fix KillBoxMinX = Fix.FromInt(-100);
    public static readonly Fix KillBoxMaxX = Fix.FromInt(100);
    public static readonly Fix KillBoxMinY = Fix.FromInt(-50);
    public static readonly Fix KillBoxMaxY = Fix.FromInt(200);

    /// <summary>
    /// The kill box as a WorldBounds, for states built in code rather than loaded
    /// from a file. A machine-driven run uses the file's box instead.
    /// </summary>
    public static readonly WorldBounds DefaultBounds =
        new WorldBounds(KillBoxMinX, KillBoxMinY, KillBoxMaxX, KillBoxMaxY);
    }
}