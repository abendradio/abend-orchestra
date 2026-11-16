// --percussion sonatina: drums and timpani played from the Sonatina orchestra's own percussion recordings instead of the
// default soundfont's drum kit (the default kit is a pop/rock kit; Sonatina's are orchestral bass drum, snare, clash
// cymbals, triangle, tambourine, castanets and timpani).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Abend
{
    public static class Percussion
    {
        // Sonatina presets: "All Percussion" (one recording per key, stereo, snare in three velocity layers) and "Timpani"
        const int Kit = 36, Timp = 40;
        const int BassP = 36, BassF = 37, SnareL = 38, SnareRoll = 39, SnareR = 40, Piatti = 45, Tamb = 52, TriMuted = 55, TriOpen = 57,
            Castanets = 61, Cabasa = 62, Shaker = 64;

        // General MIDI drum key -> kit key (-1: no orchestral recording, the part keeps the default kit for that sound)
        static int KitKey(int gm, int vel, ref bool right)
        {
            switch (gm)
            {
                case 35: case 36: return vel < 72 ? BassP : BassF;
                case 38: case 40: right = !right; return right ? SnareR : SnareL;   // alternate hands, as a drummer does
                case 49: case 52: case 55: case 57: return Piatti;
                case 54: return Tamb;
                case 80: return TriMuted;
                case 81: return TriOpen;
                case 85: return Castanets;
                case 69: return Cabasa;
                case 70: return Shaker;
                default: return -1;
            }
        }
        static bool IsSnare(int gm) { return gm == 38 || gm == 40; }

        // Sonatina recorded one octave of timpani, C2-C3, in steps of a minor third. The SF2 lays that octave out four
        // times: left-hand strokes on keys C1-C2, right-hand strokes on C3-C4, rolls on C5-C6 (so written notes in the
        // usual range land in holes or on the wrong stroke, an octave off). Real pitch q (36-48) plays these keys:
        static int LeftKey(int q) { return q - 12; }
        static int RightKey(int q) { return q + 12; }
        static int RollKey(int q) { return q + 36; }
        public const int TimpLo = 36, TimpHi = 48, StretchDown = 3, StretchUp = 5;   // beyond C2-C3: bent up to 5 semitones, else an octave moved

        public static bool Plays(Song s)
        {
            return s != null && s.Tracks.Any(t => t.Notes.Count > 0 && (t.IsDrums || t.Program == 47));
        }

        // a drum roll or timpani roll is written as fast repeated notes ([ d2/32 ]*8): four or more of one sound at most a
        // sixteenth apart become one roll
        static List<List<NoteEv>> Runs(IEnumerable<NoteEv> notes)
        {
            var runs = new List<List<NoteEv>>();
            List<NoteEv> cur = null;
            foreach (var n in notes.OrderBy(n => n.Start))
            {
                if (cur != null && n.Start - cur[cur.Count - 1].Start <= Song.PPQ / 4 + 30) cur.Add(n);
                else { cur = new List<NoteEv> { n }; runs.Add(cur); }
            }
            return runs;
        }

        // struck sounds ring for their whole recording, whatever the written length (the next stroke on the same key still stops it)
        static void Ring(Song s, NoteEv n, Sf2Info sounds, int program)
        {
            double len = sounds != null ? sounds.Sustain(0, program, n.Pitch, n.Vel) : 1.0;
            if (double.IsInfinity(len) || double.IsNaN(len)) return;
            int end = s.MsToTick(s.TickToMs(n.Start) + len * 1000);
            if (end - n.Start > n.Dur) n.Dur = end - n.Start;
        }

        // level match with the default kit (measured on drum-only and timpani-only renders: the recordings sit 2.4-5.6 dB lower
        // at the same velocities); CC7 volume follows about 40*log10(v/127)
        public const double KitGainDb = 3.5, TimpGainDb = 2.5;
        static int Boost(int volume, double db) { return Math.Min(127, (int)Math.Round(volume * Math.Pow(10, db / 40))); }

        static Track NewPart(Song s, Track from, string name, int program, Func<int> channel)
        {
            int ch = channel();
            if (ch < 0) return null;
            var p = from.Clone(); p.Name = from.Name + " (" + name + ")"; p.Channel = ch; p.Bank = Library.Bank; p.Program = program;
            if (from.IsDrums) p.Volume = Boost(from.Volume, KitGainDb);
            p.Notes = new List<NoteEv>(); p.Controls = new List<CtlEv>();
            s.Tracks.Insert(s.Tracks.IndexOf(from) + 1, p);
            return p;
        }

        public static void Apply(Song s, List<string> used, List<string> kept, Sf2Info sounds, Func<int> freeChannel)
        {
            foreach (var t in s.Tracks.Where(x => x.IsDrums && x.Notes.Count > 0).ToList()) Drums(s, t, used, kept, sounds, freeChannel);
            foreach (var t in s.Tracks.Where(x => !x.IsDrums && x.Program == 47 && x.Bank == 0 && x.Notes.Count > 0).ToList()) Timpani(s, t, used, sounds, freeChannel);
        }

        static void Drums(Song s, Track t, List<string> used, List<string> kept, Sf2Info sounds, Func<int> freeChannel)
        {
            Track kit = null, roll = null;
            bool right = false;
            int hits = 0, rolls = 0;
            var counts = new SortedDictionary<string, int>();
            var snareRuns = Runs(t.Notes.Where(n => IsSnare(n.Pitch))).Where(r => r.Count >= 4).ToList();
            foreach (var r in snareRuns)
            {
                if (roll == null) roll = NewPart(s, t, "snare rolls", Kit, freeChannel);
                if (roll == null) break;
                // one looped roll recording for the whole run; its loudness follows the written strokes (CC11 on its own channel)
                int top = r.Max(n => n.Vel);
                var last = r[r.Count - 1];
                roll.Notes.Add(new NoteEv { Start = r[0].Start, Dur = last.Start + Math.Max(last.Dur, Song.PPQ / 8) - r[0].Start, Pitch = SnareRoll, Vel = top });
                foreach (var n in r) roll.Controls.Add(new CtlEv { Tick = n.Start, Cc = 11, Val = Math.Max(1, Math.Min(127, (int)Math.Round(127.0 * n.Vel / top))) });
                foreach (var n in r) t.Notes.Remove(n);
                rolls++;
            }
            foreach (var n in t.Notes.OrderBy(n => n.Start).ToList())
            {
                int k = KitKey(n.Pitch, n.Vel, ref right);
                if (k < 0) continue;
                if (kit == null) kit = NewPart(s, t, "Sonatina percussion", Kit, freeChannel);
                if (kit == null) break;
                var m = n.Clone(); m.Pitch = k; Ring(s, m, sounds, Kit); kit.Notes.Add(m); t.Notes.Remove(n); hits++;
                string what = k == BassP || k == BassF ? "bass drum" : k == SnareL || k == SnareR ? "snare" : k == Piatti ? "cymbals" : k == Tamb ? "tambourine" : k == TriMuted || k == TriOpen ? "triangle" : "small percussion";
                int c; counts.TryGetValue(what, out c); counts[what] = c + 1;
            }
            if (rolls > 0) counts["snare rolls"] = rolls;
            if (hits + rolls > 0) used.Add(t.Name + " = Sonatina percussion (" + string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value)) + ")");
            if (t.Notes.Count > 0 && hits + rolls > 0) kept.Add(t.Name + ": " + t.Notes.Count + " stroke(s) with no orchestral recording (hi-hat, toms, ride...) keep the default kit");
        }

        static void Timpani(Song s, Track t, List<string> used, Sf2Info sounds, Func<int> freeChannel)
        {
            int folded = 0, bent = 0, rollCount = 0;
            var bendParts = new Dictionary<int, Track>();
            Track rollB = null;
            bool right = false;
            var src = t.Notes.ToList();
            t.Notes = new List<NoteEv>();
            t.Bank = Library.Bank; t.Program = Timp; t.Volume = Boost(t.Volume, TimpGainDb);
            // runs per drum (pitch): a fast alternation between two drums is strokes, not a roll
            foreach (var run in src.GroupBy(n => n.Pitch).SelectMany(g => Runs(g)).OrderBy(r => r[0].Start))
            {
                // the real pitch to play, and how far a recording at the edge of C2-C3 has to be bent to reach it
                int q = run[0].Pitch, bend = 0;
                while (q < TimpLo - StretchDown) { q += 12; folded += run.Count; }
                while (q > TimpHi + StretchUp) { q -= 12; folded += run.Count; }
                if (q < TimpLo) { bend = q - TimpLo; q = TimpLo; }
                if (q > TimpHi) { bend = q - TimpHi; q = TimpHi; }
                Track part = t;
                if (bend != 0)
                {
                    if (!bendParts.TryGetValue(bend, out part))
                    {
                        part = NewPart(s, t, "timpani bent " + (bend > 0 ? "+" : "") + bend, Timp, freeChannel);
                        if (part != null)
                        {
                            foreach (var cc in new[] { new[] { 101, 0 }, new[] { 100, 0 }, new[] { 38, 0 }, new[] { 6, 12 } }) part.Controls.Add(new CtlEv { Tick = 0, Cc = cc[0], Val = cc[1] });
                            part.Controls.Add(new CtlEv { Tick = 0, Cc = CtlEv.PitchBend, Val = 8192 + (int)Math.Round(bend * 8192 / 12.0) });
                        }
                        bendParts[bend] = part;
                    }
                    if (part == null) { part = t; bend = 0; }   // no channel left: the edge note unbent
                    else bent += run.Count;
                }
                if (run.Count >= 4)
                {
                    // a roll: the recorded roll (2.2 s), restarted on a second channel with a short overlap when it is longer
                    int top = run.Max(n => n.Vel), start = run[0].Start, end = run[run.Count - 1].Start + Math.Max(run[run.Count - 1].Dur, Song.PPQ / 8);
                    double seg = 2.0, overlap = 0.15;
                    double a = s.TickToMs(start) / 1000, b = s.TickToMs(end) / 1000;
                    bool second = false;
                    while (a < b - 0.01)
                    {
                        double e = Math.Min(b, a + seg);
                        var target = part;
                        if (second && bend == 0)
                        {
                            if (rollB == null) rollB = NewPart(s, t, "timpani roll continued", Timp, freeChannel);
                            if (rollB != null) target = rollB;
                        }
                        int st = s.MsToTick(a * 1000), en = s.MsToTick(Math.Min(b, e + (e < b ? overlap : 0)) * 1000);
                        int vel = run.Where(n => n.Start >= st - 1 && n.Start < en).Select(n => n.Vel).DefaultIfEmpty(top).Max();
                        target.Notes.Add(new NoteEv { Start = st, Dur = Math.Max(1, en - st), Pitch = RollKey(q), Vel = vel });
                        a = e; second = !second;
                    }
                    rollCount++;
                    continue;
                }
                foreach (var n in run)
                {
                    right = !right;
                    var m = n.Clone(); m.Pitch = right ? RightKey(q) : LeftKey(q);
                    Ring(s, m, sounds, Timp);
                    part.Notes.Add(m);
                }
            }
            var info = new List<string>();
            if (rollCount > 0) info.Add(rollCount + " roll(s)");
            if (bent > 0) info.Add(bent + " note(s) bent beyond the recorded C2-C3");
            if (folded > 0) info.Add(folded + " note(s) moved by an octave");
            used.Add(t.Name + " = Sonatina timpani" + (info.Count > 0 ? " (" + string.Join(", ", info) + ")" : ""));
        }
    }
}
