# Ninety Feet

Arcade baseball on an honest simulation. Thirty-two clubs, a real front office, and a race to
ninety feet.

It looks like a cartoon and it plays like one — big heads, chunky limbs, signature moves — but
underneath, every rate in the game is measured against the 2024 major-league season and held there.
The two halves are the point: a game that feels like Backyard Baseball and keeps books like OOTP.

Godot 4.7.1 (C#/Mono, .NET 8). No image assets — every player, ballpark and menu is drawn
procedurally, and every sound but the name callouts is synthesised.

---

## Running it

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet build SandlotSlugfest.csproj
godot471cs --path .
```

Godot needs `dotnet` on its `PATH`; without the exports it fails with
`.NET: Failed to load hostfxr`. The assembly is still named `SandlotSlugfest` — the project was
renamed, the namespace was not.

Renaming the game moved `user://` with it, because Godot derives that path from `config/name`.
Saves live in `~/.local/share/godot/app_userdata/Ninety Feet/`.

---

## What is in it

**Season** — a full schedule with a calendar, weather, gate receipts and standings. Play your
club's games or simulate them.

**Dynasty** — the same, carried across years: development, ageing, retirement, the draft, free
agency, arbitration, waivers, awards, single-season records and a Hall of Fame.

**The Collection** — packs, a market, and a club built out of cards you own. Cards can be spent to
sign a player into your actual season, and he *transfers* rather than being copied: there is one of
everybody in this league and that stays true.

**Online** — two machines, one ballgame, by decision exchange over a shared seed rather than state
replication. Both peers build the identical league and trade only what each player decides; the
host is a sequencer, not the simulation.

**Exhibition** — any two clubs, any length.

---

## The league

Thirty-two clubs in real major-league markets — the thirty current ones plus Montreal and
Nashville. Names, colours and marks are all original.

Rosters are 26 men with 13-man staffs: five starters, a closer, two setup men, three in middle
relief and two long men, each with a role the bullpen logic actually respects. A few clubs open
carrying up to 29, because several written players can land on the same club and none of them is
cut to make room. There are 1,152 handwritten players, 512 of whom are placed when a league opens;
the rest arrive through the draft and free agency as the written share decays and generated players
take over — from 59% of the league in year one to about 44% by year four.

Every player has a repertoire of two to four pitches out of eight, chosen by what kind of pitcher
he is — a power arm lives off a fastball and a slider, a sinkerballer wants ground balls, a crafty
veteran survives on a cutter and a changeup. About a third of hitters bat left and one in ten
switches, which matters because the platoon is modelled (see below).

A three-rung farm system — Triple-A, Double-A, High-A — with its own rosters, its own seasons,
its own standings, and scouting that only tells you what your scouts actually know. You can play
or watch any of your affiliates' games.

---

## Controls

| Where | Key | Does |
| --- | --- | --- |
| At the plate | Mouse | Aim the hitting reticle |
| | Left click / Space | Normal swing |
| | F | Power swing — smaller barrel, more damage |
| | C | Contact swing — bigger barrel, less power |
| | B | Bunt |
| | WASD / arrows | Aim without a mouse |
| | Shift / Tab | Spend the signature move |
| | R | Challenge the call |
| Managing at the plate | G | Send the runner |
| | H | Pinch hit (before the first pitch only) |
| | Q / E | Hold runners / send runners |
| On the mound | 1 2 3 4 | This pitcher's own repertoire, in order |
| | Mouse | Aim the pitch |
| | Left click / Space | Deal |
| | P | Go to the pen — the manager walks out |
| | V | Mound visit. Five a game; the sixth has to be a change |
| | I | Intentional walk |
| In the field | 1 2 3 4 | Throw to first, second, third, home |
| Anywhere | Esc | Back out · M mute · N commentary · `-`/`=` volume |

---

## How the simulation works

Play happens in field space measured in feet, home plate at the origin, `+Y` toward centre field.
`FieldGeometry` owns the ballpark; `PlaySimulation` runs a ball in play from contact until it is
dead. The play simulation steps at a **fixed 1/120 s**, never the frame delta — integrating flight
at a long frame's delta moves the ball tens of feet per step and fielders sail past catchable
balls.

- **Pitching** (`Pitching.cs`) — each type has its own speed and break signature, and sinkers and
  cutters break opposite ways off a left-hander and a right-hander. Command error scatters the ball
  around the target. Break shapes the *path* to the crossing point, never the crossing point
  itself.
- **Batting** (`Batting.cs`) — a swing is scored on timing error and on how close the bat was to
  the ball in the plate plane, with horizontal misses forgiven because a bat is long and thin.
  Whether you make contact is forgiving; how well you struck it is a much sharper question, and
  that second number is what drives exit velocity.
- **The platoon** (`Platoon.cs`) — a hitter facing the opposite hand sees the ball longer and the
  breaking stuff moves toward him rather than away. It is applied to the bat and to the read, not
  bolted onto the result, and it is deliberately asymmetric: left-handers suffer more against
  left-handed pitching than right-handers do against right-handed.
- **Ball flight** — projectile motion with quadratic drag, the coefficient derived from a
  baseball's ~95 mph terminal velocity (`g / v_terminal²`). That is what keeps a well-struck ball
  near 400 feet instead of 600. Wind acts above six feet.
- **Fielding and baserunning** — fielders cover the bags they are actually responsible for and
  converge on the projected landing spot; runners compare their time to the next bag against how
  long the defence needs to get the ball there.

Every random draw comes from one seeded xorshift generator (`Rng`), which is what makes a league
reproducible and what makes online play possible at all.

---

## Where a season's money goes

Contracts with real service time — club control, arbitration at three years, free agency at six.
Budgets scale with market size and with what the club drew last year. A luxury tax at 214,000
whose rate climbs the longer you stay over it, settled before the winter market so a club that
spent shops with less.

A coaching staff of four — hitting, pitching, bench, scouting — hired out of the same money as the
players, affecting how men develop, how fast they heal, and how much your scouts actually know. An
empty post is worse than an ordinary coach.

---

## Verifying changes

The rule here is that nothing is asserted that could be measured. Every harness drives the real
rules engine, pitch factory, swing resolver and field simulation, and each builds a clean league
from the seed rather than reading whatever save is on disk.

```bash
godot471cs --headless --path . -- --sim 350        # league rates against real MLB 2024
godot471cs --headless --path . -- --platoon 400000 # the left-right split
godot471cs --headless --path . -- --audit-outs 40  # every half inning must record three outs
godot471cs --headless --path . -- --unique         # no duplicate names or faces
godot471cs --headless --path . -- --pen 60         # bullpen usage and roster integrity
godot471cs --headless --path . -- --farm           # can all 96 affiliates field a side?
godot471cs --headless --path . -- --drift 3        # roster health across seasons
godot471cs --headless --path . -- --netplay host --minutes 40
godot471cs --headless --path . -- --netplay join 127.0.0.1 --minutes 40
godot471cs --path . -- --shot /tmp/shots 1.5 8 --scene res://Scenes/Season.tscn
```

Current `--sim 350`, both clubs combined per game:

| | Ninety Feet | MLB 2024 | |
| --- | --- | --- | --- |
| Runs | 8.93 | 8.79 | +1.6% |
| Hits | 16.96 | 16.39 | +3.5% |
| Doubles | 3.09 | 3.20 | −3.5% |
| Triples | 0.29 | 0.29 | −1.5% |
| Home runs | 2.15 | 2.24 | −4.1% |
| Walks | 5.83 | 6.15 | −5.3% |
| Strikeouts | 17.91 | 16.96 | +5.6% |
| Stolen bases | 1.52 | 1.49 | +2.2% |

Current `--platoon 400000`, batting average by matchup:

| Matchup | Ninety Feet | Real |
| --- | --- | --- |
| RH vs LHP | .190 | .259 |
| RH vs RHP | .178 | .245 |
| LH vs RHP | .189 | .254 |
| LH vs LHP | .167 | .232 |
| **RH platoon advantage** | **12 pts** | 14 |
| **LH platoon advantage** | **21 pts** | 22 |

The absolute averages in the platoon audit run low because it scores batted balls with a crude
model rather than the full defensive simulation — only the *split* is calibrated there. The
league's real batting rates are the `--sim` table above.

Known gaps: strikeouts run about 6% high and walks about 5% low.

---

## Layout

```
Scripts/
  Core/       rules, pitching, batting, platoon, ball-in-play simulation, CPU brain, harnesses
  Data/       the 32 clubs, players, the written kids, roster generation, uniforms
  Season/     league state, schedule, contracts, free agency, the farm, coaches, finances, save/load
  Cards/      the collection: packs, market, the reward program, signing cards into a season
  Net/        online play — the link, the command stream, the self-test
  Stats/      batting, pitching and team lines; the record book; awards and the Hall
  Gameplay/   the game scene, batting view, field view, scoreboard, mound visits
  UI/         menus, front office, league office, draft, trades, the cartoon player renderer
Audio/vo/     the only assets in the project: name callouts
Scenes/       one thin .tscn per screen; the scripts build their own children
```

`CartoonPlayer.cs` renders every player from his `LookSeed`, so a given man always looks the same
— and `--unique` proves no two of the 869 in a league share a face or a name.
