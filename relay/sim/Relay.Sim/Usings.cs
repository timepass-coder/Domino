// one alias, project-wide, Everything in the sim says 'Fix' instead of 'FixMath.Fix64'
// If we ever swap fixed point libraries, this file is the only thing that changes.

global using Fix = FixMath.F64;
global using Vec2 = FixMath.F64Vec2;