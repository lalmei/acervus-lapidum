"""The layout list is written down in four places. They have to agree.

A layout exists as a C# enum member, a key in the generated config, a Cairo icon and an entry in
the tool-mode picker, plus a display name in the lang file. Adding one and forgetting another does
not fail the build — it fails quietly in game, as an untranslated string or a pile that renders
into a single spot because its config key never matched. So check the four against each other.
"""

from __future__ import annotations

import json
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))

import rockpile_geometry as geo  # noqa: E402

MOD = ROOT / "mod"
UTIL = MOD / "src/Storage/RockPileUtil.cs"
BEHAVIOR = MOD / "src/Items/CollectibleBehaviorRockPileable.cs"
LANG = MOD / "assets/acervuslapidum/lang/en.json"
CONFIG = MOD / "assets/acervuslapidum/config/rockpile-layout.json"


def enum_members():
    """(name, value) for RockPileLayoutMode, in declared order."""
    body = re.search(
        r"public enum RockPileLayoutMode\s*\{(.*?)\}", UTIL.read_text(), re.S
    ).group(1)
    return [
        (m.group(1), int(m.group(2)))
        for m in re.finditer(r"(\w+)\s*=\s*(\d+)", body)
    ]


def picker_codes():
    """The AssetLocation codes the tool-mode picker builds, in order."""
    block = re.search(
        r"return new SkillItem\[\]\s*\{(.*?)\n            \};", BEHAVIOR.read_text(), re.S
    ).group(1)
    return re.findall(r'new AssetLocation\("acervuslapidum", "(\w+)"\)', block)


class TestLayoutWiring(unittest.TestCase):
    def setUp(self):
        self.members = enum_members()
        self.names = [name.lower() for name, _ in self.members]

    def test_enum_values_are_dense_and_ordered(self):
        """The tool mode index is cast straight to the enum, so the values must be 0..n-1 in
        order. A gap would silently map a picker slot onto the wrong layout."""
        self.assertEqual([value for _, value in self.members], list(range(len(self.members))))

    def test_picker_lists_every_layout_in_enum_order_then_rotate(self):
        codes = picker_codes()
        self.assertEqual(codes[: len(self.names)], self.names)
        self.assertEqual(codes[len(self.names):], ["rotate"])

    def test_every_layout_has_a_display_name(self):
        lang = json.loads(LANG.read_text())
        for name in self.names:
            with self.subTest(layout=name):
                self.assertIn(f"rockpile-layout-{name}", lang)

    def test_every_layout_resolves_to_config_slots(self):
        """Cairn is the one that maps to several keys, one per segment height."""
        config = json.loads(CONFIG.read_text())
        for name in self.names:
            with self.subTest(layout=name):
                if name == "cairn":
                    for segment in range(geo.CAIRN_SEGMENTS):
                        self.assertIn(f"cairn{segment}", config)
                else:
                    self.assertIn(name, config)

    def test_every_config_key_belongs_to_a_layout(self):
        """The other direction: a generated layout nothing can select is dead weight."""
        config = json.loads(CONFIG.read_text())
        selectable = set(self.names) - {"cairn"}
        selectable |= {f"cairn{i}" for i in range(geo.CAIRN_SEGMENTS)}
        self.assertEqual(set(config), selectable)

    def test_every_layout_has_an_icon(self):
        icons = (MOD / "src/Storage/RockPileLayoutIcons.cs").read_text()
        for name in self.names + ["rotate"]:
            with self.subTest(layout=name):
                self.assertRegex(icons, rf"public static void Draw\w*\(", "no icons at all")
                self.assertIn(name, icons.lower())

    def test_inventory_is_big_enough_for_the_largest_layout(self):
        """MaxSlots sizes the inventory; a layout wanting more would render stones nobody holds."""
        declared = int(re.search(r"public const int MaxSlots = (\d+);", UTIL.read_text()).group(1))
        self.assertEqual(declared, geo.MAX_SLOTS)

        config = json.loads(CONFIG.read_text())
        self.assertLessEqual(max(len(v) for v in config.values()), declared)

    def test_heap_capacity_agrees_between_generator_and_mod(self):
        declared = int(
            re.search(r"public const int HeapCapacity = (\d+);", UTIL.read_text()).group(1)
        )
        self.assertEqual(declared, geo.HEAP_CAPACITY)


class TestRotationIsIdempotent(unittest.TestCase):
    """Turning a pile must survive being applied twice.

    The tool mode picker runs SetToolMode on the client and again on the server. That is harmless
    for a layout change, because setting the same mode twice is that mode — but a relative "turn
    one more step" applied on both sides turns 45 degrees into 90, which put half the orientations
    out of reach. Source guards, since neither side can be constructed without a running world.
    """

    def setUp(self):
        self.entity = (MOD / "src/Storage/BlockEntityRockPile.cs").read_text()
        self.behavior = BEHAVIOR.read_text()

    def test_the_pile_turns_to_an_orientation_rather_than_by_one(self):
        self.assertIn("public void TurnTo(int value", self.entity)
        self.assertNotIn("RotateBy", self.entity)
        self.assertNotIn("RotateBy", self.behavior)

    def test_the_rotate_packet_carries_the_destination(self):
        """A payload meaning 'end up here' is safe to apply twice; 'go one further' is not."""
        self.assertIn("var target = pile.Orientation + 1;", self.behavior)
        self.assertIn("BitConverter.GetBytes(target)", self.behavior)

    def test_only_the_client_picks_the_next_orientation(self):
        self.assertIn("if (world.Side != EnumAppSide.Client)", self.behavior)


class TestMixedRockTypes(unittest.TestCase):
    """A pile takes whatever stone you hand it — granite into a basalt pile stays granite.

    This falls out of one slot per stone: each slot keeps its own stack, renders its own mesh and
    drops what it holds. What would break it is somebody adding a "same rock type only" gate on
    the put path, the way vanilla ground storage compares against slot 0 before accepting an item.
    These are source guards rather than behavioural tests — the block entity needs a running world
    to construct — so they are deliberately narrow: they check that no such comparison crept in.
    """

    def setUp(self):
        self.source = (MOD / "src/Storage/BlockEntityRockPile.cs").read_text()

    def method_body(self, name):
        start = self.source.index(f"public bool {name}(")
        depth, i = 0, self.source.index("{", start)
        for j in range(i, len(self.source)):
            if self.source[j] == "{":
                depth += 1
            elif self.source[j] == "}":
                depth -= 1
                if depth == 0:
                    return self.source[i : j + 1]
        raise AssertionError(f"could not read the body of {name}")

    def test_putting_a_stone_does_not_compare_it_against_the_pile(self):
        body = self.method_body("TryPut")
        for gate in ("Equals(", "Satisfies(", "IsPileableStone(inventory", "Itemstack.Collectible =="):
            with self.subTest(gate=gate):
                self.assertNotIn(gate, body)

    def test_a_slot_takes_the_held_stone_as_it_is(self):
        """The stone that leaves your hand is the object the slot stores, so its rock type, and
        anything else on the stack, survives the trip."""
        body = self.method_body("TryPut")
        self.assertIn("var taken = hotbar.TakeOut(1);", body)
        self.assertIn("empty.Itemstack = taken;", body)

    def test_drops_come_from_the_slots_rather_than_one_template(self):
        """Breaking a mixed pile has to give back each stone as what it was."""
        self.assertIn("stacks.Add(slot.Itemstack!.Clone());", self.source)


if __name__ == "__main__":
    unittest.main()
