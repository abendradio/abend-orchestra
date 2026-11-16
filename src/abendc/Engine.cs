// Bridge to the symphony engine (Sym namespace): notation parsing and harmony realisation.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Abend
{
    public static class Engine
    {
        static Sym.Movement Scratch(Song s)
        {
            var mv = new Sym.Movement("scratch");
            mv.Meter(1, s.Num, s.Den);
            return mv;
        }

        static List<NoteEv> Convert(Sym.Movement mv, IEnumerable<Sym.Note> notes, List<Sym.DynMark> dyn, int offset, int velShift)
        {
            var curve = Sym.Render.Resolve(dyn);
            var res = new List<NoteEv>();
            foreach (var n in notes)
            {
                double lv = Sym.Render.LevelAt(curve, n.Start);
                double v = 16 + lv * 13 + (n.Accent ? 14 : 0) + (n.Sfz ? 10 : 0) + velShift;
                int dur = n.Staccato ? Math.Max(40, n.Dur / 2) : (n.Legato || n.Tenuto) ? n.Dur : (int)(n.Dur * 0.95);
                bool normal = !n.Staccato && !n.Legato && !n.Tenuto;
                res.Add(new NoteEv { Start = n.Start + offset, Dur = Math.Max(20, dur), Pitch = Math.Max(0, Math.Min(127, n.Midi)), Vel = Math.Max(1, Math.Min(127, (int)Math.Round(v))), Gap = normal ? n.Dur - Math.Max(20, dur) : 0, Written = n.Dur });
            }
            return res;
        }

        // Parse note notation starting at the given bar (1-based). Throws with a readable message on errors.
        public static List<NoteEv> Notation(Song s, int bar, string text, int transpose) { return Notation(s, bar, text, transpose, null); }

        // curve: if given, receives the passage's dynamics as a resolved curve in absolute ticks (for continuous hairpins)
        public static List<NoteEv> Notation(Song s, int bar, string text, int transpose, List<Sym.DynMark> curve)
        {
            var mv = Scratch(s);
            var ph = mv.P(bar, text);
            int origin = mv.Pos(bar, 0);
            var notes = Convert(mv, ph.Notes, ph.Dyn, origin, 0);
            if (curve != null) curve.AddRange(Sym.Render.Resolve(ph.Dyn).Select(m => new Sym.DynMark { Time = m.Time + origin, Level = m.Level, Ramp = m.Ramp }));
            foreach (var n in notes) n.Pitch = Math.Max(0, Math.Min(127, n.Pitch + transpose));
            return notes;
        }

        // Realise a chord progression as voice-led accompaniment with a rhythm pattern.
        public static List<NoteEv> Harmony(Song s, int bar, string prog, int voices, int lo, int hi, bool bassLowest, string pattern, int vel)
        {
            return HarmonyAt(s, (bar - 1) * s.BarTicks, prog, voices, lo, hi, bassLowest, pattern, vel);
        }

        // same, starting at any tick (used when a chord passage is split by dynamics mid-bar)
        public static List<NoteEv> HarmonyAt(Song s, int startTick, string prog, int voices, int lo, int hi, bool bassLowest, string pattern, int vel)
        {
            var mv = Scratch(s);
            int bar = startTick / s.BarTicks + 1;
            var h = mv.H(bar, (startTick % s.BarTicks) / (double)Song.PPQ, prog);
            var vo = h.Voice(voices, lo, hi, bassLowest);
            var part = mv["va"];
            part.Notes.Clear(); part.Dyn.Clear();
            part.Lo = lo; part.Hi = hi;
            part.Pattern(h, vo, null, pattern);
            var res = Convert(mv, part.Notes, new List<Sym.DynMark>(), 0, 0);
            bool sustained = pattern.Split(':')[0].Trim() == "*";
            foreach (var n in res) { n.Vel = Math.Max(1, Math.Min(127, vel + (n.Vel - 74))); n.Sustained = sustained; }
            return res;
        }

        // "C4", "F#3", "Bb2" or lilypond style "fis3"
        public static bool TryParseNoteName(string s, out int midi)
        {
            midi = 0; s = (s ?? "").Trim();
            if (s.Length < 2) return false;
            int used;
            string low = s.ToLowerInvariant();   // accept the notation spelling in any case: fis3, Ees2, BES3
            if (Sym.Theory.TryParsePitch(low, out midi, out used) && used == low.Length) return true;
            int[] pcs = { 9, 11, 0, 2, 4, 5, 7 };
            char c = char.ToUpper(s[0]);
            if (c < 'A' || c > 'G') return false;
            int pc = pcs[c - 'A'], i = 1;
            while (i < s.Length && (s[i] == '#' || s[i] == 'b')) { pc += s[i] == '#' ? 1 : -1; i++; }
            int oct;
            if (!int.TryParse(s.Substring(i), out oct)) return false;
            midi = (oct + 1) * 12 + pc;
            return midi >= 0 && midi <= 127;
        }
    }
}
