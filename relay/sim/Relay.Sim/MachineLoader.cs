using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Relay.Sim
{
    /// <summary>
    /// Machine file to MachineDef, with every rule checked on the way through.
    /// <para>
    /// The governing principle: a machine that cannot be loaded correctly must fail
    /// loudly. Nothing here defaults, clamps or ignores. A misspelled "restituton"
    /// silently becoming 0.0 would produce a level that plays differently from the one
    /// its author wrote and looks fine doing it - so unknown fields are an error, not
    /// a shrug. The same goes for the fields Phase 0 has no use for yet: they are
    /// required to be empty, rather than quietly skipped.
    /// </para>
    /// </summary>
    public static class MachineLoader
    {
        /// <summary>
        /// Longest run a machine may ask for: ten minutes of simulated time. A file
        /// asking for more has a typo in it, and the harness would appear to hang.
        /// </summary>
        public const int MaxTicks = SimConstants.TicksPerSecond * 600;

        // ------------------------------------------------------------------
        // Entry points

        /// <summary>
        /// Loads and validates a machine file. The file's "id" must match its own
        /// filename - the golden hashes are keyed by id, and a file whose name and id
        /// disagree would bless the wrong machine.
        /// <para>
        /// A convenience for the tests and the harness, not the API Unity should use.
        /// Android keeps StreamingAssets inside the APK where File cannot reach them, so
        /// the render bridge at Step 13 reads the text its own way and calls Parse.
        /// </para>
        /// </summary>
        public static MachineDef LoadFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new MachineFormatException("no machine path given");

            if (!File.Exists(path))
                throw new MachineFormatException(
                    $"no machine file at \"{path}\"");

            return Parse(
                ReadUtf8(path),
                Path.GetFileNameWithoutExtension(path));
        }

        /// <summary>
        /// Validates machine text. <paramref name="expectedId"/> is the filename the
        /// text came from, or null to skip that check (tests build text inline).
        /// </summary>
        public static MachineDef Parse(string text, string? expectedId)
        {
            JsonValue root = JsonReader.Parse(text);

            if (root.Kind != JsonKind.Object)
            {
                throw new MachineFormatException(
                    $"a machine must be an object, found {root.Kind}");
            }

            RequireOnly(
                root,
                "machine",
                "id",
                "version",
                "notes",
                "world",
                "surfaces",
                "spawns",
                "fixed",
                "gaps",
                "budget",
                "target",
                "sim");

            // Version first. Every message after this one assumes the v1 field set, so
            // a v2 file must be rejected before we start complaining about its shape.
            int version = Integer(root, "version", "machine");

            if (version != MachineDef.SupportedVersion)
            {
                throw new MachineFormatException(
                    $"machine.version is {version}, and this loader only understands " +
                    $"{MachineDef.SupportedVersion}");
            }

            string id = Text(root, "id", "machine");

            if (id.Length == 0)
                throw new MachineFormatException("machine.id is empty");

            if (expectedId != null &&
                !string.Equals(id, expectedId, StringComparison.Ordinal))
            {
                throw new MachineFormatException(
                    $"machine.id is \"{id}\" but the file is named \"{expectedId}\"");
            }

            string notes =
                root.TryGet("notes", out JsonValue n) && !n.IsNull
                    ? n.Text
                    : "";

            ParseWorld(
                root,
                out Fix gravity,
                out WorldBounds bounds);

            ParseSurfaces(
                root,
                bounds,
                out Surface[] surfaces,
                out string[] surfaceNames);

            var takenIds = new List<string>();

            ParseSpawns(
                root,
                bounds,
                takenIds,
                out Ball[] balls,
                out string[] ballNames);

            ParseFixed(
                root,
                surfaces,
                surfaceNames,
                takenIds,
                out Domino[] dominoes,
                out string[] dominoNames,
                out LeanHint[] leans);

            RequireEmptyArray(
                root,
                "gaps",
                "machine",
                "gaps are Phase 1 - Phase 0 machines have no player placement");

            RequireEmptyObject(
                root,
                "budget",
                "machine",
                "budgets are Phase 1 - Phase 0 has nothing to spend");

            RequireNull(
                root,
                "target",
                "machine",
                "targets are Phase 1 - a Phase 0 machine just runs");

            int ticks = ParseSim(root);

            return new MachineDef(
                id,
                version,
                notes,
                gravity,
                bounds,
                surfaces,
                surfaceNames,
                balls,
                ballNames,
                dominoes,
                dominoNames,
                leans,
                ticks);
        }

        // ------------------------------------------------------------------
        // World

        static void ParseWorld(
            JsonValue root,
            out Fix gravity,
            out WorldBounds bounds)
        {
            JsonValue world = Object(root, "world", "machine");

            RequireOnly(
                world,
                "world",
                "gravity",
                "bounds");

            gravity = Decimal(
                world,
                "gravity",
                "world");

            if (gravity.Raw >= 0)
            {
                throw new MachineFormatException(
                    "world.gravity must be negative - y grows up, so gravity points down");
            }

            JsonValue b = Object(
                world,
                "bounds",
                "world");

            RequireOnly(
                b,
                "world.bounds",
                "x0",
                "y0",
                "x1",
                "y1");

            Fix x0 = Decimal(b, "x0", "world.bounds");
            Fix y0 = Decimal(b, "y0", "world.bounds");
            Fix x1 = Decimal(b, "x1", "world.bounds");
            Fix y1 = Decimal(b, "y1", "world.bounds");

            if (x1.Raw <= x0.Raw)
            {
                throw new MachineFormatException(
                    "world.bounds: x1 must be greater than x0");
            }

            if (y1.Raw <= y0.Raw)
            {
                throw new MachineFormatException(
                    "world.bounds: y1 must be greater than y0");
            }

            // Stage 8 now tests this box and not the constant one, so the constant's only
            // remaining job is to be an outer limit on what a file may ask for. A machine
            // declaring a box 10000 units wide would let a ball wander that far before the
            // run was called failed, which is a hang in everything but name.

            if (x0.Raw < SimConstants.KillBoxMinX.Raw ||
                x1.Raw > SimConstants.KillBoxMaxX.Raw ||
                y0.Raw < SimConstants.KillBoxMinY.Raw ||
                y1.Raw > SimConstants.KillBoxMaxY.Raw)
            {
                throw new MachineFormatException(
                    "world.bounds reaches outside the sim's kill box, which is the outer " +
                    "limit on how far a run may stray before it is called failed");
            }

            bounds = new WorldBounds(
                x0,
                y0,
                x1,
                y1);
        }

        // ------------------------------------------------------------------
        // Surfaces

        static void ParseSurfaces(
            JsonValue root,
            WorldBounds bounds,
            out Surface[] surfaces,
            out string[] names)
        {
            JsonValue arr = Array(
                root,
                "surfaces",
                "machine");

            if (arr.Count == 0)
            {
                throw new MachineFormatException(
                    "machine.surfaces is empty - every machine needs something to stand on");
            }

            surfaces = new Surface[arr.Count];
            names = new string[arr.Count];

            for (int i = 0; i < arr.Count; i++)
            {
                string where = $"surfaces[{i}]";
                JsonValue s = arr[i];

                if (s.Kind != JsonKind.Object)
                {
                    throw new MachineFormatException(
                        $"{where}: expected an object, found {s.Kind}");
                }

                RequireOnly(
                    s,
                    where,
                    "id",
                    "x0",
                    "y0",
                    "x1",
                    "y1",
                    "friction",
                    "restitution");

                string name = Text(s, "id", where);

                if (name.Length == 0)
                    throw new MachineFormatException($"{where}.id is empty");

                for (int k = 0; k < i; k++)
                {
                    if (string.Equals(
                        names[k],
                        name,
                        StringComparison.Ordinal))
                    {
                        throw new MachineFormatException(
                            $"{where}.id \"{name}\" is already used by surfaces[{k}]");
                    }
                }

                names[i] = name;

                Fix x0 = Decimal(s, "x0", where);
                Fix y0 = Decimal(s, "y0", where);
                Fix x1 = Decimal(s, "x1", where);
                Fix y1 = Decimal(s, "y1", where);
                Fix friction = Decimal(s, "friction", where);
                Fix restitution = Decimal(s, "restitution", where);

                if (friction.Raw < 0)
                {
                    throw new MachineFormatException(
                        $"{where}.friction is negative, which would speed the ball up");
                }

                // Restitution 1.0 conserves energy at the impact, so the only decay left
                // is the landing snap discarding one tick of gravity - linear in bounce
                // count, not geometric. A 4-unit drop takes 49 s and 75 bounces to settle.
                // The sim terminates; the level is unplayable. See sim/UNITS.md.
                if (restitution.Raw < 0 ||
                    restitution.Raw >= Fix.One.Raw)
                {
                    throw new MachineFormatException(
                        $"{where}.restitution must be at least 0 and below 1 - at 1.0 a " +
                        "bounce decays linearly and takes 49 s to settle");
                }

                var surf = new Surface(
                    i,
                    new Vec2(x0, y0),
                    new Vec2(x1, y1),
                    friction,
                    restitution);

                if (!surf.IsAxisAligned)
                {
                    throw new MachineFormatException(
                        $"{where} is diagonal. Phase 0 surfaces are axis-aligned: a ramp " +
                        "needs gravity resolved along the surface, which is Phase 1 work");
                }

                if (x0.Raw == x1.Raw &&
                    y0.Raw == y1.Raw)
                {
                    throw new MachineFormatException(
                        $"{where} has zero length");
                }

                if (!bounds.Contains(surf.A) ||
                    !bounds.Contains(surf.B))
                {
                    throw new MachineFormatException(
                        $"{where} extends outside world.bounds");
                }

                surfaces[i] = surf;
            }
        }

        // ------------------------------------------------------------------
        // Spawns

        static void ParseSpawns(
            JsonValue root,
            WorldBounds bounds,
            List<string> takenIds,
            out Ball[] balls,
            out string[] names)
        {
            JsonValue arr = Array(
                root,
                "spawns",
                "machine");

            balls = new Ball[arr.Count];
            names = new string[arr.Count];

            for (int i = 0; i < arr.Count; i++)
            {
                string where = $"spawns[{i}]";
                JsonValue s = arr[i];

                if (s.Kind != JsonKind.Object)
                {
                    throw new MachineFormatException(
                        $"{where}: expected an object, found {s.Kind}");
                }

                RequireOnly(
                    s,
                    where,
                    "type",
                    "id",
                    "x",
                    "y",
                    "vx",
                    "vy",
                    "radius",
                    "mass");

                string type = Text(s, "type", where);

                if (!string.Equals(
                    type,
                    "ball",
                    StringComparison.Ordinal))
                {
                    throw new MachineFormatException(
                        $"{where}.type is \"{type}\" - Phase 0 only spawns balls");
                }

                names[i] = TakeId(
                    s,
                    where,
                    takenIds);

                Fix x = Decimal(s, "x", where);
                Fix y = Decimal(s, "y", where);
                Fix vx = Decimal(s, "vx", where);
                Fix vy = Decimal(s, "vy", where);
                Fix radius = Decimal(s, "radius", where);
                Fix mass = Decimal(s, "mass", where);

                if (radius.Raw <= 0)
                {
                    throw new MachineFormatException(
                        $"{where}.radius must be positive");
                }

                if (mass.Raw <= 0)
                {
                    throw new MachineFormatException(
                        $"{where}.mass must be positive");
                }

                var pos = new Vec2(x, y);

                if (!bounds.Contains(pos))
                {
                    throw new MachineFormatException(
                        $"{where} starts outside world.bounds");
                }

                // The tunnelling guarantee: at MaxSpeed a body moves less than one domino
                // thickness per tick, so discrete contact tests cannot miss it. The sim
                // clamps to this anyway - rejecting here means the file says what it means.
                Fix speed = Vec2.Length(
                    new Vec2(vx, vy));

                if (speed.Raw > SimConstants.MaxSpeed.Raw)
                {
                    throw new MachineFormatException(
                        $"{where} starts faster than the tunnelling budget of " +
                        $"{Show(SimConstants.MaxSpeed)} u/s");
                }

                // Spawned airborne, with no surface. The file does not say which surface
                // the ball starts on, and inventing one would be a guess that the first
                // tick can make properly: it falls, meets a shelf, and pins itself.
                balls[i] = new Ball(
                    i,
                    pos,
                    new Vec2(vx, vy),
                    radius,
                    mass,
                    Ball.Airborne,
                    false);
            }
        }

        // ------------------------------------------------------------------
        // Fixed

        static void ParseFixed(
            JsonValue root,
            Surface[] surfaces,
            string[] surfaceNames,
            List<string> takenIds,
            out Domino[] dominoes,
            out string[] names,
            out LeanHint[] leans)
        {
            JsonValue arr = Array(
                root,
                "fixed",
                "machine");

            dominoes = new Domino[arr.Count];
            names = new string[arr.Count];
            leans = new LeanHint[arr.Count];

            // Parallel to dominoes: which surface each one stands on, for the overlap
            // check below. Not stored on the def - the domino's base y already says it.
            var onSurface = new int[arr.Count];

            for (int i = 0; i < arr.Count; i++)
            {
                string where = $"fixed[{i}]";
                JsonValue f = arr[i];

                if (f.Kind != JsonKind.Object)
                {
                    throw new MachineFormatException(
                        $"{where}: expected an object, found {f.Kind}");
                }

                RequireOnly(
                    f,
                    where,
                    "type",
                    "id",
                    "surface",
                    "x",
                    "height",
                    "thickness",
                    "mass",
                    "lean");

                string type = Text(f, "type", where);

                if (!string.Equals(
                    type,
                    "domino",
                    StringComparison.Ordinal))
                {
                    throw new MachineFormatException(
                        $"{where}.type is \"{type}\" - Phase 0 only places dominoes");
                }

                names[i] = TakeId(
                    f,
                    where,
                    takenIds);

                // Surfaces are resolved by a linear scan in file order, so the index a
                // name maps to is the same on every platform and in every run.
                string surfaceName = Text(
                    f,
                    "surface",
                    where);

                int si = -1;

                for (int k = 0; k < surfaceNames.Length; k++)
                {
                    if (string.Equals(
                        surfaceNames[k],
                        surfaceName,
                        StringComparison.Ordinal))
                    {
                        si = k;
                        break;
                    }
                }

                if (si < 0)
                {
                    throw new MachineFormatException(
                        $"{where}.surface is \"{surfaceName}\", which is not a surface in this machine");
                }

                Surface surf = surfaces[si];

                if (!surf.IsHorizontal)
                {
                    throw new MachineFormatException(
                        $"{where} stands on \"{surfaceName}\", which is vertical - " +
                        "a domino needs a floor");
                }

                onSurface[i] = si;

                Fix x = Decimal(f, "x", where);
                Fix height = Decimal(f, "height", where);
                Fix thickness = Decimal(f, "thickness", where);
                Fix mass = Decimal(f, "mass", where);

                if (height.Raw <= 0)
                {
                    throw new MachineFormatException(
                        $"{where}.height must be positive");
                }

                if (thickness.Raw <= 0)
                {
                    throw new MachineFormatException(
                        $"{where}.thickness must be positive");
                }

                if (mass.Raw <= 0)
                {
                    throw new MachineFormatException(
                        $"{where}.mass must be positive");
                }

                // A domino thicker than it is tall cannot topple: theta_crit reaches past
                // 45 degrees and the centre of mass never leaves the base under gravity.
                if (thickness.Raw >= height.Raw)
                {
                    throw new MachineFormatException(
                        $"{where} is at least as thick as it is tall, so it can never topple");
                }

                // The whole footprint has to be on the shelf, not just the centre line.
                Fix half = Fix.Div(
                    thickness,
                    Fix.Two);

                if ((x - half).Raw < surf.MinX.Raw ||
                    (x + half).Raw > surf.MaxX.Raw)
                {
                    throw new MachineFormatException(
                        $"{where} at x = {Show(x)} hangs off the end of \"{surfaceName}\"");
                }

                leans[i] = ParseLean(
                    f,
                    where);

                // No y in the file. The base sits on the named surface, which is the
                // point of naming it: move the shelf and the dominoes move with it.
                dominoes[i] = new Domino(
                    i,
                    new Vec2(x, surf.A.Y),
                    Fix.Zero,
                    Fix.Zero,
                    height,
                    thickness,
                    mass,
                    DominoState.Standing);
            }

            // Two dominoes in the same place is always a mistake, and the sim would
            // resolve it as a permanent mutual contact rather than crash.
            for (int i = 0; i < dominoes.Length; i++)
            {
                for (int j = i + 1; j < dominoes.Length; j++)
                {
                    if (onSurface[i] != onSurface[j])
                        continue;

                    Fix gap = Fix.Abs(
                        dominoes[i].Base.X -
                        dominoes[j].Base.X);

                    Fix touching =
                        dominoes[i].HalfThickness +
                        dominoes[j].HalfThickness;

                    if (gap.Raw < touching.Raw)
                    {
                        throw new MachineFormatException(
                            $"fixed[{i}] \"{names[i]}\" and fixed[{j}] \"{names[j]}\" overlap");
                    }
                }
            }
        }

        static LeanHint ParseLean(
            JsonValue f,
            string where)
        {
            if (!f.TryGet("lean", out JsonValue v) || v.IsNull)
                return LeanHint.None;

            string lean = v.Kind == JsonKind.String
                ? v.Text
                : throw new MachineFormatException(
                    $"{where}.lean: expected a string, found {v.Kind}");

            if (string.Equals(
                lean,
                "left",
                StringComparison.Ordinal))
            {
                return LeanHint.Left;
            }

            if (string.Equals(
                lean,
                "right",
                StringComparison.Ordinal))
            {
                return LeanHint.Right;
            }

            throw new MachineFormatException(
                $"{where}.lean is \"{lean}\" - it must be \"left\" or \"right\"");
        }

        // ------------------------------------------------------------------
        // Sim

        static int ParseSim(JsonValue root)
        {
            JsonValue sim = Object(
                root,
                "sim",
                "machine");

            RequireOnly(
                sim,
                "sim",
                "ticks");

            int ticks = Integer(
                sim,
                "ticks",
                "sim");

            if (ticks <= 0)
            {
                throw new MachineFormatException(
                    "sim.ticks must be at least 1");
            }

            if (ticks > MaxTicks)
            {
                throw new MachineFormatException(
                    $"sim.ticks is {ticks}, over the {MaxTicks}-tick (10 minute) ceiling");
            }

            return ticks;
        }

        // ------------------------------------------------------------------
        // Helpers

        /// <summary>
        /// Reads the file as UTF-8 explicitly rather than letting the platform decide,
        /// and drops a byte-order mark if an editor left one. Two machines that differ
        /// only in their BOM must load identically.
        /// </summary>
        static string ReadUtf8(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string text = new UTF8Encoding(false).GetString(bytes);

            return text.Length > 0 && text[0] == '\uFEFF'
                ? text.Substring(1)
                : text;
        }

        /// <summary>
        /// An id that no other body has taken. Unique across spawns and fixed together:
        /// the reporter prints them side by side, and two "b0"s would be unreadable.
        /// </summary>
        static string TakeId(
            JsonValue o,
            string where,
            List<string> taken)
        {
            string id = Text(
                o,
                "id",
                where);

            if (id.Length == 0)
                throw new MachineFormatException($"{where}.id is empty");

            for (int i = 0; i < taken.Count; i++)
            {
                if (string.Equals(
                    taken[i],
                    id,
                    StringComparison.Ordinal))
                {
                    throw new MachineFormatException(
                        $"{where}.id \"{id}\" is already used by another body");
                }
            }

            taken.Add(id);

            return id;
        }

        /// <summary>
        /// Rejects fields this loader does not know. A typo that defaulted to zero would
        /// change the physics and look like a working file.
        /// </summary>
        static void RequireOnly(
            JsonValue o,
            string where,
            params string[] allowed)
        {
            string[] names = o.Names;

            for (int i = 0; i < names.Length; i++)
            {
                bool known = false;

                for (int k = 0; k < allowed.Length; k++)
                {
                    if (string.Equals(
                        names[i],
                        allowed[k],
                        StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    throw new MachineFormatException(
                        $"{where}: unknown field \"{names[i]}\"");
                }
            }
        }

        static void RequireEmptyArray(
            JsonValue root,
            string name,
            string where,
            string why)
        {
            JsonValue v = Array(
                root,
                name,
                where);

            if (v.Count != 0)
            {
                throw new MachineFormatException(
                    $"{where}.{name} must be empty: {why}");
            }
        }

        static void RequireEmptyObject(
            JsonValue root,
            string name,
            string where,
            string why)
        {
            JsonValue v = Object(
                root,
                name,
                where);

            if (v.Count != 0)
            {
                throw new MachineFormatException(
                    $"{where}.{name} must be empty: {why}");
            }
        }

        static void RequireNull(
            JsonValue root,
            string name,
            string where,
            string why)
        {
            JsonValue v = root.Get(
                name,
                where);

            if (!v.IsNull)
            {
                throw new MachineFormatException(
                    $"{where}.{name} must be null: {why}");
            }
        }

        // Typed accessors. Each one names the full field path in its error, so a bad
        // machine tells you where to look instead of just that it was bad.

        static JsonValue Object(
            JsonValue o,
            string name,
            string where)
            => Typed(
                o,
                name,
                where,
                JsonKind.Object,
                "an object");

        static JsonValue Array(
            JsonValue o,
            string name,
            string where)
            => Typed(
                o,
                name,
                where,
                JsonKind.Array,
                "an array");

        static string Text(
            JsonValue o,
            string name,
            string where)
            => Typed(
                o,
                name,
                where,
                JsonKind.String,
                "a string").Text;

        static Fix Decimal(
            JsonValue o,
            string name,
            string where)
            => FixParse.Decimal(
                Typed(
                    o,
                    name,
                    where,
                    JsonKind.Number,
                    "a number").RawNumber,
                $"{where}.{name}");

        static int Integer(
            JsonValue o,
            string name,
            string where)
            => FixParse.Integer(
                Typed(
                    o,
                    name,
                    where,
                    JsonKind.Number,
                    "a number").RawNumber,
                $"{where}.{name}");

        static JsonValue Typed(
            JsonValue o,
            string name,
            string where,
            JsonKind kind,
            string described)
        {
            JsonValue v = o.Get(
                name,
                where);

            if (v.Kind != kind)
            {
                throw new MachineFormatException(
                    $"{where}.{name}: expected {described}, found {v.Kind}");
            }

            return v;
        }

        /// <summary>
        /// A Fix in an error message, to three decimals. Integer maths, so it does not
        /// undo the whole point of FixParse just to print something.
        /// </summary>
        static string Show(Fix v)
        {
            long raw = v.Raw;
            bool negative = raw < 0;

            if (negative)
                raw = -raw;

            long whole = raw >> 32;
            long milli = ((raw & 0xFFFFFFFFL) * 1000) >> 32;

            return (negative ? "-" : "") +
                   whole.ToString() +
                   "." +
                   milli.ToString("D3");
        }
    }
}