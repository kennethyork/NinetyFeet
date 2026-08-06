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

## Selling it from your own site, with Paddle

Four files in `page/`:

| | |
| --- | --- |
| `index.html` | the page, with a Paddle checkout |
| `verify.php` | asks Paddle whether a transaction really completed, and issues the link |
| `download.php` | streams the zip if the link is one you issued and has not lapsed |
| `config.example.php` | copy to `config.php` and fill in |

To put it live:

1. **A Paddle account, verified.** Paddle reviews sellers before going live, which takes a
   few days — start it before you need it.
2. **A product and two prices** at $19.99, one per platform, so a receipt says which build was
   bought. Copy the `pri_…` ids into both `config.php` and `prices` in `index.html`. They must
   match, or the page will happily take money for a transaction the server then refuses.
3. **Credentials.** Developer tools → Authentication. The client-side token goes in `index.html`
   where it says `CLIENT_TOKEN`; the API key goes in `config.php` and must never reach a browser.
4. **Put the zips outside your web root.** `config.php` points at them by absolute path. If they
   sit in a public folder then every check below is theatre — the file answers to its own URL and
   nobody needs to buy anything.
5. **A link secret.** `openssl rand -hex 32` into `link_secret`.
6. Upload `page/` to your site. PHP 8 and cURL, which any shared host has.

**Why there is a server part at all.** A page that reveals the download when Paddle's JavaScript
reports success reveals it to anybody who opens the console and calls that callback. The browser is
the buyer's, not yours, and nothing it says about a payment is evidence. So the transaction id is
the only thing that crosses, and `verify.php` asks Paddle directly: did this transaction complete,
and was it for this price? Only then does it mint a link — signed, tied to one transaction and one
platform, dead in two hours. The buyer's Paddle receipt carries a permanent one, so the short life
of this link costs them nothing.

`verify.php` deliberately does **not** check the amount. Paddle sets the customer's local price,
adds their tax, and may apply a discount you created, so the total legitimately differs from
$19.99. The price id is what identifies the thing bought, and that is what is checked.

## Tax: this is the reason for Paddle

Paddle is a **merchant of record**, not merely a payment processor. It sells to the customer and
you sell to Paddle. So the VAT or sales tax on every order — worked out for wherever the buyer is,
charged at checkout, declared and filed — is Paddle's obligation rather than yours. That is the
whole difference, and it is why `config.php` has no tax table in it: a second, hand-maintained
answer could only ever be a wrong one.

It matters most for exactly this product. VAT on a download is owed in the buyer's country from
the first sale, with no small-seller threshold for a seller outside the EU or UK. Twenty-seven
possible rates, each with a registration, is not a thing to take on beside writing a baseball game.

Paddle takes a cut for it — around 5% plus 50c on a transaction of this size at the time of
writing, against roughly 3.5% for a bare processor. The difference is the price of not filing VAT
returns in twenty-seven countries.

**If you would rather not write any server code at all**, Lemon Squeezy is the same company, is
also merchant of record, and hosts the file and emails the download link itself — which would make
`verify.php` and `download.php` unnecessary and reduce this to a link on a button. Paddle is built
here because it is what you asked for, and it is the better fit if you ever want the checkout
inside your own page rather than on somebody else's.

## What it is not

No licensed clubs and no real players. All thirty-two clubs are originals in real markets, and
every player is invented. The *baseball* is measured against the real thing; the names are ours.
