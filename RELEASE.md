# Ninety Feet — selling it from your own site

**Price: $19.99 USD.**

## The builds

| | |
| --- | --- |
| `dist/NinetyFeet-x86_64.AppImage` | 63 MB · Linux · **one file**, mark it executable and run it |
| `dist/NinetyFeet-linux-x86_64.zip` | 68 MB · Linux · unzip and run `NinetyFeet.x86_64` |
| `dist/NinetyFeet-windows-x86_64.zip` | 78 MB · Windows · unzip and run `NinetyFeet.exe` |

**Offer the AppImage first.** The two zips are an executable plus a
`data_SandlotSlugfest_*` folder that has to stay beside it, and that is the likeliest way
somebody ends up with a game that will not start: unzip, drag the program somewhere tidy, leave
the runtime behind. An AppImage is all of it in one file that runs on any distribution from the
last decade. `packaging/make-appimage.sh` builds it from the exported Linux build.

The proof it works is that it was run from `/` — a directory with nothing of the game in it —
and played forty games through the out audit. That is exactly the case the folder-beside-it
arrangement fails.

Checksums, so a buyer can tell a good download from a truncated one:

```
d9ca71a8aed5856f04006320b5c9c1f20980bce3606fb59651024485d9c1f8c7  NinetyFeet-linux-x86_64.zip
c47d2a6124c54125b19a4c1c8e399967469472f4f2bd24f724342828960e845d  NinetyFeet-windows-x86_64.zip
49f47ef85ec4c198e7d4c9b5b86bb1a1bcb40ca480db3f28c033d47917f83ef9  NinetyFeet-x86_64.AppImage
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

**The Windows build is made by CI, not here.** `.github/workflows/windows.yml` builds it on a real
Windows runner and then *runs* it, requiring forty games through the out audit before the artifact
is uploaded. A build exported from Linux and never started is not a build anybody should be
charged for; the zip in `dist/` is the one that came off that job and passed.

## Selling it from your own site, today

Gumroad, because you can be taking money within the hour. There is no seller review to sit
through, it hosts the two zips and delivers them itself, and it is a merchant of record — so the
VAT and sales tax are still not yours to work out, charge or file.

`page/index.html` is the whole thing now. There is no server code at all, which is the good kind
of simplification: the PHP that verified a payment and minted an expiring download link is gone,
because Gumroad does both and does not need your help. Nothing left to misconfigure, and no zips
sitting on your own server waiting to be found by URL.

To put it live:

1. **Sign up at gumroad.com.** Minutes, not days.
2. **Two products** at $19.99 — one Windows, one Linux — and upload
   `dist/NinetyFeet-windows-x86_64.zip` and `dist/NinetyFeet-linux-x86_64.zip` to them.
3. **Copy the two links.** Each product has one like `https://yourname.gumroad.com/l/permalink`.
   Put them in `index.html` where it says `YOURNAME` and the two permalinks.
4. Upload `page/index.html` anywhere. It is one static file.

The buttons open Gumroad's overlay on top of your page rather than sending anybody away, so the
checkout looks like it belongs to you. Gumroad emails the buyer a permanent download link and keeps
a library page for them, which is better than anything the expiring-link machinery was doing.

## What it costs, and what you are buying with it

Gumroad takes **10% flat**, so about $2 of each $19.99. Paddle is nearer 5% and Stripe nearer 3.5%.

That is the price of starting today rather than waiting on approval, and of not owning the tax.
Both of the cheaper options make you wait: Paddle vets sellers over several days, and Stripe is
quick but is *not* a merchant of record — VAT on a download is owed in the buyer's country from the
first sale with no small-seller threshold, so with Stripe that becomes twenty-seven possible
registrations and yours to file.

Worth revisiting once the game is actually selling. At a hundred copies the difference between
Gumroad and Paddle is about a hundred dollars a year, which is not worth a week of waiting now. At
ten thousand it is ten thousand dollars, which is.

## What it is not

No licensed clubs and no real players. All thirty-two clubs are originals in real markets, and
every player is invented. The *baseball* is measured against the real thing; the names are ours.
