"""Vanilla stone piles must keep working while this mod is installed.

`BlockEntityGroundStorage` does not persist its storage properties. It looks them up from the
held stone's `GroundStorable` collectible behavior every time it loads, so removing that behavior
from stone does not just stop new vanilla piles being made — it blanks every pile already in the
world. A pile with no storage properties draws no mesh, refuses every interaction and can only be
broken to get the stone back.

None of that shows up at build time, and it only shows up in game in a world that already has
piles in it. So the rule is checked here instead: RockPileable goes *in front of* GroundStorable,
it never replaces it.
"""

from __future__ import annotations

import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MOD = ROOT / "mod"
MODSYSTEM = MOD / "src/AcervusLapidumModSystem.cs"
CONVERTER = MOD / "src/Storage/BlockEntityBehaviorRockPileConverter.cs"
COMMANDS = MOD / "src/Storage/RockPileCommands.cs"
CONFIG = MOD / "src/AcervusLapidumConfig.cs"
LANG = MOD / "assets/acervuslapidum/lang/en.json"


def code(path: Path) -> str:
    """The file with its comments stripped, so prose about a rule cannot satisfy the rule."""
    text = path.read_text()
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return "\n".join(
        line for line in text.splitlines() if not line.lstrip().startswith("//")
    )


class GroundStorableSurvives(unittest.TestCase):
    def test_mod_system_never_filters_the_behavior_out(self):
        body = code(MODSYSTEM)
        self.assertIn("CollectibleBehaviorRockPileable", body)
        for banned in ("is not CollectibleBehaviorGroundStorable", "Where(behavior"):
            self.assertNotIn(
                banned,
                body,
                "GroundStorable must stay on stone or existing vanilla piles go blank",
            )

    def test_rockpileable_is_ordered_first(self):
        body = code(MODSYSTEM)
        self.assertIn("CollectibleBehaviors[0] is CollectibleBehaviorRockPileable", body)
        self.assertIn("OrderBy", body)


class ConversionIsOptIn(unittest.TestCase):
    def test_converter_checks_the_config_before_touching_anything(self):
        self.assertIn("ConvertVanillaPilesOnLoad", code(CONVERTER))

    def test_config_defaults_to_leaving_the_world_alone(self):
        body = code(CONFIG)
        # An auto-property with no initialiser: bool defaults to false.
        self.assertRegex(body, r"bool ConvertVanillaPilesOnLoad \{ get; set; \}\s*$|"
                               r"bool ConvertVanillaPilesOnLoad \{ get; set; \}\n")
        self.assertNotIn("ConvertVanillaPilesOnLoad { get; set; } = true", body)


class UninstallPathExists(unittest.TestCase):
    """Removing the mod deletes every rockpile block, so reverting has to be reachable."""

    def test_both_directions_are_commands(self):
        body = code(COMMANDS)
        self.assertIn('Create("rockpile")', body)
        self.assertIn('BeginSubCommand("convert")', body)
        self.assertIn('BeginSubCommand("revert")', body)

    def test_command_strings_are_translated(self):
        lang = json.loads(LANG.read_text())
        used = set(re.findall(r'Lang\.Get\(\s*"acervuslapidum:([\w-]+)"', code(COMMANDS)))
        self.assertTrue(used, "expected the command to use lang keys")
        self.assertEqual(set(), used - lang.keys())


if __name__ == "__main__":
    unittest.main()
