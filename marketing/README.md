# Ninety Feet Capture Assets

These 1280×720 PNGs are unedited captures from the game renderer. They are suitable as a starting
point for release pages, playtest recruitment and store submissions; check each store's current
dimension and device-frame rules before uploading.

- `gameplay/`: batting, ball-in-play feedback and a completed fielding result;
- `moments/`: the persistent challenge list and first-clear rewards;
- `settings/`: the scrollable settings and visible controller focus;
- `exhibition/`: restored matchup, innings, controls and fielding selections.

Recreate a capture with the repository's Godot Mono build:

```bash
godot --path . -- --shot marketing/screenshots/gameplay 1.5 3 --bat --home 14 --away 21
godot --path . -- --shot marketing/screenshots/moments 1 1 \
  --scene res://Scenes/Moments.tscn --textfit
```

`--textfit` performs one layout report before each image. Settings uses a custom manually clipped
scroller, so the checker also sees intentionally off-screen rows; inspect its visible PNG alongside
the report. `--scroll N`, `--controller-nav N` and `--touch` exercise scrolled, controller-focused
and touch-converted states before capture. Never capture private IP addresses or third-party roster
content.
