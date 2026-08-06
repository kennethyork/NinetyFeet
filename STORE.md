# Ninety Feet — store listing

**Price: $19.99 USD**

This file is copy, not configuration. The price is set in the storefront's own dashboard —
itch.io under *Edit game → Pricing*, Steam under *Store → Pricing & Release*. Nothing in this
repository can set it, and no build here is aware of it.

## Builds

| | |
| --- | --- |
| `dist/NinetyFeet-linux-x86_64.zip` | 68 MB · Linux x86_64 · run `NinetyFeet.x86_64` |
| `dist/NinetyFeet-windows-x86_64.zip` | 78 MB · Windows x86_64 · run `NinetyFeet.exe` |

Both are self-contained: the .NET runtime is packaged alongside the executable in the
`data_SandlotSlugfest_*` folder next to it. **That folder has to travel with the executable** — the
game will not start without it, which is the usual way a Godot C# build gets broken in
distribution.

Rebuild either with:

```
dotnet build SandlotSlugfest.sln -c ExportRelease
godot471cs --headless --export-release "Linux x86_64"   build/linux/NinetyFeet.x86_64
godot471cs --headless --export-release "Windows x86_64" build/windows/NinetyFeet.exe
```

The solution file matters. Without `SandlotSlugfest.sln` the export completes, reports success,
and produces a binary with none of the game's C# in it — an 87 MB program that opens a window and
does nothing.

## Short description

Arcade baseball on an honest simulation. Aim the bat, pick the pitch, and run a club for as many
seasons as you like — with a league that keeps its own books.

## Long description

**The baseball is real.** Every rate in this game is measured against 87,799 real major-league
pitches, and the numbers are in the repository for anybody who wants to check them. Runs, hits,
doubles, home runs and strikeouts all land within a few percent of the 2024 season. Not tuned by
feel — measured, and re-measured every time something changes.

**Play it or run it.** Take the bat yourself with three ways to hit and two ways to pitch, or sit
in the office and never touch a controller. Both are the same league.

**A whole organisation.** Twenty-six men on the roster and sixty-six more across three levels of
farm system, every one of them a different ballplayer — 869 men in the league and no two share a
name, a face, or a rating sheet. Contracts, arbitration, the amateur draft, free agency, a luxury
tax, injuries, morale, a hall of fame.

**A shared season.** Two people, one league, over the wire. Each owner runs a club and plays his
own games; the results cross and both machines fingerprint the entire league every night, so a
disagreement surfaces the day it happens rather than in August.

**Make it yours.** Rename and recolour any club, supply your own names for all 832 players from a
text file, rebuild any ballpark down to the fence distances — and a fence you move genuinely
changes the baseball. Choose a league of 8, 12, 16, 20, 24, 28 or 32 clubs.

## What it is not

No licensed clubs and no real players. Every one of the thirty-two is an original club in a real
market, and every player is invented. The *baseball* is measured against the real thing; the names
are ours.

Single player and two-player online. No controller support beyond the keyboard and mouse yet.
