"""Where a rock pile slot actually puts a stone.

Every slot in the shipped layout config is a pose for one copy of the vanilla ``item/stone``
mesh. The block entity renders that mesh once per stone it holds, so the pile is proportional by
construction: one stone in hand is one slot filled is one rock you can see.

Two facts make the conversion exact rather than eyeballed.

1. ``survival/shapes/item/stone.json`` is a **single cube**, ``from [5.5, 0, 6]`` ``to
   [10.5, 2, 10]``, rotation origin ``[8, 0, 8]``. Horizontally it is centred on the block; its
   bottom sits at y = 0. We patch an identity ``groundStorageTransform`` onto the stone item, so
   ``BlockEntityDisplay`` hands that mesh to us untouched.

2. ``BlockEntityRockPile.genTransformationMatrices`` applies
   ``Translate(x, y, z) . RotY(yaw) . RotX(pitch) . RotZ(roll) . Translate(-0.5, 0, -0.5)``.
   That trailing translate puts the stone's own bottom-centre on the origin before the rotation,
   so a slot's ``(x, y, z)`` *is* where the stone's bottom-centre lands, and its angles are the
   stone's own angles. No baked-in transform to cancel, unlike Liber Terra's books.

Every cube in vanilla's ``item/stone-pile.json`` also rotates about its own bottom-centre, which
is the same pivot. So the heap layout is vanilla's arrangement read straight off the shape —
re-driven at one stone per rock instead of vanilla's two.
"""

from __future__ import annotations

import argparse
import json
import math
import random
from pathlib import Path

# --- the stone mesh, in block units -------------------------------------------------------------

PX = 1.0 / 16.0
STONE_DIMS_PX = (5.0, 2.0, 4.0)  # the item/stone cube, x by y by z
STONE_LENGTH = STONE_DIMS_PX[0] * PX
STONE_HEIGHT = STONE_DIMS_PX[1] * PX
STONE_DEPTH = STONE_DIMS_PX[2] * PX

# Eight 2px layers fill a block exactly.
LAYERS = 8

# Coursed layouts leave a joint between neighbouring stones instead of laying them face to face.
# Two stones that touch share a face exactly, and a course of them reads as one milled slab with
# lines scored on it; a hair of daylight is what makes the eye count stones again. The joint is
# not a constant but whatever is left over once a row takes as many stones as it can hold without
# touching — which is why these layouts hold fewer stones than their axes would otherwise take.
# Four 4px stones tile a 16px axis exactly, so the row lays three and spends the fourth stone's
# width on joints. A stone fewer, a pile you can read.

# The loose-pile density vanilla itself uses: its 32-cube heap tops out at y = 14.4px. Heap and
# neat both hold this, so a pile you just tip on the ground behaves like vanilla's.
HEAP_CAPACITY = 32

# The inventory size, and so the ceiling on any layout. Masonry is the layout that reaches
# highest — 9 jointed stones a course, 8 courses — but the ceiling stays where it was, above it:
# piles saved when masonry tiled its cube hold up to 96 stones, and they have to load before the
# block entity can hand the surplus back.
MAX_SLOTS = 96

# Layouts that fill their block solidly enough to stand on. These must never overhang, and so
# never rotate off the axis: a coursed cube turned 45 degrees puts its corners through the wall of
# the block next door, which is exactly the solidity it just promised not to violate.
SOLID_LAYOUTS = frozenset({"masonry"})

# A pile turns in 45 degree steps.
ORIENTATION_STEPS = 8


# Cairn segments get narrower the higher up the column they sit. Beyond this index they stay at
# the narrowest profile, so a very tall cairn keeps a spire rather than pinching to nothing.
CAIRN_SEGMENTS = 3

# Layouts built to stack into something taller than one block. Each has to reach the ceiling, or
# the segment above it hangs in mid-air — the fault that made stacked cairns look wrong. Steps is
# not here on purpose: a flight is meant to be short at the front, and a loaded one swaps onto the
# masonry slots instead, which do fill the block.
STACKING_LAYOUTS = frozenset({"wall", "masonry"}) | {
    f"cairn{i}" for i in range(CAIRN_SEGMENTS)
}

# Vintage Story composes a shape element's own rotation as Rx . Ry . Rz about its rotationOrigin.
ELEMENT_ROTATION_ORDER = "XYZ"

# Fixed so the generated config is reproducible and reviewable in a diff.
SEED = 0x10CC


# --- small matrix helpers -----------------------------------------------------------------------


def rot_x(deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return ((1, 0, 0), (0, c, -s), (0, s, c))


def rot_y(deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return ((c, 0, s), (0, 1, 0), (-s, 0, c))


def rot_z(deg):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return ((c, -s, 0), (s, c, 0), (0, 0, 1))


def mul(a, b):
    return tuple(tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)) for i in range(3))


def apply(m, v):
    return tuple(sum(m[i][k] * v[k] for k in range(3)) for i in range(3))


def to_yxz(m):
    """Decompose a rotation matrix into the (yaw, pitch, roll) our Ry . Rx . Rz chain replays.

    For ``M = Ry(a) . Rx(b) . Rz(c)``: ``M[1][2] = -sin b``, ``M[0][2] / M[2][2] = tan a``, and
    ``M[1][0] / M[1][1] = tan c``. When ``cos b`` collapses, yaw and roll fold into one angle and
    we hand the whole rotation to yaw.
    """
    pitch = math.asin(max(-1.0, min(1.0, -m[1][2])))
    if abs(math.cos(pitch)) < 1e-6:
        yaw = math.atan2(-m[2][0], m[0][0])
        roll = 0.0
    else:
        yaw = math.atan2(m[0][2], m[2][2])
        roll = math.atan2(m[1][0], m[1][1])
    return tuple(round(math.degrees(v), 3) for v in (yaw, pitch, roll))


def axis_aligned_rotations():
    """The 24 rotations that map a box onto itself, as matrices."""
    seen, out = set(), []
    for x in (0, 90, 180, 270):
        for y in (0, 90, 180, 270):
            for z in (0, 90, 180, 270):
                m = mul(rot_x(x), mul(rot_y(y), rot_z(z)))
                key = tuple(round(v) for row in m for v in row)
                if key not in seen:
                    seen.add(key)
                    out.append(m)
    return out


AXIS_ROTATIONS = axis_aligned_rotations()


def permutation_for(dims_px):
    """Rotation that turns the standard stone box into a box with these authored dimensions.

    Five of vanilla's 32 heap cubes are drawn as 5x4x2 or 4x5x2 or 4x2x5 boxes — the artist
    re-authored a tipped stone rather than rotating the standard one. Our mesh is always the
    standard 5x2x4 box, so we recover the rotation those dimensions imply and fold it in ahead of
    the cube's own authored rotation.
    """
    target = tuple(round(v, 3) for v in dims_px)
    for m in AXIS_ROTATIONS:
        rotated = tuple(round(abs(v), 3) for v in apply(m, STONE_DIMS_PX))
        if rotated == target:
            return m
    raise ValueError(f"no axis-aligned rotation maps {STONE_DIMS_PX} onto {target}")


# --- the heap: vanilla's own arrangement ---------------------------------------------------------


def load_heap(game_path: Path):
    """Convert survival/shapes/item/stone-pile.json into slot poses."""
    shape = json.loads((game_path / "assets/survival/shapes/item/stone-pile.json").read_text())
    elements = shape["elements"]

    slots = []
    for element in elements:
        frm, to = element["from"], element["to"]
        dims = tuple(to[i] - frm[i] for i in range(3))

        authored = mul(
            rot_x(element.get("rotationX") or 0.0),
            mul(rot_y(element.get("rotationY") or 0.0), rot_z(element.get("rotationZ") or 0.0)),
        )
        total = mul(authored, permutation_for(dims))
        yaw, pitch, roll = to_yxz(total)

        # Vanilla puts every rotationOrigin on the cube's own bottom-centre, which is our pivot,
        # so the origin is the slot position outright.
        origin = element.get("rotationOrigin") or [
            (frm[0] + to[0]) / 2,
            frm[1],
            (frm[2] + to[2]) / 2,
        ]
        slots.append(slot(origin[0] * PX, origin[1] * PX, origin[2] * PX, yaw, pitch, roll))

    # The shape file lists Cube31 before Cube30; sort by height so filling a pile builds it
    # bottom-up rather than jumping a layer and back.
    slots.sort(key=lambda s: (s["y"], s["x"], s["z"]))
    if len(slots) != HEAP_CAPACITY:
        raise ValueError(f"expected {HEAP_CAPACITY} heap slots, vanilla shape gave {len(slots)}")
    return slots


# --- analytic layouts -----------------------------------------------------------------------------


def slot(x, y, z, yaw=0.0, pitch=0.0, roll=0.0, x_bond=None):
    """One stone's pose.

    ``x_bond`` belongs to a **bond course**: one that lays a stone across the joint with the pile
    next door, the way a through stone ties a wall together. Without it every block boundary shows
    an unbroken vertical joint on every course, because each pile is otherwise a self-contained
    brick.

    A course has to know about *both* of its ends, not just one. It carries four positions, picked
    by whether there is a pile behind and a pile ahead — ``[none, ahead, behind, both]``. Reasoning
    about the -X neighbour alone left the far end of a run notched open while the near end sat
    flush, so the same wall looked different from each end and flipped when you turned it round.
    """
    record = {
        "x": round(x, 5),
        "y": round(y, 5),
        "z": round(z, 5),
        "yawDeg": round(yaw, 2),
        "pitchDeg": round(pitch, 2),
        "rollDeg": round(roll, 2),
    }
    if x_bond is not None:
        record["xBond"] = [round(v, 5) for v in x_bond]
    return record


def coursed_positions(extent, span=1.0):
    """Centres for a row of identical stones laid along one axis with an even joint between them.

    Half a joint is left at each end too, so the joint carries straight across the block boundary:
    two piles set side by side course together rather than showing a doubled seam every 16 pixels,
    and neither one overhangs its own block.

    The row takes the most stones that still leave daylight between them, and the width they give
    up becomes the joints.
    """
    count = max(1, int((span - 1e-9) // extent))
    return course_positions([extent] * count, False, False)


def course_joint(extents, behind, ahead):
    """The joint this course leaves between one stone and the next.

    Every gap is the same width — the ones inside the course, the half-widths at each face, and
    the one left for the pile ahead — so the whole run of piles is mortared to a single joint. The
    ends are what vary: a pile behind takes the first stone out onto the boundary and so needs no
    leading joint, while a pile ahead reaches its own bond stone back over our far face and needs
    a whole one.
    """
    lead = 0.0 if behind else 0.5
    trail = 1.0 if ahead else 0.5
    reach = (1.0 - extents[0] / 2 if ahead else 1.0) - (-extents[0] / 2 if behind else 0.0)
    return (reach - sum(extents)) / (len(extents) - 1 + lead + trail)


def course_positions(extents, behind, ahead):
    """Where a course's stones sit, given what it has to tie into at each end.

    Left alone the course is a jointed row: half a joint in from each face. A pile behind takes the
    first stone out onto the boundary itself and the rest follow at the same pitch, which lands the
    course half a stone off the one below — a running bond. A pile ahead brings its own bond stone
    back over our far face, so the row stops one joint short of where that one begins.
    """
    joint = course_joint(extents, behind, ahead)
    cursor = -extents[0] / 2 if behind else joint / 2

    centres = []
    for extent in extents:
        centres.append(cursor + extent / 2)
        cursor += extent + joint
    return centres


def bond_course(extents, index):
    """The four X positions for stone ``index`` of a bond course, in [none, ahead, behind, both]."""
    return [
        course_positions(extents, behind, ahead)[index]
        for behind in (False, True)
        for ahead in (False, True)
    ]


def build_neat(rng):
    """Four stones a layer on the block quarters, courses crossed like stacked timber."""
    quarters = [(0.3, 0.3), (0.7, 0.3), (0.3, 0.7), (0.7, 0.7)]
    slots = []
    for layer in range(HEAP_CAPACITY // 4):
        # Alternate the course by 90 degrees so the stones bind instead of forming four columns.
        base_yaw = 0.0 if layer % 2 == 0 else 90.0
        for x, z in quarters:
            slots.append(
                slot(x, layer * STONE_HEIGHT, z, base_yaw + rng.uniform(-3.0, 3.0))
            )
    return slots


# Cairn profiles are written as stones-per-layer, not radii, because a closed ring of N stones has
# exactly one radius: r = N * STONE_LENGTH / tau. Choosing the count therefore chooses the radius,
# every ring closes by construction, and the taper is legible right here in the numbers.
#
# The counts fall as the column rises, which is the whole point — an earlier version averaged four
# stones a layer throughout and came out a cylinder. A broad base costs stones: six to a ring at
# r = 0.30 against two at r = 0.10.
CAIRN_PROFILES = [
    [6, 6, 5, 5, 5, 5, 4, 4],  # 40 - the footing, widest course on the ground
    [4, 4, 4, 4, 3, 3, 3, 3],  # 28 - the body
    [3, 3, 3, 2, 2, 2, 2, 2],  # 19 - the shoulder, and every course above it
]


def ring_radius(count):
    """The radius at which ``count`` stones lie tangentially end to end, closing the ring."""
    return count * STONE_LENGTH / math.tau


def cairn_rings(segment):
    """(count, radius) per layer for one cairn segment, narrowing as the column rises."""
    counts = CAIRN_PROFILES[min(segment, len(CAIRN_PROFILES) - 1)]
    return [(count, ring_radius(count)) for count in counts]


def build_cairn(rng, segment):
    """Rings of stones laid tangentially and tipped inward, so the segment reads as a cone."""
    slots = []
    phase = rng.uniform(0, math.tau)
    widest = ring_radius(max(CAIRN_PROFILES[0]))

    for layer, (count, radius) in enumerate(cairn_rings(segment)):
        # Advance the phase half a stone every layer so each ring bridges the joints of the one
        # below rather than lining them up into a vertical seam.
        if layer > 0:
            phase += math.pi / count
        for i in range(count):
            theta = phase + math.tau * i / count
            slots.append(
                slot(
                    0.5 + radius * math.cos(theta),
                    layer * STONE_HEIGHT,
                    0.5 + radius * math.sin(theta),
                    # The stone's long axis is X, so a yaw of -theta lays it along the ring.
                    -math.degrees(theta) + rng.uniform(-8.0, 8.0),
                    rng.uniform(-4.0, 4.0),
                    # Tip the outer face down so the cone sheds rather than looking like a stack
                    # of hoops. Narrow rings sit flatter, in proportion to the widest course.
                    -7.0 * (radius / widest) + rng.uniform(-3.0, 3.0),
                )
            )
    return slots


def build_wall(rng):
    """Two leaves of stone running along X, joints staggered course to course.

    Uses the block's full eight courses, which is what lets walls stack: a wall that stopped short
    would leave the segment above it hanging in the air, the way the cairn once did. Stack as many
    as you like and the courses run straight through the joins.

    The block entity yaws the whole set by the orientation stored on the pile, so this only ever
    has to describe the wall running west to east.
    """
    per_row, rows = 4, 2
    spacing = 1.0 / per_row
    z_rows = [0.5 - 0.13, 0.5 + 0.13]

    slots = []
    for layer in range(LAYERS):
        # Four to a course at a quarter-block spacing: the stones are longer than the gap between
        # them, so a course is continuous rather than three stones with slivers of daylight
        # between, which is what the old three-per-course spacing left.
        # Alternate courses tie into the piles either side, putting a stone across each joint.
        # That is what stops a run of walls reading as separate blocks stood in a line.
        bonded_course = layer % 2 == 1
        for row in range(rows):
            for i in range(per_row):
                x_bond = bond_course([STONE_LENGTH] * per_row, i) if bonded_course else None
                slots.append(
                    slot(
                        x_bond[3] if x_bond else (i + 0.5) * spacing,
                        layer * STONE_HEIGHT,
                        z_rows[row] + rng.uniform(-0.015, 0.015),
                        rng.uniform(-5.0, 5.0),
                        rng.uniform(-3.0, 3.0),
                        rng.uniform(-4.0, 4.0),
                        x_bond=x_bond,
                    )
                )
    return slots


# A masonry course is three stones across: two laid lengthwise and one turned onto its 4px side.
# Which end the turned stone sits at swaps every course, and that is what breaks the joints — two
# courses of identical stones, jointed and symmetric, can only put their joints in the same place.
MASONRY_COURSES = (
    (STONE_LENGTH, STONE_LENGTH, STONE_DEPTH),
    (STONE_DEPTH, STONE_LENGTH, STONE_LENGTH),
)


def build_masonry(rng):
    """A whole cube of coursed stone — the layout that gives you a solid block back.

    Nine stones to a course, three across by three deep, every one of them laid with a joint
    between it and the next. That joint is the whole difference from the twelve-a-course version
    this replaced: twelve stones tiled the cube exactly, face to face, and the pile came out as a
    smooth block with a grid scored on it rather than as stones someone had laid. Three courses of
    stone short is what the joint costs, and it is worth it.

    The joint is the same width everywhere — inside the course, at each face, and across the seam
    into the pile next door — so a run of masonry piles reads as one mortared wall.

    Joints still have to break course to course or the pile reads as columns. Alternating the
    turned stone between the two ends of the course is what does it: the joints of one course land
    a full pixel clear of the joints of the one below. The lengthwise stone leading each bond
    course is also the one that carries across the seam, because a through stone crosses the joint
    on its long face.

    Courses sit straight on top of one another, with no bed joint. A gap there would leave every
    stone floating a hair above the one below, and would stop the top course finishing flush with
    the ceiling — which is what lets masonry piles stack into a wall.
    """
    # No jitter anywhere in here. Every other layout gets a degree or two of slop to look laid by
    # hand, but this one has to stay inside its own cube to be allowed to call itself solid, and a
    # dressed, coursed wall is square in any case.
    slots = []
    for layer in range(LAYERS):
        extents = MASONRY_COURSES[layer % 2]
        # Bond the course that leads with a stretcher; the other one breaks the joints instead.
        bonded = layer % 2 == 0
        centres = course_positions(extents, False, False)

        for col, extent in enumerate(extents):
            turned = extent == STONE_DEPTH
            x_bond = None if not bonded else bond_course(extents, col)
            for z in coursed_positions(STONE_LENGTH if turned else STONE_DEPTH):
                slots.append(
                    slot(
                        x_bond[3] if x_bond else centres[col],
                        layer * STONE_HEIGHT,
                        z,
                        90.0 if turned else 0.0,
                        x_bond=x_bond,
                    )
                )
    return slots


def build_ring(rng):
    """A hearth ring: three courses of stones round a hollow centre."""
    count = 6
    radius = ring_radius(count)
    slots = []
    phase = rng.uniform(0, math.tau)
    for layer in range(3):
        if layer > 0:
            phase += math.pi / count
        for i in range(count):
            theta = phase + math.tau * i / count
            slots.append(
                slot(
                    0.5 + radius * math.cos(theta),
                    layer * STONE_HEIGHT,
                    0.5 + radius * math.sin(theta),
                    -math.degrees(theta) + rng.uniform(-5.0, 5.0),
                    rng.uniform(-3.0, 3.0),
                    rng.uniform(-3.0, 3.0),
                )
            )
    return slots


def build_spiral(rng):
    """A twisted column: every course turns a little further than the one below.

    Each ring still closes, so nothing is cantilevered — the stones simply do not sit directly on
    their neighbours below, which is what draws the helical seam up the side.
    """
    count = 4
    radius = ring_radius(count)
    slots = []
    for layer in range(LAYERS):
        # A steady 15 degrees a course: about a quarter turn over the block, enough to read as a
        # spiral without any stone losing the one beneath it.
        phase = math.radians(15.0 * layer)
        for i in range(count):
            theta = phase + math.tau * i / count
            slots.append(
                slot(
                    0.5 + radius * math.cos(theta),
                    layer * STONE_HEIGHT,
                    0.5 + radius * math.sin(theta),
                    -math.degrees(theta) + rng.uniform(-3.0, 3.0),
                    rng.uniform(-2.0, 2.0),
                    rng.uniform(-2.0, 2.0),
                )
            )
    return slots


def build_steps(rng):
    """A flight of three steps, the top one finishing flush with the block's ceiling.

    Solid all the way down — every stone rests on stone, not on air — so it works as a mounting
    block or a stile beside a wall rather than being purely decorative.

    Treads are a jointed row like the masonry courses, and that is what takes the flight from four
    steps to three: four 4px treads tile the block exactly and would have to touch, which made the
    flight read as a single ramp of stone. Three leave a joint at every nosing, and the rises share
    the block's eight courses between them so the climb still arrives at the top.

    A stair taller than one block is built the way a real one is: this flight goes on top, and the
    pile underneath fills in solid to carry it. The block entity swaps a steps pile onto the
    masonry slots as soon as something is stacked above it, so the climb continues instead of
    restarting at the bottom of every block.
    """
    treads = coursed_positions(STONE_DEPTH)
    across = coursed_positions(STONE_LENGTH)
    slots = []
    for step, z in enumerate(treads):
        # Round up, so the top tread reaches the ceiling rather than stopping a course short.
        height = math.ceil(LAYERS * (step + 1) / len(treads))
        for layer in range(height):
            for x in across:
                slots.append(
                    slot(
                        x,
                        layer * STONE_HEIGHT,
                        z,
                        rng.uniform(-2.0, 2.0),
                    )
                )
    return slots


def build_balanced(rng):
    """The trail-marker look: a few flat stones stacked centrally, each turned off the last.

    Eight of them, so the stack spends the block's full height like everything else rather than
    stopping a course short.
    """
    slots = []
    for layer in range(LAYERS):
        slots.append(
            slot(
                0.5 + rng.uniform(-0.035, 0.035),
                layer * STONE_HEIGHT,
                0.5 + rng.uniform(-0.035, 0.035),
                layer * 37.0 + rng.uniform(-6.0, 6.0),
                rng.uniform(-3.5, 3.5),
                rng.uniform(-3.5, 3.5),
            )
        )
    return slots


def build_twin_columns(rng):
    """Two slender columns with a gap between them — gateposts, or the start of a doorway.

    One stone a course, turned a quarter every layer so the column binds to itself instead of
    being a single long domino waiting to fall over.
    """
    slots = []
    for layer in range(LAYERS):
        for x in (0.24, 0.76):
            slots.append(
                slot(
                    x,
                    layer * STONE_HEIGHT,
                    0.5,
                    (90.0 if layer % 2 else 0.0) + rng.uniform(-4.0, 4.0),
                    rng.uniform(-2.0, 2.0),
                    rng.uniform(-2.0, 2.0),
                )
            )
    return slots


# The arrow, in block units: where its point sits, how far back the barbs sweep and how wide they
# open, then the centres of the shaft stones running back from the point.
ARROW_TIP_Z = 0.90
ARROW_BARB_Z = 0.30
ARROW_BARB_X = 0.34
ARROW_SHAFT_Z = (0.13, 0.36, 0.59)
# How far down each barb its two stones sit, as a fraction of the barb's length. The near pair is
# well forward of the quarter point so the two barbs overlap across the centre line: a stone is
# almost square, so barbs that merely meet leave a notch where the point should be.
ARROW_BARB_T = (0.15, 0.60)
ARROW_LAYERS = 4


def build_arrow(rng):
    """A waypoint arrow: two barbs meeting at a point, with a shaft running back from it.

    Pointing is the whole job, and the pile already knows how to point — the turn entry swings it
    45 degrees a click, so all eight headings are reachable and the layout itself only has to
    agree on one: the arrow is built facing +Z and turned from there.

    Four courses rather than the usual eight. An arrow is read from above, and a full-height one
    is a wedge of stone you mostly see edge-on. Half a block also keeps it below eye level, which
    is where a marker beside a path belongs.

    Nothing here leaves the block: the barb tails stop short of the side walls and the point stops
    short of the front one, so a row of arrows down a trail keeps its spacing.
    """
    slots = []
    for layer in range(ARROW_LAYERS):
        y = layer * STONE_HEIGHT

        for side in (-1, 1):
            dx = side * ARROW_BARB_X
            dz = ARROW_BARB_Z - ARROW_TIP_Z
            # A stone's long axis is X, and our Ry sends it to planar angle -yaw, so a barb's
            # stones line up with it by taking the negative of the barb's own bearing.
            yaw = -math.degrees(math.atan2(dz, dx))
            for t in ARROW_BARB_T:
                slots.append(
                    slot(
                        0.5 + dx * t,
                        y,
                        ARROW_TIP_Z + dz * t,
                        yaw + rng.uniform(-3.0, 3.0),
                        rng.uniform(-2.0, 2.0),
                        rng.uniform(-2.0, 2.0),
                    )
                )

        # The shaft, laid nose to tail down the centre line and overlapping the barb crossing, so
        # there is no daylight between the point and the tail.
        for z in ARROW_SHAFT_Z:
            slots.append(
                slot(
                    0.5,
                    y,
                    z,
                    -90.0 + rng.uniform(-3.0, 3.0),
                    rng.uniform(-2.0, 2.0),
                    rng.uniform(-2.0, 2.0),
                )
            )

    return slots


def build_layouts(game_path: Path):
    rng = random.Random(SEED)
    layouts = {
        "heap": load_heap(game_path),
        "neat": build_neat(rng),
        "wall": build_wall(rng),
        "masonry": build_masonry(rng),
        "ring": build_ring(rng),
        "spiral": build_spiral(rng),
        "steps": build_steps(rng),
        "balanced": build_balanced(rng),
        "twincolumns": build_twin_columns(rng),
    }
    # Flat keys rather than a nested list: the block entity picks cairn{min(segment, 2)} and the
    # C# config stays one dictionary of named slot arrays.
    for segment in range(CAIRN_SEGMENTS):
        layouts[f"cairn{segment}"] = build_cairn(rng, segment)

    # New layouts draw from the shared rng last, after everything that was here before them.
    # One rng and one seed is what makes the config reproducible, but it also means an insertion
    # anywhere in the middle re-rolls the jitter of every layout built after it — a thousand-line
    # diff for a change that added one arrow. Appending keeps the diff to the layout you added.
    layouts["arrow"] = build_arrow(rng)

    # Piles fill in slot order, so every layout has to be laid out bottom-up or a stone would
    # appear above a gap. Sorting is stable, which keeps each course in the order its builder
    # wrote it.
    for name, slots in layouts.items():
        slots.sort(key=lambda s: s["y"])
        if not 0 < len(slots) <= MAX_SLOTS:
            raise ValueError(f"{name} produced {len(slots)} slots, outside 1..{MAX_SLOTS}")

    return layouts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--game",
        default="/Applications/Vintage Story.app",
        help="Vintage Story install to read the vanilla stone shapes from",
    )
    parser.add_argument(
        "--out",
        default="mod/assets/acervuslapidum/config/rockpile-layout.json",
        help="where to write the generated layout config",
    )
    args = parser.parse_args()

    layouts = build_layouts(Path(args.game))
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(layouts, indent=2) + "\n")
    total = sum(len(v) for v in layouts.values())
    print(f"Wrote {len(layouts)} layouts, {total} slots to {out}")


if __name__ == "__main__":
    main()
