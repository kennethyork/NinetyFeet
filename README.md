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

**Faces and biographies.** Every roster, card and scouting report in this game was text. The men
have had faces the whole time — skin, hair, ears, brows, nose, mouth, eye spacing and shape, cap
worn forward, backwards or not at all, all of it from the look seed and no two alike across 869 —
and the only place any of it was visible was out on the field, thumbnail-sized, under a helmet,
usually from behind. `CartoonPlayer.Portrait` points the renderer that already exists at a
rectangle instead of a ballfield, so the portrait on a man's card is drawn by the same code from
the same seed as the man at the plate. They are on every clubhouse row and on the player card.

Biographies are composed rather than picked. The old one-line description came off a shelf, which
means it described a *type*: two different sluggers got the same sentence and it was true of
neither in particular. A biography is now assembled from that man's own numbers — where he came
from, what he is known for, where he is in a career, his signature ability, and how the room
regards him.

**And asking whether every player is really different turned up the worst content bug in the
project.** `--unique` said what it always had: no two men share a name, a face, an identifier, and
— once it was taught to ask — no two share a rating sheet either, across all 869. What it could not
see was *given* names. Of the 1,152 hand-written players, **372 were called Dougal, 268 Tancred and
225 Vidal**: three quarters of the authored cast under three first names, with 844 distinct
surnames hiding it. San Francisco fielded eight men called Vidal. Every one has a proper name now,
drawn from the pool that matches his surname, and the worst club is down from eight to two — which
is what a real clubhouse looks like. A written player's name is authored data rather than save
data, so leagues already in progress pick the change up on load without losing a statistic.

Their biographies had the same disease: 246 distinct lines across 1,152 men, the common ones used
sixteen times each. A line shared with fifteen other ballplayers is not that man's biography, so
those men get a composed one; the ones whose line appears exactly once keep it, because it is
genuinely theirs.

**Your own ballparks.** `user://stadiums.cfg`, written out from the club editor with every ground
as it currently stands — five fence distances, five wall heights, air density, foul territory, roof,
and four colours apiece. Starting from a real park and moving a wall is a job somebody can do;
starting from an empty bracket is not, which is why the file is a copy rather than a form.

This overlay is different from the other two and the difference matters. A club's name and a
player's name are labels — change them and no baseball moves. A fence distance is not a label, it
goes into the physics. `--ballparks` demonstrates that by pulling one ground in to 280 feet with a
four-foot wall and watching the same forty games produce 528 home runs instead of 106. Absurd
numbers are clamped, a row of four distances is refused rather than padded out with a guess, and
every audit ignores the file entirely — an audit that read it would be measuring somebody's own
ballpark rather than the game.

**A league of any size.** Settings → Clubs takes it to 8, 12, 16, 20, 24, 28 or 32, evenly from all
four divisions so the pennant races stay comparable. A league keeps the size it was built with for
ever: the count is written into the save and restored on load, because opening a sixteen-club
dynasty into a thirty-two-club league would be half a league, a schedule that cannot be rebuilt and
a race against sixteen ghosts.

Clubs keep the identifiers they shipped with, so a sixteen-club league is literally sixteen of these
thirty-two — same ids, same ballparks, same written players. Nothing keyed by club id means
something different at a different size: not a save, not the club editor, not a roster file, not a
rebuilt ground. Renumbering the survivors 0 to 15 would have been fractionally simpler and would
have quietly changed which club every one of those files was talking about.

The cost of that choice is that a club id is no longer its position in the list, and `id + 1` is no
longer the next club — which is exactly the sort of thing that does not crash, it just schedules a
game against a club that is not playing. `--size` therefore plays a whole season at every size: a
schedule that balances, every fixture between two clubs that actually exist, every game played, a
champion crowned, a draft held and the league rolled into the next year.

**Your own names.** The clubs could always be renamed and recoloured; the men in them could not,
and no screen anywhere wrote a player's name. So a league could be made to look like one you follow
while every man in it stayed invented. `user://rosters.txt` is a plain text file — a club per
section, a man per line — and the generator uses it in place of the names it would have drawn. Write
a blank one with every slot labelled from the club editor or `--names-template`, fill in as much as
you like (a club, or nine men of one, is fine), and either start a new league or apply it to the one
you are already running.

Nothing is shipped in it. The file lives in your own directory and the repository contains no names
but its own.

One thing to know before you start typing: sixteen of every club's twenty-seven men are written
players — 512 across the league, with faces and biographies — and a written player takes a generated
man's slot outright, so names aimed at those slots are never used. **Settings → Written players**
turns them off, and then every slot on a club is one a file can name. `--names` reports exactly which
of your names landed and which did not, because starting a league and reading one roster screen tells
you nothing about the other thirty-one.

**The Collection** — packs, a market, and a club built out of cards you own. Cards can be spent to
sign a player into your actual season, and he *transfers* rather than being copied: there is one of
everybody in this league and that stays true.

**Online** — one ballgame, or a whole season two people run together. Both are reached from
**Online** on the title screen. Until now neither was: the netcode was finished and proven and the
only thing that could open a socket was a headless self-test on the command line.

**A shared season.** Each of you runs a club in the same league. Every game neither of you is
playing is simulated by both machines from the fixture's own seed and lands on the same score to
the last run, so a season costs a few kilobytes a day rather than a stream of standings. The games
you play cannot be re-derived by anyone else, so those travel as packets. The day is a barrier:
neither calendar turns until both owners have finished with it, and every night both machines
fingerprint the entire league and compare, so a disagreement is caught on the day it happens
rather than in August. The one fixture nobody plays is the two of you against each other — both
machines settle that from the seed, because a result one of you produced by hand is a result the
other cannot have.

A shared league is never written to disk. It belongs to two people and only half of it is on this
machine, so saving it would put a season nobody can resume into a slot somebody cares about.

Netplay works by having both machines build the identical game from a shared seed and exchange only
what each human decides. The same trick generalises to a whole season — but only if a season is
replayable, and a season-long desync is far nastier than a game-long one: inside a game it costs
you the game and you see it at once, while a drift on the third of April costs you the season and
you find out in August when the two sides disagree about who is in first place.

`--determinism` builds two leagues from one seed, advances both a day at a time and fingerprints
each after every day. Forty days, 576 games, identical every single day.

It then puts one of them through its own save file and reads it back, because two machines agreeing
while both stay running is not enough — people quit and come back, and if a league is not the same
after a round trip the two sides split apart the moment one of them closes the game, with the
netcode seeing nothing because both are behaving perfectly. Forty days match, and so does the
league after a save and a load.

`--league` then runs the real arrangement in one process: two owners, one league, each playing his
own club's games from a seed the league itself would never use — a result neither machine could
work out for itself — and posting it to the other. Forty-five days, 76 games posted across, 656
played, identical every day.

`--netleague` runs the same thing over an actual socket, two processes, because none of what only
exists once there are two of them can be reasoned about from one side: that the terms agree before
either builds a league, that a packet survives being marshalled, that a day-done message cannot
overtake the ballgame it refers to, and that the fingerprints actually arrive and get compared.
Sixty days, 864 games, both sides finishing on `F8EFDA3B020C90DB`.

Neither audit is allowed to pass quietly on a case it did not reach. The first version picked the
two owners as clubs 0 and 1 and ran sixty days without them ever meeting, so the one fixture that
behaves differently went untested while the run reported success — both now choose the second owner
as whoever the first plays on opening day, and both print how many times the two met.

**Getting there found four real bugs.** Injuries and pitcher workload
were never written to the save at all: a man on the shelf came back healthy on reload and every arm
came back fresh however hard it had just been used, which also quietly reset the workload the
pitching coach reads before he writes to you. Fixed. **Player ids used to collide, and that is fixed.** Two causes, both of them the same mistake in
different clothes. An id was `team.Id * 100 + usedNames.Count`, and `usedNames` is a set — so a
club that drew a name it already had did not grow the count, and handed the next man the identity
of the one before him. And written players were numbered `team.Id * 100 + 90 + legendId` with
ninety-six of them, which ran straight over the top of the next club's range. Forty-nine of 869
players were sharing an identity with somebody else. `--unique` now checks ids as well as names and
faces, and reads zero.

**The lineup card was never saved.** Found by widening the league fingerprint to cover it — a
pinch hitter is permanent, so two machines holding identical players with identical statistics
could still be sending out different nines, and a checksum that cannot see that would call an
already-broken league healthy. The moment the fingerprint could see the card, a save round trip
stopped matching: loading a league rebuilt every club from its player list and let the trade engine
work the nine out again from ratings. A pinch hitter who stayed in, a man moved up to leadoff, a
bench player covering an injury — all of it undone by closing the game. Fixed, and the round trip
matches again.

That was never only a save bug: the stat book, the box scores, the game logs and the splits are all
keyed by player, so a collision merged two men's careers wherever it happened.

**The round trip passes.** The last cause was the load path reassigning jobs: `TradeEngine.Rebuild`
puts a club back together — the staff, the lineup, the order — and on the way decided every
pitcher's role again from his ratings, which is not its call to make on a league that already had
one. Generation hands out roles by the order arms are built in; Rebuild ranks them by stuff, so a
starter came back a long man and his overall moved with him. The saved role wins now.

Restoring generation's answer on load, rather than making generation call `Rebuild`, keeps the fix
to the load path — `Rebuild` also rewrites the batting order, and handing it the lineup would throw
away the leadoff, three-hole and cleanup logic a roster is built with.

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

**The designated hitter** — on or off, in Settings. Applied per game rather than baked into a
roster, so nobody is added or removed and the ninth spot simply belongs to a different man.
Measured with it off: scoring falls from −4.4% against the real rate to −10.5% and strikeouts rise,
which is what a pitcher batting ninth does to a league.

**The playoff format** — best of 3, 5 or 7 in the first round, with the later rounds two longer
and capped at seven. A short October is a different competition from a long one: the best club wins
a seven far more often than it wins a three, and which of those you want is a league rule rather
than the game's.

**Four leagues at once** — there was one. A single `season.json`, so a dynasty you had run for ten
seasons was the only league that could exist, and starting a fresh one to try something meant
destroying it. Slots are chosen in Settings and the league you leave is written out first, because
switching must never be how somebody loses a season. Slot one keeps the original filename, so every
league that existed before this still opens.

**Your clubs** — any of the thirty-two can be renamed and recoloured, from Settings. What can be
edited stops at the name, the abbreviation and the two colours: a club's league, division and
playing biases are what make the thirty-two different from one another as opponents, and letting
somebody hand his own club a pitching bias would turn a customisation screen into a cheat menu.
Overrides live in their own file rather than being written over the built-in list, so a club can
always be put back exactly as it shipped and nothing is ever lost.

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
| At the plate | (Settings) | **Zone**, **Directional** or **Timing** — pick how much aiming you want |
| | Mouse or left stick | Aim the hitting reticle — whichever you touch keeps it |
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
| On the mound | (Settings) | **Classic** or **Meter** — aim and throw, or work the bar |
| | 1 2 3 4 | This pitcher's own repertoire, in order |
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
godot471cs --headless --path . -- --unique         # no duplicate names, faces, ids or rating sheets
godot471cs --headless --path . -- --namepool       # every first name the pool actually uses
godot471cs --headless --path . -- --pen 60         # bullpen usage and roster integrity
godot471cs --headless --path . -- --farm           # can all 96 affiliates field a side?
godot471cs --headless --path . -- --plate          # the batting view, in milliseconds and pixels
godot471cs --headless --path . -- --careermode 40  # whole careers, played end to end
godot471cs --headless --path . -- --boxes 20       # box scores must add up to the season book
godot471cs --headless --path . -- --defence 4000   # what each fielding alignment actually does
godot471cs --headless --path . -- --people 4       # is personality a mechanic or a label?
godot471cs --headless --path . -- --clubs          # the club editor renames and nothing else
godot471cs --headless --path . -- --slots          # four leagues that cannot destroy each other
godot471cs --headless --path . -- --determinism 40 # two leagues, one seed: do they still agree?
godot471cs --headless --path . -- --drift 3        # roster health across seasons
godot471cs --headless --path . -- --size          # a whole season at every league size
godot471cs --headless --path . -- --ballparks 40  # a moved wall must change the baseball
godot471cs --headless --path . -- --talent        # written players against generated ones
godot471cs --headless --path . -- --extrabase 300 # one change at a time, and what it does to 2B/3B
godot471cs --headless --path . -- --names          # what your own roster file actually did
godot471cs --headless --path . -- --names-template # write a blank one to fill in
godot471cs --headless --path . -- --league 45      # two owners, one league, results crossing
godot471cs --headless --path . -- --netplay host --minutes 40
godot471cs --headless --path . -- --netplay join 127.0.0.1 --minutes 40
godot471cs --headless --path . -- --netleague host --days 60 --minutes 7
godot471cs --headless --path . -- --netleague join 127.0.0.1 --days 60 --minutes 7
godot471cs --path . -- --shot /tmp/shots 1.5 8 --scene res://Scenes/Season.tscn
```

Current `--sim 400`, both clubs combined per game. The second column of differences is the same
league with the written players turned off — the configuration somebody importing his own names
has to use, since a written player takes a generated man's slot.

| | Ninety Feet | MLB 2024 | |
| --- | --- | --- | --- |
| Runs | 8.55 | 8.79 | −2.7% |
| Hits | 15.65 | 16.39 | −4.5% |
| Doubles | 3.22 | 3.20 | **+0.7%** |
| Triples | 0.28 | 0.29 | **−1.7%** |
| Home runs | 2.23 | 2.24 | **−0.4%** |
| Walks | 5.72 | 6.15 | −7.0% |
| Strikeouts | 16.98 | 16.96 | **+0.1%** |
| Stolen bases | 1.50 | 1.49 | **+0.5%** |
| Caught stealing | 0.46 | 0.51 | −9.8% |
| Hit by pitch | 0.72 | 0.79 | −8.5% |
| Wild pitches | 0.75 | 0.76 | **−2.0%** |
| Sacrifice flies | 0.33 | 0.79 | −58.2% |
| Sacrifice bunts | 0.17 | 0.19 | −11.8% |
| Grounded into DP | 1.59 | 1.44 | +10.2% |
| BABIP | .259 | .294 | −11.9% |

**The calibration had been resting on the written cast, and nobody knew.** Turning the written
players off used to move run scoring from four percent under the majors to twelve — the same
simulation, the same park, eight percent of the league's offence gone. Sixteen of every club's
twenty-seven men are hand written, so the calibration had never once measured the generator on its
own; it had always measured a league that was three-fifths authored.

`--talent` was built to find out why, and every rating on it pointed the wrong way. Without the
written players the lineups that actually play are *better* — contact +0.25, power +0.57 — and the
rotations *worse*. On ratings alone the league should have scored more, not eight percent less.

It was the specials. `ApplyPositionProfile` decided whether a man's signature was his bat or his
glove by asking `Fielding + Arm > Contact + Power`, and asked it *after* the positional shaping had
run. The two sums are generated around the same centre, so on its own the question is fair — but
the shaping hands a catcher +2 arm, a shortstop +2 fielding, a second baseman +1 fielding and −2
power, every one of them right about baseball and every one of them tilting the scales. Up the
middle the answer was "glove" almost regardless of the man, and catchers, shortstops, second
basemen and centre fielders essentially never drew a bat special at all.

The league it produced carried 18.8% bat specials against the written cast's 39.9%, and 27.4% glove
ones against 17.0%. ContactMaster widens the sweet spot by a third; VacuumGlove more than doubles a
fielder's catch radius. Asking the question before the shaping — the switch consumes no randomness,
so every other draw in the league is untouched — closes the run gap from 8.4 points to 0.6.

**And then the doubles, which took being wrong four times.** A league without the written players
hit doubles fifteen percent light, so the search was for what the written players were contributing.
`--extrabase` changes one thing at a time about an otherwise identical league, over the same
matchups and seeds, and reads the answer off — an intervention rather than an argument, because the
first attempt at this was reasoned from league averages and had to be reverted.

It answered flatly. Strip *every special in the league* out of the written cast and it still hits
3.05 doubles a game against its usual 3.03. It was never the specials. Nor was it the running
specials, nor gap power: giving every man in every order TurboLegs — far past anything the
generator would produce — bought back the doubles but sent triples to 1.31 a game against a real
0.29. Bringing the generated men's gloves and arms down to the written cast's level, and stripping
their glove specials as well, recovered less than half.

What had been missed was that **both** leagues were short — 3.03 and 2.63 against a real 3.20 — so
the thing to fix was a level, not a difference. `StretchToSecond`, the one number that decides
whether a single becomes a double, was simply set a shade low at 0.80. At 0.84 the league hits
3.23 doubles, which is the closest any statistic on this table has come to the majors.

The triples in the last column are still short and are left that way. Triples *are* special-driven
— take every special away and they collapse from 0.27 to 0.10 — so a league with fewer of the
written cast's runners has fewer of them, and the shipped configuration is already within six
percent of real. Fixing the one would overshoot the other.

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

### Where the outs come from

```
balls in play 50.9/game — caught in the air 43% (real about 45%)
of those not caught, an out 42% — 12.18/game (real is nearer 15)
```

This used to read 67% caught in the air and 0.6 ground outs a game, and the reason turned out to
have nothing to do with the defence.

**Every ball in play was leaving the bat between 0 and 40 degrees** — 99% of them between 10 and
40, mean near 25. There were no ground balls. Not few: none. Measured over 1,568 balls in play,
the band under 10 degrees held 0.7% of them against a real 45%. So the infield never saw a ground
ball, never recorded a ground out, and never turned a double play; the league's entire out total
was carried by fly balls. Three defects chased at the infield end for a long time were one line in
the batting model.

Two things were wrong with it. The launch angle barely responded to where the bat met the ball,
and quality was measured from dead centre — so any ball hit high enough to carry was by definition
a mishit and left the bat slowly. The two fought each other, and the only way to elevate was to
miss. A barrel is struck about a quarter of a barrel *under* the middle of the ball, and it is
modelled that way now.

`--infield` follows a ground ball through the defence stage by stage. It is reached by a fielder
100% of the time and the batter is retired on 73% of them, against a real figure near 75% — the
infield was never broken. It had nothing to field.

Rebuilding the batted-ball distribution meant re-deriving the whole offensive calibration, since
every number in the table above had been tuned around a defence that could not record a ground
out. Six measured passes brought it back: runs +0.9%, home runs +3.3%, strikeouts +2.4%.

The strikeouts came back from the two-strike swing. Its profile had been held deliberately mild,
with a note explaining that widening it sent BABIP to .314 because "weak contact in this model
still finds grass". That was true of a model where nothing was hit on the ground and the infield
recorded 0.6 outs a game; it is not true of one where a ball fought off with two strikes goes down
at a low angle and gets fielded. The note was right when it was written and wrong afterwards,
which is the ordinary way a comment goes stale.

### The harness plays in weather now

`--sim` used to set the ballpark and never call `SetConditions`, so every number above was measured
in still, neutral air while a real season was played in wind and heat. The two were not describing
the same game, and it showed: a club's box scores over a fortnight came out around 11 hits a game
against the 8.2 the harness was reporting. It now samples the same summer temperature curve and
the same mostly-gentle wind that `Weather.For` gives a real date, so the calibration table above is
measured against the game the season actually plays.

### There was never a close play at the plate

Measured over 5,000 batted balls, the defence threw home 0 times and to third 0 times. Every throw
in the game went to a man with no choice in the matter — the batter, forced to first.

It was not a bug in a line. A runner decided to go when `runnerTime < BallArrivalTime × aggression`
with aggression around 0.42, so he only left the bag when he was about twice as fast as the throw.
The defence decided to throw when `BallArrivalTime < runnerTime − 0.25`. Both call the same
estimator, so any runner who had chosen to run had already proved the throw could not get him, and
the throw was never worth making.

Runners now carry a `Nerve` rolled once when the ball is struck, which straddles break-even — a
third-base coach is guessing, and he is wrong often enough to matter. The defence throws home 254
times and to third 423 times across 350 games.

It cost something and the cost is here rather than hidden: runs went from +0.9% to +6.4%, because
runners who advance score. Trying to take that back out of the bat instead was measured and was
worse — it held runs at +0.2% and dropped home runs to −18% and doubles to −23%, which is a
deader game than a slightly high-scoring one. Triples are 41% low, which is the same knob from the
other side: the batter-runner is deliberately held to his old scale, since a man bold enough to
gamble at the plate will also stretch a double into a triple, and letting him do so put triples at
1.48 a game against a real 0.29.

An infielder who fields a ground ball cleanly now throws it. He used to hold it on 16.3% of them
— one in six, a man standing on the dirt with the ball in his glove watching the batter jog to
first, which is the most obviously wrong thing in the game to look at however respectable the
league's totals are. Every throw option had to clear a time margin first, and when none did he
simply declined. He now throws at the lead forced runner anyway and the race decides it, since a
runner who reaches the bag before the ball is off the list by the time it arrives.

That made the defence about ten per cent better and left the league light across the board. It was
brought back with the fielders' catch radius rather than the bat — the right lever for a uniform
shortfall precisely because a ball over the fence is not caught by anybody, so it lifts hits,
doubles and runs together and leaves power where it was measured. The bat was tried three times for
this and overshot home runs past +20% every time.

Everything now sits inside five per cent except the walks, the steal game and the sacrifice fly.

Run scoring drifted to +8.4% across the gameplay fixes and was brought back to +3.4% on the
baserunning side rather than by detuning the bat — the defence now attempts a throw with a tenth of
a second of daylight instead of demanding a quarter, so close plays are played out and the race
decides them. Triples came back from −45% at the same time: how boldly the batter-runner takes an
extra base is a separate knob from how boldly the men already aboard do, and after the batted-ball
rebuild it needed resetting rather than inheriting its old value.

Other known gaps: caught stealing is 40% low, so runners succeed at 82% against a real 75%.
Sacrifice flies are 56% low. The sacrifice bunt now exists — it did not, because the only man who
would lay one down was a pitcher and the designated hitter means a pitcher almost never bats — but
at 0.14 a game against a real 0.19 it is still called for too rarely.
The hit-by-pitch, wild-pitch, sacrifice
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
