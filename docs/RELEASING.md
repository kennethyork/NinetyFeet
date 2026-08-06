# Releasing Ninety Feet

Normal pushes build temporary Android, Windows and AppImage artifacts. A semantic version tag such
as `v0.2.0` runs the same platform tests and then creates a permanent GitHub Release containing:

- a Windows x86_64 ZIP;
- a Linux x86_64 AppImage;
- an Android arm64 APK;
- `SHA256SUMS.txt` for download verification.

Create a release only from a tested `main` commit:

```bash
git tag -a v0.2.0 -m "Ninety Feet v0.2.0"
git push origin v0.2.0
```

## Android signing

Development builds use Godot's debug key. Store-ready tag builds require these GitHub Actions
repository secrets:

- `ANDROID_KEYSTORE_BASE64`: the release keystore encoded as one base64 string;
- `ANDROID_KEY_ALIAS`: its key alias;
- `ANDROID_KEY_PASSWORD`: the keystore and key password (Godot requires them to match).

The workflow writes the keystore only into the runner's temporary directory and supplies Godot's
documented `GODOT_ANDROID_KEYSTORE_RELEASE_*` environment variables. Never commit the keystore or
password. Keep an offline backup: losing the key prevents updates to an installed Android app.

Until all three secrets are configured, tag builds clearly warn and produce a debug-signed APK
that is suitable for testing but not a store submission.

## Windows signing

The Windows ZIP is not Authenticode-signed yet. A trusted code-signing certificate and its secret
storage must be supplied before claiming that Windows builds are signed. The unsigned build remains
usable, but Windows may show a SmartScreen warning.

## Release checklist

1. Confirm the release workflow is green on `main`.
2. Test batting, pitching, saving and resume on real Windows, Linux and Android hardware.
3. Back up the Android signing key and verify the three repository secrets.
4. Tag the exact tested commit.
5. Download every release asset and verify `sha256sum -c SHA256SUMS.txt`.
6. Install each packaged build rather than testing only an editor build.
7. Add known issues and save-compatibility notes to the generated release notes.
