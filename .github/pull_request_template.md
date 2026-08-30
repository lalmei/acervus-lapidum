## What this changes

<!-- What a player would notice, or what a maintainer needs to know. -->

## Why

<!-- The problem behind the change. Link an issue with "Closes #123" if there is one. -->

## How it was checked

<!-- Tick what you ran; say what you did in game if the change is visible there. -->

- [ ] `make test` (geometry + compile)
- [ ] Tried it in game
- [ ] Existing piles still load and behave

## Release notes

- [ ] Labelled for the release draft — `feature`, `layout`, `fix`, `visual`, `compat`,
      `docs`, `build`, `ci`, `chore`, or `skip-changelog` to leave it out. The autolabeler
      guesses from the branch name, touched files, and the title's gitmoji; correct it here
      if it guessed wrong, because the label picks the category in the draft.
- [ ] Version bumped if this ships something players download —
      `make bump-patch-version` or `make bump-minor-version`, which moves
      `mod/modinfo.json`, `AcervusLapidumModMetadata.Version`, and the issue-template
      placeholders together. CI reads the version out of `modinfo.json` to name the package
      and pick which release draft to attach it to, so an unbumped merge lands on top of the
      previous version's draft.
