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
STONE_HEIGHT = STONE_DIMS_PX[1] * PX

# How many stones one pile block holds. Vanilla's 32-cube heap tops out at y = 14.4px, so 32
# stones fill a block at exactly vanilla's visual density. Stone 33 has to start a new segment,
# which is what turns a full pile into the first course of a cairn.
CAPACITY = 32

# Cairn segments get narrower the higher up the column they sit. Beyond this index they stay at
# the narrowest profile, so a very tall cairn keeps a spire rather than pinching to nothing.
CAIRN_SEGMENTS = 3

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
    if len(slots) != CAPACITY:
        raise ValueError(f"expected {CAPACITY} heap slots, vanilla shape gave {len(slots)}")
    return slots


# --- analytic layouts -----------------------------------------------------------------------------


def slot(x, y, z, yaw=0.0, pitch=0.0, roll=0.0):
    return {
        "x": round(x, 5),
        "y": round(y, 5),
        "z": round(z, 5),
        "yawDeg": round(yaw, 2),
        "pitchDeg": round(pitch, 2),
        "rollDeg": round(roll, 2),
    }


def build_neat(rng):
    """Four stones a layer on the block quarters, courses crossed like stacked timber."""
    quarters = [(0.3, 0.3), (0.7, 0.3), (0.3, 0.7), (0.7, 0.7)]
    slots = []
    for layer in range(CAPACITY // 4):
        # Alternate the course by 90 degrees so the stones bind instead of forming four columns.
        base_yaw = 0.0 if layer % 2 == 0 else 90.0
        for x, z in quarters:
            slots.append(
                slot(x, layer * STONE_HEIGHT, z, base_yaw + rng.uniform(-3.0, 3.0))
            )
    return slots


def cairn_rings(segment):
    """(count, radius) per layer for one cairn segment, narrowing as the column rises.

    Each segment picks up where the one below it left off — 0.30 down to 0.20, then 0.20 down to
    0.13, then 0.13 to the cap — so a column is one cone rather than three stacked drums.

    That is why the higher segments hold fewer stones. A ring of radius r fits about
    ``2*pi*r / 0.3125`` stones, so a narrow course physically cannot take 32 of them, and a block
    only has room for eight 2px layers. Rather than fake it with overlap, a cairn crown simply
    holds less — which is what a real one does, and keeps every stone in the pile a stone you can
    see.
    """
    profiles = [
        ([6, 5, 5, 4, 4, 4, 4], 0.30, 0.20),
        ([4, 4, 3, 3, 3, 3], 0.20, 0.13),
        ([3, 3, 2, 2, 2], 0.13, 0.05),
    ]
    counts, outer, inner = profiles[min(segment, len(profiles) - 1)]
    layers = len(counts)
    return [
        (counts[i], outer + (inner - outer) * (i / max(1, layers - 1)))
        for i in range(layers)
    ]


def build_cairn(rng, segment):
    """Rings of stones laid tangentially and tipped inward, so the segment reads as a cone."""
    slots = []
    for layer, (count, radius) in enumerate(cairn_rings(segment)):
        # Offset each ring so stones bridge the gaps in the ring below rather than stacking into
        # vertical seams.
        phase = rng.uniform(0, math.tau) if layer == 0 else math.pi / max(1, count)
        for i in range(count):
            theta = phase + math.tau * i / count
            x = 0.5 + radius * math.cos(theta)
            z = 0.5 + radius * math.sin(theta)
            # The stone's long axis is X, so a yaw of -theta lays it along the ring.
            yaw = -math.degrees(theta) + rng.uniform(-8.0, 8.0)
            slots.append(
                slot(
                    x,
                    layer * STONE_HEIGHT,
                    z,
                    yaw,
                    rng.uniform(-4.0, 4.0),
                    # Tip the outer face down so the cone sheds rather than looking like a stack
                    # of hoops. Inner rings sit flatter.
                    -7.0 * (radius / 0.30) + rng.uniform(-3.0, 3.0),
                )
            )
    if len(slots) > CAPACITY:
        raise ValueError(f"cairn segment {segment} produced {len(slots)} slots, over the {CAPACITY} cap")
    return slots


def build_wall(rng):
    """Two courses of stones running along X, joints staggered course to course.

    The block entity yaws the whole set by the facing stored at placement, so this only ever has
    to describe the wall running west to east.
    """
    per_row, rows = 3, 2
    per_layer = per_row * rows
    layers = math.ceil(CAPACITY / per_layer)
    z_rows = [0.5 - 0.13, 0.5 + 0.13]

    slots = []
    for layer in range(layers):
        # Half-stone offset on alternate courses: the joints break, the way a drystone wall is laid.
        stagger = 0.0 if layer % 2 == 0 else 0.5 * (1.0 / per_row)
        for row in range(rows):
            for i in range(per_row):
                if len(slots) == CAPACITY:
                    break
                x = (i + 0.5) / per_row + stagger
                x = min(0.93, max(0.07, x))
                slots.append(
                    slot(
                        x,
                        layer * STONE_HEIGHT,
                        z_rows[row] + rng.uniform(-0.015, 0.015),
                        rng.uniform(-5.0, 5.0),
                        rng.uniform(-3.0, 3.0),
                        rng.uniform(-4.0, 4.0),
                    )
                )
    return slots


def build_scattered(rng):
    """A low, wide spread — a marker you notice from a distance, not a heap you built."""
    counts = [13, 11, 8]
    radii = [0.36, 0.26, 0.15]
    slots = []
    for layer, (count, radius) in enumerate(zip(counts, radii)):
        phase = rng.uniform(0, math.tau)
        for i in range(count):
            theta = phase + math.tau * i / count + rng.uniform(-0.2, 0.2)
            r = radius * rng.uniform(0.55, 1.0)
            slots.append(
                slot(
                    0.5 + r * math.cos(theta),
                    layer * STONE_HEIGHT,
                    0.5 + r * math.sin(theta),
                    rng.uniform(-180.0, 180.0),
                    rng.uniform(-6.0, 6.0),
                    rng.uniform(-6.0, 6.0),
                )
            )
    if len(slots) != CAPACITY:
        raise ValueError(f"scattered produced {len(slots)} slots, want {CAPACITY}")
    return slots


# --- assembly --------------------------------------------------------------------------------------


def build_layouts(game_path: Path):
    rng = random.Random(SEED)
    layouts = {
        "heap": load_heap(game_path),
        "neat": build_neat(rng),
        "wall": build_wall(rng),
        "scattered": build_scattered(rng),
    }
    # Flat keys rather than a nested list: the block entity picks cairn{min(segment, 2)} and the
    # C# config stays one dictionary of named slot arrays.
    for segment in range(CAIRN_SEGMENTS):
        layouts[f"cairn{segment}"] = build_cairn(rng, segment)
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
