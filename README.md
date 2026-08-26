# Acervus Lapidum

![Acervus Lapidum mod icon](mod/modicon.png)

Drop a stone on the ground in Vintage Story and you get a pile. Drop a second one and you get…
the same pile. Nothing changes until the third.

That is not a bug — vanilla's stone pile draws a fixed 32-rock model scaled to a 64-stone stack, so
two stones share one visible rock. It also means a half-empty pile looks full, a pile can never be
tidied, and nothing can ever go on top of one.

Acervus Lapidum ("heap of stones") makes the pile tell the truth. **One stone in your hand is one
stone in the pile is one rock you can see.** Then it lets you do something with them.

## What you get

- **Honest piles.** Every stone you put down is rendered. Take one back and one rock disappears.
  Mixed rock types show as what they are — a granite in a pile of basalt looks like a granite.
- **Cairns.** Fill a pile and it will carry another on top. Keep going and the column narrows into
  a proper waymarker, course by course, the way you would actually build one.
- **Low walls.** A wall layout lays the same stones in two staggered courses. Put a few in a row
  and they line up into continuous drystone rather than a row of separate heaps.
- **Five layouts** — heap, neat course, cairn, low wall, scattered — switched with **F** while
  looking at a pile, or from the tool-mode picker with a stone in hand.
- **Your old piles still work.** Existing vanilla stone piles convert themselves when their chunk
  loads. A full 64-stone one becomes a two-segment cairn, because that is what it always was.
- **Other mods' rocks pile too.** Anything whose item code starts `stone-` is picked up, so the
  rock types from Geology Addons and friends work without a compatibility patch each.
- **Knapping is untouched.** Piling uses Ctrl as well as sneak, so the sneak + right-click that
  starts a knapping surface still belongs to vanilla.

Requires **Vintage Story 1.22.x**.

## How a pile works

**Sneak + Ctrl + right-click** with stones in hand to place or add — hold the button down to keep
feeding the pile a stone at a time. Plain **right-click** takes one back, **Ctrl + right-click**
takes several. **F** with empty hands cycles the layout.

Ctrl is not there to be awkward. Sneak + right-click on its own is how you lay the first stone for
**knapping**, and vanilla keeps its own stone piles out of the way by asking for Ctrl too
(`ctrlKey: true` on stone's ground-storage properties). An earlier version of this mod claimed
sneak + right-click and made every hard stone unknappable.

A pile holds **32 stones** — that is what one block fits at vanilla's own visual density. The 33rd
has nowhere to go but the block above, and that is the whole cairn mechanic: a pile only becomes
solid on top once it is full, so you finish a course before you start the next.

The upper courses of a cairn hold **fewer** stones — 25, then 20. This is not an arbitrary nerf. A
ring of radius `r` needs about `2 pi r / 0.3125` stones to close, so a narrower course simply takes
fewer, and every cairn ring is sized from its own radius rather than hand-picked. Each segment
spends all eight of a block's two-pixel layers, so the segment above lands exactly on top of it
with no daylight in between. Changing a full heap into a narrow cairn course is refused instead of
quietly hiding the surplus.

## Where the geometry comes from

The heap layout is not hand-authored. It is vanilla's own `item/stone-pile.json`, converted.

Two facts make that exact. The stone item is a single 5x2x4 cube centred on the block with its
rotation origin at the bottom-centre; and every cube in vanilla's pile shape rotates about *its*
own bottom-centre too — which is precisely the pivot the block entity's render chain uses. So the
32 cubes map onto 32 slot poses with no fudging, and the layer heights fall out at 0, 2, 4, 6, 8,
10.3 and 12.4 pixels.

`tools/rockpile_geometry.py` does that conversion and generates the other four layouts, writing
`mod/assets/acervuslapidum/config/rockpile-layout.json`. It is seeded, so regenerating on an
unchanged install is a no-op diff — there is a test for that. Five of vanilla's cubes are drawn as
re-proportioned boxes rather than rotated ones, and the tool recovers the rotation those
dimensions imply before folding in the cube's own.

## Building

```bash
make test
```

Two suites: the Python geometry that writes the layout config, and the C# the game runs. Neither
needs a world or a running game.

```bash
make install
```

Builds, zips, and drops the result in your Mods folder. `make deploy` does the same after bumping
the patch version; `make run` launches the game; `make deploy-run` does both.

```bash
make assets
```

Regenerates the layout config from the game's shapes. Only this target reads the install —
everything else builds from the committed config, so a clean checkout works offline.

## Related

Sibling to [Liber Terra](https://github.com/lalmei/liber-terra), whose book piles solve the same
problem for books and whose architecture this borrows wholesale.

If you want crafted, static cairn blocks with lantern mounts and fence connections rather than
ones you build stone by stone, look at
[Wilderlands Waymarkers](https://mods.vintagestory.at/show/mod/19736) — it is doing a different
and complementary thing.
