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
    """Putting is a plain right-click. Sneak would place the torch instead of reaching the pile.

    This is the one that has to be written down, because the obvious gesture is the wrong one.
    Sneak + right-click is what the game itself uses to step over a block's interaction and place
    what you are holding — it is how you build against a chest rather than opening it. A torch is a
    block carried as an item, so sneak + right-click placed it in the world and never arrived here,
    while a stick, which cannot be placed, fell through and worked. The niche took sticks but not
    torches, which is exactly backwards from what it is for.

    Without sneak, the block's interaction wins over placement, so a plain click always arrives.
    Taking still uses sneak, because an empty hand has nothing to place.
    """

    def setUp(self):
        self.entity = ENTITY.read_text()
        self.body = body_of(self.entity, "public bool OnPlayerInteract(")

    def test_putting_does_not_ask_for_sneak(self):
        put = self.body.index("ok = PutInNiche(byPlayer);")
        branch = self.body.rindex("else if", 0, put)
        condition = self.body[branch:put]
        self.assertNotIn("sneaking &&", condition)
        self.assertIn("HasNiche", condition)
        self.assertIn("!RockPileUtil.IsPileableStone", condition)

    def test_taking_asks_for_sneak_and_an_empty_hand(self):
        take = self.body.index("ok = TakeFromNiche(byPlayer);")
        branch = self.body.rindex("else if", 0, take)
        condition = self.body[branch:take]
        self.assertIn("sneaking", condition)
        self.assertIn("hotbar.Empty", condition)

    def test_putting_only_answers_while_the_pocket_is_empty(self):
        """Otherwise a plain click on a full niche would stop taking stones from the pile."""
        put = self.body.index("ok = PutInNiche(byPlayer);")
        condition = self.body[self.body.rindex("else if", 0, put):put]
        self.assertIn("NicheSlot.Empty", condition)

    def test_taking_a_stone_still_answers_a_plain_right_click(self):
        self.assertLess(self.body.index("TakeFromNiche"), self.body.index("TryTake(byPlayer)"))
        self.assertIn("else if (!sneaking)", self.body)

    def test_adding_a_stone_still_comes_first(self):
        """Sneak + Ctrl with a stone must never be read as a niche gesture."""
        self.assertLess(self.body.index("TryPut(byPlayer)"), self.body.index("PutInNiche(byPlayer)"))

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
        self.util = UTIL.read_text()

    def test_the_item_is_drawn_in_the_pocket_and_turns_with_the_pile(self):
        body = body_of(self.entity, "private float[] NicheMatrix(")
        self.assertIn("RotateYDeg(yaw)", body)
        self.assertIn("matrices[RockPileUtil.NicheSlotIndex] = NicheMatrix(yaw);", self.entity)

    def test_the_niche_corrects_for_the_item_s_own_ground_transform(self):
        """Most items declare none and some declare one, so without this they sit inconsistently.

        Vanilla's book steps 0.12 blocks sideways and rolls 90 degrees; its shattered clay scales to
        0.38 and sinks 0.3 down. Both are authored for lying in a ground-storage pile, and both leave
        a pocket that is ten pixels across. Scale is divided out and translation cancelled; rotation
        is kept, because that is the part that makes an item stand up on a surface.
        """
        body = body_of(self.entity, "private float[] NicheMatrix(")
        self.assertIn("Attributes?[AttributeTransformCode]", body)
        self.assertIn("NicheScale / (ownScale", body)
        self.assertIn("Translate(-undo.X, -undo.Y, -undo.Z)", body)

        # Rotation is deliberately left alone; undoing it would lay the book on its face.
        self.assertNotIn("own?.Rotation", body)

    def test_the_pile_emits_what_the_niche_holds(self):
        body = body_of(self.block, "public override byte[] GetLightHsv(")
        self.assertIn("RockPileUtil.NicheLightHsv(blockAccessor, pile.NicheStack)", body)

    def test_the_held_thing_is_asked_about_itself_and_not_about_this_position(self):
        """Two ways to ask a torch how brightly it burns, and only one of them works here.

        A block asked about a *position* answers for the block entity standing at it: BlockLantern
        looks for a BELantern there, BlockGroundStorage for a BEGroundStorage. What stands at our
        position is a rock pile, so asking about it is asking the lantern to describe somebody else.
        Vanilla passes null, which sends it down the branch that reads the stack it was handed.

        The collectible rather than the block, for the same reason vanilla does: GetLightHsv is
        declared on CollectibleObject, and reaching through `stack.Block` drops every light-emitting
        item on the floor.
        """
        body = body_of(self.util, "public static byte[]? NicheLightHsv(")
        self.assertIn("stack?.Collectible?.GetLightHsv(accessor, null, stack)", body)
        self.assertNotIn(".Block", body)

    def test_the_world_is_asked_to_light_the_pile_again_when_the_niche_changes(self):
        """The lighting task only re-reads a position's emission when the *block* there changes.

        Putting a torch in a pocket changes the block entity and nothing else, so MarkBlockDirty
        (redraw) and MarkBlockModified (resend) both leave the pile dark. ExchangeBlock swaps the
        block for itself without disturbing the block entity, and that swap is the change the
        lighting task is waiting for — the same nudge BlockEntityGroundStorage.LightUpdate gives.
        """
        body = body_of(self.entity, "private void UpdateNicheLight()")
        self.assertIn("ExchangeBlock(Block.Id, Pos)", body)

        # Re-reading a position that no longer emits does not undo light already spread from it.
        self.assertIn("RemoveBlockLight", body)

        self.assertIn("UpdateNicheLight();", body_of(self.entity, "private void OnNicheChanged()"))
        for method in ("public bool PutInNiche(", "public bool TakeFromNiche(", "private void ShedNiche()"):
            with self.subTest(method=method):
                self.assertIn("OnNicheChanged();", body_of(self.entity, method))

    def test_a_pile_that_is_about_to_go_hands_its_light_back(self):
        """RemoveBlockLight has to be called while the block entity is still there — once the block
        is gone there is nothing left to ask what it had been giving off."""
        self.assertIn("ClearNicheLight();", body_of(self.entity, "public bool OnPlayerInteract("))
        self.assertIn("ClearNicheLight()", body_of(self.block, "public override void OnBlockBroken("))

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


class TestItemsArePosedAsThingsOnAShelf(unittest.TestCase):
    """A niche is a shelf, so the pile poses stacks by the shelf transform.

    One named transform decides how every stack in the pile is posed before its slot matrix runs,
    stones and the niche's contents alike, so it has to suit both. Stone declares no shelf transform,
    so the lookup finds nothing and its mesh arrives exactly as authored — which is what the layout
    generator assumes. A book declares both, and they say different things: its ground storage
    transform lays it flat and steps it sideways (found books) or leaves it standing at 35 degrees
    (the ones written in creative), while its shelf transform is empty, meaning upright and square
    on. The pocket wants the second.
    """

    def setUp(self):
        self.entity = ENTITY.read_text()

    def test_the_pile_poses_stacks_by_the_shelf_transform(self):
        self.assertIn('public override string AttributeTransformCode => "onshelfTransform";',
                      self.entity)

    def test_the_identity_patch_is_gone_rather_than_left_lying_around(self):
        """It existed only to make the old lookup a no-op. Asking for a code stone does not declare
        is the same no-op with nothing to keep in step — and a dead patch with a load-bearing
        comment on it is a trap."""
        patch = json.loads((MOD / "assets/acervuslapidum/patches/stone-rockpileable.json").read_text())
        self.assertNotIn("/attributes/groundStorageTransform", [e.get("path") for e in patch])

        # And the generator, which depends on the stone mesh arriving as authored, says why it does.
        preamble = (ROOT / "tools/rockpile_geometry.py").read_text().split('"""', 2)[1]
        self.assertIn("onshelfTransform", preamble)

    def test_an_empty_transform_does_not_shrink_the_item_to_nothing(self):
        """A book's shelf transform is `{}`, whose fields come back as zeroes — a scale of zero
        among them, which would render nothing at all."""
        body = body_of(self.entity, "private float[] NicheMatrix(")
        self.assertIn("EnsureDefaultValues()", body)
        self.assertIn("ownScale > 0.01f", body)


class TestOnlyTheNicheCairnHasOne(unittest.TestCase):
    def test_the_layout_decides_it_in_one_place(self):
        util = UTIL.read_text()
        self.assertIn("public static bool HasNiche(RockPileLayoutMode mode, int segment)", util)
        self.assertIn("mode == RockPileLayoutMode.NicheCairn", util)

        entity = ENTITY.read_text()
        self.assertIn("public bool HasNiche => RockPileUtil.HasNiche(layoutMode, segmentIndex);", entity)

    def test_the_pocket_is_a_course_up_the_column_and_not_the_footing(self):
        """A pocket at ground level is a pocket you kneel to, so the niche goes in the middle of
        the cairn: plain footing under it, spire over it, socket between.

        Both sides have to agree on which course that is — the generator cuts the stones out of one
        profile and the block entity decides which height wears it — so the two constants are
        checked against each other rather than trusted to stay in step.
        """
        util = UTIL.read_text()
        self.assertIn("public const int NicheSegment = 1;", util)
        self.assertIn("NICHE_SEGMENT = 1", (ROOT / "tools/rockpile_geometry.py").read_text())

        # The footing and the spire of a niche cairn are the plain cairn profiles.
        forMode = body_of(util, "public RockPileSlotTransform[] ForMode(")
        self.assertIn("RockPileUtil.NicheSegment => NicheCairn", forMode)
        self.assertIn("0 => Cairn0", forMode)

    def test_a_heap_will_not_carry_a_pile(self):
        """Stone tipped on the ground is not laid on anything, so nothing may be laid on it."""
        util = UTIL.read_text()
        self.assertIn("CanBearLoad(RockPileLayoutMode mode) => mode != RockPileLayoutMode.Heap", util)
        self.assertIn(
            "RockPileUtil.CanBearLoad(pile.LayoutMode)",
            body_of(BLOCK.read_text(), "public override bool CanAttachBlockAt("),
        )

        # Nothing else may decide for itself what counts as a niche layout.
        for source in (ENTITY, BLOCK):
            with self.subTest(source=source.name):
                text = source.read_text()
                hits = re.findall(r"==\s*RockPileLayoutMode\.NicheCairn", text)
                self.assertEqual(hits, [], "checks for the niche layout by hand")


if __name__ == "__main__":
    unittest.main()
