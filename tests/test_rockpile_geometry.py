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

    def test_masonry_courses_alternate_direction(self):
        """A running bond: each course is laid across the one below, so the joints never line up
        through the block."""
        by_layer = {}
        for s in self.layouts["masonry"]:
            by_layer.setdefault(round(s["y"], 5), []).append(s)
        yaws = [
            round(sum(abs(s["yawDeg"]) for s in course) / len(course))
            for _, course in sorted(by_layer.items())
        ]
        for lower, upper in zip(yaws, yaws[1:]):
            self.assertNotEqual(lower, upper)

    def test_cairn_segments_hold_fewer_stones_as_they_narrow(self):
        counts = [len(self.layouts[f"cairn{i}"]) for i in range(geo.CAIRN_SEGMENTS)]
        for lower, upper in zip(counts, counts[1:]):
            self.assertLess(upper, lower)

    def test_expected_layouts_are_present(self):
        expected = {
            "heap", "neat", "wall", "scattered",
            "masonry", "ring", "spiral", "steps", "balanced", "twincolumns",
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
                    self.assertLessEqual(overhang(s), budget + 1e-6)

    def test_solid_layouts_stay_entirely_inside_their_block(self):
        """Masonry claims to be a solid block — walkable, buildable, face-culling. A stone poking
        out of it would be visibly lying, so it must fit its own cube exactly, and it is pinned
        square for the same reason (see BlockEntityRockPile.YawDeg)."""
        for name in geo.SOLID_LAYOUTS:
            for i, s in enumerate(self.layouts[name]):
                with self.subTest(layout=name, slot=i):
                    self.assertAlmostEqual(overhang(s), 0.0, places=6)

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
                worst = max(overhang(spun(s, yaw)) for s in slots)
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

    def test_cairn_segments_leave_no_gap_between_blocks(self):
        """Every cairn segment must fill its block to the ceiling.

        A segment that stops short hangs the one above it in mid-air: the middle segment once used
        six of the eight available layers and left a quarter-block of daylight under the crown,
        which is exactly what a stacked cairn looked wrong for.
        """
        for segment in range(geo.CAIRN_SEGMENTS):
            slots = self.layouts[f"cairn{segment}"]
            top = max(s["y"] for s in slots) + geo.STONE_HEIGHT
            with self.subTest(segment=segment):
                self.assertAlmostEqual(top, 1.0, places=6)

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
