namespace Relay.Sim
{
    /// <summary>
    /// The tick. One pure function: same input, same output, forever, on every CPU.
    /// <para>
    /// The order of the stages below is not an implementation detail - it IS the
    /// physics. Two contacts fighting over the same body resolve in index order,
    /// so index order is the tiebreak, and it must never change. Never iterate a
    /// Dictionary here, never sort by a computed value, never parallelise.
    /// </para>
    /// </summary>
    public static class Simulation
    {
        /// <summary>
        /// Advances the world by exactly one tick (1/120 s). Allocates fresh
        /// arrays rather than mutating: a caller holding the previous state must
        /// still see the previous state, or replay and rewind stop working.
        /// </summary>
        public static SimState Step(in SimState s)
        {
            if (s.Phase == SimPhase.Settled || s.Phase == SimPhase.Failed)
                return s; // terminal: a no-op, not an error

            var balls = new Ball[s.Balls.Length];
            var dominoes = new Domino[s.Dominoes.Length];

            // Surfaces never move, so the array is shared rather than copied.
            Surface[] surfaces = s.Surfaces;

            // 1 + 2. Ball velocity then position. Velocity first, so a ball that
            // starts a tick at rest still moves during that tick.
            for (int i = 0; i < balls.Length; i++)
                balls[i] = IntegrateBall(s.Balls[i], s.Gravity);

            // 3. Ball vs surface. in surface index order
            ResolveBallSurfaceContacts(balls, s.Balls, surfaces);

            // 4. Domino angular integration, in index order.
            for (int i = 0; i < dominoes.Length; i++)
                dominoes[i] = IntegrateDomino(s.Dominoes[i], s.Gravity);

            // 5. Domino vs domino. -- Step 7
            ResolveDominoContacts(dominoes);

            // 6. Ball vs domino. -- Step 7
            ResolveBallDominoContacts(balls, dominoes);

            // 7. Settle.
            for (int i = 0; i < dominoes.Length; i++)
                dominoes[i] = SettleDomino(dominoes[i]);

            // 8. Kill box.
            bool failed = AnyBallOutOfBounds(balls, s.Bounds);

            // 9. Bookkeeping.
            int chain = CountToppled(dominoes);
            SimPhase phase = failed
                ? SimPhase.Failed
                : (IsQuiescent(balls, dominoes)
                    ? SimPhase.Settled
                    : SimPhase.Running);

            return new SimState(
                s.Tick + 1,
                s.Gravity,
                balls,
                dominoes,
                surfaces,
                chain,
                phase,
                s.Bounds);
        }
        /// <summary>
        /// Runs n ticks. Convenience only - carries no state of its own.
        /// </summary>
        public static SimState StepMany(SimState s, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                s = Step(in s);

            return s;
        }

        // ------------------------------------------------------------------ stage 1+2

        static Ball IntegrateBall(Ball b, Fix gravity)
        {
            if (b.Asleep)
                return b;

            // a rolling ball is owned entirely by stage 3, which knows the surface
            // it is pinned to, applying gravity here and cancelling it there would
            // work, but it throws away precision every trick for no reason
            if (b.IsRolling) return b;

            Vec2 vel = new Vec2(
                b.Vel.X,
                b.Vel.Y + gravity * SimConstants.Dt);

            vel = ClampSpeed(vel);

            Vec2 pos = b.Pos + vel * SimConstants.Dt;

            return b.WithMotion(pos, vel, false);
        }

        /// <summary>
        /// Holds the tunnelling guarantee: MaxSpeed * Dt is under the thinnest
        /// feature in the game, so nothing can cross a domino in a single tick.
        /// </summary>
        static Vec2 ClampSpeed(Vec2 v)
        {
            Fix speed = Vec2.Length(v);

            if (speed.Raw <= SimConstants.MaxSpeed.Raw)
                return v;

            return v * Fix.Div(SimConstants.MaxSpeed, speed);
        }

        // -------------------------------------------------------------------- stage 4

        /// <summary>
        /// Gravity torque about the pivoting base corner, per unit mass:
        /// <c>|g| * d_com * sin(|theta| - theta_crit)</c>, signed by fall direction.
        /// <para>
        /// Negative below theta_crit - the centre of mass is still inside the base,
        /// so gravity pushes the domino back upright. Zero exactly at theta_crit.
        /// Positive beyond it, and from there it accelerates over. Mass cancels:
        /// torque and inertia both scale with m.
        /// </para>
        /// </summary>
        static Domino IntegrateDomino(Domino d, Fix gravity)
        {
            int dir = FallDirection(d);

            if (dir == 0)
                return d; // upright and motionless: nothing to do

            Fix lean = Fix.Abs(d.Theta);

            Fix alpha = Fix.Div(
                Fix.Abs(gravity)
                    * SimConstants.DominoComRadius
                    * Fix.Sin(lean - SimConstants.DominoThetaCrit),
                SimConstants.DominoInertiaPerMass);

            if (dir < 0)
                alpha = -alpha;

            Fix omega = d.Omega + alpha * SimConstants.Dt;
            Fix theta = d.Theta + omega * SimConstants.Dt;

            // Flat on its face: stop dead. No bounce in Phase 0.
            if (Fix.Abs(theta).Raw >= SimConstants.DominoFallenAngle.Raw)
            {
                Fix flat = dir > 0
                    ? SimConstants.DominoFallenAngle
                    : -SimConstants.DominoFallenAngle;

                return d.WithMotion(
                    flat,
                    Fix.Zero,
                    DominoState.Fallen);
            }

            // Rocked back down through upright: land, do not pass through.
            if (theta.Raw != 0 && Sign(theta) != dir)
            {
                return d.WithMotion(
                    Fix.Zero,
                    Fix.Zero,
                    DominoState.Standing);
            }

            return d.WithMotion(
                theta,
                omega,
                ClassifyLean(theta, d.State));
        }

        /// <summary>
        /// Which way this domino is going over. Theta's sign decides once it has
        /// leaned at all; before that, omega's sign does. Both zero means upright
        /// and undisturbed, which has no direction and needs none.
        /// </summary>
        static int FallDirection(Domino d)
        {
            if (d.State == DominoState.Fallen)
                return 0;

            if (d.Theta.Raw != 0)
                return Sign(d.Theta);

            return Sign(d.Omega);
        }

        static DominoState ClassifyLean(Fix theta, DominoState current)
        {
            if (current == DominoState.Fallen)
                return DominoState.Fallen;

            return Fix.Abs(theta).Raw > SimConstants.DominoThetaCrit.Raw
                ? DominoState.Toppling
                : DominoState.Standing;
        }

        static int Sign(Fix v)
        {
            return v.Raw > 0 ? 1 : v.Raw < 0 ? -1 : 0;
        }

        // -------------------------------------------------------------------- stage 7

        static Domino SettleDomino(Domino d)
        {
            if (d.State != DominoState.Standing)
                return d;

            bool stillLeaning =
                Fix.Abs(d.Theta).Raw > SimConstants.RestAngle.Raw;

            bool stillTurning =
                Fix.Abs(d.Omega).Raw > SimConstants.RestOmega.Raw;

            if (stillLeaning || stillTurning)
                return d;

            // Snap to exact zero rather than a near-zero. Two runs that both
            // "nearly" settle can settle to different near-zeros; exact ones
            // cannot, and the hash notices the difference.
            return d.WithMotion(
                Fix.Zero,
                Fix.Zero,
                DominoState.Standing);
        }

        // -------------------------------------------------------------------- stage 8

        /// <summary>
        /// Tests the machine's own box, carried on the state, rather than a constant. A
        /// level 9 units wide should fail a ball that leaves it, not wait until the ball
        /// is 100 units away - and the box travels with the state so a replay cannot
        /// disagree with the run it replays.
        /// </summary>
        static bool AnyBallOutOfBounds(Ball[] balls, WorldBounds bounds)
        {
            for (int i = 0; i < balls.Length; i++)
            {
                if (!bounds.Contains(balls[i].Pos))
                    return true;
            }

            return false;
        }

        // -------------------------------------------------------------------- stage 9

        static int CountToppled(Domino[] dominoes)
        {
            // Recomputed from scratch every tick on purpose. A stored counter can
            // disagree with the dominoes it is counting; a derived one cannot.
            int n = 0;

            for (int i = 0; i < dominoes.Length; i++)
            {
                if (dominoes[i].State != DominoState.Standing)
                    n++;
            }

            return n;
        }

        static bool IsQuiescent(Ball[] balls, Domino[] dominoes)
        {
            for (int i = 0; i < balls.Length; i++)
            {
                if (!balls[i].Asleep)
                    return false;
            }

            for (int i = 0; i < dominoes.Length; i++)
            {
                if (dominoes[i].Omega.Raw != 0)
                    return false;
            }

            return true;
        }

        // ------------------------------------------------------- stubs: Steps 6 and 7

        // Deliberately empty, and deliberately called anyway. The call sites fix
        // the stage order now, so filling these in later cannot quietly reorder
        // the tick and invalidate every golden hash.

        static void ResolveBallSurfaceContacts(
    Ball[] balls,
    Ball[] previous,
    Surface[] surfaces)
{
    for (int i = 0; i < balls.Length; i++)
    {
        Ball b = balls[i];

        if (b.Asleep)
            continue;

        Fix fromX = previous[i].Pos.X;

        if (b.IsRolling)
        {
            fromX = b.Pos.X;
            b = Roll(b, surfaces);
        }
        else
        {
            b = Land(b, previous[i], surfaces);
        }

        b = BounceOffWalls(b, fromX, surfaces);
        balls[i] = Sleep(b);
    }
}

/// <summary>
/// Airborne: did the ball cross a floor during this tick?
/// Surfaces are tested in index order and the first crossing wins,
/// so two overlapping shelves resolve deterministically instead
/// of by whichever is nearer.
/// </summary>
static Ball Land(Ball b, Ball prev, Surface[] surfaces)
{
    if (b.Vel.Y.Raw >= 0)
        return b;

    // Rising or level: cannot land.
    Fix wasBottom = prev.Pos.Y - b.Radius;
    Fix nowBottom = b.Pos.Y - b.Radius;

    for (int i = 0; i < surfaces.Length; i++)
    {
        Surface surf = surfaces[i];

        if (!surf.IsHorizontal)
            continue;

        Fix top = surf.A.Y;

        bool crossed =
            wasBottom.Raw >= top.Raw &&
            nowBottom.Raw <= top.Raw;

        if (!crossed || !surf.SpansX(b.Pos.X))
            continue;

        Fix rest = surf.Restitution;
        Fix bounce = -(rest * b.Vel.Y);

        // Vel.Y is negative here.
        Vec2 pos = new Vec2(
            b.Pos.X,
            top + b.Radius);

        // Too slow to be worth a bounce: start rolling instead.
        //
        // The threshold scales with |g| * dt. An absolute threshold
        // can allow a permanent micro-bounce that never settles.
        //
        // The comparison is inclusive: at restitution 1, the rebound
        // from a resting ball is exactly BounceRestSpeed. Using a
        // strict '<' would leave it hovering motionless-but-airborne.
        if (bounce.Raw <= SimConstants.BounceRestSpeed.Raw)
        {
            return b.WithContact(
                pos,
                new Vec2(b.Vel.X, Fix.Zero),
                i,
                false);
        }

        return b.WithContact(
            pos,
            new Vec2(b.Vel.X, bounce),
            Ball.Airborne,
            false);
    }

    return b;
}

/// <summary>
/// Rolling: pinned to the surface top, losing speed to rolling friction.
/// Phase 0 surfaces are axis-aligned, so there is no tangential gravity;
/// friction is the only horizontal force.
/// </summary>
static Ball Roll(Ball b, Surface[] surfaces)
{
    Surface surf = surfaces[b.SurfaceIndex];

    Fix decel =
        surf.Friction *
        Fix.Abs(SimConstants.Gravity) *
        SimConstants.Dt;

    Fix vx = b.Vel.X;

    // Snap to zero rather than stepping past it.
    // Letting velocity cross zero is the classic jitter bug:
    // the ball can oscillate by one raw unit forever and never settle.
    if (Fix.Abs(vx).Raw <= decel.Raw)
    {
        vx = Fix.Zero;
    }
    else if (vx.Raw > 0)
    {
        vx = vx - decel;
    }
    else
    {
        vx = vx + decel;
    }

    Fix x = b.Pos.X + vx * SimConstants.Dt;
    Fix y = surf.A.Y + b.Radius;

    // Rolled off the end: airborne again, keeping horizontal speed.
    if (!surf.SpansX(x))
    {
        return b.WithContact(
            new Vec2(x, y),
            new Vec2(vx, Fix.Zero),
            Ball.Airborne,
            false);
    }

    return b.WithContact(
        new Vec2(x, y),
        new Vec2(vx, Fix.Zero),
        b.SurfaceIndex,
        false);
}

/// <summary>
/// Vertical surfaces, swept in X.
/// Applies whether the ball is rolling or airborne, so a ball can
/// roll into a backstop and bounce back.
/// </summary>
static Ball BounceOffWalls(
    Ball b,
    Fix fromX,
    Surface[] surfaces)
{
    for (int i = 0; i < surfaces.Length; i++)
    {
        Surface surf = surfaces[i];

        if (!surf.IsVertical || !surf.SpansY(b.Pos.Y))
            continue;

        Fix wall = surf.A.X;
        Fix r = b.Radius;

        bool fromLeft =
            (fromX + r).Raw <= wall.Raw &&
            (b.Pos.X + r).Raw > wall.Raw;

        bool fromRight =
            (fromX - r).Raw >= wall.Raw &&
            (b.Pos.X - r).Raw < wall.Raw;

        if (!fromLeft && !fromRight)
            continue;

        Fix x = fromLeft
            ? wall - r
            : wall + r;

        Fix vx = -(surf.Restitution * b.Vel.X);

        if (Fix.Abs(vx).Raw < SimConstants.RestSpeed.Raw)
            vx = Fix.Zero;

        return b.WithMotion(
            new Vec2(x, b.Pos.Y),
            new Vec2(vx, b.Vel.Y),
            false);
    }

    return b;
}

/// <summary>
/// A ball rolling with no speed left is done moving.
/// </summary>
static Ball Sleep(Ball b)
{
    if (!b.IsRolling)
        return b;

    if (b.Vel.X.Raw != 0 || b.Vel.Y.Raw != 0)
        return b;

    return b.WithMotion(
        b.Pos,
        Vec2.Zero,
        true);
}

        /// <summary>
        /// Domino strikes domino. Pairs are visited in index order and each impulse
        /// is applied before the next pair is tested, so a chain resolves the same
        /// way every run. Never iterate this by distance or by which contact looks
        /// most urgent - that is how a chain stops being reproducible.
        /// </summary>
        static void ResolveDominoContacts(Domino[] dominoes)
        {
            for (int i = 0; i < dominoes.Length; i++)
            {
                int dir = FallDirection(dominoes[i]);
                if (dir == 0)
                    continue;

                // Upright: nothing reaches.
                for (int j = 0; j < dominoes.Length; j++)
                {
                    if (j == i)
                        continue;

                    if (!InContact(dominoes[i], dominoes[j], dir))
                        continue;

                    TransferImpulse(dominoes, i, j, dir);
                }
            }
        }

        /// <summary>
        /// Has i's leading corner reached j's back face, at a height j can actually
        /// be hit at?
        /// </summary>
        static bool InContact(Domino faller, Domino struck, int dir)
        {
            // Phase 0 keeps chains on one level. Dominoes on different shelves are
            // not each other's problem, and pretending otherwise would let a domino
            // topple something a metre below it.
            if (faller.Base.Y.Raw != struck.Base.Y.Raw)
                return false;

            // Only what lies ahead. Without this a domino falling right would also
            // report contact with the neighbour behind it.
            bool ahead = dir > 0
                ? struck.Base.X.Raw > faller.Base.X.Raw
                : struck.Base.X.Raw < faller.Base.X.Raw;

            if (!ahead)
                return false;

            // Corner above the struck domino's top: it swings over, not into it.
            Fix arm = faller.Arm;
            if (arm.Raw > struck.Height.Raw)
                return false;

            Fix corner = faller.LeadingCornerX(dir);
            Fix face = struck.StruckFaceX(dir);

            return dir > 0
                ? corner.Raw >= face.Raw
                : corner.Raw <= face.Raw;
        }

        /// <summary>
        /// Fully inelastic angular impulse about the two pivots. Step 7 is
        /// impulse-only on purpose: it under-delivers compared with a real domino,
        /// which lands on its neighbour and keeps pushing. Step 10 measures how much
        /// that costs before anyone decides to add sustained contact.
        /// </summary>
        /// <para>
        /// e is fixed at 0. Raising it above 1 to make chains work would be energy
        /// from nowhere, and Phase 2's energy thresholds would inherit the lie.
        /// </para>
        static void TransferImpulse(Domino[] dominoes, int i, int j, int dir)
        {
            Domino faller = dominoes[i];
            Domino struck = dominoes[j];

            Fix arm = faller.Arm;
            if (arm.Raw == 0)
                return; // Flat on its face: no lever left.

            // Work in the fall direction so both rates are positive when closing.
            Fix omegaFaller = dir > 0
                ? faller.Omega
                : -faller.Omega;

            Fix omegaStruck = dir > 0
                ? struck.Omega
                : -struck.Omega;

            Fix closing = omegaFaller - omegaStruck;
            if (closing.Raw <= 0)
                return; // Separating, or already matched.

            Fix inertiaFaller = faller.Inertia;
            Fix inertiaStruck = struck.Inertia;

            // J = v_rel / (arm^2 * (1/I_f + 1/I_s)), with e = 0.
            Fix vrel = arm * closing;

            Fix invSum =
                Fix.Div(Fix.One, inertiaFaller) +
                Fix.Div(Fix.One, inertiaStruck);

            Fix impulse = Fix.Div(
                vrel,
                arm * arm * invSum);

            Fix deltaStruck = Fix.Div(
                impulse * arm,
                inertiaStruck);

            Fix deltaFaller = Fix.Div(
                impulse * arm,
                inertiaFaller);

            Fix newStruck = dir > 0
                ? struck.Omega + deltaStruck
                : struck.Omega - deltaStruck;

            Fix newFaller = dir > 0
                ? faller.Omega - deltaFaller
                : faller.Omega + deltaFaller;

            dominoes[j] = struck.WithMotion(
                struck.Theta,
                newStruck,
                ClassifyLean(struck.Theta, struck.State));

            dominoes[i] = faller.WithMotion(
                faller.Theta,
                newFaller,
                ClassifyLean(faller.Theta, faller.State));
        }

        /// <summary>
        /// Ball strikes domino. Balls in index order, dominoes in index order inside that,
        /// each impulse applied before the next pair is tested - same rule as the
        /// domino-domino pass, for the same reason.
        /// </summary>
        static void ResolveBallDominoContacts(Ball[] balls, Domino[] dominoes)
        {
            for (int i = 0; i < balls.Length; i++)
            {
                if (balls[i].Asleep)
                    continue;

                int dir = Sign(balls[i].Vel.X);

                // Not travelling: nothing to give.
                if (dir == 0)
                    continue;

                for (int j = 0; j < dominoes.Length; j++)
                {
                    if (!BallReaches(balls[i], dominoes[j], dir))
                        continue;

                    TransferBallImpulse(balls, dominoes, i, j, dir);
                }
            }
        }

        /// <summary>
        /// Is the ball's leading edge in the domino's near face, at a height on the
        /// face rather than over the top of it?
        /// </summary>
        static bool BallReaches(Ball b, Domino d, int dir)
        {
            // A fallen domino is scenery. It has no lever left and nothing to give.
            if (d.State == DominoState.Fallen)
                return false;

            // Contact height above the domino's base, which is also the moment arm -
            // the same coincidence that makes the domino-domino case cheap, except
            // here the height comes from the ball's centre instead of h*cos(theta).
            Fix arm = b.Pos.Y - d.Base.Y;

            if (arm.Raw <= 0 || arm.Raw > d.Height.Raw)
                return false;

            // The face on the side the ball is arriving from, taken at the base.
            Fix face = d.StruckFaceX(dir);

            Fix edge = dir > 0
                ? b.Pos.X + b.Radius
                : b.Pos.X - b.Radius;

            // Reached the near face, and the centre has not yet crossed the far one.
            // The second half matters: without it a ball that got through keeps being
            // grabbed from behind and hauled backwards.
            Fix back = dir > 0
                ? face + d.Thickness
                : face - d.Thickness;

            return dir > 0
                ? edge.Raw >= face.Raw && b.Pos.X.Raw <= back.Raw
                : edge.Raw <= face.Raw && b.Pos.X.Raw >= back.Raw;
        }

        /// <summary>
        /// Fully inelastic horizontal impulse: the ball's linear inertia against the
        /// domino's angular inertia reduced to the contact point, arm^2.
        ///
        /// Impulse-only and e = 0, the same two choices as TransferImpulse. There is
        /// no position correction: a ball that fails to topple a domino keeps creeping
        /// into it rather than being stopped dead by it. Step 10 decides whether that
        /// needs sustained contact, and it is the same decision for both passes.
        /// </summary>
        static void TransferBallImpulse(
            Ball[] balls,
            Domino[] dominoes,
            int i,
            int j,
            int dir)
        {
            Ball b = balls[i];
            Domino d = dominoes[j];

            Fix arm = b.Pos.Y - d.Base.Y;

            // Work in the direction of travel so both speeds are positive when closing.
            Fix ballSpeed = dir > 0
                ? b.Vel.X
                : -b.Vel.X;

            Fix faceSpeed = arm * (dir > 0
                ? d.Omega
                : -d.Omega);

            Fix closing = ballSpeed - faceSpeed;

            // The face is outrunning the ball.
            if (closing.Raw <= 0)
                return;

            Fix inertia = d.Inertia;

            Fix invSum =
                Fix.Div(Fix.One, b.Mass) +
                Fix.Div(arm * arm, inertia);

            Fix impulse = Fix.Div(closing, invSum);

            Fix deltaOmega =
                Fix.Div(impulse * arm, inertia);

            Fix deltaBall =
                Fix.Div(impulse, b.Mass);

            Fix newOmega = dir > 0
                ? d.Omega + deltaOmega
                : d.Omega - deltaOmega;

            Fix newVx = dir > 0
                ? b.Vel.X - deltaBall
                : b.Vel.X + deltaBall;

            dominoes[j] = d.WithMotion(
                d.Theta,
                newOmega,
                ClassifyLean(d.Theta, d.State));

            // WithMotion, not WithContact: a rolling ball is still on its surface
            // after hitting a domino. Awake, though - it has just been given work.
            balls[i] = b.WithMotion(
                b.Pos,
                new Vec2(newVx, b.Vel.Y),
                false);
        }
    }
}