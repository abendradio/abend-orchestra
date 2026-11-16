# Third-party notices

Abend Orchestra runs FluidSynth and ffmpeg as separate programs, and reads soundfont (SF2) files. None of them is compiled into Abend Orchestra's code, which is MIT-licensed (see LICENSE).

## Tools

| Tool | Licence | How you get it |
|---|---|---|
| **FluidSynth** (https://www.fluidsynth.org) | GNU LGPL 2.1. Source: https://github.com/FluidSynth/fluidsynth | Windows: included in the release download, with its licence. Mac/Linux: install it with your package manager |
| **libsndfile** (sndfile.dll, part of FluidSynth's Windows build) | GNU LGPL 2.1. Source: https://github.com/libsndfile/libsndfile | Windows: included with FluidSynth |
| **SDL3** (SDL3.dll, part of FluidSynth's Windows build) | zlib licence. Source: https://github.com/libsdl-org/SDL | Windows: included with FluidSynth |
| **ffmpeg** (https://ffmpeg.org) | LGPL/GPL, depending on the build | Not included. Install it yourself (winget, Homebrew, apt); on Windows the setup script can run winget for you (`-InstallFfmpeg`) |

The licence texts are in `licenses/`, and in the Windows download also next to FluidSynth in `tools/fluidsynth/`.

## Sounds

| Sounds | Author | Licence | How you get it |
|---|---|---|---|
| **GeneralUser GS** (the default sounds) | S. Christian Collins, https://www.schristiancollins.com | GeneralUser GS License v2.0: free to use for music, private or commercial, and to include in software (its licence file is included) | Included in the release download |
| **Sonatina Symphonic Orchestra** (`--library sonatina`) | Mattias Westlund; SF2 conversion by "Symphony2" (musescore.org) | Creative Commons Sampling Plus 1.0 | **Not included.** The setup script downloads it from its original source, with its licence |
| **VCSL harpsichords** (English and Flemish), converted to SF2 with `src/tools/WavToSf2.cs` | Versilian Community Sample Library, Sam Gossner / Versilian Studios, https://github.com/sgossner/VCSL | CC0 1.0 (public domain) | Included in the release download |
| **Abend-Extras-2.sf2**: pipe organ | VCSL "Pipe Organ" (sampled by Simon Dalzell, Ivy Audio) | CC0 1.0 | Included in the release download |
| **Abend-Extras-2.sf2**: button accordion | FreePats project, "Button Accordion HN" (Hohner), https://freepats.zenvoid.org | CC0 1.0 | Included in the release download |

Music you make with these sounds is yours. If you publish music rendered with the Sonatina Symphonic Orchestra, show its credit and licence link, as its licence asks.

GeneralUser GS's author notes that, as with most free soundfonts, he can't be 100% certain of every sample's origin, and that no complaint has ever been raised. See his licence file for details.
