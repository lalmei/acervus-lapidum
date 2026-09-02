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


def spun(s, yaw_deg):
    """A slot as the pile draws it once turned by the pile's own orientation."""
    dx, dz = s["x"] - 0.5, s["z"] - 0.5
    x, _, z = geo.apply(geo.rot_y(yaw_deg), (dx, 0.0, dz))
    return {**s, "x": 0.5 + x, "z": 0.5 + z, "yawDeg": s["yawDeg"] + yaw_deg}


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
        for name in ("heap", "neat", "scattered"):
            with self.subTest(layout=name):
                self.assertEqual(len(self.layouts[name]), geo.HEAP_CAPACITY)

    def test_masonry_tiles_every_course(self):
        """Masonry is the layout that hands back a solid block, so every course has to be full —
        a gap anywhere means the block is claiming a solidity it does not have."""
        slots = self.layouts["masonry"]
        self.assertEqual(len(slots), geo.MAX_SLOTS)

        by_layer = {}
        for s in slots:
            by_layer.setdefault(round(s["y"], 5), []).append(s)
        self.assertEqual(len(by_layer), geo.LAYERS)
        for height, course in by_layer.items():
            with self.subTest(height=height):
                self.assertEqual(len(course), 12)

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

    def test_bonding_layouts_carry_a_stone_across_each_joint(self):
        """The stone that ties one pile to the next, at whichever ends have a pile to tie to."""
        half = geo.STONE_LENGTH / 2
        for name in ("masonry", "wall"):
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
                xs = [s["xBond"][bond] for s in course]
                west = min(xs) - half
                east = max(xs) + half
                with self.subTest(layout=name, bond=bond):
                    # Near end: crosses the face when there is something to tie into, else flush.
                    self.assertAlmostEqual(west, -half if behind else 0.0, places=5)
                    # Far end: stops where the neighbour's own bond stone begins, else flush.
                    self.assertAlmostEqual(east, 1.0 - half if ahead else 1.0, places=5)

    def test_a_lone_bonded_pile_is_symmetric_end_to_end(self):
        """With no neighbours a wall or a masonry block must look the same from either end.

        It did not: the course only reasoned about the pile behind it, so the far end was left
        notched open — by 0.094 on a wall and 0.177 on masonry — while the near end sat flush.
        Turning the pile round swapped which end was which.
        """
        half = geo.STONE_LENGTH / 2
        for name in ("masonry", "wall"):
            for height, course in courses_of(self.layouts[name]).items():
                xs = [posed(s)["x"] for s in course]
                west = min(xs) - half
                east = 1.0 - (max(xs) + half)
                with self.subTest(layout=name, height=height):
                    self.assertAlmostEqual(west, east, places=5)

    def test_the_joint_between_two_piles_is_bridged(self):
        """Lay two piles side by side and the seam between them must not run top to bottom.

        A stone is 0.3125 long and a block is 1.0 wide, so a course cannot tile a block exactly;
        what matters is that the joint at the block boundary is covered by the course above or
        below it, rather than every course stopping dead at the same place. The bond courses are
        the ones that cover it.
        """
        half = geo.STONE_LENGTH / 2
        for name in ("masonry", "wall"):
            by_layer = {}
            for s in self.layouts[name]:
                by_layer.setdefault(round(s["y"], 5), []).append(s)

            bridged = []
            for height, course in sorted(by_layer.items()):
                # Tile this course at x and x+1, the way two piles side by side render it: the
                # left one has a neighbour ahead, the right one has a neighbour behind.
                spans = [
                    (posed(s, bond)["x"] + offset - half, posed(s, bond)["x"] + offset + half)
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
        """Stones may not tile a block exactly, but the slivers left over have to stay slivers."""
        half = geo.STONE_LENGTH / 2

        # Three stones of 0.3125 cover 0.9375 of a block, so 0.0625 of gap has to go somewhere.
        # Split evenly, that is 0.03125 a side — half a texture pixel at the game's 16px scale,
        # and the best a symmetric three-stone course can do. Anything wider is a real hole.
        widest_allowed = 1.0 / 32 + 1e-9

        for name in ("masonry", "wall"):
            by_layer = {}
            for s in self.layouts[name]:
                by_layer.setdefault(round(s["y"], 5), []).append(s["x"])

            for height, xs in by_layer.items():
                spans = sorted((x - half, x + half) for x in xs)
                reach = spans[0][1]
                for start_x, end_x in spans[1:]:
                    with self.subTest(layout=name, height=height):
                        self.assertLessEqual(max(0.0, start_x - reach), widest_allowed)
                    reach = max(reach, end_x)

    def test_cairn_segments_hold_fewer_stones_as_they_narrow(self):
        counts = [len(self.layouts[f"cairn{i}"]) for i in range(geo.CAIRN_SEGMENTS)]
        for lower, upper in zip(counts, counts[1:]):
            self.assertLess(upper, lower)

    def test_expected_layouts_are_present(self):
        expected = {
            "heap", "neat", "wall", "scattered",
            "masonry", "ring", "spiral", "steps", "balanced", "twincolumns", "arrow",
        }
        expected |= {f"cairn{i}" for i in range(geo.CAIRN_SEGMENTS)}
        self.assertEqual(set(self.layouts), expected)

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
        pile never intrudes on a neighbour worse than the game's own."""
        budget = max(overhang(s) for s in self.layouts["heap"])
        self.assertLess(budget, 0.25, "vanilla heap overhang is larger than assumed")

        for name, slots in self.layouts.items():
            for i, s in enumerate(slots):
                with self.subTest(layout=name, slot=i):
                    self.assertLessEqual(overhang(standalone(s)), budget + 1e-6)

    def test_solid_layouts_stay_entirely_inside_their_block(self):
        """Masonry claims to be a solid block — walkable, buildable, face-culling. A stone poking
        out of it would be visibly lying, so it must fit its own cube exactly, and it is pinned
        square for the same reason (see BlockEntityRockPile.YawDeg)."""
        for name in geo.SOLID_LAYOUTS:
            for i, s in enumerate(self.layouts[name]):
                with self.subTest(layout=name, slot=i):
                    self.assertAlmostEqual(overhang(standalone(s)), 0.0, places=6)

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
        """This is the one that has to be exact: a cairn stacks segments, so a stone crossing
        y = 1 would collide with the segment above it."""
        for name, slots in self.layouts.items():
            for i, s in enumerate(slots):
                with self.subTest(layout=name, slot=i):
                    self.assertGreaterEqual(s["y"], 0.0)
                    self.assertLessEqual(s["y"] + geo.STONE_HEIGHT, 1.0)

    def test_slots_are_ordered_bottom_up(self):
        """Piles fill in slot order, so a pile must never grow a stone above an empty gap."""
        for name, slots in self.layouts.items():
            heights = [s["y"] for s in slots]
            with self.subTest(layout=name):
                self.assertEqual(heights, sorted(heights))

    def test_first_stone_sits_on_the_ground(self):
        for name, slots in self.layouts.items():
            with self.subTest(layout=name):
                self.assertAlmostEqual(slots[0]["y"], 0.0)

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

    def test_scattered_is_flatter_and_wider_than_the_heap(self):
        scattered, heap = self.layouts["scattered"], self.layouts["heap"]
        self.assertLess(max(s["y"] for s in scattered), max(s["y"] for s in heap))


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
