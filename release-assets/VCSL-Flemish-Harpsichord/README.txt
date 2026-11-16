VCSL Flemish harpsichord: the harpsichord for abendc --library sonatina

What it is: the "Harpsichord, Flemish" recordings of the Versilian Community Sample Library (VCSL),
by Sam Gossner / Versilian Studios, CC0 (public domain; see LICENSE.txt). Source:
https://github.com/sgossner/VCSL  (Chordophones/Zithers/Harpsichord, Flemish)

In this file:
  preset 0   main 8' stop (the "Low" sustain recordings, 28 notes)
  preset 10  key-up noises (the matching "Low" release recordings), played by abendc at each note end

abendc uses it for every harpsichord part with --library sonatina. The VCSL English harpsichord in
../VCSL-Harpsichord/ can be chosen instead with:  --harpsichord VCSL-English-Harpsichord.sf2
(it is also used automatically if this folder is missing).

Built with Abend Orchestra's WavToSf2 (src/tools/WavToSf2.cs):
  WavToSf2 --multi VCSL-Flemish-Harpsichord.sf2 "VCSL Flemish" "0=<vcsl-flemish>/Sustains/Low" "10=rel:<vcsl-flemish>/Releases/Low:0"
