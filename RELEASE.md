# Ninety Feet Release Readiness

The repository can build Windows x86_64, Linux x86_64 AppImage and Android arm64 packages. Version
tags matching `v*` package those artifacts into a permanent GitHub Release with SHA-256 checksums.
The tag version is stamped into Windows resources, Android `version/name` and the Godot application
metadata; Android's monotonically increasing `version/code` uses the Actions run number.

## Verified in source

- Debug and ExportRelease C# builds complete with zero warnings.
- Forty-game inning/out integrity, deterministic league replay and save/load fingerprints pass.
- Box scores survive save round trips and agree with season totals.
- All seven supported league sizes finish a season, playoffs, draft and next-year rollover.
- Complete Career simulations terminate normally; generated players have unique IDs, names, looks
  and rating sheets; custom ballpark dimensions affect play and malformed files are rejected.
- Native 1280×720 gameplay and interface captures are in [`marketing/`](marketing/README.md).

## Required outside the repository

- **GitHub-hosted runner access:** this repository is private. Two manual validations each waited
  exactly 15 minutes without a runner and were cancelled before any step began (`runner_id: 0`).
  GitHub bills private-repository jobs against the owner's included Actions allowance and blocks
  further use after the quota when no valid payment method is available. Check **Settings → Billing
  and licensing → Usage / Budgets**, add payment or wait for the monthly allowance to reset, or make
  the repository public if that is genuinely acceptable. Then rerun **Release builds**. See
  [GitHub's Actions billing documentation](https://docs.github.com/en/billing/concepts/product-billing/github-actions).
- **Android release key:** configure `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS` and
  `ANDROID_KEY_PASSWORD`, then keep an offline key backup. Without all three, the workflow creates
  a clearly warned debug-signed testing APK.
- **Windows signing:** purchase or supply an Authenticode certificate and secret-storage strategy.
  Current Windows ZIPs are unsigned and may show SmartScreen.
- **Physical hardware:** install the packaged artifacts and test touch on at least one phone and
  tablet, controller play on Windows/Linux, suspend/resume, clean install, update-over-old-version,
  per-mode autosave, and recovery from process termination. No connected Android device, tablet or
  Windows signing certificate is available in this development environment.
- **Store accounts and declarations:** complete each store's identity, tax, content-rating, privacy,
  pricing and support-contact requirements. Do not claim hosted matchmaking or licensed players.

Do not sell or tag a release merely because a local editor build runs. Follow
[`docs/RELEASING.md`](docs/RELEASING.md) and [`docs/PLAYTESTING.md`](docs/PLAYTESTING.md), rerun the
three-platform workflow, install its exact artifacts, and tag only the commit that passed.
