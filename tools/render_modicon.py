"""Draws mod/modicon.png: a cairn in silhouette.

Generated rather than hand-painted so the icon can be regenerated at any size, and so the shape
it shows is the same tapering profile the cairn layout actually builds — the ring counts and radii
come straight from rockpile_geometry.
"""

from __future__ import annotations

import argparse
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw

import rockpile_geometry as geo

SIZE = 512
BACKGROUND = (58, 62, 66)
GROUND = (44, 47, 50)

# Weathered granite, lit from the upper left.
STONE_LIGHT = (168, 165, 158)
STONE_MID = (134, 131, 125)
STONE_DARK = (99, 97, 93)


def stone_colour(rng, height):
    """Higher stones catch more light, so the cairn reads as a solid form rather than a pattern."""
    base = rng.choice([STONE_LIGHT, STONE_MID, STONE_DARK])
    lift = 0.10 + 0.30 * height
    return tuple(min(255, int(c * (0.82 + lift))) for c in base)


def draw(size):
    image = Image.new("RGB", (size, size), BACKGROUND)
    canvas = ImageDraw.Draw(image)
    canvas.rectangle([0, int(size * 0.86), size, size], fill=GROUND)

    rng = random.Random(geo.SEED)

    # Three segments of the real cairn profile, stacked into one column and squashed to fit.
    courses = []
    for segment in range(geo.CAIRN_SEGMENTS):
        for count, radius in geo.cairn_rings(segment):
            courses.append((count, radius))

    stone_w = size * 0.13
    stone_h = size * 0.052
    baseline = size * 0.88
    step = (baseline - size * 0.12) / len(courses)

    for layer, (count, radius) in enumerate(courses):
        y = baseline - layer * step
        # One row of the ring seen side on: spread its stones across the ring's diameter, with
        # alternate courses offset half a stone so the joints break instead of forming seams.
        stagger = 0.5 if layer % 2 else 0.0
        for i in range(count):
            t = (i + 0.5 + stagger) / (count + stagger)
            x = size * 0.5 + (t - 0.5) * 2 * radius * size * 1.55
            jitter = rng.uniform(-0.012, 0.012) * size
            box = [
                x - stone_w / 2 + jitter,
                y - stone_h,
                x + stone_w / 2 + jitter,
                y,
            ]
            canvas.rounded_rectangle(
                box,
                radius=stone_h * 0.42,
                fill=stone_colour(rng, layer / len(courses)),
            )

    return image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", default="mod/modicon.png")
    parser.add_argument("--size", type=int, default=SIZE)
    args = parser.parse_args()

    image = draw(args.size)
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    image.save(out)
    print(f"Wrote {out} ({args.size}x{args.size})")


if __name__ == "__main__":
    main()
