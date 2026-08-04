# Sandlot Slugfest

A Backyard-Baseball-style arcade baseball game in Godot 4.7 (C#/Mono), with a 32-club league
spanning every real major-league market plus two expansion cities.

## Running it

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
dotnet build
~/Documents/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64 .
```

Godot needs `dotnet` on its `PATH` — launching it without the exports above fails with
`.NET: Failed to load hostfxr`.

## Controls

| Where | Key | Does |
| --- | --- | --- |
| At the plate | Arrows / WASD | Move the bat around the zone |
| | Space | Swing |
| | Shift + Space | Bunt |
| | Q / E | Hold runners / send runners |
| On the mound | 1 2 3 4 | Fastball, curveball, changeup, slider |
| | Arrows / WASD | Aim the pitch |
| | Space | Deal |
| In the field | 1 2 3 4 | Throw to first, second, third, home |
| Anywhere | Esc | Back out |

## The league

32 clubs, all original, placed in real major-league markets: the 30 current ones plus Montreal
and Nashville. Two leagues, two divisions, eight clubs each. Team names, colours and logos are
our own — no real club marks are used.

Rosters are generated from a seed, so a given league always produces the same 16 players per
club: five pitchers, eight position starters and three bench players, each with ratings on a
1–10 scale and sometimes a signature move (Fireball, Crazy Curve, Moon Shot, Turbo Legs,
Vacuum Glove, Cannon Arm, Wall Climber and friends).

## Screens

- **Play Ball** — pick the visitors, the home club, game length and who holds the controller.
- **League Office** — standings, hitting and pitching leaderboards, and a club's full stat sheet.
- **Trade Desk** — tag players on both sides and send an offer; the other club weighs talent,
  positional need and whether the deal leaves it able to field a team.
- **Browse the League** — an almanac of all 32 rosters with full ratings.

The season (rosters, stats, standings, completed trades) is saved to `user://season.json` and
reloaded at startup, so trades and statistics carry across sessions.

## How the simulation works

Play happens in field space measured in feet, home plate at the origin, `+Y` toward centre
field. `FieldGeometry` owns the ballpark; `PlaySimulation` runs a ball in play from contact
until the ball is dead.

- **Pitching** (`Pitching.cs`) — each pitch has a speed and break signature. The pitcher aims at
  a spot; command error scatters the ball around it. Break shapes the *path* to the crossing
  point, never the crossing point itself.
- **Batting** (`Batting.cs`) — a swing is scored on timing error and on how close the bat was to
  the ball in the plate plane. Squared-up contact leaves the bat near 26 degrees; swinging above
  the ball tops it, swinging under it lifts it. Early swings pull, late swings go the other way.
- **Ball flight** — projectile motion with quadratic drag. The drag coefficient is derived from a
  baseball's ~95 mph terminal velocity (`g / v_terminal²`), which is what keeps a well-struck
  ball near 400 feet instead of 600.
- **Fielding and baserunning** — fielders converge on the projected landing spot; runners compare
  their time to the next bag against how long the defence needs to get the ball there. Forced
  runners always go, everyone runs with two out, and non-forced runners hold on a ball in the air
  so they are not doubled off.

The play simulation runs on a **fixed 1/120 s timestep**, not the frame delta. Integrating ball
flight at a long frame's delta moves the ball tens of feet per step and fielders sail past
catchable balls.

## Verifying changes

Two development modes are built in. Both need the `dotnet` exports above.

```bash
# Play complete games with no window and print box scores plus balance diagnostics.
godot --headless -- --sim 30

# Capture screenshots of a scene (default: a CPU-vs-CPU game).
godot -- --shot /tmp/shots 1.5 8 [--scene res://Scenes/LeagueOffice.tscn] [--fast 12]
```

`--sim` is the balance harness: it drives the real rules engine, pitch factory, swing resolver
and field simulation, and reports per-game rates next to real major-league numbers. Current
output over 30 games (both clubs combined per game):

| | This game | Real baseball |
| --- | --- | --- |
| Runs | 6.6 | 8.6 |
| Hits | 18.0 | 17 |
| Home runs | 2.8 | 2.4 |
| Strikeouts | 16.9 | 16.5 |
| Walks | 5.4 | 6.4 |
| Pitches | 264 | 292 |
| Zone % | 49.0 | 49 |
| Swing % | 47.4 | 47 |
| Whiff / swing | 22.2% | 24% |
| Foul / swing | 36.8% | 38% |
| In play / swing | 41.0% | 38% |

Everything except run scoring sits close to the real thing; the league currently plays about
25% below major-league run scoring, which reads as a slightly pitcher-friendly environment.

## Layout

```
Scripts/
  Core/       rules, pitching, batting, ball-in-play simulation, CPU decisions, harnesses
  Data/       the 32 clubs, players, roster generation
  Season/     league state, trade valuation and execution, save/load
  Stats/      batting, pitching and team stat lines; the record book
  Gameplay/   the game scene, batting view, field view, scoreboard
  UI/         menus, team select, league office, trade desk, cartoon player renderer
Scenes/       one thin .tscn per screen; the scripts build their own children
```

All art is drawn procedurally — there are no image assets. `CartoonPlayer.cs` renders the kids
(oversized heads, chunky limbs, thick outlines, per-club uniforms), with appearance driven by
each player's `LookSeed` so a given kid always looks the same.
