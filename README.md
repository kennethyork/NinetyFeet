# Ninety Feet

[![Release builds](https://github.com/kennethyork/NinetyFeet/actions/workflows/builds.yml/badge.svg)](https://github.com/kennethyork/NinetyFeet/actions/workflows/builds.yml)

**Arcade baseball on an honest simulation. Thirty-two clubs, a real front office, and a race to ninety feet.**

Ninety Feet combines approachable, cartoon baseball with a persistent fictional league. Bat,
pitch, field and run the bases yourself, then manage the same players through trades, contracts,
injuries, development, drafts and entire careers.

The game is built with Godot 4 and C#. Players, uniforms, ballparks and most interface art are
drawn procedurally. The simulation is deterministic and calibrated against modern professional
baseball rates.

## Highlights

- **Play the game:** zone, directional or timing hitting; classic or meter pitching; manual
  fielding and baserunning; bunts, steals, defensive positioning, bullpens and mound visits.
- **Season:** play or simulate a complete schedule with standings, playoffs, weather, attendance,
  injuries, inbox stories, box scores and statistical splits.
- **Dynasty:** continue across seasons through development, ageing, retirement, arbitration, free
  agency, waivers, the draft, awards, records and a Hall of Fame.
- **Front office:** evaluate and execute trades, manage contracts and payroll, hire coaches, scout
  players, set lineups and run three levels of affiliates.
- **Career:** follow one player from the farm system through promotions, setbacks, free agency and
  retirement.
- **Moments:** play short, scripted situations with persistent attempts, wins and rewards.
- **Collection:** open packs, use the market and build a card club. A card signed into a season
  transfers the actual player rather than cloning him.
- **Online:** play a head-to-head game or share a deterministic season between two owners.
- **Customization:** rename and recolor clubs, edit ballparks, choose 8–32 clubs and import your own
  local roster data.
- **Per-mode resume:** Season, Dynasty, Career, Collection, Moments, Exhibition and Online each
  reopen with their own saved progress or selections. Completed trades are saved immediately.

## Downloads

Every push to `main` builds three downloadable GitHub Actions artifacts:

- Android arm64 APK
- Windows x86_64
- Linux x86_64 AppImage

Open the [latest Release builds run](https://github.com/kennethyork/NinetyFeet/actions/workflows/builds.yml),
select a successful run and download the artifact for your platform. GitHub requires you to be
signed in to download workflow artifacts.

Android builds may need permission to install apps from the browser or file manager used to open
the APK. Windows may show a SmartScreen warning because development builds are not code-signed.

## Controls

Touch controls appear automatically on Android. Menus and gameplay buttons accept direct taps.

| Situation | Keyboard and mouse | Controller |
| --- | --- | --- |
| Aim while batting | Mouse or WASD | Left stick |
| Normal swing | Left click or Space | A |
| Power swing | Right click or F | Y |
| Contact swing | Middle click or C | X |
| Bunt | B | Left bumper |
| Signature move | Shift or Tab | — |
| Aim a pitch | Mouse | Left stick |
| Choose / throw a pitch | 1–4, then click or Space | A/B/X/Y selects that slot; press it again to deal |
| Bullpen / mound visit | P / V | On-screen menu |
| Throw to first–home | 1–4 | Face buttons |
| Steal / advance / hold | Arrow keys | On-screen controls |
| Pause or go back | Escape | Menu / Back |

Batting and pitching styles, assists, automatic fielding and difficulty can be changed in
**Settings**. Accessibility options include larger interface text, high-contrast labels, reduced
decorative motion and optional controller vibration.

## Saves

Ninety Feet saves established leagues during screen changes and when the application is paused,
unfocused or closed. Accepted trades and completed games save immediately. Automatic resume can be
disabled only by deleting the relevant mode from **Settings → Saved Modes**. Season/Dynasty,
Career, Collection, Moments, Exhibition and Online are reset separately, with two activations
required so one mistaken tap cannot erase progress.

Godot stores player data outside the installation directory:

- Linux: `~/.local/share/godot/app_userdata/Ninety Feet/`
- Windows: `%APPDATA%\Godot\app_userdata\Ninety Feet\`
- Android: the application's private data directory

Uninstalling the Android application or clearing its storage can remove local saves. Back up the
application data before doing either.

## Local roster imports

Ninety Feet ships only fictional clubs and players. It does not include, scrape or automatically
download third-party names, photographs, logos or roster databases.

Players can create `user://rosters.txt` to provide their own local data. Generate a blank,
slot-labelled template from the club editor or from the command line:

```bash
godot --headless --path . -- --names-template
```

The basic format remains one club section followed by one player per roster slot:

```ini
[BAL]
Example Pitcher
Example Hitter
```

Optional fields follow the name and are separated by `|`:

```text
Example Player | number=27 | bats=R | throws=R | age=28 | contact=8 | power=7 | speed=6 | arm=8 | fielding=7 | potential=9 | salary=12500 | contractyears=4 | serviceyears=5 | archetype=FiveTool | special=GapPower | lookseed=12345
```

Pitchers may also use `pitchpower`, `pitchcontrol`, `stamina` and `role`. Accepted roles are
`Starter`, `Long`, `Middle`, `Setup` and `Closer`. Ratings are clamped to 1–10 and salary is stored
in thousands of dollars. Every omitted field keeps its generated value, so old name-only files
continue to work.

Run the roster report to see which sections and slots were applied:

```bash
godot --headless --path . -- --nolegends --names
```

Turn off **Settings → Written players** before starting a fully imported league so fictional
authored characters do not occupy imported slots. Roster content is the user's responsibility;
the importer does not grant rights to third-party names, likenesses, branding or data sources.

## Build from source

Requirements:

- Godot 4.7.1 Mono
- .NET SDK 8 or newer

```bash
git clone https://github.com/kennethyork/NinetyFeet.git
cd NinetyFeet
dotnet build SandlotSlugfest.csproj
godot --path .
```

Godot must be able to find the same .NET installation used for the build. On Linux, if necessary:

```bash
export DOTNET_ROOT=/path/to/dotnet
export PATH="$DOTNET_ROOT:$PATH"
godot --path .
```

The project name changed during development, but the C# assembly and namespace intentionally remain
`SandlotSlugfest` for save and code compatibility.

## Simulation and verification

The rules operate in feet and advance ball-in-play physics at a fixed 120 Hz. Pitch shape,
command, swing timing, contact location, handedness, drag, wind, fielding routes and baserunning
decisions all feed the result. A seeded xorshift generator makes verification and online play
reproducible.

Useful headless checks:

```bash
godot --headless --path . -- --sim 350         # league rates
godot --headless --path . -- --audit-outs 40  # inning and out integrity
godot --headless --path . -- --unique         # unique names, ids, faces and ratings
godot --headless --path . -- --boxes 20       # box scores agree with season totals
godot --headless --path . -- --determinism 40 # identical leagues remain identical
godot --headless --path . -- --size           # complete season at every league size
godot --headless --path . -- --careermode 40  # complete career simulations
godot --headless --path . -- --ballparks 40   # park dimensions affect results
```

GitHub Actions builds Windows, Linux AppImage and Android packages and runs gameplay integrity checks
against the desktop exports.

Version tags create permanent GitHub Releases with checksums. Signing requirements and the release
checklist are documented in [`docs/RELEASING.md`](docs/RELEASING.md).

Release preparation also includes the [`playtest checklist`](docs/PLAYTESTING.md), ready-to-edit
[`store copy and capture plan`](docs/STORE_COPY.md), and the project's [`privacy notice`](PRIVACY.md).

## Project structure

```text
Scripts/
  Core/       baseball rules, input, simulation, CPU and verification
  Data/       clubs, players, roster generation, uniforms and parks
  Season/     schedules, saves, contracts, trades, finances and development
  Cards/      collection, packs, market and card-to-season signing
  Net/        deterministic head-to-head and shared-season networking
  Stats/      box scores, splits, records, awards and Hall of Fame
  Gameplay/   live game, HUD, batting, fielding, Moments and replays
  UI/         menus, league office, career, trades and editors
Scenes/       Godot scenes and autoloads
Audio/        synthesized audio support and name callouts
packaging/    AppImage packaging
```

## Status

Ninety Feet is under active development. Expect balance changes, interface improvements and save
migrations before a stable release. Bug reports should include the platform, build commit, mode,
and what happened immediately before the problem.
