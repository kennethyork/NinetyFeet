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

**Career** — one player, from the bottom of somebody's farm system to wherever he gets to. You do
not pick where you play: you are drafted, told which rung you are on, and moved when the club
decides you have earned it. Measured over forty careers, 36 reach the majors after about six years
and 4 are released without ever arriving.

**Moments** — one situation, one question, ninety seconds. Bottom of the ninth with the tying run
on third; a save situation with the tying run on second and nobody out. They pay into the same
reward programme as the rest of the collection.

**Exhibition** — any two clubs, any length.

### The stat sheet

Every plate appearance is filed four ways: by the pitcher's hand, by home or road, by month, and by
whether a man was in scoring position. The simulation has always modelled the platoon matchup —
`Platoon.cs` has been deciding at-bats by handedness for a long time — and until recently nothing
wrote down which hand was on the mound, so the one thing the engine did best was the one thing you
could not see. Splits are on the player card; the broadcast card at the plate reads from them.

Box scores are kept for your club's games — the line score by inning, both sides' hitters and arms,
and who took the decision — with a form line on the player card showing his last ten nights. A
season total says a man is hitting .240; it cannot say whether he got there steadily or by going
2-for-40 in September.

### Managing

**In the field** — five alignments on `Y`: straight, double-play depth, infield in, no doubles, and
the shift. There is no hidden bonus behind any of them; the nine men simply stand somewhere else
and the play simulation decides from where they are. `--defence` hits the same batted balls at each
one and reports what changed.

**The pen** — `U` gets somebody up. He needs about an inning to get loose; rushed in cold he cannot
find his release point for ten pitches. Left standing out there too long costs him too.

**The people** — every player has a work ethic, a loyalty and a poise, and a morale that moves with
playing time, winning and the state of his contract. Work ethic decides how much he improves over a
winter; loyalty and morale decide what he will re-sign for. None of it touches his bat, deliberately
— a league whose run environment moved with how happy everybody was would be a league whose
calibration could not be trusted.

**The inbox** — the pitching coach on a workload nobody else can see, the hitting coach on a man who
cannot handle left-handers, the bench coach on somebody who has had enough, the owner on what he
expects. Everyone writing is reading real state; nothing is invented to have something to say.

---

## The league

Thirty-two clubs in real major-league markets — the thirty current ones plus Montreal and
Nashville. Names, colours and marks are all original, parent clubs and affiliates alike.

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

### The clubs, and their farm systems

Each club carries three affiliates, and every one of them is a real club: it has a roster, plays a
schedule, keeps a record, and can be watched or managed for a night. A prospect climbs Wisconsin
before he sees Milwaukee.

| | Club | | Triple-A | Double-A | High-A |
| --- | --- | --- | --- | --- | --- |
| AL E | **Baltimore Blue Crabs** | BAL | Chesapeake Skipjacks | Annapolis Watermen | Ocean City Sandpipers |
| AL E | **Boston Lobsters** | BOS | Worcester Bay Staters | Portland Lightkeepers | Lowell Millhands |
| AL E | **Bronx Bombardiers** | BRX | Scranton Anthracite | Trenton Ironworks | Poughkeepsie Riverfolk |
| AL E | **Tampa Bay Thunderheads** | TAM | Sarasota Squalls | Ocala Thunderclaps | Fort Myers Gale |
| AL E | **Toronto Maple Bats** | TOR | Hamilton Steel Cats | London Timberjacks | Sudbury Sap Runners |
| AL E | **Montreal Voyageurs** | MTL | Quebec Portagers | Sherbrooke Trappers | Gatineau Canoemen |
| AL E | **Cleveland Rockers** | CLE | Akron Amplifiers | Youngstown Backbeat | Sandusky Breakers |
| AL E | **Detroit Motorheads** | DET | Toledo Gearheads | Flint Pistons | Kalamazoo Sparkplugs |
| AL W | **South Side Sluggers** | SSS | Joliet Stockyards | Rockford Foundrymen | Peoria Haymakers |
| AL W | **Kansas City Smoke** | KCS | Wichita Brisket | Springfield Embers | Topeka Kindling |
| AL W | **Minnesota Loons** | MIN | Duluth Ore Boats | Rochester Northern Lights | Mankato Goslings |
| AL W | **Houston Moonshots** | HOU | Galveston Gantry | Beaumont Booster Stage | Waco Countdown |
| AL W | **Anaheim Angelfish** | ANA | Riverside Tide Pools | Bakersfield Kelp | Ventura Anemones |
| AL W | **Oakland Oaks** | OAK | Modesto Acorns | Fresno Saplings | Stockton Grove Hands |
| AL W | **Seattle Sasquatch** | SEA | Tacoma Timberline | Spokane Trailblazers | Olympia Footprints |
| AL W | **Texas Twisters** | TEX | Amarillo Dust Devils | Lubbock Funnel | Abilene Windrows |
| NL E | **Atlanta Peaches** | ATL | Macon Preserves | Augusta Orchardmen | Columbus Cobblers |
| NL E | **Miami Flamingos** | MIA | Fort Lauderdale Wading Birds | Naples Spoonbills | Key West Fledglings |
| NL E | **Queens Apples** | QNS | Syracuse Orchard | Binghamton Cider Press | Coney Island Crabapples |
| NL E | **Philadelphia Liberty Bells** | PHI | Allentown Foundry Bells | Reading Clappers | Camden Chimes |
| NL E | **Washington Monuments** | WAS | Richmond Obelisks | Harrisburg Cornerstones | Norfolk Pediments |
| NL E | **Pittsburgh Ironmen** | PIT | Altoona Blast Furnace | Erie Puddlers | Wheeling Rivetheads |
| NL E | **Cincinnati Riverboats** | CIN | Louisville Paddlewheels | Dayton Deckhands | Evansville Steamers |
| NL E | **Nashville Hot Chickens** | NSH | Knoxville Cayenne | Chattanooga Skillets | Jackson Brine |
| NL W | **North Side Ivy** | NSI | Des Moines Trellis | Springfield Creepers | South Bend Tendrils |
| NL W | **Milwaukee Cheeseheads** | MIL | Madison Curds | Green Bay Cheddar | Appleton Whey |
| NL W | **St. Louis Archers** | STL | Memphis Fletchers | Columbia Quivers | Cape Girardeau Bowstrings |
| NL W | **Phoenix Roadrunners** | PHX | Tucson Coyotes | Yuma Ocotillo | Flagstaff Chaparral |
| NL W | **Denver Mountaineers** | DEN | Colorado Springs Switchbacks | Pueblo Timberline | Grand Junction Cairns |
| NL W | **Hollywood Stars** | HOL | Pasadena Klieg Lights | Bakersfield Second Unit | Long Beach Extras |
| NL W | **San Diego Surfers** | SD | Chula Vista Longboards | Escondido Undertow | Oceanside Shorebreak |
| NL W | **San Francisco Fog** | SF | Sacramento Delta Mist | Stockton Marine Layer | Santa Rosa Haar |

Affiliate towns are real places in each parent's region — that is geography, not a trademark — and
the nicknames are invented, leaning the way minor-league names actually lean: local industry, local
food, local weather, local jokes. A Milwaukee farmhand comes up through Appleton, Green Bay and
Madison playing for the Whey, the Cheddar and the Curds.

Affiliate sizes are 20 at Triple-A, 22 at Double-A and 24 at High-A, with roster spots at 28, 30
and 32 — the headroom is what lets you option a man down. `--farm` checks that all 96 can field a
side.

---

## Controls

| Where | Key | Does |
| --- | --- | --- |
| At the plate | Mouse or left stick | Aim the hitting reticle — whichever you touch keeps it |
| | Left click / Space / A | Normal swing |
| | Right click / F / Y | Power swing — smaller barrel, more damage |
| | Middle click / C / X | Contact swing — bigger barrel, less power |
| | B / left bumper | Bunt |
| | WASD | Aim without a mouse |
| | Shift / Tab | Spend the signature move |
| | R | Challenge the call |
| **Managing** | ← | **Steal** — send the runner |
| the arrow keys | → | Pinch hit (before the first pitch only) |
| | ↑ / ↓ | Send the runners / hold them |
| On the mound | 1 2 3 4 | This pitcher's own repertoire, in order |
| | Mouse | Aim the pitch |
| | Left click / Space | Deal |
| | P | Go to the pen — the manager walks out |
| | V | Mound visit. Five a game; the sixth has to be a change |
| | I | Intentional walk |
| | U | Get somebody up in the pen — again to walk down it |
| In the field | 1 2 3 4 | Throw to first, second, third, home |
| | Y | Move the defence — DP depth, in, no doubles, shift |
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
godot471cs --headless --path . -- --plate          # the batting view, in milliseconds and pixels
godot471cs --headless --path . -- --careermode 40  # whole careers, played end to end
godot471cs --headless --path . -- --boxes 20       # box scores must add up to the season book
godot471cs --headless --path . -- --defence 4000   # what each fielding alignment actually does
godot471cs --headless --path . -- --people 4       # is personality a mechanic or a label?
godot471cs --headless --path . -- --drift 3        # roster health across seasons
godot471cs --headless --path . -- --netplay host --minutes 40
godot471cs --headless --path . -- --netplay join 127.0.0.1 --minutes 40
godot471cs --path . -- --shot /tmp/shots 1.5 8 --scene res://Scenes/Season.tscn
```

Current `--sim 350`, both clubs combined per game:

| | Ninety Feet | MLB 2024 | |
| --- | --- | --- | --- |
| Runs | 8.89 | 8.79 | +1.1% |
| Hits | 16.64 | 16.39 | +1.5% |
| Doubles | 2.90 | 3.20 | −9.3% |
| Triples | 0.30 | 0.29 | +2.5% |
| Home runs | 2.15 | 2.24 | −4.0% |
| Walks | 5.59 | 6.15 | −9.1% |
| Strikeouts | 18.08 | 16.96 | +6.6% |
| Stolen bases | 1.44 | 1.49 | −3.4% |
| Caught stealing | 0.34 | 0.51 | −33.3% |
| Hit by pitch | 0.80 | 0.79 | +1.3% |
| Wild pitches | 0.70 | 0.76 | −7.5% |
| Sacrifice flies | 0.95 | 0.79 | +20.1% |
| Sacrifice bunts | 0.00 | 0.19 | −100% |
| Grounded into DP | 0.04 | 1.44 | **−97.0%** |

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

### The one that matters

**The infield does not field.** Of every ball in play not caught on the fly, 95% goes down as a
hit. The defence records 0.6 ground outs a game against a real figure near fifteen, there are
almost no double plays, and 66% of balls in play are caught in the air against a real 45% — the
league's entire out total is carried by fly balls. Run `--sim` and read the "where the outs come
from" block.

This is why the double-play and sacrifice-bunt rows above are empty, and why bringing the infield
in costs runs instead of saving them (`--defence` measures both). Part of the cause is found and
written up at `PlaySimulation.ThrowTime`: the play is charged twice for the same glove-to-hand
transfer, so on a routine ground ball the infielder decides he cannot get the batter and holds the
ball. Removing that double-charge helps — ground outs go to 1.1 a game and runners retired on the
bases from 0.8 to 1.8 — but it is only part of the fix, and on its own it drops league scoring 15%
below the real rate. It is left in place, documented, rather than half-applied.

Fixing it properly means giving the infield real range and then re-deriving the batted-ball
calibration behind it. It is the single highest-value piece of work left in the simulation.

### The harness does not play the same game the season does

Found while checking box scores, and not yet resolved. `--sim` plays every game in neutral
conditions — it sets the ballpark but never calls `FieldGeometry.SetConditions`, so there is no
wind and no temperature. `SeasonState.SimulateGame` does set them, from `Weather.For`. The
calibration table above is therefore measured under conditions a real season never plays in.

A club's box scores over a fortnight came out around 11 hits a game against the 8.2 the harness
reports, which is a large enough gap to be worth chasing rather than filing under sampling. It has
not been chased: making `--sim` sample weather would move every number in the table at once, and
that is a deliberate re-calibration rather than a fix to slip in.

Other known gaps: strikeouts run about 7% high and walks about 9% low. Caught stealing is a third
low, so runners are succeeding at 82% against a real 75%. The hit-by-pitch, wild-pitch, sacrifice
and double-play reference figures in `RealBaseball` are from memory rather than from the stats API,
unlike everything else in that file, and are flagged there as needing a refetch.

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
