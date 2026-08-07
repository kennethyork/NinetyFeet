#!/usr/bin/env bash
#
# Builds dist/NinetyFeet-x86_64.AppImage from the exported Linux build.
#
# The plain Linux download is an executable plus a data_SandlotSlugfest_* folder that
# has to stay beside it. That is the single most likely way somebody ends up with a
# game that will not start: they unzip, drag the program somewhere tidy, and leave the
# runtime behind. An AppImage is one file with all of it inside, marked executable and
# double-clickable on any distribution from the last decade.
#
# Run the export first:
#   dotnet build SandlotSlugfest.sln -c ExportRelease
#   godot471cs --headless --export-release "Linux x86_64" build/linux/NinetyFeet.x86_64

set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$here"

build="build/linux"
appdir="build/NinetyFeet.AppDir"
tool="${APPIMAGETOOL:-appimagetool}"

if [[ ! -x "$build/NinetyFeet.x86_64" ]]; then
  echo "No Linux build at $build. Export it first — see the header of this script." >&2
  exit 1
fi

if ! command -v "$tool" >/dev/null 2>&1 && [[ ! -x "$tool" ]]; then
  echo "appimagetool not found. Set APPIMAGETOOL to it, or put it on PATH." >&2
  echo "  https://github.com/AppImage/appimagetool/releases" >&2
  exit 1
fi

rm -rf "$appdir"
mkdir -p "$appdir/usr/bin" "$appdir/usr/share/applications" \
         "$appdir/usr/share/icons/hicolor/512x512/apps"

# The game and its runtime, kept together where they expect to be.
cp -r "$build/NinetyFeet.x86_64" "$appdir/usr/bin/"
cp -r "$build"/data_SandlotSlugfest_* "$appdir/usr/bin/"
cp LICENSE "$appdir/usr/share/LICENSE.NinetyFeet"

cp packaging/ninetyfeet.png "$appdir/usr/share/icons/hicolor/512x512/apps/ninetyfeet.png"
cp packaging/ninetyfeet.png "$appdir/ninetyfeet.png"          # appimagetool wants it at the root
cp packaging/ninetyfeet.desktop "$appdir/ninetyfeet.desktop"
cp packaging/ninetyfeet.desktop "$appdir/usr/share/applications/"

# AppRun has to cd into the binary's own directory before launching it. Godot's C#
# builds find their runtime relative to the working directory, not to the executable,
# so running it by absolute path from somewhere else fails to start the .NET host —
# which is the same "keep the folder beside it" trap this format exists to remove.
cat > "$appdir/AppRun" <<'RUN'
#!/usr/bin/env bash
here="$(dirname "$(readlink -f "$0")")"
cd "$here/usr/bin"
exec ./NinetyFeet.x86_64 "$@"
RUN
chmod +x "$appdir/AppRun"

mkdir -p dist
ARCH=x86_64 "$tool" "$appdir" "dist/NinetyFeet-x86_64.AppImage"

echo
echo "dist/NinetyFeet-x86_64.AppImage"
ls -lh dist/NinetyFeet-x86_64.AppImage
