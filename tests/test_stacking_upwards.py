"""A click on a finished course must carry on building the column above it.

Every course of a cairn above the first is narrower than the one below, so the finished base
keeps a rim sticking out however high the cairn gets — and that rim, not the small segment on
top, is what the crosshair lands on from anywhere but directly overhead. While placement demanded
that the click hit the working course itself, a cairn got harder to aim at with every course and
clicking the obvious wide target did nothing at all.

So `TryInteract` walks up from whatever full pile was clicked: the stone goes into the first
course above with room in it, or starts a new one on top of the last finished course. The walk
only ever passes *through* courses that are already full, which is what keeps the drystone rule
intact — you still cannot stack on top of a course with gaps in it.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PILEABLE = ROOT / "mod/src/Items/CollectibleBehaviorRockPileable.cs"


def code(path: Path) -> str:
    """The file with its comments stripped, so prose about a rule cannot satisfy the rule."""
    text = path.read_text()
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return "\n".join(
        line for line in text.splitlines() if not line.lstrip().startswith("//")
    )


def method(body: str, name: str) -> str:
    """The text from a method's signature to the start of the next member declaration."""
    start = body.index(name)
    rest = body[start:]
    end = re.search(r"\n    (?:public|private|protected|internal)\b", rest[1:])
    return rest[: end.start() + 1] if end else rest


class ClicksCarryUpTheColumn(unittest.TestCase):
    def setUp(self):
        self.body = code(PILEABLE)

    def test_a_full_pile_hands_the_click_upwards(self):
        interact = method(self.body, "static bool TryInteract")
        self.assertIn("FindOpenPileAbove(world, pile, out var topFull)", interact)

    def test_a_full_pile_no_longer_demands_the_click_hit_its_top_face(self):
        interact = method(self.body, "static bool TryInteract")
        self.assertNotIn(
            "!pile.IsFull || blockSel.Face != BlockFacing.UP",
            interact,
            "a finished course must route upward from any face, not only from directly overhead",
        )

    def test_the_new_course_starts_on_the_last_finished_one(self):
        interact = method(self.body, "static bool TryInteract")
        self.assertIn("Position = topFull", interact)
        self.assertIn("Face = BlockFacing.UP", interact)

    def test_a_course_with_room_still_takes_the_stone_itself(self):
        interact = method(self.body, "static bool TryInteract")
        self.assertIn("if (!pile.IsFull)", interact)


class TheWalkRespectsTheDrystoneRule(unittest.TestCase):
    def setUp(self):
        self.walk = method(code(PILEABLE), "static BlockEntityRockPile? FindOpenPileAbove")

    def test_it_stops_at_the_first_course_with_room(self):
        self.assertIn("if (!above.IsFull)", self.walk)
        self.assertIn("return above;", self.walk)

    def test_it_only_climbs_through_rock_piles(self):
        self.assertIn("is BlockEntityRockPile above", self.walk)

    def test_it_cannot_run_off_the_top_of_the_world(self):
        self.assertIn("topFull.Y + 1 < world.BlockAccessor.MapSizeY", self.walk)


class VanillaPilesAreStillLeftAlone(unittest.TestCase):
    def test_ground_storage_is_never_built_on(self):
        interact = method(code(PILEABLE), "static bool TryInteract")
        self.assertIn("FindTargetGroundStorage(world, blockSel) is not null", interact)


if __name__ == "__main__":
    unittest.main()
