# Ninety Feet — selling it from your own site

**Price: $19.99 USD.**

## The builds

| | |
| --- | --- |
| `dist/NinetyFeet-linux-x86_64.zip` | 68 MB · run `NinetyFeet.x86_64` |
| `dist/NinetyFeet-windows-x86_64.zip` | 78 MB · run `NinetyFeet.exe` |

Both are self-contained: the .NET runtime sits in the `data_SandlotSlugfest_*` folder beside the
executable. **That folder has to travel with it.** Unzip the whole archive; the game will not start
from the executable alone, which is the usual way a Godot C# build gets broken in distribution.

Checksums, so a buyer can tell a good download from a truncated one:

```
54c44a70d917ab01aa9e1fa33969640295215ef9a3b23c22d8ea0530f839c224  NinetyFeet-linux-x86_64.zip
e539a6f8f5acc88045010b672429c01b7d7428ef25e6a132e83515b3806b1c8b  NinetyFeet-windows-x86_64.zip
```

## Rebuilding

```
dotnet build SandlotSlugfest.sln -c ExportRelease
godot471cs --headless --export-release "Linux x86_64"   build/linux/NinetyFeet.x86_64
godot471cs --headless --export-release "Windows x86_64" build/windows/NinetyFeet.exe
```

`SandlotSlugfest.sln` has to exist. Without it the export completes, reports success, exits zero,
and writes a binary with none of the game's C# in it — a program that opens a window and does
nothing. The error is buried in the log among the progress lines. Check the log, not the exit code.

## What selling it from your own site actually needs

`page/index.html` is a landing page you can upload as it stands. It has the price, what the game
is, and two download buttons pointing at `downloads/`. It is one file with no external requests,
so it will work on any host.

What it does not do — and cannot, from here — is take the $19.99.

A file on a web server is public to anybody who knows or guesses the URL, and a buy button that
links straight to the zip is a zip anybody can link to. Taking money needs two things this
repository has no way to provide:

1. **A payment processor.** Stripe Payment Links, Lemon Squeezy, or Paddle. Lemon Squeezy and
   Paddle act as merchant of record and handle sales tax and VAT for you, which for a $19.99
   download sold internationally is the difference between a side project and a tax return.
2. **A download the link does not give away.** The processor redirects the buyer to a URL you
   issue after payment — a signed link that expires, or a one-time token. Every one of the three
   above will host the file and do this for you, which is the shortest path by a distance.

So the honest shape of it: put `page/index.html` on your site, create a product with the processor,
upload the two zips to them, and point the buttons at the checkout link they give you. The page has
`data-checkout` on both buttons marking exactly where those go.

Until then the buttons link to `downloads/` and the game is free to anybody with the URL.

## What it is not

No licensed clubs and no real players. All thirty-two clubs are originals in real markets, and
every player is invented. The *baseball* is measured against the real thing; the names are ours.
