"""The niche holds one item, and the stone machinery must never touch it.

The pile is one inventory slot per stone, and the niche borrows a slot on the end of that same
inventory. That is the cheap way to get persistence, rendering and networking for free — it is an
ItemSlot like any other — and it is also a trap: every walk that counts, fills, sheds or hands back
stones runs over the inventory, and any one of them that forgets to stop short will treat a torch as
a rock. Most of these are source guards, since the block entity cannot be built without a world.
"""

from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MOD = ROOT / "mod"
ENTITY = MOD / "src/Storage/BlockEntityRockPile.cs"
BLOCK = MOD / "src/Storage/BlockRockPile.cs"
UTIL = MOD / "src/Storage/RockPileUtil.cs"
LANG = MOD / "assets/acervuslapidum/lang/en.json"


def body_of(source: str, declaration: str) -> str:
    """The braces-balanced body of one method."""
    start = source.index(declaration)
    depth, opened = 0, source.index("{", start)
    for i in range(opened, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[opened:i + 1]
    raise AssertionError(f"could not read the body of {declaration}")


class TestTheNicheSlotIsReserved(unittest.TestCase):
    def setUp(self):
        self.entity = ENTITY.read_text()
        self.util = UTIL.read_text()

    def test_the_inventory_is_one_slot_bigger_than_the_stone_count(self):
        self.assertIn("public const int NicheSlotIndex = MaxSlots;", self.util)
        self.assertIn("public const int InventorySize = MaxSlots + 1;", self.util)
        self.assertIn("RockPileUtil.InventorySize, null, null", self.entity)

    def test_no_stone_walk_runs_to_the_end_of_the_inventory(self):
        """`inventory.Count` is the whole inventory, niche included. A stone walk wants MaxSlots.

        This is the one that bites: ShedSurplus walks from the top down looking for stones to hand
        back, so reaching the last slot would shed whatever is standing in the niche *first*, before
        any surplus stone.
        """
        for declaration in (
            "private void ShedSurplus()",
            "private ItemSlot? FirstEmptySlot()",
            "private ItemSlot? LastFilledSlot()",
            "public ItemStack[] GetContentStacks()",
            "public void PopulateFrom(",
        ):
            with self.subTest(method=declaration):
                body = body_of(self.entity, declaration)
                self.assertNotIn("inventory.Count", body)
                self.assertTrue(
                    "StoneSlots" in body or "RockPileUtil.MaxSlots" in body,
                    "walks the inventory without stopping short of the niche",
                )

    def test_the_stone_count_counts_stones(self):
        self.assertIn("foreach (var slot in StoneSlots)", body_of(self.entity, "public int StoneCount"))

    def test_an_item_in_the_niche_does_not_keep_a_stoneless_pile_standing(self):
        """`inventory.Empty` is false while the niche holds something, which would leave a pile of
        no stones standing there for ever."""
        body = body_of(self.entity, "public bool OnPlayerInteract(")
        self.assertIn("StoneCount == 0", body)
        self.assertNotIn("inventory.Empty", body)


class TestTheNicheGesture(unittest.TestCase):
    """Sneak + right-click, and only on a pile that has a pocket.

    It has to thread between three gestures that are already taken: plain right-click takes a stone,
    sneak + Ctrl adds one, and sneak alone with a stone in hand is how knapping starts — which is the
    reason adding needs Ctrl at all. So the niche answers sneak + right-click for anything that is
    not a stone, and for an empty hand, which takes back.
    """

    def setUp(self):
        self.entity = ENTITY.read_text()
        self.body = body_of(self.entity, "public bool OnPlayerInteract(")

    def test_the_gesture_is_sneak_without_ctrl_and_not_holding_a_stone(self):
        self.assertIn("sneaking && HasNiche && !adding && !RockPileUtil.IsPileableStone", self.body)

    def test_an_empty_hand_takes_and_a_full_one_puts(self):
        self.assertIn("hotbar.Empty ? TakeFromNiche(byPlayer) : PutInNiche(byPlayer)", self.body)

    def test_taking_a_stone_still_answers_a_plain_right_click(self):
        self.assertLess(self.body.index("TakeFromNiche"), self.body.index("TryTake(byPlayer)"))
        self.assertIn("else if (!sneaking)", self.body)

    def test_the_niche_holds_one_item_rather_than_a_stack(self):
        """What it holds is drawn standing on the shelf, so a count would be a lie."""
        self.assertIn("hotbar.TakeOut(1)", body_of(self.entity, "public bool PutInNiche("))


class TestTheNicheEmptiesWhenItStopsExisting(unittest.TestCase):
    """Restyle a niche cairn into anything else and the pocket is gone.

    Same rule the stones follow: a pile never holds something it cannot show you. An item left in a
    slot nothing draws is exactly the quiet lie ShedSurplus exists to prevent.
    """

    def setUp(self):
        self.entity = ENTITY.read_text()

    def test_the_niche_is_shed_wherever_surplus_stones_are(self):
        shed_surplus = self.entity.count("ShedSurplus();")
        shed_niche = self.entity.count("ShedNiche();")
        self.assertEqual(shed_niche, shed_surplus, "a layout change that sheds stones must shed the niche")

    def test_shedding_only_happens_when_the_layout_has_no_niche(self):
        body = body_of(self.entity, "private void ShedNiche()")
        self.assertIn("|| HasNiche", body)
        self.assertIn("EnumAppSide.Server", body)


class TestTheNicheIsSeenAndLit(unittest.TestCase):
    def setUp(self):
        self.entity = ENTITY.read_text()
        self.block = BLOCK.read_text()

    def test_the_item_is_drawn_in_the_pocket_and_turns_with_the_pile(self):
        body = body_of(self.entity, "private static float[] NicheMatrix(")
        self.assertIn("RotateYDeg(yaw)", body)
        self.assertIn("Scale(NicheScale", body)
        self.assertIn("matrices[RockPileUtil.NicheSlotIndex] = NicheMatrix(yaw);", self.entity)

    def test_the_pile_emits_what_the_niche_holds(self):
        body = body_of(self.block, "public override byte[] GetLightHsv(")
        self.assertIn("pile.NicheStack is { } held", body)
        self.assertIn("lit.GetLightHsv(blockAccessor, pos, held)", body)

    def test_the_world_is_asked_to_light_the_pile_again_when_the_niche_changes(self):
        """GetLightHsv is only consulted when something says the block changed."""
        body = body_of(self.entity, "private void OnNicheChanged()")
        self.assertIn("MarkBlockModified(Pos)", body)
        for method in ("public bool PutInNiche(", "public bool TakeFromNiche(", "private void ShedNiche()"):
            with self.subTest(method=method):
                self.assertIn("OnNicheChanged();", body_of(self.entity, method))

    def test_breaking_the_pile_gives_the_niche_item_back(self):
        body = body_of(self.block, "public override ItemStack[] GetDrops(")
        self.assertIn("pile.NicheStack is { } held", body)
        self.assertIn("held.Clone()", body)

    def test_the_revert_command_is_not_handed_a_torch(self):
        """GetContentStacks feeds the vanilla stone pile that `/rockpile revert` builds, and a
        vanilla pile has nowhere to put anything but stone — so the niche is added at the drop
        site instead."""
        self.assertIn("pile.GetContentStacks()", (MOD / "src/Storage/RockPileMigration.cs").read_text())
        self.assertNotIn("NicheStack", (MOD / "src/Storage/RockPileMigration.cs").read_text())

    def test_the_niche_has_words_for_every_state_it_can_be_in(self):
        lang = json.loads(LANG.read_text())
        for key in ("blockhelp-rockpile-niche-put", "blockhelp-rockpile-niche-take",
                    "blockinfo-rockpile-niche", "blockinfo-rockpile-niche-empty"):
            with self.subTest(key=key):
                self.assertIn(key, lang)


class TestOnlyTheNicheCairnHasOne(unittest.TestCase):
    def test_the_layout_decides_it_in_one_place(self):
        util = UTIL.read_text()
        self.assertIn("public static bool HasNiche(RockPileLayoutMode mode)", util)
        self.assertIn("mode == RockPileLayoutMode.NicheCairn", util)

        entity = ENTITY.read_text()
        self.assertIn("public bool HasNiche => RockPileUtil.HasNiche(layoutMode);", entity)

        # Nothing else may decide for itself what counts as a niche layout.
        for source in (ENTITY, BLOCK):
            with self.subTest(source=source.name):
                text = source.read_text()
                hits = re.findall(r"==\s*RockPileLayoutMode\.NicheCairn", text)
                self.assertEqual(hits, [], "checks for the niche layout by hand")


if __name__ == "__main__":
    unittest.main()
