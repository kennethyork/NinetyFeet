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
d9ca71a8aed5856f04006320b5c9c1f20980bce3606fb59651024485d9c1f8c7  NinetyFeet-linux-x86_64.zip
242a206b7671730acec043c88a447a70992bc1b42ba0b8a589b395320f2a1d83  NinetyFeet-windows-x86_64.zip
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

## Selling it from your own site, with PayPal

Four files in `page/`:

| | |
| --- | --- |
| `index.html` | the page, with PayPal buttons at $19.99 |
| `verify.php` | asks PayPal whether an order was really paid, and issues the link |
| `download.php` | streams the zip if the link is one you issued and has not lapsed |
| `config.example.php` | copy to `config.php` and fill in |

To put it live:

1. **PayPal credentials.** developer.paypal.com → Apps & Credentials → Live. Put the client id in
   `index.html` where it says `CLIENT_ID`, and both the id and the secret in `config.php`. The
   client id is public and belongs in the page; the secret must never reach a browser.
2. **Put the zips outside your web root.** `config.php` points at them by absolute path. If they
   sit in a public folder then every check below is theatre — the file answers to its own URL and
   nobody needs to buy anything.
3. **A link secret.** `openssl rand -hex 32` into `link_secret`.
4. Upload `page/` to your site. PHP 8 and cURL, which any shared host has.

**Why there is a server part at all.** A page that reveals the download when PayPal's JavaScript
reports success reveals it to anybody who opens the console and calls that function. The browser is
the buyer's, not yours, and nothing it says about a payment is evidence. So the order id is the only
thing that crosses, and `verify.php` asks PayPal directly: is this order COMPLETED, in USD, for at
least 19.99? Only then does it mint a download link — signed, tied to one order and one platform,
and dead in two hours.

## Tax: PayPal will not do this for you

You asked for the tax to be automatic. With PayPal it cannot be, and it is worth knowing exactly
why before you sell anything.

PayPal is a **payment processor**, not a merchant of record. It moves money and stops there. You
remain the seller, which means the tax on every sale is yours to work out, collect, declare and
pay. What PayPal offers is a table of rates you configure yourself in your account, and
`config.php` has a `tax_rates` hook to match — but you are setting those rates, keeping them
current, and filing against them.

That matters most for digital goods sold abroad. EU and UK VAT on a download is owed in the
**buyer's** country, from the very first sale, with no small-seller threshold for a seller outside
those territories. Twenty-seven possible rates, and a registration to go with them.

If "automatic" is a requirement rather than a preference, the processor has to be a **merchant of
record** — it sells to the customer, you sell to it, and the tax becomes its problem. That is
Paddle, Lemon Squeezy or FastSpring. All three also host the file and issue the gated link, which
would make `verify.php` and `download.php` unnecessary.

So: PayPal is built here as asked, and it is the option where the tax is manual. Say the word and
the same page can point at Paddle or Lemon Squeezy instead — it is a smaller change than this one
was, because their checkout is a link rather than an integration.

## What it is not

No licensed clubs and no real players. All thirty-two clubs are originals in real markets, and
every player is invented. The *baseball* is measured against the real thing; the names are ours.
