// The instructions given to other AI models (shared by the app, the command-line tool and AI-GUIDE.md).
namespace Abend
{
    public static class AiGuide
    {
        public const string Example =
@"title: Evening Waltz
composer: (your name)
tempo: 132
meter: 3/4
rit @15-17: 100

# The tune: 16 bars, every bar checked with |
track Melody | instrument: flute
\mp d5/2 b4/4 | (a4/4 g4/4 a4/4 | b4/2.) | r/4 g4/4 a4/4 |
b4/2 d5/4 | (e5/4 d5/4 c5/4 | a4/2.) | r/4 a4/4 b4/4 |
\< c5/2 e5/4 | d5/4 c5/4 b4/4 | \mf a4/2 fis4/4 | (g4/4 a4/4 b4/4 |
c5/2 e5/4 | d5/4 b4/4 g4/4 | \> a4/2 fis4/4 | \p g4/2. |

# Waltz accompaniment: rest on beat 1, chords on beats 2 and 3
chords Strings | instrument: strings | voices: 3 | range: G3-B4 | pattern: 4: r c' c'
\p G/2. | D7 | G | G | G | C | D | D7 |
\< C | G | \mp D7 | G | C | G | \> D7 | \pp G |

# Bass line on beat 1, taken from the chord's bass note
chords Cellos | instrument: cello | voices: 1 | range: D2-D3 | bass: yes | pattern: 4: B r r | velocity: 72
G/2. | D7 | G | G | G | C | D | D7 |
C | G/B | D7/A | G | C | G | D7 | G |
";

        public const string Text =
@"# Writing music for Abend Orchestra — instructions for AI models

You are writing a piece of music as a SCORE SCRIPT: a plain-text file that
Abend Orchestra turns into a multi-track MIDI piece. Answer with the complete
score inside ONE fenced code block and nothing else inside the block. The
user pastes it into the editor (File > New from score script, then Build)
or saves it as song.abscore and runs  abendc song.abscore --check  on the
command line. Errors are reported with line numbers and
say exactly what is wrong (""bar 18 is too short by a quarter (/4)""). If
the user sends errors back, fix exactly those places and resend the whole
score.

## 1. Layout of a score script

    title: Evening Waltz          # header lines first
    composer: Your Name           # optional
    tempo: 132                    # QUARTER notes per minute, in every meter
    meter: 3/4                    # beats per bar / beat unit (2, 4, 8 or 16)

    track Melody | instrument: flute        # notes, written in the notation below
    ...notation lines...

    chords Strings | instrument: strings | voices: 3 | range: G3-B4 | pattern: 4: r c c
    ...chord symbols...

Allowed headers: title, composer, tempo, meter, key (only used to check that
the piece ends on its home chord), cover, tuning (concert pitch: the
frequency of A4 in Hz, 415-466, default 440; e.g. tuning: 444), gap (the
silence after the piece in the rendered MP3, 0-10 seconds; leave it out for the
normal pause; gap: 0 for a movement that flows straight into the
next, e.g. ending on a held chord the next movement picks up), album (the
name of a multi-movement work, e.g. album: The Quiet House; it becomes the
MP3's album name, and all movements in one folder with the same album are
given one shared volume: the loudest movement plays at the target level and
softer movements stay softer, by at most 3 dB, so a finale's climax really
is the loudest moment; leave it out for single pieces), percussion
(percussion: generaluser keeps the old drum kit and timpani for the whole
score; leave it out for the orchestral ones), and style and description
(ignored).
There is NO key signature: write every sharp and flat on every note (in
D major every F is fis and every C is cis).

Cover art (optional): `cover: art.png` names a PNG or JPEG next to the script
that is embedded in the rendered MP3 and shown by music players. Without it, an
image named like the script (Song.png beside Song.abscore) or a cover.png /
cover.jpg in the same folder is used; one cover.png in a work's folder covers
all of its movements. Square images of 1000-1500 px look best.

Tempo is always counted in quarter notes. In 6/8 or 12/8, where the felt
beat is a dotted quarter, multiply the dotted-quarter speed by 1.5: a 6/8
march at 80 dotted quarters per minute is tempo: 120.

- Lines starting with # or // are comments. Blank lines are ignored.
- A block runs until the next `track` or `chords` line. One block = one
  instrument; give horns and trombones separate blocks.
- Track names may contain spaces (Piano RH, Violins I).
- Every block starts at bar 1 unless it begins with `@`. Blocks with the
  SAME name add to the same track: a `track Piano` block and a
  `chords Piano` block, or several `chords` blocks for different sections.
  The first block of a name sets its instrument, volume and pan; later
  blocks of that name may change voices, range, pattern, velocity and bass.
- `@13` jumps to bar 13. It may stand on its own line or before notes on a
  line, in track AND chord blocks. Without `@`, each new passage continues
  where the previous one ended.
- Bar numbers always count bars AS PLAYED, with repeats written out: in
  @bar, in tempo/rit/fermata lines, and in error and warning messages. If
  bars 1-20 are a repeat played twice (`[ ... ]*2`), the next bar is 41.
- Several bars per line is fine; so is one bar per line.

Optional fields on a block line (after `|`):
  instrument: name or General MIDI number 1-128 (see section 6)
  volume: 0-127 (the track's overall level, default 100)
  pan: -100 (left) to 100 (right)
  transpose: semitones (track blocks only)

## 2. Notation (track blocks)

PITCH: lower-case letter + octave number. c4 is middle C; the octave number
changes between B and C, so b3 is the B just below middle C and c5 is an
octave above middle C.
  The note B is the letter b (b3, b4); the FLAT SIGN is written es.
  sharp: add is      cis4 dis4 fis4 gis4 ais4   (double sharp: isis)
  flat:  add es      des4 ees4 ges4 aes4 bes4   (shorter: es4, as4; double: eses)
  NEVER write # or upper case in notation, and never use b as a flat sign:
  F#4 and Bb3 are errors; write fis4 and bes3. Every pitch needs its
  octave number.

DURATION: a slash and a number after the pitch.
  /1 whole  /2 half  /4 quarter  /8 eighth  /16 sixteenth  /32
  dotted /4.  double-dotted /4..   triplets: /12 = triplet eighth,
  /6 = triplet quarter, /24 = triplet sixteenth (three /12 fill one /4)
  A duration stays in force until you write another: c4/8 d4 e4 = three eighths.
  The very first note without a duration is a quarter.

RESTS  r/4  r/2.  r/1     CHORD (one track, several notes)  <c4 e4 g4>/2
TIE    d4/2~ d4/8   (same pitch, next note; works across bar lines)
SLUR   (c4/4 d4/4 e4/4)   legato phrase; parentheses touch the notes
REPEAT [ c4/8 e4/8 g4/8 e4/8 | ]*4   (may contain bar lines and dynamics;
       works in chord blocks too: \f [ D/2. | G | ]*2)

ARTICULATION, written right after the duration (also after dots):
  '  staccato    >  accent    -  tenuto      e.g. g4/8'  c5/4>  d2/2.>  e4/2-

DYNAMICS, as separate words anywhere (start of a line, mid-bar, after |):
  \ppp \pp \p \mp \mf \f \ff \fff      \< crescendo   \> diminuendo
  A hairpin runs from where it is written to the NEXT dynamic mark, so
  always close it: \p c4/4 \< d4/4 e4/4 f4/4 | \f g4/1 |
  Write the backslash: a lone < or > is read as \< or \> too, with a
  warning, but < c4 e4>/2 is still a chord and c5/4> still an accent.
  Put a dynamic at the start of every track.
  Lead into big changes with a hairpin (\< or \> over the last beat or bar):
  a jump of two levels or more in a part that keeps playing (\f to \p) sounds
  abrupt in a synthesizer. Keep sudden changes (subito, tutti entries) for
  places that are meant to surprise.

BAR LINES: write | (with spaces around it) at the end of EVERY bar. The
program checks that each bar adds up exactly.
  4/4 bar = /1 (four quarters)     3/4 = /2. (three quarters)
  2/4 = /2      6/8 = /2. (six eighths, or two dotted quarters /4. /4.)
  12/8 = four dotted quarters (/1.)   2/2 = /1
A pickup (upbeat) at the very start fills the first bar with rests:
  in 4/4: r/2. g4/4 | c5/1 |   (the tune's first strong beat is bar 2)
A pickup into a later section goes at the end of the previous bar:
  the verse ends ... | a4/2 r/4 c5/4 |  and the chorus begins on the next bar.

## 3. Chord blocks (accompaniment)

    chords Piano LH | instrument: piano | voices: 3 | range: C3-C4 | pattern: 8: 0 1 2 1 | bass: no

The body is a chord progression with durations and bar lines:
    Dm/1 | Gm/2 A7/2 | Bb/2 Gm6/Bb/2 | A7/1 | Dm/1 |
  Root: upper-case A-G, with # or b (F#, Bb, Eb, C#). In CHORD SYMBOLS the
  b IS a flat sign, unlike in notation.
  Quality: (nothing)=major  m  dim  aug  +  o  7  maj7  m7  m7b5  dim7  o7
           7b9  9  m9  maj9  add9  6  m6  sus4  sus2  7sus4  mMaj7  Fr6  5
  Bass note: slash, e.g. C/E, G7/B, D/F#, Eb/Bb.
  Duration as in notation (/1 /2 /4 /2. ...). It carries on to the next
  chord, so give the FIRST chord a duration: G/2. | D7 | G |
  =/2  holds the previous chord longer.   r/1 (or r/2. in 3/4 and 6/8) is
  a bar without chords.
  Dynamics work here too: \p Eb/1 | Cm | \< Ab | Bb7 | \f Eb |
  When a passage has dynamics, they set the loudness, and the block's
  velocity: setting is ignored.

The program voices each chord with smooth voice leading, inside `range`.
Fields:
  voices: 1-6 notes per chord (default 3)
  range: lowest-highest note, e.g. C3-G4 (default G3-C5). Either spelling
         works: F#3-D5, Eb2-Bb3, fis3-d5, ees2-bes3.
  bass: yes  makes voice 0 the chord's bass note (the slash note, or the
         root); use it for bass lines. Without it, voice 0 is simply the
         lowest chord tone the voice leading chose.
  velocity: 1-127 loudness when the passage has no dynamics (default 70;
         45 soft, 70 medium, 95 loud)
  pattern: how each chord is played, written as  note-length: tokens
    The pattern RESTARTS at every chord, repeats until the chord ends, and
    is cut off where the chord ends: a half-note chord plays the first half
    of a one-bar pattern. Tokens:
      0 1 2 ...  one voice (0 = lowest; a number above the voice count plays
                 the top voice)
      c  the whole chord     r  rest
      B  the chord's bass note in the range   b  bass an octave up
      F  the fifth above the bass
    plus optional ' (staccato) or > (accent) after a token.
    Examples:
      *: c             one sustained chord per chord symbol (pad, organ, strings)
      4: c             a chord on every beat
      8: c c c c       repeated eighths (agitated)
      8: r c           off-beat chords (classical accompaniment, ska)
      4: B c c         waltz: bass then two chords (3/4)
      4: r c' c'       waltz chords when the bass is a separate track
      8: 0 2 1 2       Alberti bass (voices: 3)
      8: 0 1 2 1       broken chord (voices: 3)
      16: 0 1 2 3      rising arpeggio (voices: 4)
      4: B F B F       bass line on roots and fifths (voices: 1, bass: yes)
      8: B b B b       octave bass in eighths (voices: 1, bass: yes)
      4.: c            one chord per dotted-quarter beat (6/8, 12/8)

## 4. Tempo changes, ritardando, fermata

These lines may go anywhere (usually after the headers):
    tempo @17: 108          sudden change at bar 17 (tempo @17.3: 108 = beat 3)
    rit @21-24: 60          slow down gradually from the start of bar 21 to
                            the start of bar 24, arriving at 60
    accel @9-12: 140        speed up gradually the same way
                            (a tempo line where a rit or accel ends, e.g.
                            tempo @24: 72 after rit @21-24: 60, sets the
                            speed from that point on)
    fermata @24.4: 2.5     hold beat 4 of bar 24 two and a half times as long
                            (beats are counted in the meter's beat unit:
                            quarters in 4/4, eighths in 6/8)
For a final held chord, write it as a whole bar and put a fermata on its
first beat, e.g. fermata @32.1: 3

## 5. Drums and timpani

Drums: `track Drums | instrument: drums`. Each pitch is a drum sound:
  c2 kick   d2 snare   e2 snare 2   dis2 hand clap   fis2 closed hi-hat
  gis2 pedal hi-hat   ais2 open hi-hat   cis3 crash   dis3 ride
  g2 low tom   b2 mid tom   c3 high tom
Grooves (one bar each):
  rock 4/4:   <c2 fis2>/8 fis2 <d2 fis2> fis2 <c2 fis2> c2 <d2 fis2> fis2 |
  ballad 4/4: <c2 dis3>/4 dis3 <d2 dis3> dis3 |
  march 6/8:  <c2 fis2>/8 fis2 fis2 <d2 fis2> fis2 fis2 |
  waltz 3/4:  c2/4 <d2 fis2> <d2 fis2> |
  snare roll (one 4/4 bar): [ d2/32 ]*32 |
  fill (4/4): d2/16 d2 d2 d2 c3 c3 b2 b2 g2/8 g2 <c2 cis3>/4 |
A crash on the first beat of a new section: put cis3 in that chord.
Drums and timpani play from orchestral recordings: c2, d2/e2, cis3,
fis3 (tambourine), gis5/a5 (muted/open triangle) and cis6 (castanets);
snare rolls written as fast repeated notes become a real roll, and timpani
sound at true pitch. Hi-hat, toms, ride, cowbell and woodblock keep the
General MIDI kit. Write  percussion: generaluser  in the header to keep the
old drum kit and timpani for a whole score.

Timpani: `instrument: timpani`, written as ordinary pitches, usually the
tonic and dominant of the key (d2 and a2 in D). The recordings cover c2-c3;
a1-b1 and cis3-f3 are bent slightly to reach, and notes beyond those play
an octave nearer (g3 sounds as g2). A roll lasting one quarter note:
[ d2/32 ]*8

There is no sustain pedal: for a pedalled sound, hold the notes longer or
add a chords block with pattern *: c underneath.

## 6. Instruments

Orchestral: flute, piccolo, oboe, english horn, clarinet, bass clarinet,
bassoon, contrabassoon, horn, trumpet, trombone, bass trombone, tuba,
timpani, harp, violin, viola, cello, contrabass, strings, pizzicato strings,
tremolo strings, choir, celesta, glockenspiel, xylophone, tubular bells
(or chimes).
Pipe organ: pipe organ (c2-cis7) for the manuals, a real church organ
recording; pipe organ pedal (c1-g4) for the pedal line, the deep General
MIDI church organ (chosen by ear over a recorded pedal; low notes are
welcome); plain ""organ"" is a simpler sound.
Accordion: a real Hohner button accordion (g2-c6).
Others: piano, electric piano, harpsichord, organ, guitar,
electric guitar, bass guitar, synth bass, saxophone (alto sax, tenor sax),
marimba, vibraphone, recorder, pad, synth lead, drums.
Or any General MIDI number 1-128 (instrument: 41 is violin).
Up to 15 instrument tracks plus drums sound distinct.

Comfortable ranges (sounding pitch):
  flute c4-c7    oboe bes3-f6    clarinet d3-bes6    bassoon bes1-e5
  horn b1-f5 (best f3-c5)    trumpet fis3-c6    trombone e2-f5    tuba d1-f4
  timpani d2-a3    violin g3-e7    viola c3-e6    cello c2-a5    contrabass e1-g3
  piano a0-c8    electric piano e1-g6    voice/choir c3-a5    guitar e2-e5
  bass guitar e1-g3    synth lead and pads c3-c7
Notes outside the orchestral ranges produce a warning.

Bass: give the piece a real bass line, moving or held, whichever suits it.
Important bass lines can be doubled an octave lower (cellos + contrabass,
or bassoon + contrabass); the contrabass goes down to e1 (41 Hz). Tuba,
organ pedal notes (church organ, c1-c2) and timpani on the root and fifth
also fill the bottom. Keep the low register clean: below about c3 use one
bass line in octaves or open fifths, not several instruments in close
position (thirds or seconds down there sound muddy).

Loudness: dynamics (or velocity: in chord blocks) shape each note; volume:
sets a whole track's level. For balance, keep accompaniment about one
dynamic level below the melody (melody mf, chords mp or velocity 55).

## 6b. Writing for the sample-library renderer (--library sonatina --expression)

Rendered with --library sonatina --expression (as Abend Radio does), every
piece plays with real orchestral recordings (the Sonatina Symphonic
Orchestra, plus a sampled Flemish harpsichord) and natural playing: swells
on long notes, smooth legato, slight human timing.
Write with these in mind:

Ranges the recordings cover (written pitch). A part with a few notes just
beyond them is stretched to fit; further out, the whole part falls back to
a plainer General MIDI sound (the render log says so):
  violins g3-c7   violas c3-c6   cellos c2-e5   contrabass c1-c4
  solo violin g3-c7   solo cello c2-c6   harp c2-c7   piano c1-c8
  flute c4-a6   piccolo c5-g7   oboe bes3-c6 (oboes to e6)
  english horn f3-f5   clarinet d3-d6   bass clarinet d2-d5
  bassoon bes1-d5   contrabassoon bes0-bes3   horn e2-e5 (horns to c5)
  trumpet e3-e6   trombone e2-b4 (trombones to e5)
  bass trombone e1-g4   tuba e1-d4   choir g2-c6   harpsichord cis1-f6
  xylophone g2-bes5   glockenspiel c3-c6   timpani c2-c3 (see section 5)
Plainer sound in any case: celesta, organ, pipe organ pedal, recorder,
tremolo strings, synths and some drums (section 5). Real recordings instead: pipe organ (c2-cis7)
and accordion (g2-c6).
Every instrument sounds at the written pitch: piccolo, glockenspiel and
xylophone are not an octave higher, contrabass and contrabassoon not an
octave lower. Write the pitch you want to hear.

- Section or soloist: a part plays the section when its track name or its
  instrument is plural (track Oboes, or track Oboe | instrument: oboes) or
  says section/tutti; otherwise the soloist. ""Solo"" in the track name
  always means the soloist (track Solo Oboe | instrument: oboes).
- Solo violin and solo cello were recorded with a wide vibrato, and every
  flute part is played by the flute section. For a calm, even line use the
  sections (Violins, Cellos) rather than solo instruments.
- There is no tremolo sound: write tremolo as repeated notes, e.g.
  [ c4/32 ]*8 is a quarter-note tremolo, [ c4/32 ]*16 a half note.
- Fast runs and passagework sound best in section parts (Violins, Flutes,
  Clarinets); the solo recordings speak slowly.
- Long held notes and chords are fine, as long as you like: they are
  continued smoothly. Held chords may use = (C/1 | = | =).
- Endings work best when they are clear: a snap, a stamp, a unison, a
  cut-off or a held chord, rather than fading out by repetition or
  stopping mid-phrase.
- Lead into big dynamic changes with a hairpin (\< or \>); held chords
  really swell or fade across the hairpin.
- tuning: sets the concert pitch for the whole piece, e.g. tuning: 444
  (A4 = 444 Hz, so C5 = 528 Hz). Leave it out for standard 440.

## 7. Checklist before you answer

1. Count every bar: the durations in each bar must add up to the meter.
   Check the LAST bar too, especially in 3/4 and 6/8 (/2. not /2).
   In 6/8 a bar has TWO dotted-quarter beats (six eighths): do not put a
   bar line after every dotted quarter. f5/4. | is half a bar; write
   f5/4. e5/8 d5/8 c5/8 | instead.
2. Pitches are lower case with is/es and an octave number: fis4, bes3.
3. Every track and chord block covers the bars it should; use @bar to start late.
4. Give each track a dynamic at its start, and the first chord a duration.
5. Write every accidental: there is no key signature.
6. Answer with ONE code block containing the whole script.

Besides errors, a build lists WARNINGS about musical sense: a track that
runs past the others or stops bars early, a passage whose first note is
later than its @bar (""@5 r/2. | ..."" really enters in bar 6), a second
block of the same name without @ (it starts again at bar 1), melody notes
on strong beats a semitone away from a held chord note, an ending that
is not on the home chord (taken from the key: header, or from the opening
bass note; an ending on another note is fine if it clearly sounds final).
With --lint, a build also gives style hints, e.g. a melody made mostly of
scale runs (5 or more notes in a row stepping the same way; a tune should
have leaps and a shape you can hum, runs only as decoration).
If the user sends warnings back, fix them unless the effect is intended.

Good results come from a clear melody with phrases that breathe, harmony
that supports it, contrast between sections and a clear ending.

## 8. Complete example

```
" + Example + @"```

## 9. Prompt for the user

Copy this into any AI chat, then describe the piece you want:

    Using the score-script format in the instructions below, write a
    [style] piece for [instruments], about [length] bars, in [key/mood].
    Reply with the whole score in one code block.
    [paste these instructions]
";
    }
}
