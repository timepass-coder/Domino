# Write the units down
 
No code. One file. Twenty minutes.
 
This looks like busywork and isn't: nearly every bug in a sim like this traces back to two files disagreeing about what 1 means. Writing it down once, in a place you can point at, is the cheapest bug prevention in the project.
 
Create sim\UNITS.md
 
notepad sim\UNITS.md
 
Paste this in and save:
 
# Units & constants
 
1 unit  = one standard domino height. Not pixels, not metres, not Unity units.
x grows right, y grows up.
 
## Authoritative constants
 
domino height  h = 1.0
domino thickness t = 0.18
ball radius    r = 0.35
mass             = 1.0 for everything in Phase 0
gravity        g = -20.0 units/s²   (game-feel, deliberately NOT 9.81)
dt               = 1/120 s exactly
playfield        = portrait, ~9 units wide x 16 tall
 
## Derived - know these before debugging anything
 
theta_crit = atan(t/h)                      = 10.20 deg   domino balance point
d_com      = sqrt(h^2+t^2)/2                = 0.5080      pivot -> centre of mass
I_pivot    = m(h^2+t^2)/3                   = 0.3441 m    inertia about the base edge
E_tip      = |g|(*d_com*-h/2) = 0.16071      energy to put one domino over
R_geom     = t + h                          = 1.18        furthest the leading corner reaches
omega_min  = sqrt(2*E_tip/I_pivot)          = 0.9664 rad/s min spin to topple (mass independent)
 
## R (effective reach) = MEASURED AT STEP 10, NEVER ASSUMED
 
R_geom is pure geometry. Effective R - the furthest spacing where a chain actually
keeps going - is smaller, because a near-horizontal leading corner strikes with almost
no moment arm. R is the single most important number in this game. Expect 0.85-1.10.
 
## Tunnelling budget - an invariant, assert it in code
 
Discrete collision tests are only safe while:
    max_speed * dt < min_feature_thickness
    max_speed < 0.18 * 120 = 21.6 units/s
 
Clamp speed to this in the sim and fail loudly if exceeded. When Phase 3 adds a spring
that breaks it, you want a test failure, not a ball through a wall.
 
The three numbers actually worth understanding
 
theta_crit = 10.20° — a domino isn't a stick, it's a box with thickness. Tip it less than 10.2° and its weight is still over its base, so it falls back upright. Past 10.2° and it's going over. You get "a nudge that wasn't hard enough bounces back" free from the geometry, with no special-case code. That's the whole reason we model the domino as a box and not a line.
 
E_tip = 0.1603 — the energy needed to put one domino over. This becomes a number you literally draw on screen in Phase 2 ("needs 30"), because GAME.md says energy has no silhouette and must be made visible. It starts here.
 
R, deliberately left blank — this is the number your entire game design rests on. GAME.md currently asserts "a domino reaches roughly its own height", and the whole gap-bridging formula G ≤ (n+1)·R depends on it. At step 10 you'll measure it with a binary-search test rather than trusting that sentence. If the measurement disagrees with the design doc, the measurement wins and we update the doc.
 
Why gravity is −20, not −9.81: the game reads better with a snappier fall, and since everything is in "domino heights" rather than metres, real gravity has no claim here. Pick it once, write it down, don't tune it casually — it's baked into E_tip and every threshold after it.