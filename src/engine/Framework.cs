// Composition framework: note DSL, harmony + voice leading, textures, tempo, export.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Sym
{
    public static class T
    {
        public const int PPQ = 480;
        public const int W = 1920, H = 960, Q = 480, E = 240, S = 120;
    }

    public class Note
    {
        public int Start, Dur, Midi;
        public bool Staccato, Accent, Tenuto, Legato, Tie, Sfz;
        public string Art = "";      // "", "pz", "tr", "harm"
        public int Vel;              // filled at render
        public Note Clone() { return (Note)MemberwiseClone(); }
    }

    public class DynMark { public int Time; public double Level; public bool Ramp; }

    public class Phrase
    {
        public List<Note> Notes = new List<Note>();
        public List<DynMark> Dyn = new List<DynMark>();
        public int Length;

        public Phrase Tr(int semis)
        {
            var p = Copy();
            foreach (var n in p.Notes) n.Midi += semis;
            return p;
        }
        public Phrase Aug(double f)
        {
            var p = Copy();
            foreach (var n in p.Notes) { n.Start = (int)Math.Round(n.Start * f); n.Dur = (int)Math.Round(n.Dur * f); }
            foreach (var d in p.Dyn) d.Time = (int)Math.Round(d.Time * f);
            p.Length = (int)Math.Round(p.Length * f);
            return p;
        }
        public Phrase Inv(int axisMidi)
        {
            var p = Copy();
            foreach (var n in p.Notes) n.Midi = 2 * axisMidi - n.Midi;
            return p;
        }
        public Phrase NoDyn() { var p = Copy(); p.Dyn.Clear(); return p; }
        // Diatonic transposition within a scale given as pitch classes (7 entries, ascending from tonic).
        public Phrase DTr(int steps, int[] scale)
        {
            var p = Copy();
            foreach (var n in p.Notes) n.Midi = Theory.DiatonicShift(n.Midi, steps, scale);
            return p;
        }
        public Phrase Copy()
        {
            var p = new Phrase { Length = Length };
            foreach (var n in Notes) p.Notes.Add(n.Clone());
            foreach (var d in Dyn) p.Dyn.Add(new DynMark { Time = d.Time, Level = d.Level, Ramp = d.Ramp });
            return p;
        }
        public Phrase Then(Phrase b)
        {
            var p = Copy();
            foreach (var n in b.Notes) { var c = n.Clone(); c.Start += Length; p.Notes.Add(c); }
            foreach (var d in b.Dyn) p.Dyn.Add(new DynMark { Time = d.Time + Length, Level = d.Level, Ramp = d.Ramp });
            p.Length = Length + b.Length;
            return p;
        }
    }

    public static class Theory
    {
        static readonly int[] StepPc = { 9, 11, 0, 2, 4, 5, 7 }; // a b c d e f g
        public static readonly Dictionary<string, double> Levels = new Dictionary<string, double> {
            {"ppp",1},{"pp",2},{"p",3},{"mp",4},{"mf",5},{"f",6},{"ff",7},{"fff",8},{"sf",6.5},{"fp",3}
        };

        // LilyPond-style pitch: c cis ces cisis ceses d dis des ... es (=ees) as (=aes) + octave (scientific, C4=60)
        public static bool TryParsePitch(string s, out int midi, out int consumed)
        {
            midi = 0; consumed = 0;
            if (s.Length == 0) return false;
            char c = s[0];
            if (c < 'a' || c > 'h') return false;
            // "h" is the German name of B natural (his = B sharp, as AI composers sometimes write it)
            int pc = c == 'h' ? 11 : StepPc[c - 'a'];
            int i = 1, alt = 0;
            while (true)
            {
                if (i + 1 < s.Length + 1 && Sub(s, i, 2) == "is") { alt++; i += 2; continue; }
                if (Sub(s, i, 2) == "es") { alt--; i += 2; continue; }
                if (i == 1 && (c == 'a' || c == 'e') && Sub(s, i, 1) == "s" && !(Sub(s, i, 2) == "es"))
                {
                    // "as" / "es" shorthand
                    alt--; i += 1; continue;
                }
                break;
            }
            int j = i;
            bool neg = false;
            if (j < s.Length && s[j] == '-' && j + 1 < s.Length && char.IsDigit(s[j + 1])) { neg = true; j++; }
            int st = j;
            while (j < s.Length && char.IsDigit(s[j])) j++;
            if (j == st) return false;
            int oct = int.Parse(s.Substring(st, j - st)) * (neg ? -1 : 1);
            midi = (oct + 1) * 12 + pc + alt;
            consumed = j;
            return true;
        }
        static string Sub(string s, int i, int n) { return i + n <= s.Length ? s.Substring(i, n) : ""; }

        public static int ParseDur(string s, ref int i)
        {
            // expects s[i]=='/'
            i++;
            int st = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            int n = int.Parse(s.Substring(st, i - st));
            if (1920 % n != 0) throw new Exception("bad duration /" + n);
            double d = 1920 / n;
            double add = d / 2;
            while (i < s.Length && s[i] == '.') { d += add; add /= 2; i++; }
            return (int)Math.Round(d);
        }

        public static int DiatonicShift(int midi, int steps, int[] scale)
        {
            int pc = ((midi % 12) + 12) % 12;
            int oct = (int)Math.Floor(midi / 12.0);
            int idx = -1; int chrom = 0;
            for (int k = 0; k < 7; k++) if (scale[k] == pc) { idx = k; break; }
            if (idx < 0)
            {
                // chromatic note: shift the scale note below and keep the chromatic offset
                for (int d = 1; d < 12 && idx < 0; d++)
                    for (int k = 0; k < 7; k++) if (scale[k] == ((pc - d) % 12 + 12) % 12) { idx = k; chrom = d; break; }
            }
            int baseMidi = midi - chrom;
            int basePc = ((baseMidi % 12) + 12) % 12;
            int baseOct = (int)Math.Floor(baseMidi / 12.0);
            int t = idx + steps;
            int octs = (int)Math.Floor(t / 7.0);
            int ni = t - octs * 7;
            int target = (baseOct + octs) * 12 + scale[ni];
            // keep monotonic octave relative to scale wraparound
            int origRel = basePc - scale[0]; if (origRel < 0) origRel += 12;
            int newRel = scale[ni] - scale[0]; if (newRel < 0) newRel += 12;
            int tonicMidi = baseMidi - origRel;
            target = tonicMidi + octs * 12 + newRel;
            return target + chrom;
        }

        public static int[] Scale(string key)
        {
            // "d" major, "dm" natural minor, "dhm" harmonic minor
            int midi, used;
            string name = key;
            int[] major = { 0, 2, 4, 5, 7, 9, 11 }, minor = { 0, 2, 3, 5, 7, 8, 10 }, harm = { 0, 2, 3, 5, 7, 8, 11 };
            int[] pat = major;
            if (name.EndsWith("hm")) { pat = harm; name = name.Substring(0, name.Length - 2); }
            else if (name.EndsWith("m")) { pat = minor; name = name.Substring(0, name.Length - 1); }
            TryParsePitch(name + "4", out midi, out used);
            int t = midi % 12;
            return pat.Select(x => (x + t) % 12).ToArray();
        }

        public static string NoteName(int midi)
        {
            string[] n = { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };
            return n[((midi % 12) + 12) % 12] + (midi / 12 - 1);
        }
    }

    public class ChordSym
    {
        public string Name;
        public int Root, Bass;
        public int[] Pcs;           // includes root first
        public int Third = -1, Seventh = -1, Fifth = -1;
        public bool IsRest;

        static readonly string[] Qual = { "maj9", "maj7", "mMaj7", "m7b5", "m9", "m7", "m6", "dim7", "dim", "o7", "aug", "7b9", "7sus4", "sus4", "sus2", "add9", "Fr6", "9", "7", "6", "5", "m", "+", "o", "" };
        static readonly int[][] Ints = {
            new[]{0,4,7,11,2}, new[]{0,4,7,11}, new[]{0,3,7,11}, new[]{0,3,6,10}, new[]{0,3,7,10,2}, new[]{0,3,7,10}, new[]{0,3,7,9},
            new[]{0,3,6,9}, new[]{0,3,6}, new[]{0,3,6,9}, new[]{0,4,8}, new[]{0,4,7,10,1}, new[]{0,5,7,10}, new[]{0,5,7}, new[]{0,2,7},
            new[]{0,4,7,2}, new[]{0,4,6,10}, new[]{0,4,7,10,2}, new[]{0,4,7,10}, new[]{0,4,7,9}, new[]{0,7}, new[]{0,3,7}, new[]{0,4,8}, new[]{0,3,6}, new[]{0,4,7}
        };

        public static int ParseRootPc(string s, ref int i)
        {
            char c = char.ToUpper(s[i]);
            int[] map = { 9, 11, 0, 2, 4, 5, 7 };
            if (c < 'A' || c > 'G') throw new Exception("bad chord root in " + s);
            int pc = map[c - 'A']; i++;
            while (i < s.Length && (s[i] == '#' || s[i] == 'b'))
            {
                // 'b' as flat only if followed by something that is not a quality start (always flat here)
                pc += s[i] == '#' ? 1 : -1; i++;
            }
            return ((pc % 12) + 12) % 12;
        }

        public static ChordSym Parse(string s)
        {
            var c = new ChordSym { Name = s };
            if (s == "r" || s == "-") { c.IsRest = true; c.Pcs = new int[0]; return c; }
            int i = 0;
            c.Root = ParseRootPc(s, ref i);
            string rest = s.Substring(i);
            string bassStr = null;
            int slash = rest.IndexOf('/');
            if (slash >= 0) { bassStr = rest.Substring(slash + 1); rest = rest.Substring(0, slash); }
            int qi = -1;
            for (int k = 0; k < Qual.Length; k++) if (rest == Qual[k]) { qi = k; break; }
            if (qi < 0) throw new Exception("unknown chord quality '" + rest + "' in " + s);
            var ints = Ints[qi];
            c.Pcs = ints.Select(x => (x + c.Root) % 12).ToArray();
            foreach (var x in ints)
            {
                if (x == 3 || x == 4 || x == 5 && (rest.StartsWith("sus") || rest.StartsWith("7sus"))) c.Third = (x + c.Root) % 12;
                if (x == 10 || x == 11 || (x == 9 && (rest == "dim7" || rest == "o7"))) c.Seventh = (x + c.Root) % 12;
                if (x == 6 || x == 7 || x == 8) c.Fifth = (x + c.Root) % 12;
            }
            c.Bass = c.Root;
            if (bassStr != null) { int j = 0; c.Bass = ParseRootPc(bassStr, ref j); }
            return c;
        }
    }

    public class Span { public int Start, Dur; public ChordSym Chord; }

    public class Harm
    {
        public List<Span> Spans = new List<Span>();
        public Movement Mv;

        // Choose voicings with smooth voice leading. n voices within [lo,hi]. If withBass, the lowest voice is the chord bass.
        public List<int[]> Voice(int n, int lo, int hi, bool withBass = false, int[] start = null)
        {
            var res = new List<int[]>();
            int[] prev = start;
            foreach (var sp in Spans)
            {
                if (sp.Chord.IsRest) { res.Add(null); continue; }
                var v = Voicing.Choose(sp.Chord, n, lo, hi, prev, withBass);
                res.Add(v);
                prev = v;
            }
            return res;
        }
    }

    public static class Voicing
    {
        public static int[] Choose(ChordSym c, int n, int lo, int hi, int[] prev, bool withBass)
        {
            var cands = new List<int>();
            for (int m = lo; m <= hi; m++) if (c.Pcs.Contains(((m % 12) + 12) % 12)) cands.Add(m);
            int[] best = null; double bestCost = double.MaxValue;
            var cur = new int[n];
            Action<int, int> dfs = null;
            dfs = (idx, from) =>
            {
                if (idx == n)
                {
                    double cost = Cost(c, cur, prev, withBass, lo, hi);
                    if (cost < bestCost) { bestCost = cost; best = (int[])cur.Clone(); }
                    return;
                }
                for (int k = from; k < cands.Count; k++)
                {
                    int m = cands[k];
                    if (idx > 0)
                    {
                        int gap = m - cur[idx - 1];
                        if (gap <= 0) continue;
                        int maxGap = idx == 1 ? 19 : 12;
                        if (gap > maxGap) break;
                    }
                    if (idx == 0 && withBass && ((m % 12) + 12) % 12 != c.Bass) continue;
                    cur[idx] = m;
                    dfs(idx + 1, k + 1);
                }
            };
            dfs(0, 0);
            if (best == null)
            {
                // fall back: allow unisons / ignore spacing; stack chord tones upward from lo
                best = new int[n];
                int k = 0;
                for (int i = 0; i < n; i++) { best[i] = cands[Math.Min(k, cands.Count - 1)]; k++; }
            }
            return best;
        }

        static double Cost(ChordSym c, int[] v, int[] prev, bool withBass, int lo, int hi)
        {
            double cost = 0;
            var pcs = v.Select(m => ((m % 12) + 12) % 12).ToList();
            int distinct = pcs.Distinct().Count();
            // required tones
            if (!pcs.Contains(c.Root) && !(withBass && c.Bass != c.Root && c.Pcs.Length > v.Length)) cost += 20;
            if (c.Third >= 0 && !pcs.Contains(c.Third)) cost += 25;
            if (c.Seventh >= 0 && !pcs.Contains(c.Seventh)) cost += 18;
            foreach (var pc in c.Pcs)
                if (!pcs.Contains(pc) && pc != c.Fifth && pc != c.Root && pc != c.Third && pc != c.Seventh) cost += 10; // 9ths etc
            if (c.Fifth >= 0 && !pcs.Contains(c.Fifth) && v.Length >= c.Pcs.Length) cost += 6;
            // doubling
            foreach (var g in pcs.GroupBy(x => x))
            {
                int cnt = g.Count();
                if (cnt < 2) continue;
                if (g.Key == c.Seventh) cost += 12 * (cnt - 1);
                else if (g.Key == c.Third) cost += 3 * (cnt - 1);
                else if (g.Key == c.Fifth) cost += 1 * (cnt - 1);
                else if (g.Key != c.Root) cost += 4 * (cnt - 1);
            }
            if (distinct < Math.Min(v.Length, c.Pcs.Length)) cost += 6;
            if (prev != null && prev.Length == v.Length)
            {
                for (int i = 0; i < v.Length; i++) cost += Math.Abs(v[i] - prev[i]) * 1.0;
                for (int i = 0; i < v.Length; i++)
                    for (int j = i + 1; j < v.Length; j++)
                    {
                        int a = prev[j] - prev[i], b = v[j] - v[i];
                        bool perfA = a % 12 == 0 || a % 12 == 7, perfB = b % 12 == 0 || b % 12 == 7;
                        int mi = v[i] - prev[i], mj = v[j] - prev[j];
                        if (perfA && perfB && a % 12 == b % 12 && mi != 0 && mj != 0 && Math.Sign(mi) == Math.Sign(mj)) cost += 15;
                    }
            }
            else
            {
                double mean = v.Average();
                cost += Math.Abs(mean - (lo + hi) / 2.0) * 0.5;
            }
            // close spacing preference in upper voices
            for (int i = 1; i < v.Length; i++) if (v[i] - v[i - 1] > 9) cost += 0.5;
            return cost;
        }
    }

    public class Part
    {
        public string Id, Name;
        public int Channel, Program, Lo, Hi;
        public List<Note> Notes = new List<Note>();
        public List<DynMark> Dyn = new List<DynMark>();
        public Movement Mv;

        public Part Add(int bar, string dsl) { return Put(bar, Mv.P(bar, dsl, this)); }
        public Part Add(int bar, double q, string dsl) { return PutAt(Mv.Pos(bar, q), Mv.P(bar, q, dsl, this)); }
        public Part Put(int bar, Phrase p) { return PutAt(Mv.Pos(bar, 0), p); }
        public Part Put(int bar, double q, Phrase p) { return PutAt(Mv.Pos(bar, q), p); }
        public Part PutAt(int tick, Phrase p)
        {
            foreach (var n in p.Notes) { var c = n.Clone(); c.Start += tick; Notes.Add(c); }
            foreach (var d in p.Dyn) Dyn.Add(new DynMark { Time = d.Time + tick, Level = d.Level, Ramp = d.Ramp });
            return this;
        }
        // dynamics: "p" at a point; Hairpin from (bar,q) to (bar2,q2) reaching level
        public Part D(int bar, double q, string level)
        {
            Dyn.Add(new DynMark { Time = Mv.Pos(bar, q), Level = Theory.Levels[level] });
            return this;
        }
        public Part D(int bar, string level) { return D(bar, 0, level); }
        public Part Hair(int bar, double q, int bar2, double q2, string level)
        {
            Dyn.Add(new DynMark { Time = Mv.Pos(bar, q), Level = -1, Ramp = true });
            Dyn.Add(new DynMark { Time = Mv.Pos(bar2, q2), Level = Theory.Levels[level] });
            return this;
        }
        // copy notes from another part in a bar range (inclusive start bar, exclusive end bar), transposed
        public Part Double(Part src, int fromBar, int toBar, int semis = 0, bool withDyn = false)
        {
            int a = Mv.Pos(fromBar, 0), b = Mv.Pos(toBar, 0);
            foreach (var n in src.Notes) if (n.Start >= a && n.Start < b) { var c = n.Clone(); c.Midi += semis; Notes.Add(c); }
            if (withDyn) foreach (var d in src.Dyn) if (d.Time >= a && d.Time < b) Dyn.Add(new DynMark { Time = d.Time, Level = d.Level, Ramp = d.Ramp });
            return this;
        }
        public Part Clear(int fromBar, int toBar)
        {
            int a = Mv.Pos(fromBar, 0), b = Mv.Pos(toBar, 0);
            Notes.RemoveAll(n => n.Start >= a && n.Start < b);
            return this;
        }

        // Pattern texture over harmony. voices: indices into the voicing (0 = lowest). pattern: "8: 0 1 2 1" / "4: c r c r" / "*: c"
        // tokens: digit = single voice (relative index into 'voices'), c = chord of all selected voices, r = rest,
        // B = chord bass note nearest previous within part range, b = bass an octave higher; suffix ' > - ^pz ^tr ~
        public Part Pattern(Harm h, List<int[]> vo, int[] voices, string pattern, int fromSpan = 0, int toSpan = -1, int octave = 0)
        {
            if (toSpan < 0) toSpan = h.Spans.Count;
            int colon = pattern.IndexOf(':');
            string unitS = pattern.Substring(0, colon).Trim();
            var toks = pattern.Substring(colon + 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int lastBass = (Lo + Hi) / 2 - 6;
            for (int si = fromSpan; si < toSpan; si++)
            {
                var sp = h.Spans[si];
                if (sp.Chord.IsRest || vo[si] == null) continue;
                var sel = voices == null ? vo[si] : voices.Select(k => vo[si][Math.Min(k, vo[si].Length - 1)]).ToArray();
                int unit;
                if (unitS == "*") unit = sp.Dur;
                else { int ii = 0; unit = Theory.ParseDur("/" + unitS, ref ii); }
                int t = 0, k2 = 0;
                while (t < sp.Dur)
                {
                    string tok = toks[k2 % toks.Length]; k2++;
                    int dur = Math.Min(unit, sp.Dur - t);
                    string core = tok; var proto = new Note();
                    ParseSuffix(ref core, proto);
                    var pitches = new List<int>();
                    if (core == "r") { }
                    else if (core == "c") pitches.AddRange(sel);
                    else if (core == "B" || core == "b")
                    {
                        int best = -1, bd = 999;
                        for (int m = Lo; m <= Hi; m++) if (((m % 12) + 12) % 12 == sp.Chord.Bass && Math.Abs(m - lastBass) < bd) { bd = Math.Abs(m - lastBass); best = m; }
                        if (best < 0) best = lastBass;
                        lastBass = best;
                        pitches.Add(core == "B" ? best : best + 12);
                    }
                    else if (core == "F")
                    {
                        int fifthPc = sp.Chord.Fifth >= 0 ? sp.Chord.Fifth : sp.Chord.Root;
                        int m = lastBass + 1; while (((m % 12) + 12) % 12 != fifthPc) m++;
                        pitches.Add(m);
                    }
                    else
                    {
                        int idx = int.Parse(core);
                        pitches.Add(sel[Math.Min(idx, sel.Length - 1)]);
                    }
                    foreach (var m in pitches)
                    {
                        var n = proto.Clone();
                        n.Start = sp.Start + t; n.Dur = dur; n.Midi = m + octave;
                        Notes.Add(n);
                    }
                    t += unit;
                }
            }
            return this;
        }

        public static void ParseSuffix(ref string core, Note n)
        {
            while (core.Length > 0)
            {
                char ch = core[core.Length - 1];
                if (ch == '\'') { n.Staccato = true; core = core.Substring(0, core.Length - 1); continue; }
                if (ch == '>') { n.Accent = true; core = core.Substring(0, core.Length - 1); continue; }
                if (ch == '-' && core.Length > 1) { n.Tenuto = true; core = core.Substring(0, core.Length - 1); continue; }
                if (ch == '~') { n.Tie = true; core = core.Substring(0, core.Length - 1); continue; }
                if (ch == '_') { n.Legato = true; core = core.Substring(0, core.Length - 1); continue; }
                int caret = core.LastIndexOf('^');
                if (caret >= 0 && caret > core.LastIndexOf('/') && core.IndexOf('>', caret) < 0)
                {
                    string a = core.Substring(caret + 1);
                    if (a == "pz" || a == "tr" || a == "arco" || a == "harm") { n.Art = a == "arco" ? "" : a; core = core.Substring(0, caret); continue; }
                    if (a == "sfz") { n.Sfz = true; n.Accent = true; core = core.Substring(0, caret); continue; }
                    if (a == "st") { n.Staccato = true; core = core.Substring(0, caret); continue; }
                    if (a == "ten") { n.Tenuto = true; core = core.Substring(0, caret); continue; }
                    if (a == "acc") { n.Accent = true; core = core.Substring(0, caret); continue; }
                }
                break;
            }
        }
    }

    public class Marker { public int Time; public string Text; }

    public class Movement
    {
        public string Title, Tempo = "";
        public Dictionary<string, Part> Parts = new Dictionary<string, Part>();
        public List<string> Order = new List<string>();
        List<int[]> meters = new List<int[]>();          // bar, num, den
        List<KeyValuePair<int, double>> tempos = new List<KeyValuePair<int, double>>();
        List<double[]> ferm = new List<double[]>();      // start, end, factor
        public List<Marker> Markers = new List<Marker>();
        public int EndBar;

        public Movement(string title)
        {
            Title = title;
            meters.Add(new[] { 1, 4, 4 });
            foreach (var p in Orchestra.Layout)
            {
                var part = new Part { Id = p.Id, Name = p.Name, Channel = p.Channel, Program = p.Program, Lo = p.Lo, Hi = p.Hi, Mv = this };
                Parts[p.Id] = part; Order.Add(p.Id);
            }
        }
        public Part this[string id] { get { return Parts[id]; } }

        public void Meter(int bar, int num, int den)
        {
            meters.RemoveAll(m => m[0] == bar);
            meters.Add(new[] { bar, num, den });
            meters.Sort((a, b) => a[0].CompareTo(b[0]));
        }
        public int BarLen(int bar)
        {
            int[] cur = meters[0];
            foreach (var m in meters) if (m[0] <= bar) cur = m;
            return cur[1] * 1920 / cur[2];
        }
        public int Pos(int bar, double q)
        {
            int t = 0;
            for (int b = 1; b < bar; b++) t += BarLen(b);
            return t + (int)Math.Round(q * T.Q);
        }
        public int BarAt(int tick, out int offset)
        {
            int t = 0, b = 1;
            while (t + BarLen(b) <= tick) { t += BarLen(b); b++; if (b > 100000) break; }
            offset = tick - t; return b;
        }
        public void SetTempo(int bar, double q, double bpm) { tempos.Add(new KeyValuePair<int, double>(Pos(bar, q), bpm)); }
        public void SetTempo(int bar, double bpm) { SetTempo(bar, 0, bpm); }
        // gradual tempo change (rit./accel.) from (bar,q) at bpm0 to (bar2,q2) reaching bpm1, steps of an eighth
        public void Rit(int bar, double q, int bar2, double q2, double bpm0, double bpm1)
        {
            int a = Pos(bar, q), b = Pos(bar2, q2);
            for (int t = a; t < b; t += T.E)
            {
                double f = (double)(t - a) / (b - a);
                tempos.Add(new KeyValuePair<int, double>(t, bpm0 + (bpm1 - bpm0) * f));
            }
        }
        // fermata: lengthen (bar,q)+len quarters by factor
        public void Fermata(int bar, double q, double lenQ, double factor = 2.0)
        {
            int a = Pos(bar, q);
            ferm.Add(new double[] { a, a + lenQ * T.Q, factor });
        }
        public void Mark(int bar, string text) { Markers.Add(new Marker { Time = Pos(bar, 0), Text = text }); }
        // dynamics for several parts at once: ids space-separated, e.g. "v1 v2 va"
        public void D(string ids, int bar, double q, string level) { foreach (var id in ids.Split(' ')) if (id.Length > 0) Parts[id].D(bar, q, level); }
        public void D(string ids, int bar, string level) { D(ids, bar, 0, level); }
        public void Hair(string ids, int bar, double q, int bar2, double q2, string level) { foreach (var id in ids.Split(' ')) if (id.Length > 0) Parts[id].Hair(bar, q, bar2, q2, level); }

        double BpmAt(int tick)
        {
            double bpm = 120; int bestT = -1;
            foreach (var kv in tempos) if (kv.Key <= tick && kv.Key >= bestT) { bestT = kv.Key; bpm = kv.Value; }
            foreach (var f in ferm) if (tick >= f[0] && tick < f[1]) bpm /= f[2];
            return bpm;
        }
        double[] secCache;
        public void BuildTimeMap(int endTick)
        {
            int steps = endTick / 60 + 2;
            secCache = new double[steps + 1];
            double s = 0;
            for (int i = 0; i <= steps; i++)
            {
                secCache[i] = s;
                s += 60.0 / BpmAt(i * 60) * 60 / T.Q;
            }
        }
        public double Sec(int tick)
        {
            int i = tick / 60; int r = tick % 60;
            if (i + 1 >= secCache.Length) i = secCache.Length - 2;
            return secCache[i] + (secCache[i + 1] - secCache[i]) * r / 60.0;
        }
        public List<KeyValuePair<int, double>> TempoEvents(int endTick)
        {
            // flatten to change list on 60-tick grid
            var res = new List<KeyValuePair<int, double>>();
            double last = -1;
            for (int t = 0; t <= endTick; t += 60)
            {
                double b = BpmAt(t);
                if (Math.Abs(b - last) > 0.01) { res.Add(new KeyValuePair<int, double>(t, b)); last = b; }
            }
            return res;
        }
        public List<int[]> Meters { get { return meters; } }

        // ---- DSL ----
        public Phrase P(int bar, string dsl, Part part = null) { return P(bar, 0, dsl, part); }
        public Phrase P(int bar, double q, string dsl, Part part = null)
        {
            return Dsl.Parse(this, Pos(bar, q), dsl, part == null ? "?" : part.Id, bar);
        }
        public Harm H(int bar, string prog) { return H(bar, 0, prog); }
        public Harm H(int bar, double q, string prog)
        {
            var h = new Harm { Mv = this };
            int t = Pos(bar, q);
            int hOff; BarAt(t, out hOff); int hLast = t - hOff;
            int dur = T.Q;
            foreach (var raw in Dsl.Expand(prog).Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw == "|")
                {
                    int off; int b = BarAt(t, out off);
                    if (off != 0) throw new Exception(Title + ": harmony " + Dsl.BarCheckMessage(this, hLast, t) + " (chord durations)");
                    hLast = t;
                    continue;
                }
                string tok = raw;
                int slash = tok.LastIndexOf('/');
                string chordS = tok;
                // duration is the last /N[.] where N is digits
                if (slash >= 0 && slash + 1 < tok.Length && char.IsDigit(tok[slash + 1]))
                {
                    int i = slash;
                    dur = Theory.ParseDur(tok, ref i);
                    chordS = tok.Substring(0, slash);
                }
                if (chordS == "=")
                {
                    // extend previous chord
                    h.Spans[h.Spans.Count - 1].Dur += dur; t += dur; continue;
                }
                h.Spans.Add(new Span { Start = t, Dur = dur, Chord = ChordSym.Parse(chordS) });
                t += dur;
            }
            return h;
        }
        static string Trunc(string s) { return s.Length > 80 ? s.Substring(0, 80) + "..." : s; }

        public int EndTick()
        {
            int end = 0;
            foreach (var p in Parts.Values) foreach (var n in p.Notes) end = Math.Max(end, n.Start + n.Dur);
            if (EndBar > 0) end = Math.Max(end, Pos(EndBar, 0));
            return end;
        }
    }

    public static class Dsl
    {
        public static string Expand(string s)
        {
            s = s.Replace("|", " | ");   // tolerate bar lines written without spaces: "| G7 |C"
            // [ ... ]*N  (innermost first)
            while (true)
            {
                int close = s.IndexOf("]*");
                if (close < 0) break;
                int open = s.LastIndexOf('[', close);
                int j = close + 2; int st = j;
                while (j < s.Length && char.IsDigit(s[j])) j++;
                int n = int.Parse(s.Substring(st, j - st));
                string body = s.Substring(open + 1, close - open - 1);
                var sb = new StringBuilder();
                for (int k = 0; k < n; k++) { sb.Append(' '); sb.Append(body); sb.Append(' '); }
                s = s.Substring(0, open) + sb.ToString() + s.Substring(j);
            }
            return s;
        }

        public static Phrase Parse(Movement mv, int absStart, string dsl, string pid, int bar0)
        {
            var p = new Phrase();
            int t = 0, dur = T.Q;
            int off0; int startBar = mv.BarAt(absStart, out off0);
            int lastLine = absStart - off0;   // last bar line passed (or the start of the bar we begin in)
            bool legato = false;
            bool pendingRamp = false;
            var tieOpen = new Dictionary<int, Note>();
            var toks = Expand(dsl).Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int ti = 0; ti < toks.Length; ti++)
            {
                string tok = toks[ti];
                try
                {
                    if (tok == "|")
                    {
                        int off; int b = mv.BarAt(absStart + t, out off);
                        if (off != 0) throw new Exception(BarCheckMessage(mv, lastLine, absStart + t));
                        lastLine = absStart + t;
                        continue;
                    }
                    if (tok.StartsWith("\\"))
                    {
                        string d = tok.Substring(1);
                        if (d == "<" || d == ">") { pendingRamp = true; p.Dyn.Add(new DynMark { Time = t, Level = -1, Ramp = true }); continue; }
                        if (d == "!") continue;
                        p.Dyn.Add(new DynMark { Time = t, Level = Theory.Levels[d] });
                        pendingRamp = false;
                        continue;
                    }
                    if (tok == "(") { legato = true; continue; }
                    if (tok == ")") { legato = false; continue; }
                    bool startLeg = false, endLeg = false;
                    if (tok.StartsWith("(")) { startLeg = true; tok = tok.Substring(1); }
                    if (tok.EndsWith(")")) { endLeg = true; tok = tok.Substring(0, tok.Length - 1); }
                    if (startLeg) legato = true;

                    var pitches = new List<int>();
                    string rest;
                    if (tok.StartsWith("<"))
                    {
                        // chord: gather until token containing '>' right after an octave digit
                        var inner = new StringBuilder(tok.Substring(1));
                        while (!ChordClosed(inner.ToString())) { ti++; inner.Append(' '); inner.Append(toks[ti]); }
                        string all = inner.ToString();
                        int gt = ChordCloseIdx(all);
                        foreach (var ps in all.Substring(0, gt).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            int m, used;
                            if (!Theory.TryParsePitch(ps, out m, out used) || used != ps.Length) throw new Exception("bad chord pitch '" + ps + "'");
                            pitches.Add(m);
                        }
                        rest = all.Substring(gt + 1);
                        if (rest.EndsWith(")")) { endLeg = true; rest = rest.Substring(0, rest.Length - 1); }
                    }
                    else if (tok.StartsWith("r") || tok.StartsWith("s/") || tok == "s")
                    {
                        rest = tok.Substring(1);
                        pitches = null;
                    }
                    else
                    {
                        int m, used;
                        if (!Theory.TryParsePitch(tok, out m, out used)) throw new Exception("cannot parse");
                        pitches.Add(m);
                        rest = tok.Substring(used);
                    }
                    int i = 0;
                    if (rest.Length > 0 && rest[0] == '/') { dur = Theory.ParseDur(rest, ref i); }
                    string suffix = rest.Substring(i);
                    var proto = new Note();
                    string core = "x" + suffix;
                    Part.ParseSuffix(ref core, proto);
                    if (core != "x") throw new Exception("unparsed suffix '" + core.Substring(1) + "'");
                    if (pitches != null)
                    {
                        var newTies = new Dictionary<int, Note>();
                        foreach (var m in pitches)
                        {
                            Note prevTie;
                            if (tieOpen.TryGetValue(m, out prevTie) && prevTie.Start + prevTie.Dur == t)
                            {
                                prevTie.Dur += dur;
                                if (proto.Tie) newTies[m] = prevTie;
                                if (proto.Staccato) prevTie.Staccato = true;
                                continue;
                            }
                            var n = proto.Clone();
                            n.Start = t; n.Dur = dur; n.Midi = m; n.Legato = legato || proto.Legato;
                            p.Notes.Add(n);
                            if (proto.Tie) newTies[m] = n;
                        }
                        tieOpen = newTies;
                    }
                    else tieOpen.Clear();
                    t += dur;
                    if (endLeg) legato = false;
                }
                catch (Exception ex)
                {
                    int off; int b = mv.BarAt(absStart + t, out off);
                    throw new Exception(mv.Title + " part " + pid + " bar " + b + "+" + off + " token '" + toks[Math.Min(ti, toks.Length - 1)] + "': " + ex.Message);
                }
            }
            p.Length = t;
            return p;
        }
        // "bar check failed: bar 18 is too short by a quarter (/4); a bar of 6/8 lasts a dotted half (/2.)"
        public static string BarCheckMessage(Movement mv, int lastLine, int now)
        {
            int o; int bar = mv.BarAt(lastLine, out o);
            int len = mv.BarLen(bar), expected = lastLine + len;
            string meter = MeterName(len);
            if (now < expected)
                return "bar check failed: bar " + bar + " is too short by " + DurName(expected - now) + "; " + meter;
            return "bar check failed: bar " + bar + " is too long by " + DurName(now - expected) + (now - expected >= len ? " (a bar line | may be missing)" : "") + "; " + meter;
        }
        static string MeterName(int len)
        {
            return "a full bar here lasts " + DurName(len);
        }
        public static string DurName(int ticks)
        {
            switch (ticks)
            {
                case 1920: return "a whole note (/1)";
                case 1440: return "a dotted half (/2.)";
                case 960: return "a half note (/2)";
                case 720: return "a dotted quarter (/4.)";
                case 480: return "a quarter (/4)";
                case 360: return "a dotted eighth (/8.)";
                case 240: return "an eighth (/8)";
                case 160: return "a triplet eighth (/12)";
                case 120: return "a sixteenth (/16)";
                case 60: return "a thirty-second (/32)";
            }
            if (ticks % 240 == 0) return (ticks / 240) + " eighths";
            if (ticks % 120 == 0) return (ticks / 120) + " sixteenths";
            return (ticks / 480.0).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " quarter notes";
        }

        static int ChordCloseIdx(string s)
        {
            for (int i = 1; i < s.Length; i++) if (s[i] == '>' && char.IsDigit(s[i - 1])) return i;
            return -1;
        }
        static bool ChordClosed(string s) { return ChordCloseIdx(s) >= 0; }
    }

    public class PartDef { public string Id, Name, Sample; public int Channel, Program, Lo, Hi; }

    public static class Orchestra
    {
        public static readonly PartDef[] Layout = {
            new PartDef{Id="fl", Name="Flutes",        Channel=0,  Program=73, Lo=60, Hi=98},
            new PartDef{Id="ob", Name="Oboes",         Channel=1,  Program=68, Lo=58, Hi=89},
            new PartDef{Id="cl", Name="Clarinets",     Channel=2,  Program=71, Lo=50, Hi=94},
            new PartDef{Id="bn", Name="Bassoons",      Channel=3,  Program=70, Lo=34, Hi=76},
            new PartDef{Id="hn", Name="Horns",         Channel=4,  Program=60, Lo=35, Hi=77},
            new PartDef{Id="tp", Name="Trumpets",      Channel=5,  Program=56, Lo=54, Hi=84},
            new PartDef{Id="tb", Name="Trombones",     Channel=6,  Program=57, Lo=40, Hi=77},
            new PartDef{Id="tu", Name="Tuba",          Channel=7,  Program=58, Lo=26, Hi=65},
            new PartDef{Id="ti", Name="Timpani",       Channel=8,  Program=47, Lo=40, Hi=57},
            new PartDef{Id="hp", Name="Harp",          Channel=10, Program=46, Lo=24, Hi=103},
            new PartDef{Id="v1", Name="Violins I",     Channel=11, Program=48, Lo=55, Hi=100},
            new PartDef{Id="v2", Name="Violins II",    Channel=12, Program=48, Lo=55, Hi=96},
            new PartDef{Id="va", Name="Violas",        Channel=13, Program=48, Lo=48, Hi=88},
            new PartDef{Id="vc", Name="Cellos",        Channel=14, Program=42, Lo=36, Hi=81},
            new PartDef{Id="cb", Name="Basses",        Channel=15, Program=43, Lo=28, Hi=55},
        };
    }
}
