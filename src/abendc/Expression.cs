// "Natural playing" for audio renders (abendc render --expression): swells, legato, delayed vibrato and
// small human variations. Works on a copy of the song; the same piece always gets the same variations.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Abend
{
    public static class Expression
    {
        // instruments that hold their sound (bowed, blown, sung, organ); plucked and struck ones only get humanised
        public static bool Sustains(Track t)
        {
            if (t.IsDrums) return false;
            int p = t.Program;
            return (p >= 16 && p <= 23) || (p >= 40 && p <= 44) || (p >= 48 && p <= 54) || (p >= 56 && p <= 79);
        }

        static bool Vibrato(Track t) { return Sustains(t) && !(t.Program >= 16 && t.Program <= 23); }   // not organs

        // stable across runs and machines (string.GetHashCode is not guaranteed to be)
        static int Seed(string s)
        {
            unchecked { uint h = 2166136261; foreach (char c in s ?? "") { h ^= c; h *= 16777619; } return (int)(h & 0x7FFFFFFF); }
        }

        static int MsTicks(Song s, int at, double ms) { return Math.Max(1, s.MsToTick(s.TickToMs(at) + ms) - at); }

        // Terraced dynamics (\f straight to \p, no hairpin) switch instantly in a synthesizer; players ease into them. When a
        // part that keeps playing jumps by two dynamic levels or more (velocity 18+) after a steady stretch, the change is
        // spread over one beat: the first note lands halfway and the part reaches the new level by the end of the beat.
        // A part that enters after a rest still starts at its written dynamic.
        static void EaseTerraces(Song s, Track t)
        {
            int beat = Song.PPQ * 4 / s.Den;
            var groups = t.Notes.GroupBy(n => n.Start).OrderBy(g => g.Key).Select(g => new { at = g.Key, end = g.Max(n => n.Start + n.Dur), notes = g.ToList(), vel = g.Average(n => n.Vel) }).ToList();
            for (int k = 2; k < groups.Count; k++)
            {
                double before = groups[k - 1].vel, jump = groups[k].vel - before;
                if (Math.Abs(jump) < 18 || Math.Abs(before - groups[k - 2].vel) >= 6) continue;   // small change, or already moving (a hairpin)
                if (groups[k].at - groups[k - 1].end > beat) continue;                             // after a rest: a new entry
                foreach (var g in groups.Skip(k).TakeWhile(g => g.at < groups[k].at + beat))
                {
                    double f = (g.at - groups[k].at) / (double)beat;
                    int shift = (int)Math.Round(-jump * 0.5 * (1 - f));
                    foreach (var n in g.notes) n.Vel = Math.Max(1, Math.Min(127, n.Vel + shift));
                }
            }
        }

        public static Song Apply(Song song)
        {
            var s = song.Clone();
            var rnd = new Random(Seed(s.Title));
            var written = new Dictionary<NoteEv, int>();   // each note's written onset, before the human timing moves it
            int half = Song.PPQ * 2;
            foreach (var t in s.Tracks)
            {
                t.Notes.Sort((a, b) => a.Start.CompareTo(b.Start));
                if (t.Notes.Count == 0) continue;

                // 1. humanise: chords move together (onset +-8 ms), each note's loudness +-4
                foreach (var g in t.Notes.GroupBy(n => n.Start).ToList())
                {
                    int span = MsTicks(s, g.Key, 8);
                    int shift = g.Key == 0 ? 0 : rnd.Next(-span, span + 1);
                    foreach (var n in g) { written[n] = n.Start; n.Start = Math.Max(0, n.Start + shift); n.Vel = Math.Max(1, Math.Min(127, n.Vel + rnd.Next(-4, 5))); }
                }
                t.Notes.Sort((a, b) => a.Start.CompareTo(b.Start));
                bool shaped = Sustains(t) && t.Dyn != null && t.Dyn.Count > 0;   // dynamics shaped continuously below
                if (!t.IsDrums && !shaped) EaseTerraces(s, t);
                if (!Sustains(t)) continue;

                // 2. legato: in a single melodic line, notes that touch overlap by about 30 ms (not repeated pitches)
                for (int i = 0; i + 1 < t.Notes.Count; i++)
                {
                    var a = t.Notes[i]; var b = t.Notes[i + 1];
                    if (b.Start == a.Start || (i + 2 < t.Notes.Count && t.Notes[i + 2].Start == b.Start)) continue;   // chords
                    if (i > 0 && t.Notes[i - 1].Start + t.Notes[i - 1].Dur > a.Start) continue;
                    int gap = b.Start - (a.Start + a.Dur);
                    if (b.Pitch != a.Pitch && gap >= -Song.PPQ / 32 && gap <= Song.PPQ / 16) a.Dur = b.Start - a.Start + MsTicks(s, b.Start, 30);
                }

                // 3. swells (CC11) on held notes and chords, and vibrato (CC1) that starts a moment into long notes
                var onsets = t.Notes.Select(n => n.Start).Distinct().OrderBy(x => x).ToList();
                bool vib = Vibrato(t);
                for (int k = 0; k < onsets.Count; k++)
                {
                    int on = onsets[k], next = k + 1 < onsets.Count ? onsets[k + 1] : int.MaxValue;
                    int dur = t.Notes.Where(n => n.Start == on).Max(n => n.Dur);
                    int end = Math.Min(on + dur, next);
                    if (dur < half || end - on < half)
                    {
                        t.Controls.Add(new CtlEv { Tick = on, Cc = 11, Val = 120 });
                        if (vib) t.Controls.Add(new CtlEv { Tick = on, Cc = 1, Val = 0 });
                        continue;
                    }
                    int len = end - on, step = Math.Max(Song.PPQ / 8, len / 24);
                    for (int x = on; x < end; x += step)
                    {
                        double f = (x - on) / (double)len;
                        double v = f < 0.4 ? 110 + 17 * Math.Sin(f / 0.4 * Math.PI / 2) : 127 - 9 * (f - 0.4) / 0.6;   // rise to the middle, ease off (about 2.5 dB in all)
                        t.Controls.Add(new CtlEv { Tick = x, Cc = 11, Val = (int)Math.Round(v) });
                    }
                    if (vib)
                    {
                        t.Controls.Add(new CtlEv { Tick = on, Cc = 1, Val = 0 });
                        int from = on + MsTicks(s, on, 300), to = from + MsTicks(s, from, 400);
                        for (int x = from; x <= to && x < end; x += Math.Max(1, (to - from) / 6))
                            t.Controls.Add(new CtlEv { Tick = x, Cc = 1, Val = (int)Math.Round(26.0 * (x - from) / Math.Max(1, to - from)) });
                    }
                }
                if (shaped) ContinuousDynamics(s, t, written);
            }
            return s;
        }

        // A note's velocity is fixed when it starts, so written dynamics became a staircase: a hairpin or a slow breathing
        // swell over held or repeated chords jumped at every new note (Sonatina's sustained sounds have one velocity layer,
        // so a velocity step is purely a volume step). For sustaining parts with written dynamics, the written curve is
        // played continuously with CC11 instead:
        //  - the notes of one phrase (up to a rest) share one velocity, the loudest the curve reaches in it, plus each
        //    note's own accent and small human variation
        //  - CC11 = (the swell on long notes) x (written level now / that loudest level), every 1/8 beat
        //  - a written change inside a phrase (one level or more, since 2026-10-28) eases over two beats; the swell steps aside where a
        //    hairpin is shaping the sound, so the two shapes don't fight
        // a note's accent is judged against the level written at its WRITTEN onset: the human timing can move an entry a few ms
        // before its own dynamic mark, and the level there (the old one) made the change count twice (2026-10-30)
        static int WrittenStart(NoteEv n, Dictionary<NoteEv, int> written) { int w; return written.TryGetValue(n, out w) ? w : n.Start; }

        static void ContinuousDynamics(Song s, Track t, Dictionary<NoteEv, int> written)
        {
            var curve = t.Dyn.OrderBy(m => m.Time).ToList();
            int beat = Song.PPQ * 4 / s.Den, step = Math.Max(1, Song.PPQ / 8);
            Func<int, double> raw = x => 16 + 13 * Sym.Render.LevelAt(curve, x);   // the velocity the notation gives at a moment
            // (a note that starts just before the mark, moved there by the human timing, is the entry itself, not the part playing on)
            Func<int, bool> sounding = x => t.Notes.Any(n => n.Start < x - beat / 4 && n.Start + n.Dur > x - beat / 2);
            // every written change (one level or more, no hairpin) while the part is sounding eases over two beats from where
            // the sound was, evenly in dB (velocity and CC11 scale the sound as a power, so even steps of log(level) are even dB)
            int ease = 2 * beat;
            var jumps = new List<double[]>();   // time, velocity just before
            Func<int, double> level = x =>
            {
                for (int j = jumps.Count - 1; j >= 0; j--)
                    if (x >= jumps[j][0]) { double f = (x - jumps[j][0]) / ease; return f < 1 ? jumps[j][1] * Math.Pow(raw(x) / jumps[j][1], f) : raw(x); }
                return raw(x);
            };
            for (int i = 1; i < curve.Count; i++)
                if (!curve[i - 1].Ramp && Math.Abs(curve[i].Level - curve[i - 1].Level) * 13 >= 6 && sounding(curve[i].Time))
                    jumps.Add(new double[] { curve[i].Time, level(curve[i].Time - 1) });
            Func<int, bool> inHairpin = x => { for (int i = 0; i + 1 < curve.Count; i++) if (curve[i].Ramp && x >= curve[i].Time && x < curve[i + 1].Time && curve[i].Level != curve[i + 1].Level) return true; return false; };
            var swell = t.Controls.Where(k => k.Cc == 11).OrderBy(k => k.Tick).ToList();
            Func<int, int> swellAt = x => { int v = 127; foreach (var k in swell) { if (k.Tick > x) break; v = k.Val; } return v; };

            // phrases: notes joined while the next starts within half a beat of the end of what is sounding
            var notes = t.Notes.OrderBy(n => n.Start).ToList();
            var shaped = new List<CtlEv>();
            for (int i = 0; i < notes.Count; )
            {
                int from = notes[i].Start, to = notes[i].Start + notes[i].Dur, k = i + 1;
                while (k < notes.Count && notes[k].Start <= to + beat / 2) { to = Math.Max(to, notes[k].Start + notes[k].Dur); k++; }
                // levels are read from the written onset on (the moved onset may sit just before the mark)
                int w0 = Math.Max(from, WrittenStart(notes[i], written));
                double top = 1;
                for (int x = from; x <= to; x += step) top = Math.Max(top, level(Math.Max(x, w0)));
                for (int q = i; q < k; q++) notes[q].Vel = Math.Max(1, Math.Min(127, (int)Math.Round(top + (notes[q].Vel - raw(WrittenStart(notes[q], written))))));
                for (int x = from; x < to; x += step)
                    shaped.Add(new CtlEv { Tick = x, Cc = 11, Val = Math.Max(1, Math.Min(127, (int)Math.Round((inHairpin(x) ? 120 : swellAt(x)) * level(Math.Max(x, w0)) / top))) });
                i = k;
            }
            t.Controls.RemoveAll(c => c.Cc == 11);
            t.Controls.AddRange(shaped);
        }
    }
}
