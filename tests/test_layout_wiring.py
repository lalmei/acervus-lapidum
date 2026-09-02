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
MODES = MOD / "src/Storage/RockPileLayoutModes.cs"
HOTKEY = MOD / "src/Storage/RockPileLayoutHotkey.cs"
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
    """The AssetLocation codes both pickers build, in order."""
    block = re.search(
        r"return new SkillItem\[\]\s*\{(.*?)\n *\};", MODES.read_text(), re.S
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
        self.modes = MODES.read_text()

    def test_the_pile_turns_to_an_orientation_rather_than_by_one(self):
        self.assertIn("public void TurnTo(int value", self.entity)
        self.assertNotIn("RotateBy", self.entity)
        self.assertNotIn("RotateBy", self.behavior)
        self.assertNotIn("RotateBy", self.modes)

    def test_the_rotate_packet_carries_the_destination(self):
        """A payload meaning 'end up here' is safe to apply twice; 'go one further' is not."""
        self.assertIn("var target = pile.Orientation + 1;", self.modes)
        self.assertIn("BitConverter.GetBytes(target)", self.modes)

    def test_only_the_client_picks_the_next_orientation(self):
        """Apply takes a client API, so the server has no path to a turn of its own."""
        self.assertIn(
            "public static bool Apply(ICoreClientAPI capi, BlockEntityRockPile pile, int index)",
            self.modes,
        )
        self.assertIn("if (world.Api is ICoreClientAPI capi)", self.behavior)
        self.assertNotIn("pile.TurnTo", self.behavior)


class TestEmptyHandedPickerReplacesCycling(unittest.TestCase):
    """F on a pile opens a picker, and offers the same things the tool mode picker does.

    It used to step to the next layout each press, so reaching the last one meant pressing past
    every layout you did not want, and turning a pile was only possible with a stone in
    hand. Both pickers now read the one list in RockPileLayoutModes, so a layout added there shows
    up in both at the same index.
    """

    def setUp(self):
        self.hotkey = HOTKEY.read_text()
        self.dialog = (MOD / "src/Storage/GuiDialogRockPileLayout.cs").read_text()
        self.behavior = BEHAVIOR.read_text()

    def test_the_hotkey_opens_the_dialog_rather_than_stepping_a_layout(self):
        self.assertIn("new GuiDialogRockPileLayout(capi, pile.Pos)", self.hotkey)
        self.assertNotIn("NextLayoutMode", self.hotkey)
        self.assertNotIn("NextLayoutMode", (MOD / "src/Storage/RockPileUtil.cs").read_text())

    def test_both_pickers_read_the_same_entries(self):
        self.assertIn("RockPileLayoutModes.GetOrCreate(capi)", self.dialog)
        self.assertIn("RockPileLayoutModes.GetOrCreate(capi)", self.behavior)
        self.assertNotIn("new SkillItem", self.behavior)

    def test_the_picker_names_a_layout_and_its_capacity_on_hover(self):
        """A count read off the pile, not a table: a cairn holds fewer the higher it goes."""
        self.assertIn("SlotCountFor", self.dialog)
        self.assertIn(
            "public int SlotCountFor(RockPileLayoutMode mode)",
            (MOD / "src/Storage/BlockEntityRockPile.cs").read_text(),
        )
        self.assertIn("rockpile-layout-capacity", self.dialog)
        self.assertIn("rockpile-layout-capacity", json.loads(LANG.read_text()))

    def test_the_dialog_can_turn_the_pile_too(self):
        """The turn entry is the last slot in that same grid, so it needs no key of its own."""
        self.assertIn("RockPileLayoutModes.RotateIndex", self.dialog)
        self.assertIn("RockPileLayoutModes.Apply(capi, pile, index)", self.dialog)


class TestChangesStayOnOnePile(unittest.TestCase):
    """Restyling or turning a pile must not reach into its neighbours.

    Both used to run through the whole vertical column, so changing the layout of a pile stacked
    on another silently rewrote the one underneath. Stacked piles inherit their layout from the
    stone that placed them, which is what makes a cairn come out a cairn all the way up, so the
    column walk bought nothing and cost the player any mixed column they wanted to build.
    """

    def setUp(self):
        self.source = (MOD / "src/Storage/BlockEntityRockPile.cs").read_text()

    def test_no_column_walk_survives(self):
        self.assertNotIn("ColumnFrom", self.source)
        self.assertNotIn("propagate", self.source)

    def test_layout_and_rotation_touch_only_their_own_position(self):
        """Both may mark themselves dirty; neither may address another pile's position."""
        for method in ("public bool SetLayoutMode(", "public void TurnTo("):
            start = self.source.index(method)
            end = self.source.index("\n    }\n", start)
            body = self.source[start:end]
            with self.subTest(method=method):
                self.assertIn("MarkBlockDirty(Pos)", body)
                self.assertNotIn("UpCopy", body)
                self.assertNotIn("DownCopy", body)
                self.assertNotIn("segment.", body)


class TestHoldToRepeatStaysPut(unittest.TestCase):
    """Holding the place button feeds one column, and never starts a pile somewhere else.

    The repeat used to re-run the whole placement path every tick, CreatePile included. So the
    moment a pile filled up and the cursor drifted onto the ground beside it — easy to do while
    still holding the button down — the next stone started a fresh pile on the floor next door.
    Starting a pile in a new spot is something a new click does.
    """

    def setUp(self):
        self.behavior = BEHAVIOR.read_text()

    def test_the_hold_records_the_column_it_started_in(self):
        start = self.behavior.index("public override void OnHeldInteractStart(")
        end = self.behavior.index("private const float RepeatSeconds", start)
        body = self.behavior[start:end]
        self.assertIn("SetInt(AnchorXAttr, blockSel.Position.X)", body)
        self.assertIn("SetInt(AnchorZAttr, blockSel.Position.Z)", body)

    def test_the_repeat_refuses_to_leave_that_column(self):
        start = self.behavior.index("public override bool OnHeldInteractStep(")
        end = self.behavior.index("public override void OnHeldInteractStop(", start)
        body = self.behavior[start:end]
        self.assertIn("AnchorXAttr", body)
        self.assertIn("AnchorZAttr", body)
        # The guard has to come before anything is placed.
        self.assertLess(body.index("AnchorXAttr"), body.index("TryInteract"))


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
        anything else on the stack, survives the trip — bar the tool-mode layout marker, which
        means nothing once the pile owns its own layout."""
        body = self.method_body("TryPut")
        self.assertIn("var taken = hotbar.TakeOut(1);", body)
        self.assertIn("empty.Itemstack = RockPileUtil.ClearHeldLayoutMode(taken);", body)

    def test_drops_come_from_the_slots_rather_than_one_template(self):
        """Breaking a mixed pile has to give back each stone as what it was."""
        self.assertIn(
            "stacks.Add(RockPileUtil.ClearHeldLayoutMode(slot.Itemstack!.Clone())!);",
            self.source,
        )

    def test_the_layout_choice_never_rides_on_a_stone(self):
        """A loose rock is a loose rock. Writing the picked layout onto the stack made a held
        stone unequal to a plain one, so it stopped merging in hotbars, on the ground and in
        chests. The preference belongs to the player; only the scrub touches a stack."""
        for name in ("src/Storage/RockPileUtil.cs", "src/Items/CollectibleBehaviorRockPileable.cs",
                     "src/Storage/BlockRockPile.cs"):
            source = (MOD / name).read_text()
            for line in source.splitlines():
                if "LayoutAttr" not in line or "RemoveAttribute" in line:
                    continue
                with self.subTest(file=name, line=line.strip()):
                    self.assertNotIn("stack.Attributes", line)
                    self.assertNotIn("Itemstack.Attributes", line)

    def test_stones_leave_a_pile_without_the_layout_marker(self):
        """A stone carrying rockPileLayout is not equal to a plain one, so it will not merge in a
        hotbar slot or on the ground. Every route out of a pile has to hand back plain stone."""
        declarations = {
            "ShedSurplus": "private void ShedSurplus(",
            "TryTake": "public bool TryTake(",
            "GetContentStacks": "public ItemStack[] GetContentStacks(",
        }
        for route, declaration in declarations.items():
            with self.subTest(route=route):
                start = self.source.index(declaration)
                end = self.source.index("\n    }", start)
                self.assertIn("ClearHeldLayoutMode", self.source[start:end])


if __name__ == "__main__":
    unittest.main()
