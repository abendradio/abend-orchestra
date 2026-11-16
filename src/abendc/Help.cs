// Help texts and friendly error messages, shared by abendc and the editor window.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Abend
{
    public static class Help
    {
        public const string NotationGuide =
@"NOTE NAMES (lower case, octave number; c4 = middle C)
  c4 d4 e4 f4 g4 a4 b4
  sharp: add is   cis4 fis4 gis3
  flat:  add es   bes3 ees5 (or es5) aes4 (or as4)

DURATIONS (after a slash)
  /1 whole  /2 half  /4 quarter  /8 eighth  /16
  dotted: /4.   triplets: /12 (eighth) /6 (quarter)
  a duration carries on until you write a new one

RESTS  r/4     CHORDS  <c4 e4 g4>/2
TIES   d4/2~ d4/8      REPEAT  [ c4/8 e4/8 ]*4

BAR LINES  |   checked: an error tells you which
              bar does not add up

AFTER THE DURATION
  '  staccato    >  accent    -  tenuto
  (c4/4 d4/4 e4/4)   slur = legato

DYNAMICS (anywhere)
  \pp \p \mp \mf \f \ff   \< or \> = hairpin to
  the next dynamic

EXAMPLE (the motto of Per aspera)
  \mp (d4/2 bes4/2 | a4/4. g4/8 f4/4 e4/4 |
  cis4/2 d4/2) |
";

        public const string Shortcuts =
@"Space            Play / stop from the cursor
Click ruler      Move the cursor (playback start, paste point)
Click grid       Add a note (drag to set its length)
Drag note        Move it;  drag its right edge to resize
Right-click      Delete a note
Shift-click      Add to selection;  Ctrl/Shift-drag: select an area
Up / Down        Transpose selection a semitone (Shift: octave)
Left / Right     Nudge selection by the snap value
Ctrl+A / C / X / V   Select all, copy, cut, paste at cursor
Delete           Delete selection
Ctrl+Z / Ctrl+Y  Undo / redo
Ctrl+wheel       Zoom;  Shift+wheel scroll sideways
Ctrl+T           Write notation;   Ctrl+H  Harmony & accompaniment
Home             Cursor to the start";

        public static string Friendly(string msg)
        {
            // engine messages look like: "scratch part ? bar 3+240 token 'x': ..."
            msg = System.Text.RegularExpressions.Regex.Replace(msg, @"^\w+ part \? bar \d+\+\d+ token '\|': ", "");
            msg = System.Text.RegularExpressions.Regex.Replace(msg, @"^\w+ part \? bar (\d+)\+0 token '(.*?)': ", "bar $1, at \"$2\": ");
            msg = System.Text.RegularExpressions.Regex.Replace(msg, @"^\w+ part \? bar (\d+)\+\d+ token '(.*?)': ", "bar $1, at \"$2\": ");
            msg = System.Text.RegularExpressions.Regex.Replace(msg, @"^\w+ part \? ", "");
            msg = System.Text.RegularExpressions.Regex.Replace(msg, @"^\w+: harmony ", "");
            msg = msg.Replace("bar check failed: ", "");
            if (msg.Contains("cannot parse") || msg.Contains("bad chord pitch"))
                msg += "  (pitches are lower case with is/es for sharp/flat and an octave number, e.g. fis4, bes3, c5)";
            var root = System.Text.RegularExpressions.Regex.Match(msg, @"bad chord root in (\S+)");
            if (root.Success) msg = "\"" + root.Groups[1].Value + "\" is not a chord symbol: roots are A to G, with # or b (F#m, Bb7).";
            if (msg.Contains("unknown chord quality"))
                msg += "  (known: m dim aug 7 maj7 m7 m7b5 dim7 7b9 9 m9 maj9 add9 6 m6 sus2 sus4 7sus4 mMaj7 5)";
            return msg;
        }
    }
}
