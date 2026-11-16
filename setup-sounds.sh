#!/bin/sh
# Abend Orchestra: sound setup for Mac and Linux.
# Downloads the Sonatina Symphonic Orchestra (418 MB) from MuseScore's soundfont mirror into soundfonts/Sonatina,
# checks it, and writes its licence next to it. Checks that FluidSynth and ffmpeg can be found.
#
#   sh setup-sounds.sh        (asks before downloading)
#   sh setup-sounds.sh --yes  (no question)
set -eu

root=$(cd "$(dirname "$0")" && pwd)
sounds="$root/soundfonts"
target="$sounds/Sonatina"
url="https://ftp.osuosl.org/pub/musescore/soundfont/Sonatina_Symphonic_Orchestra_SF2.zip"
sf2name="Sonatina_Symphonic_Orchestra.sf2"
sf2sha256="c95351ccc95947687ec78478cd45127d00e3adf75db887b767a48acdad236761"
yes=0
[ "${1:-}" = "--yes" ] && yes=1

sha256() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi; }

# ---------- Sonatina ----------
if [ -f "$target/$sf2name" ] && [ "$(sha256 "$target/$sf2name")" = "$sf2sha256" ]; then
  echo "Sonatina Symphonic Orchestra: already installed ($target/$sf2name)."
else
  echo "Sonatina Symphonic Orchestra (the orchestra for --library sonatina)"
  echo "  file:    Sonatina_Symphonic_Orchestra_SF2.zip, about 418 MB (472 MB unpacked)"
  echo "  from:    $url (MuseScore's soundfont mirror)"
  echo "  by:      Mattias Westlund; SF2 conversion by Symphony2 (musescore.org)"
  echo "  licence: Creative Commons Sampling Plus 1.0"
  go=1
  if [ "$yes" -ne 1 ]; then
    printf "Download it now? (y/n) "; read -r a
    case "$a" in y*|Y*) ;; *) echo "Skipped. Run this script again when you want it."; go=0 ;; esac
  fi
  if [ "$go" -eq 1 ]; then
    for t in curl unzip; do command -v "$t" >/dev/null 2>&1 || { echo "Needs $t: install it first."; exit 1; }; done
    tmp=$(mktemp -d)
    trap 'rm -rf "$tmp"' EXIT
    echo "Downloading (this takes a while)..."
    curl -fL --retry 3 -o "$tmp/sonatina.zip" "$url"
    echo "Unpacking..."
    rc=0; unzip -q "$tmp/sonatina.zip" -d "$tmp/x" || rc=$?
    # unzip exits 1 for mere warnings; 2 and up mean the zip could not be read
    [ "$rc" -le 1 ] || { echo "The download is not a complete zip file (try again later). Nothing was installed."; exit 1; }
    found=$(find "$tmp/x" -name '*.sf2' | head -n 1)
    [ -n "$found" ] || { echo "The download has no .sf2 file in it."; exit 1; }
    hash=$(sha256 "$found")
    [ "$hash" = "$sf2sha256" ] || { echo "The downloaded soundfont is not the tested one (SHA-256 $hash). Nothing was installed."; exit 1; }
    mkdir -p "$target"
    mv "$found" "$target/$sf2name"
    readme=$(find "$tmp/x" -name 'README*' | head -n 1)
    [ -n "$readme" ] && cp "$readme" "$target/README"
    cat > "$target/LICENSE.txt" <<EOF
Sonatina Symphonic Orchestra
by Mattias Westlund (http://sso.mattiaswestlund.net/), SF2 conversion by "Symphony2" (musescore.org).
Downloaded from $url

Licence: Creative Commons Sampling Plus 1.0
https://creativecommons.org/licenses/sampling+/1.0/

You may make new music with these samples, also commercially. When you publish music rendered with them,
show this credit and the licence link. The samples may not be used to advertise anything other than the work.
EOF
    echo "Sonatina installed in $target."
  fi
fi

# ---------- the bundled sounds (in the release download) ----------
for item in "GeneralUser-GS.sf2:default sounds" "VCSL-Flemish-Harpsichord:harpsichord" "Abend-Extras:pipe organ and accordion"; do
  p="$sounds/${item%%:*}"
  if [ -e "$p" ]; then echo "${item#*:}: found."; else echo "${item#*:}: not found ($p). It comes with the release download."; fi
done

# ---------- tools ----------
if [ "$(uname)" = "Darwin" ]; then hint="brew install fluid-synth ffmpeg"; else hint="sudo apt install fluidsynth ffmpeg   (or your system's package manager)"; fi
for t in fluidsynth ffmpeg; do
  if command -v "$t" >/dev/null 2>&1; then echo "$t: found ($(command -v "$t"))."; else echo "$t: not found. Install it with:  $hint"; fi
done
