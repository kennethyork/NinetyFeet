# Android release checklist

## Required devices

Run the installable release APK on at least one phone and one tablet. Record model, Android version,
resolution, refresh rate and build commit. Include a narrow/notched phone and a wide phone before a
store rollout.

## First five minutes

- Fresh install opens in landscape and every title-screen choice responds to one tap.
- Start **Learn to Play** without reading separate instructions.
- Drag to aim, swing, select a pitch, deal, field, throw, pause and resume.
- Background the app during play, reopen it, and verify progress was saved.
- Force-stop and reopen; **Continue Season** must return to the occupied slot.

## Presentation and performance

- The score, count, ball, runners and selected defender are readable at arm's length.
- No button touches a notch, rounded corner or Android gesture area.
- Deep fly balls remain in the following camera and touch fielding points at the intended grass.
- Play twenty minutes with no sustained stutter, overheating, audio breakup or battery warning.
- Repeat with large text, high contrast, reduced motion and vibration off.

## Store build

- Confirm `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS` and `ANDROID_KEY_PASSWORD` are present.
- Back up the upload key offline before the first Play upload.
- Download the workflow AAB and APK; verify `SHA256SUMS.txt`.
- Upload the AAB to Play internal testing before production.
- Complete the privacy, data-safety, content-rating, target-audience and app-access declarations.
- Verify screenshots and trailer contain only fictional game content and no private addresses.

## Bug report evidence

Attach reproduction steps, a screenshot/video and the newest local Godot log from the app's data
directory. Logs remain on the device until the player deliberately shares them; the game contains
no analytics or telemetry uploader.
