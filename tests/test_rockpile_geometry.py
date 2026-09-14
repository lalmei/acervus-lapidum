"""The layout config is what the pile renders, so its invariants are worth asserting.

These run against the committed config as well as a freshly generated one, so a hand-edit that
breaks proportionality fails here rather than in game.
"""

from __future__ import annotations

import json
import math
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))

import rockpile_geometry as geo  # noqa: E402

GAME = Path("/Applications/Vintage Story.app")
COMMITTED = ROOT / "mod/assets/acervuslapidum/config/rockpile-layout.json"


def committed():
    return json.loads(COMMITTED.read_text())


# Index into a slot's xBond positions: [none, ahead, behind, both].
BOND_NONE, BOND_AHEAD, BOND_BEHIND, BOND_BOTH = 0, 1, 2, 3


def posed(s, bond=BOND_NONE):
    """A slot as it draws with the given neighbours present.

    Bond stones deliberately reach into the pile next door — that is the whole point of them — so
    the bounds worth policing are the ones a pile with no neighbours occupies, which is the
    default here.
    """
    return {**s, "x": s["xBond"][bond]} if "xBond" in s else dict(s)


def standalone(s):
    return posed(s, BOND_NONE)


def courses_of(slots):
    """Slots grouped by height."""
    by_layer = {}
    for s in slots:
        by_layer.setdefault(round(s["y"], 5), []).append(s)
    return by_layer


def footprint(s):
    """The block-space rectangle this slot's stone covers, as (x0, x1, z0, z1)."""
    quarter = round(s["yawDeg"] / 90.0) % 2
    half_x = (geo.STONE_DEPTH if quarter else geo.STONE_LENGTH) / 2
    half_z = (geo.STONE_LENGTH if quarter else geo.STONE_DEPTH) / 2
    return (s["x"] - half_x, s["x"] + half_x, s["z"] - half_z, s["z"] + half_z)


def daylight(courses, tiles=(0.0,), region=(0.0, 1.0, 0.0, 1.0)):
    """The area of ``region`` that no course covers — daylight straight through the pile.

    ``tiles`` repeats each course at the given offsets, which is how a pile renders with its
    neighbours: the stone one pile hangs over its near face lands in the notch the pile behind
    left at its far one. ``region`` narrows the question to part of the block.

    Exact rather than sampled: every stone edge is a breakpoint, so testing one point per cell of
    the grid those breakpoints make settles the whole area.
    """
    left, right, front, back = region
    boxes = [
        (x0 + dx, x1 + dx, z0 + dz, z1 + dz)
        for course in courses
        for x0, x1, z0, z1 in (footprint(s) for s in course)
        for dx in tiles
        for dz in tiles
    ]
    xs = sorted({left, right} | {v for b in boxes for v in b[:2] if left < v < right})
    zs = sorted({front, back} | {v for b in boxes for v in b[2:] if front < v < back})

    open_area = 0.0
    for x0, x1 in zip(xs, xs[1:]):
        for z0, z1 in zip(zs, zs[1:]):
            x, z = (x0 + x1) / 2, (z0 + z1) / 2
            if not any(bx0 < x < bx1 and bz0 < z < bz1 for bx0, bx1, bz0, bz1 in boxes):
                open_area += (x1 - x0) * (z1 - z0)
    return open_area


def spun(s, yaw_deg):
    """A slot as the pile draws it once turned by the pile's own orientation."""
    dx, dz = s["x"] - 0.5, s["z"] - 0.5
    x, _, z = geo.apply(geo.rot_y(yaw_deg), (dx, 0.0, dz))
    return {**s, "x": 0.5 + x, "z": 0.5 + z, "yawDeg": s["yawDeg"] + yaw_deg}


def half_span_x(s):
    """Half the width this slot's stone covers along X.

    Masonry alternates straight and quarter-turned courses, so a course is either stone lengths
    across or stone depths across. Measuring the pose rather than assuming the length is what lets
    the gap and joint checks below read both kinds of course.
    """
    # The quarter turn is what matters here, not the degree or two of jitter a hand-laid course
    # carries: a wall stone nudged 3 degrees is still a stone length across for joint purposes.
    quarter = round(s["yawDeg"] / 90.0) % 2
    return (geo.STONE_DEPTH if quarter else geo.STONE_LENGTH) / 2


def overhang(s):
    """How far past the block edge this slot's stone reaches, in block units.

    Replays the render chain on the stone's eight corners: rotate about the bottom-centre by
    (yaw, pitch, roll), translate to (x, y, z), then measure the horizontal excursion outside
    0..1. Zero means the stone sits wholly within its own block.
    """
    m = geo.mul(
        geo.rot_y(s["yawDeg"]),
        geo.mul(geo.rot_x(s["pitchDeg"]), geo.rot_z(s["rollDeg"])),
    )
    half_x, half_z = geo.STONE_DIMS_PX[0] * geo.PX / 2, geo.STONE_DIMS_PX[2] * geo.PX / 2
    worst = 0.0
    for cx in (-half_x, half_x):
        for cy in (0.0, geo.STONE_HEIGHT):
            for cz in (-half_z, half_z):
                px, _, pz = geo.apply(m, (cx, cy, cz))
                for value in (px + s["x"], pz + s["z"]):
                    worst = max(worst, -value, value - 1.0)
    return worst


class TestCommittedConfig(unittest.TestCase):
    def setUp(self):
        self.layouts = committed()

    def test_no_layout_exceeds_the_inventory(self):
        """One slot per stone is the whole promise of the mod, so no layout may want more slots
        than a pile actually has."""
        for name, slots in self.layouts.items():
            with self.subTest(layout=name):
                self.assertGreater(len(slots), 0)
                self.assertLessEqual(len(slots), geo.MAX_SLOTS)

    def test_loose_layouts_hold_vanillas_pile_density(self):
        """The layouts that are just stone tipped on the ground stay at vanilla's own density."""
        for name in ("heap", "neat"):
            with self.subTest(layout=name):
                self.assertEqual(len(self.layouts[name]), geo.HEAP_CAPACITY)

    def test_masonry_lays_a_full_jointed_course(self):
        """Masonry is the layout that hands back a solid block, so every course has to be laid
        out in full — a course short of a stone is a hole in something claiming to be solid.

        Nine, not the twelve that tiled the cube exactly: a jointed course fits three stones an
        axis, not four, and that is the whole trade. Stones that touch read as one milled slab.
        """
        slots = self.layouts["masonry"]
        courses = courses_of(slots)
        self.assertEqual(len(courses), geo.LAYERS)
        for height, course in courses.items():
            with self.subTest(height=height):
                self.assertEqual(len(course), 9)
        self.assertEqual(len(slots), 9 * geo.LAYERS)
        self.assertLessEqual(len(slots), geo.MAX_SLOTS)

    def test_bonding_layouts_stagger_their_courses(self):
        """A running bond: each course starts half a stone off the one below, so no vertical
        joint runs through more than one course."""
        for name in ("masonry", "wall"):
            by_layer = {}
            for s in self.layouts[name]:
                by_layer.setdefault(round(s["y"], 5), []).append(s["x"])
            starts = [min(xs) for _, xs in sorted(by_layer.items())]
            for lower, upper in zip(starts, starts[1:]):
                with self.subTest(layout=name):
                    self.assertNotAlmostEqual(lower, upper)

    def test_masonry_breaks_its_joints_inside_a_lone_block(self):
        """A pile on its own has to look like coursed masonry, not like twelve columns.

        Bonding is about the seam with the pile next door; this is the other half — within one
        block, alternating straight and quarter-turned courses puts the joints of each course a
        good pixel away from the joints of the course below it. When every course ran the same
        way the joints lined up perfectly all the way up, which is what the running bond exists
        to avoid.
        """
        # One texture pixel at the game's 16px block scale: closer than this and the two joints
        # read as one continuous seam.
        clearance = 1.0 / 16 - 1e-9

        def joints(course):
            # A course is several rows deep, so each X position appears more than once; only the
            # distinct ones are joints.
            spans = sorted(
                {
                    (round(posed(s)["x"] - half_span_x(s), 5), round(posed(s)["x"] + half_span_x(s), 5))
                    for s in course
                }
            )
            return [(a + previous) / 2 for (previous, (a, _)) in zip(
                (b for _, b in spans), spans[1:]
            )]

        courses = [course for _, course in sorted(courses_of(self.layouts["masonry"]).items())]
        for height, (lower, upper) in enumerate(zip(courses, courses[1:])):
            below, above = joints(lower), joints(upper)
            self.assertTrue(below and above, "a course with no joints at all")
            for a in above:
                with self.subTest(course=height + 1, joint=a):
                    self.assertGreaterEqual(min(abs(a - b) for b in below), clearance)

    def test_bonding_layouts_carry_a_stone_across_each_joint(self):
        """The stone that ties one pile to the next, at whichever ends have a pile to tie to.

        The course is mortared to one joint throughout, so its ends are checked against the rhythm
        its own stones keep: half a joint in from a free face, out onto the boundary where there is
        a pile to bond to, and a whole joint short of the far face where the pile ahead reaches its
        own bond stone back over it. Wall lays its stones overlapping rather than jointed, which is
        the same arithmetic with the joint coming out negative. Masonry is not here: its offset
        courses cross the near face outright, the same way in every pile, so it needs no xBond to
        arbitrate who lays the stone in a joint.
        """
        for name in ("wall",):
            course = [s for s in self.layouts[name] if "xBond" in s]
            with self.subTest(layout=name):
                self.assertTrue(course, "no bond courses at all")

            for s in course:
                with self.subTest(layout=name, slot=s["y"]):
                    self.assertEqual(len(s["xBond"]), 4)

            # Each pile lays the stone that crosses its OWN near joint, and correspondingly stops
            # short at its far one because the pile ahead reaches back over it. That is what keeps
            # exactly one bond stone in every joint rather than two fighting for the same space.
            for bond, behind, ahead in [
                (BOND_NONE, False, False),
                (BOND_AHEAD, False, True),
                (BOND_BEHIND, True, False),
                (BOND_BOTH, True, True),
            ]:
                spans = sorted(
                    {
                        (
                            round(s["xBond"][bond] - half_span_x(s), 5),
                            round(s["xBond"][bond] + half_span_x(s), 5),
                        )
                        for s in course
                    }
                )
                gaps = [start - end for (_, end), (start, _) in zip(spans, spans[1:])]
                lead = spans[0][1] - spans[0][0]

                with self.subTest(layout=name, bond=bond):
                    # A joint written out to five decimal places, so the comparisons carry the
                    # rounding the config was written with rather than chasing it.
                    tolerance = 3e-5

                    # One joint, kept all the way along the course.
                    for gap in gaps[1:]:
                        self.assertAlmostEqual(gap, gaps[0], delta=tolerance)

                    joint = gaps[0]
                    west, east = spans[0][0], spans[-1][1]
                    # Near end: crosses the face when there is something to tie into, else stands
                    # half a joint back from it.
                    self.assertAlmostEqual(
                        west, -lead / 2 if behind else joint / 2, delta=tolerance
                    )
                    # Far end: leaves the neighbour's own bond stone a joint's clearance, else
                    # half a joint to the face, the way the near end does.
                    self.assertAlmostEqual(
                        east,
                        1.0 - lead / 2 - joint if ahead else 1.0 - joint / 2,
                        delta=tolerance,
                    )

    def test_a_lone_bonded_pile_is_symmetric_end_to_end(self):
        """With no neighbours a wall or a masonry block must look the same from either end.

        It did not: the course only reasoned about the pile behind it, so the far end was left
        notched open — by 0.094 on a wall and 0.177 on masonry — while the near end sat flush.
        Turning the pile round swapped which end was which.

        Masonry is deliberately not symmetric any more: its offset courses hang a stone over the
        near face and leave the matching notch at the far one, which is what carries the bond
        through a block boundary. It cannot flip on you, because a solid pile is always drawn
        square — see BlockEntityRockPile.YawDeg.
        """
        for name in ("wall",):
            for height, course in courses_of(self.layouts[name]).items():
                spans = [
                    (posed(s)["x"] - half_span_x(s), posed(s)["x"] + half_span_x(s))
                    for s in course
                ]
                west = min(a for a, _ in spans)
                east = 1.0 - max(b for _, b in spans)
                with self.subTest(layout=name, height=height):
                    self.assertAlmostEqual(west, east, places=5)

    def test_the_joint_between_two_piles_is_bridged(self):
        """Lay two piles side by side and the seam between them must not run top to bottom.

        A stone is 0.3125 long and a block is 1.0 wide, so a course cannot tile a block exactly;
        what matters is that the joint at the block boundary is covered by the course above or
        below it, rather than every course stopping dead at the same place. The bond courses are
        the ones that cover it.
        """
        for name in ("masonry", "wall"):
            by_layer = {}
            for s in self.layouts[name]:
                by_layer.setdefault(round(s["y"], 5), []).append(s)

            bridged = []
            for height, course in sorted(by_layer.items()):
                # Tile this course at x and x+1, the way two piles side by side render it: the
                # left one has a neighbour ahead, the right one has a neighbour behind.
                spans = [
                    (
                        posed(s, bond)["x"] + offset - half_span_x(s),
                        posed(s, bond)["x"] + offset + half_span_x(s),
                    )
                    for s in course
                    for offset, bond in ((0.0, BOND_AHEAD), (1.0, BOND_BEHIND))
                ]
                bridged.append(any(a < 1.0 < b for a, b in spans))

            with self.subTest(layout=name):
                self.assertTrue(any(bridged), "no course bridges the joint at all")
                # No two courses in a row may both leave the joint open, or the seam shows.
                for lower, upper in zip(bridged, bridged[1:]):
                    self.assertTrue(lower or upper)

    def test_a_course_has_no_hole_wide_enough_to_see_through(self):
        """Coursed layouts are jointed, not gappy: the daylight between two stones has to stay a
        joint. Anything wider is a hole, and on masonry a hole is a block claiming a solidity it
        does not have."""
        # The joint masonry lays — the widest any of these courses leaves, because wall overlaps
        # its stones instead. Measured, not assumed: whatever the course arithmetic settles on is
        # what every gap in every course has to stay within.
        widest_allowed = geo.COURSE_PITCH - geo.STONE_LENGTH + 1e-5

        for name in ("masonry", "wall"):
            by_layer = {}
            for s in self.layouts[name]:
                by_layer.setdefault(round(s["y"], 5), []).append(s)

            for height, course in by_layer.items():
                spans = sorted(
                    (s["x"] - half_span_x(s), s["x"] + half_span_x(s)) for s in course
                )
                reach = spans[0][1]
                for start_x, end_x in spans[1:]:
                    with self.subTest(layout=name, height=height):
                        self.assertLessEqual(max(0.0, start_x - reach), widest_allowed)
                    reach = max(reach, end_x)

    def test_masonry_gaps_are_pockets_rather_than_holes(self):
        """The gaps are there to be packed with clay, so every one of them needs a stone above and
        below it. Daylight from the ceiling to the floor would be a gap with nothing to pack
        against — and a solid block you can see through.

        Two courses cannot do this. A course's mortar is a cross-hatch, and however far a second
        cross-hatch slides over the first, the x lines of one still cross the z lines of the other.
        Four course starts close every one of those crossings.
        """
        courses = list(courses_of(self.layouts["masonry"]).values())

        # As a wall: the stone each pile hangs over its near face fills the notch behind it, so
        # not one point of the footprint is open all the way through.
        self.assertAlmostEqual(daylight(courses, tiles=(-1.0, 0.0, 1.0)), 0.0, places=6)

        # On its own, what is open is the notch waiting for that neighbour, and only that: a rim
        # against the two far faces, no wider than the stone that fills it, and never a hole in
        # the body of the block.
        inside = (0.0, 1.0 - geo.STONE_LENGTH / 2, 0.0, 1.0 - geo.STONE_DEPTH / 2)
        self.assertAlmostEqual(daylight(courses, region=inside), 0.0, places=6)

    def test_the_committed_drawing_matches_the_committed_config(self):
        """docs/masonry-bond.svg is drawn from the layout rather than by hand, and committed, so
        the README's picture of the bond cannot quietly stop matching the bond."""
        drawing = ROOT / "docs/masonry-bond.svg"
        self.assertTrue(drawing.exists(), "the README links this drawing")
        self.assertEqual(drawing.read_text(), geo.masonry_diagram(self.layouts))

    def test_cairn_segments_hold_fewer_stones_as_they_narrow(self):
        counts = [len(self.layouts[f"cairn{i}"]) for i in range(geo.CAIRN_SEGMENTS)]
        for lower, upper in zip(counts, counts[1:]):
            self.assertLess(upper, lower)

    def test_expected_layouts_are_present(self):
        expected = {
            "heap", "neat", "wall", "masonry", "ring", "spiral", "steps", "balanced", "twincolumns",
            "arrow", "nichecairn",
        }
        expected |= {f"cairn{i}" for i in range(geo.CAIRN_SEGMENTS)}
        self.assertEqual(set(self.layouts), expected)

    def test_the_niche_is_a_window_with_a_shelf_under_it(self):
        """The pocket has to read as somewhere you would stand a torch.

        Two things make it read that way rather than as a missing stone: an opening about as tall as
        the thing it is for, and a course left in underneath it to stand that thing on. Both are
        measured off the cut courses, which is what decides them.
        """
        opening_from = min(geo.NICHE_LAYERS) * geo.STONE_HEIGHT
        opening_to = (max(geo.NICHE_LAYERS) + 1) * geo.STONE_HEIGHT

        # A torch is about 10px tall, and the window is sized to it.
        self.assertGreaterEqual((opening_to - opening_from) * 16, 9.5)

        # Course zero is not cut, so there is stone beneath the opening to stand something on.
        self.assertNotIn(0, geo.NICHE_LAYERS)
        px = 1 / 16
        self.assertTrue(
            geo.region_holds_stone(
                self.layouts["nichecairn"],
                (0.5 - 1.5 * px, 0.0, 0.5 + px),
                (0.5 + 1.5 * px, opening_from - 0.006, 0.5 + 5 * px),
            ),
            "nothing under the opening to stand anything on",
        )

    def test_the_niche_is_the_cairn_with_stones_left_out(self):
        """It is the cairn with an opening, not a second cone.

        Not the same coordinates — it draws its own ring phase, as every pile does, so a row of them
        does not line up like printing. The same construction: a course for every course, each on
        the radius the cairn's own profile gives it, and a count that is the cairn's count less
        whatever the pocket cuts out of it. That is what makes a pocket cairn among plain ones read
        as the same kind of pile.

        The profile it borrows is ``NICHE_PROFILE``'s — the footing's rings — even though
        ``NICHE_SEGMENT`` puts the course a block up. The body profile has no circumference to
        spare: cutting a mouth wide enough to stand a torch in empties whole courses of it, which
        is a hole through a cairn rather than a socket in one.
        """
        niche = courses_of(self.layouts["nichecairn"])
        rings = geo.cairn_rings(geo.NICHE_PROFILE)
        self.assertEqual(len(niche), len(rings))

        for layer, (height, course) in enumerate(sorted(niche.items())):
            count, radius = rings[layer]
            with self.subTest(course=layer):
                # Every stone on that course sits on the cairn's own radius for it. The lintel over
                # the mouth is the exception and is pulled in slightly.
                on_ring = [
                    s for s in course
                    if abs(math.hypot(s["x"] - 0.5, s["z"] - 0.5) - radius) < 1e-3
                ]
                self.assertGreaterEqual(len(on_ring), len(course) - 2)

                if layer in geo.NICHE_LAYERS:
                    self.assertLess(len(on_ring), count, "the pocket cut nothing out of this course")
                else:
                    self.assertEqual(len(on_ring), count, "an uncut course lost a stone")

    def test_ring_is_hollow(self):
        """A hearth ring you can lay a fire in. If the middle fills up it is just a small pile."""
        for s in self.layouts["ring"]:
            distance = math.hypot(s["x"] - 0.5, s["z"] - 0.5)
            self.assertGreater(distance, 0.2)

    def test_twin_columns_leave_a_gap_between_them(self):
        """Two columns, not one wide one — there has to be daylight down the middle."""
        xs = sorted({round(s["x"], 3) for s in self.layouts["twincolumns"]})
        self.assertEqual(len(xs), 2)
        # Clear of each other once the stones' own half-length is taken off the gap.
        self.assertGreater(xs[1] - xs[0], geo.STONE_LENGTH)

    def test_the_arrow_points(self):
        """The one layout whose whole job is a direction. It must narrow towards +Z, which is the
        heading a pile is built at before it is turned, or turning it points nothing anywhere."""
        arrow = self.layouts["arrow"]

        def half_width(z):
            return max(abs(s["x"] - 0.5) for s in arrow if abs(s["z"] - z) < 1e-6)

        point = half_width(max(s["z"] for s in arrow))
        tail = max(half_width(s["z"]) for s in arrow)

        # The front stones are the two barbs on their way to where they cross, so the point is a
        # pair straddling the centre line rather than a stone sitting on it. What has to hold is
        # that the barbs open out well behind it.
        self.assertGreater(tail, 0.15)
        self.assertGreater(tail, 3 * point)

        # And a shaft behind the point, so it reads as an arrow rather than an open V.
        self.assertLess(min(s["z"] for s in arrow), 0.2)
        self.assertTrue(any(abs(s["x"] - 0.5) < 1e-6 for s in arrow if s["z"] < 0.3))

    def test_the_arrow_is_symmetric_about_its_own_line(self):
        """A crooked arrow points somewhere between two headings, which is no heading at all."""
        for layer in {round(s["y"], 5) for s in self.layouts["arrow"]}:
            xs = sorted(
                round(s["x"], 5) for s in self.layouts["arrow"] if abs(s["y"] - layer) < 1e-6
            )
            with self.subTest(layer=layer):
                self.assertEqual(xs, sorted(round(1.0 - x, 5) for x in xs))

    def test_steps_climb(self):
        """Each step must be strictly higher than the one in front of it, and rest on stone all
        the way down rather than starting in mid-air."""
        by_row = {}
        for s in self.layouts["steps"]:
            by_row.setdefault(round(s["z"], 3), []).append(s["y"])
        rows = sorted(by_row)
        tops = [max(by_row[z]) for z in rows]
        self.assertEqual(tops, sorted(tops))
        self.assertLess(tops[0], tops[-1])
        for z in rows:
            self.assertAlmostEqual(min(by_row[z]), 0.0)

    def test_no_layout_overhangs_further_than_vanillas_own_heap(self):
        """Stones do stick out of a pile — vanilla's heap included, by design. What must hold is
        that nothing we author reaches further past the block than vanilla already does, so a
        pile never intrudes on a neighbour worse than the game's own.

        Masonry is the exception, and a deliberate one: its offset courses centre a stone on the
        block face to carry the bond across it. That stone is allowed exactly half of itself, and
        the pile next door left a notch of exactly that size waiting for it.
        """
        budget = max(overhang(s) for s in self.layouts["heap"])
        self.assertLess(budget, 0.25, "vanilla heap overhang is larger than assumed")

        for name, slots in self.layouts.items():
            allowed = geo.STONE_LENGTH / 2 if name in geo.SOLID_LAYOUTS else budget
            for i, s in enumerate(slots):
                with self.subTest(layout=name, slot=i):
                    self.assertLessEqual(overhang(standalone(s)), allowed + 1e-6)

    def test_solid_layouts_keep_every_stone_seated_in_their_own_block(self):
        """Masonry claims to be a solid block — walkable, buildable, face-culling — and it is
        pinned square for that reason (see BlockEntityRockPile.YawDeg).

        Its offset courses do hang a stone over the near face, which is what bonds one pile to the
        next. What must hold is that the stone is still seated in this block: its centre stays
        inside, so no more than half of it is ever in the neighbour's, and it is square to the
        faces rather than poking a corner through them.
        """
        for name in geo.SOLID_LAYOUTS:
            for i, s in enumerate(self.layouts[name]):
                with self.subTest(layout=name, slot=i):
                    self.assertAlmostEqual(s["yawDeg"] % 90.0, 0.0, places=6)
                    self.assertAlmostEqual(s["pitchDeg"], 0.0, places=6)
                    self.assertAlmostEqual(s["rollDeg"], 0.0, places=6)
                    self.assertGreaterEqual(s["x"], 0.0)
                    self.assertLessEqual(s["x"], 1.0)
                    self.assertGreaterEqual(s["z"], 0.0)
                    self.assertLessEqual(s["z"], 1.0)

    def test_turning_a_pile_never_makes_it_spill_much_further(self):
        """Piles turn in 45 degree steps, and a square arrangement is at its widest on the
        diagonal. Loose stone may spill over the edge — vanilla's own heap does — but the diagonal
        must not turn that spill into something that swamps the neighbouring block."""
        budget = 0.27
        for name, slots in self.layouts.items():
            if name in geo.SOLID_LAYOUTS:
                continue
            for step in range(geo.ORIENTATION_STEPS):
                yaw = step * (360 / geo.ORIENTATION_STEPS)
                worst = max(overhang(spun(standalone(s), yaw)) for s in slots)
                with self.subTest(layout=name, yaw=yaw):
                    self.assertLessEqual(worst, budget)

    def test_no_slot_reaches_into_the_block_above(self):
        """A cairn stacks segments, so a stone crossing y = 1 would collide with the one above it.

        Checked twice over. The pivot check is the exact one: no slot may be placed so high that a
        flat stone in it would cross the ceiling. The second reads the stone's true top under its own
        pose, which is the only one that can see a tilted or on-edge stone reaching past its pivot.

        That second budget is derived from the stone's own tilt rather than measured off the layouts,
        because a measured constant is a trap: it was 16.35px, tuned to what the cairn happened to
        reach, and withdrawing an unrelated layout re-rolled the shared rng and pushed the same
        course to 16.354px. A stone may overhang by what its tilt lifts its corner, and no more.
        """
        for name, slots in self.layouts.items():
            for i, s in enumerate(slots):
                with self.subTest(layout=name, slot=i):
                    self.assertGreaterEqual(s["y"], 0.0)
                    self.assertLessEqual(s["y"] + geo.STONE_HEIGHT, 1.0)

                    overhang = geo.slot_bounds_y(s)[1] - (s["y"] + geo.STONE_HEIGHT)
                    tilt = math.radians(max(abs(s["pitchDeg"]), abs(s["rollDeg"])))
                    allowed = (geo.STONE_LENGTH + geo.STONE_DEPTH) / 2 * math.sin(tilt)
                    self.assertLessEqual(overhang, allowed + 1e-9)

    def test_slots_are_ordered_bottom_up(self):
        """Piles fill in slot order, so a pile must never grow a stone above an empty gap."""
        for name, slots in self.layouts.items():
            heights = [s["y"] for s in slots]
            with self.subTest(layout=name):
                self.assertEqual(heights, sorted(heights))

    def test_first_stone_sits_on_the_ground(self):
        """Measured from the stone's own lowest corner, not from its slot y.

        The two are the same thing only while a stone is laid flat. The arch is the first layout
        that rolls one on edge, and a rolled stone's pivot sits at the middle of its standing
        height — so its slot y is half a stone length while the stone itself rests exactly on the
        ground. Checking the pivot would have called that floating and passed a genuinely floating
        on-edge stone somewhere else.

        Downward tolerance covers the tilt jitter every layout carries: vanilla's own heap dips
        0.69px at its deepest. Upward there is almost none, because floating is the failure.
        """
        for name, slots in self.layouts.items():
            bottom, _ = geo.slot_bounds_y(slots[0])
            with self.subTest(layout=name):
                self.assertLessEqual(bottom * 16, 0.05, "the first stone floats")
                self.assertGreaterEqual(bottom * 16, -0.75, "the first stone is buried")

    def test_cairn_never_flares_going_up_the_column(self):
        """A cairn is one cone, not three stacked drums. Each segment must pick up at the radius
        the one below it ended on, or the column visibly pinches and flares at every block
        boundary — which is exactly what the first draft of these profiles did."""
        for segment in range(geo.CAIRN_SEGMENTS - 1):
            ends_at = geo.cairn_rings(segment)[-1][1]
            starts_at = geo.cairn_rings(segment + 1)[0][1]
            with self.subTest(joint=f"cairn{segment}->cairn{segment + 1}"):
                self.assertLessEqual(starts_at, ends_at + 1e-9)

    def test_stacking_layouts_leave_no_gap_between_blocks(self):
        """Anything meant to stack must fill its block to the ceiling.

        A layout that stops short hangs the one above it in mid-air: the middle cairn segment once
        used six of the eight available layers and left a quarter-block of daylight under the
        crown, and the wall had the same fault before it was made stackable.
        """
        for name in geo.STACKING_LAYOUTS:
            slots = self.layouts[name]
            top = max(s["y"] for s in slots) + geo.STONE_HEIGHT
            with self.subTest(layout=name):
                self.assertAlmostEqual(top, 1.0, places=6)

    def test_a_wall_is_uniform_all_the_way_up(self):
        """Walls stack, so every course has to be the same — a thinner top course would show as a
        seam at every block join in a tall wall."""
        by_layer = {}
        for s in self.layouts["wall"]:
            by_layer.setdefault(round(s["y"], 5), []).append(s)
        self.assertEqual(len(by_layer), geo.LAYERS)
        self.assertEqual({len(course) for course in by_layer.values()}, {8})

    def test_cairn_rings_always_close(self):
        """A ring with under 100% coverage has a hole you can see straight through."""
        stone_length = geo.STONE_DIMS_PX[0] * geo.PX
        for segment in range(geo.CAIRN_SEGMENTS):
            for layer, (count, radius) in enumerate(geo.cairn_rings(segment)):
                coverage = count * stone_length / (math.tau * radius)
                with self.subTest(segment=segment, layer=layer):
                    self.assertGreaterEqual(coverage, 1.0 - 1e-9)

    def test_each_cairn_segment_narrows_within_itself(self):
        for segment in range(geo.CAIRN_SEGMENTS):
            radii = [radius for _, radius in geo.cairn_rings(segment)]
            with self.subTest(segment=segment):
                self.assertEqual(radii, sorted(radii, reverse=True))

    def test_cairn_footprint_shrinks_segment_over_segment(self):
        """The generated slots, not just the design profile, have to get narrower going up."""
        widths = [
            max(abs(s["x"] - 0.5) for s in self.layouts[f"cairn{i}"])
            for i in range(geo.CAIRN_SEGMENTS)
        ]
        for lower, upper in zip(widths, widths[1:]):
            self.assertLess(upper, lower)

    def test_wall_is_long_and_thin(self):
        """The wall layout has to read as a wall: it runs the full block along X and stays
        narrow along Z, which is what makes a row of them look continuous."""
        slots = self.layouts["wall"]
        span_x = max(s["x"] for s in slots) - min(s["x"] for s in slots)
        span_z = max(s["z"] for s in slots) - min(s["z"] for s in slots)
        self.assertGreater(span_x, 0.6)
        self.assertLess(span_z, 0.35)

    def test_a_balanced_stack_is_eight_stones_tall(self):
        """One stone a course, using the block's full height like every other layout."""
        slots = self.layouts["balanced"]
        self.assertEqual(len(slots), geo.LAYERS)
        self.assertEqual(len({round(s["y"], 5) for s in slots}), geo.LAYERS)

class TestHeapMatchesVanilla(unittest.TestCase):
    """The heap is not authored — it is vanilla's own stone-pile shape, re-driven at 1:1."""

    @unittest.skipUnless(GAME.exists(), "needs a Vintage Story install")
    def test_heap_positions_come_from_the_vanilla_shape(self):
        shape = json.loads(
            (GAME / "assets/survival/shapes/item/stone-pile.json").read_text()
        )
        vanilla = {
            (
                round(e["rotationOrigin"][0] * geo.PX, 5),
                round(e["rotationOrigin"][1] * geo.PX, 5),
                round(e["rotationOrigin"][2] * geo.PX, 5),
            )
            for e in shape["elements"]
        }
        generated = {(s["x"], s["y"], s["z"]) for s in committed()["heap"]}
        self.assertEqual(generated, vanilla)

    @unittest.skipUnless(GAME.exists(), "needs a Vintage Story install")
    def test_regenerating_reproduces_the_committed_config(self):
        """The generator is seeded, so `make assets` on an unchanged install is a no-op diff."""
        self.assertEqual(geo.build_layouts(GAME), committed())


class TestRotationMath(unittest.TestCase):
    def test_yxz_decomposition_round_trips(self):
        for yaw, pitch, roll in [
            (0, 0, 0),
            (37, 0, 0),
            (0, 24, 0),
            (0, 0, -18),
            (140, -31, 62),
            (-95, 12, 175),
        ]:
            with self.subTest(angles=(yaw, pitch, roll)):
                m = geo.mul(geo.rot_y(yaw), geo.mul(geo.rot_x(pitch), geo.rot_z(roll)))
                got = geo.to_yxz(m)
                back = geo.mul(
                    geo.rot_y(got[0]), geo.mul(geo.rot_x(got[1]), geo.rot_z(got[2]))
                )
                for a, b in zip(sum(m, ()), sum(back, ())):
                    self.assertAlmostEqual(a, b, places=5)

    def test_permutation_recovers_tipped_stone_boxes(self):
        """Five vanilla heap cubes are drawn as re-proportioned boxes rather than rotated ones."""
        for dims in [(5, 2, 4), (5, 4, 2), (4, 5, 2), (4, 2, 5)]:
            with self.subTest(dims=dims):
                m = geo.permutation_for(dims)
                got = tuple(round(abs(v), 3) for v in geo.apply(m, geo.STONE_DIMS_PX))
                self.assertEqual(got, tuple(float(d) for d in dims))


if __name__ == "__main__":
    unittest.main()
