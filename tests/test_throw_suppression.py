"""Placing stones must never end with one being thrown.

Stones carry vanilla's `CollectibleBehaviorThrowable`. Its `OnHeldInteractStop` never asks
whether the player was aiming: any release past its 0.35s windup throws a stone unless the
entity attribute `aimingCancel` is set to 1. Feeding a pile by holding the button is a release
well past 0.35s, so every hold ended with the stone still in hand being lobbed away.

Two things keep that from happening, and both are checked here: our stop handler returns
`PreventSubsequent` so the behaviors behind ours never run, and it sets `aimingCancel` so a
Throwable that somehow ends up in front of ours bails out on its own. Vanilla's ItemStone sets
the same flag after its own stone placement, for the same reason.
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


class ReleaseNeverThrows(unittest.TestCase):
    def setUp(self):
        self.body = code(PILEABLE)

    def test_stop_aiming_cancels_the_pending_throw(self):
        self.assertIn(
            'SetInt("aimingCancel", 1)',
            method(self.body, "private static void StopAiming"),
            "Throwable reads aimingCancel and nothing else; clearing aiming alone still throws",
        )

    def test_a_pile_hold_is_marked_so_its_release_can_be_recognised(self):
        self.assertIn("HoldAttr, 1", method(self.body, "public override void OnHeldInteractStart"))

    def test_stop_prevents_the_behaviors_behind_this_one(self):
        stop = method(self.body, "public override void OnHeldInteractStop")
        self.assertIn("GetInt(HoldAttr)", stop)
        self.assertIn("StopAiming(byEntity)", stop)
        self.assertIn("handling = EnumHandling.PreventSubsequent", stop)

    def test_stop_and_cancel_clear_the_hold_marker(self):
        for name in (
            "public override void OnHeldInteractStop",
            "public override bool OnHeldInteractCancel",
        ):
            self.assertIn("HoldAttr, 0", method(self.body, name), name)


if __name__ == "__main__":
    unittest.main()
