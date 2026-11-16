# Abend Orchestra: sound setup for Windows.
# Downloads the Sonatina Symphonic Orchestra (418 MB) from MuseScore's soundfont mirror into soundfonts\Sonatina,
# checks it, and writes its licence next to it. Checks that FluidSynth and ffmpeg can be found.
#
#   powershell -ExecutionPolicy Bypass -File setup-sounds.ps1            (asks before downloading)
#   powershell -ExecutionPolicy Bypass -File setup-sounds.ps1 -Yes       (no question)
#   powershell -ExecutionPolicy Bypass -File setup-sounds.ps1 -InstallFfmpeg   (also installs ffmpeg with winget)
param([switch]$Yes, [switch]$InstallFfmpeg)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = $PSScriptRoot
$sounds = Join-Path $root "soundfonts"
$target = Join-Path $sounds "Sonatina"
$url = "https://ftp.osuosl.org/pub/musescore/soundfont/Sonatina_Symphonic_Orchestra_SF2.zip"
$sf2Name = "Sonatina_Symphonic_Orchestra.sf2"
$sf2Sha256 = "c95351ccc95947687ec78478cd45127d00e3adf75db887b767a48acdad236761"

function Say($text) { Write-Host $text }

# ---------- Sonatina ----------
$have = Join-Path $target $sf2Name
if ((Test-Path $have) -and ((Get-FileHash $have -Algorithm SHA256).Hash.ToLower() -eq $sf2Sha256)) {
    Say "Sonatina Symphonic Orchestra: already installed ($have)."
} else {
    Say "Sonatina Symphonic Orchestra (the orchestra for --library sonatina)"
    Say "  file:    Sonatina_Symphonic_Orchestra_SF2.zip, about 418 MB (472 MB unpacked)"
    Say "  from:    $url (MuseScore's soundfont mirror)"
    Say "  by:      Mattias Westlund; SF2 conversion by Symphony2 (musescore.org)"
    Say "  licence: Creative Commons Sampling Plus 1.0"
    if (-not $Yes) {
        $a = Read-Host "Download it now? (y/n)"
        if ($a -notmatch '^[yY]') { Say "Skipped. Run this script again when you want it."; $skip = $true }
    }
    if (-not $skip) {
        $tmp = Join-Path ([IO.Path]::GetTempPath()) ("abend-sonatina-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
        New-Item -ItemType Directory -Force $tmp | Out-Null
        try {
            $zip = Join-Path $tmp "sonatina.zip"
            Say "Downloading (this takes a while)..."
            $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
            if ($curl) { & $curl.Source -fL --retry 3 -o $zip $url; if ($LASTEXITCODE -ne 0) { throw "Download failed." } }
            else { Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing }
            Say "Unpacking..."
            try { Expand-Archive -Path $zip -DestinationPath (Join-Path $tmp "x") -Force } catch { throw "The download is not a complete zip file (try again later). Nothing was installed." }
            $found = Get-ChildItem (Join-Path $tmp "x") -Recurse -Filter *.sf2 | Select-Object -First 1
            if (-not $found) { throw "The download has no .sf2 file in it." }
            $hash = (Get-FileHash $found.FullName -Algorithm SHA256).Hash.ToLower()
            if ($hash -ne $sf2Sha256) { throw "The downloaded soundfont is not the tested one (SHA-256 $hash). Nothing was installed." }
            New-Item -ItemType Directory -Force $target | Out-Null
            Move-Item -Force $found.FullName (Join-Path $target $sf2Name)
            $readme = Get-ChildItem (Join-Path $tmp "x") -Recurse -Filter README* | Select-Object -First 1
            if ($readme) { Copy-Item -Force $readme.FullName (Join-Path $target "README") }
            Set-Content -Encoding UTF8 (Join-Path $target "LICENSE.txt") @"
Sonatina Symphonic Orchestra
by Mattias Westlund (http://sso.mattiaswestlund.net/), SF2 conversion by "Symphony2" (musescore.org).
Downloaded from $url

Licence: Creative Commons Sampling Plus 1.0
https://creativecommons.org/licenses/sampling+/1.0/

You may make new music with these samples, also commercially. When you publish music rendered with them,
show this credit and the licence link. The samples may not be used to advertise anything other than the work.
"@
            Say "Sonatina installed in $target."
        } finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
    }
}

# ---------- the bundled sounds (in the release download) ----------
foreach ($item in @(@("GeneralUser-GS.sf2", "default sounds"), @("VCSL-Flemish-Harpsichord", "harpsichord"), @("Abend-Extras", "pipe organ and accordion"))) {
    $p = Join-Path $sounds $item[0]
    if (Test-Path $p) { Say ("{0}: found." -f $item[1]) } else { Say ("{0}: not found ({1}). It comes with the release download." -f $item[1], $p) }
}

# ---------- tools ----------
function FindTool($name) {
    $c = Get-Command "$name.exe" -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    $t = Join-Path $root "tools"
    if (Test-Path $t) { $f = Get-ChildItem $t -Recurse -Filter "$name.exe" -ErrorAction SilentlyContinue | Select-Object -First 1; if ($f) { return $f.FullName } }
    return $null
}
$fs = FindTool "fluidsynth"
if ($fs) { Say "FluidSynth: found ($fs)." } else { Say "FluidSynth: not found. It comes with the release download (tools\fluidsynth)." }
$ff = FindTool "ffmpeg"
if ($ff) { Say "ffmpeg: found ($ff)." }
elseif ($InstallFfmpeg) {
    Say "Installing ffmpeg with winget..."
    & winget install --id Gyan.FFmpeg -e --accept-package-agreements
    Say "Open a new terminal window so ffmpeg is found."
}
else { Say "ffmpeg: not found. Install it with:  winget install --id Gyan.FFmpeg -e   (or run this script with -InstallFfmpeg)" }
