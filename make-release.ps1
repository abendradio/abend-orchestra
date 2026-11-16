# Builds the release downloads into .\dist:
#   abend-orchestra-<version>-windows.zip   abendc.exe + the editor window (.NET Framework 4.8, nothing to install),
#                                           FluidSynth, the bundled sounds
#   abend-orchestra-<version>-portable.zip  abendc for Mac and Linux (needs the .NET 8 runtime, FluidSynth and ffmpeg),
#                                           the bundled sounds
# plus SHA256SUMS.txt. The sounds and FluidSynth are not in the repo; -Assets names a folder that holds
#   soundfonts\GeneralUser-GS.sf2, soundfonts\GeneralUser-GS-LICENSE.txt, soundfonts\VCSL-Flemish-Harpsichord\,
#   soundfonts\VCSL-Harpsichord\, soundfonts\Abend-Extras\, and tools\fluidsynth\<build>\bin\ (Windows FluidSynth).
#
#   powershell -ExecutionPolicy Bypass -File make-release.ps1 -Assets "D:\abend-assets" [-Version 1.0.0]
param([Parameter(Mandatory = $true)][string]$Assets, [string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force $dist | Out-Null

# ---------- build ----------
& powershell -ExecutionPolicy Bypass -File (Join-Path $root "build-windows.ps1")
if ($LASTEXITCODE -ne 0) { throw "Windows build failed" }
& (Join-Path $root "bin\tests.exe") | Select-Object -Last 1
if ($LASTEXITCODE -ne 0) { throw "tests failed" }
$pub = Join-Path $dist "portable-bin"
& dotnet publish (Join-Path $root "src\abendc\abendc.csproj") -c Release -o $pub -p:UseAppHost=false -p:DebugType=none --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "portable build failed" }

# ---------- what both downloads share ----------
function CopyCommon($to) {
    foreach ($f in @("README.md", "LICENSE", "THIRD-PARTY-NOTICES.md")) { Copy-Item (Join-Path $root $f) $to }
    Copy-Item -Recurse (Join-Path $root "docs") (Join-Path $to "docs")
    Copy-Item -Recurse (Join-Path $root "examples") (Join-Path $to "examples")
    Copy-Item -Recurse (Join-Path $root "licenses") (Join-Path $to "licenses")
    $sf = Join-Path $to "soundfonts"; New-Item -ItemType Directory -Force $sf | Out-Null
    $src = Join-Path $Assets "soundfonts"
    foreach ($f in @("GeneralUser-GS.sf2", "GeneralUser-GS-LICENSE.txt")) {
        $p = Join-Path $src $f; if (-not (Test-Path $p)) { throw "missing asset: $p" }; Copy-Item $p $sf
    }
    $want = @{
        "VCSL-Flemish-Harpsichord" = @("VCSL-Flemish-Harpsichord.sf2", "LICENSE.txt", "README.txt");
        "VCSL-Harpsichord"         = @("VCSL-English-Harpsichord.sf2", "LICENSE.txt", "VCSL-README.md");
        "Abend-Extras"             = @("Abend-Extras-2.sf2", "README.txt")
    }
    foreach ($d in $want.Keys) {
        $dd = Join-Path $sf $d; New-Item -ItemType Directory -Force $dd | Out-Null
        foreach ($f in $want[$d]) { $p = Join-Path (Join-Path $src $d) $f; if (-not (Test-Path $p)) { throw "missing asset: $p" }; Copy-Item $p $dd }
    }
}

function Zip($folder) {
    # Windows' own tar (bsdtar) writes standard zips with / in paths (Compress-Archive in PowerShell 5.1 writes
    # backslashes, which unzip on Mac and Linux warns about)
    $zip = "$folder.zip"
    $tar = Join-Path $env:WINDIR "System32\tar.exe"
    Push-Location (Split-Path $folder)
    try { & $tar -a -c -f $zip (Split-Path $folder -Leaf); if ($LASTEXITCODE -ne 0) { throw "zip failed: $zip" } } finally { Pop-Location }
    return $zip
}

# ---------- Windows ----------
$win = Join-Path $dist "abend-orchestra-$Version-windows"
New-Item -ItemType Directory -Force $win | Out-Null
Copy-Item (Join-Path $root "bin\abendc.exe"), (Join-Path $root "bin\AbendOrchestra.exe") $win
Copy-Item (Join-Path $root "setup-sounds.ps1") $win
CopyCommon $win
$fsBin = Get-ChildItem (Join-Path $Assets "tools\fluidsynth") -Recurse -Filter fluidsynth.exe | Select-Object -First 1
if (-not $fsBin) { throw "missing asset: tools\fluidsynth\...\fluidsynth.exe" }
$fsTo = Join-Path $win "tools\fluidsynth"; New-Item -ItemType Directory -Force $fsTo | Out-Null
Get-ChildItem $fsBin.DirectoryName -File | Where-Object { $_.Extension -in ".exe", ".dll" } | Copy-Item -Destination $fsTo
Copy-Item (Join-Path $root "licenses\LGPL-2.1.txt") (Join-Path $fsTo "LICENSE-LGPL-2.1.txt")
Copy-Item (Join-Path $root "licenses\zlib-SDL3.txt") (Join-Path $fsTo "LICENSE-SDL3.txt")
Set-Content -Encoding UTF8 (Join-Path $fsTo "README.txt") @"
FluidSynth $($fsBin.Directory.Parent.Name -replace '^fluidsynth-v?', '') for Windows, unmodified, as released by the FluidSynth project.
Abend Orchestra runs it as a separate program (fluidsynth.exe); it is not part of Abend Orchestra's code.

FluidSynth (fluidsynth.exe, libfluidsynth-3.dll): GNU LGPL 2.1 or later, see LICENSE-LGPL-2.1.txt.
  Source code: https://github.com/FluidSynth/fluidsynth (releases: https://github.com/FluidSynth/fluidsynth/releases)
libsndfile (sndfile.dll): GNU LGPL 2.1 or later, see LICENSE-LGPL-2.1.txt.
  Source code: https://github.com/libsndfile/libsndfile
SDL3 (SDL3.dll): zlib licence, see LICENSE-SDL3.txt.
  Source code: https://github.com/libsdl-org/SDL
"@
$zips = @(Zip $win)

# ---------- portable (Mac and Linux) ----------
$por = Join-Path $dist "abend-orchestra-$Version-portable"
New-Item -ItemType Directory -Force $por | Out-Null
Get-ChildItem $pub -File | Copy-Item -Destination $por
Copy-Item (Join-Path $root "setup-sounds.sh") $por
$launcher = "#!/bin/sh`n# runs abendc with the .NET 8 runtime (https://dotnet.microsoft.com/download/dotnet/8.0)`nexec dotnet `"`$(dirname `"`$0`")/abendc.dll`" `"`$@`"`n"
[IO.File]::WriteAllText((Join-Path $por "abendc"), $launcher)
CopyCommon $por
$zips += Zip $por

# ---------- checksums ----------
$sums = $zips | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLower(), (Split-Path $_ -Leaf) }
[IO.File]::WriteAllText((Join-Path $dist "SHA256SUMS.txt"), (($sums -join "`n") + "`n"))
Remove-Item -Recurse -Force $pub
$zips | ForEach-Object { "{0}  {1:N1} MB" -f (Split-Path $_ -Leaf), ((Get-Item $_).Length / 1MB) }
