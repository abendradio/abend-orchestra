// Builds a one-preset SoundFont (SF2) from a folder of single-note WAV recordings, e.g. the VCSL harpsichord.
// Each file's pitch is MEASURED (autocorrelation), so file-name octave conventions don't matter; the pitch error is
// stored as the sample's correction in cents. Stereo files become linked left/right samples. No loops: every
// recording plays its natural decay; key release closes it with a short damper-like release.
//   WavToSf2 <folder> <out.sf2> <preset name> [release seconds = 0.15] [velocity range dB = 30]
// Several presets in one file (e.g. a harpsichord's 8' and 4' stops and their key-up noises):
//   WavToSf2 --multi <out.sf2> <name> <program>=<folder> ... <program>=rel:<folder>:<sustain program> ...
// A "rel:" folder holds key-release recordings (the jack falling back); they have no clear pitch, so each takes the
// measured key of the sustain recording with the same note name in its file name (e.g. "_A#1_"), and plays out fully.
// Build: csc -out:WavToSf2.exe src\tools\WavToSf2.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Sym
{
    public static class WavToSf2
    {
        class Smp { public string File, Name; public short[] L, R; public int Rate, Root, Cents; public double Hz; }

        class Inst { public string Name; public int Program; public List<Smp> Smps; public double Release; }

        static string NoteToken(string file) { var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(file), @"_([A-G]#?-?\d)_"); return m.Success ? m.Groups[1].Value : null; }

        static int NoteMidi(string tok)
        {
            if (tok == null) return -1;
            int pc = "C D EF G A B".IndexOf(tok[0]); if (tok.Length > 1 && tok[1] == '#') pc++;
            return (int.Parse(tok.Substring(tok[1] == '#' ? 2 : 1)) + 1) * 12 + pc;
        }

        static List<Smp> Measured(string folder)
        {
            var smps = new List<Smp>();
            foreach (var f in Directory.GetFiles(folder, "*.wav").OrderBy(x => x))
            {
                var s = ReadWav(f);
                double hz = Pitch(s), midi = 69 + 12 * Math.Log(hz / 440, 2);
                s.Root = (int)Math.Round(midi); s.Cents = (int)Math.Round((s.Root - midi) * 100); s.Hz = hz; s.Name = "h" + s.Root;
                smps.Add(s);
            }
            // the measurement can be an octave off (it was for two Flemish recordings): where every file name carries a note,
            // the key comes from the name (shifted by the set's usual octave offset), and the measurement only gives the cents
            var named = smps.Select(s => NoteMidi(NoteToken(s.File))).ToList();
            if (named.All(n => n >= 0))
            {
                int shift = smps.Select((s, i) => (int)Math.Round((69 + 12 * Math.Log(s.Hz / 440, 2) - named[i]) / 12.0) * 12).GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
                for (int i = 0; i < smps.Count; i++)
                {
                    var s = smps[i]; double midi = 69 + 12 * Math.Log(s.Hz / 440, 2); int root = named[i] + shift;
                    midi -= 12 * Math.Round((midi - root) / 12.0);
                    if (Math.Abs(midi - root) > 0.5) throw new Exception(Path.GetFileName(s.File) + ": measured pitch does not match its name");
                    if (s.Root != root) Console.WriteLine(Path.GetFileName(s.File) + ": measured an octave off, key " + Name(root) + " from its name");
                    s.Root = root; s.Cents = (int)Math.Round((root - midi) * 100); s.Name = "h" + root;
                }
            }
            smps = smps.OrderBy(s => s.Root).ToList();
            if (smps.Select(s => s.Root).Distinct().Count() != smps.Count) throw new Exception(folder + ": two files measured as the same key");
            return smps;
        }

        static int Multi(string[] a)
        {
            var insts = new List<Inst>();
            foreach (var spec in a.Skip(3))
            {
                int eq = spec.IndexOf('='); int prog = int.Parse(spec.Substring(0, eq)); string src = spec.Substring(eq + 1);
                if (src.StartsWith("rel:"))
                {
                    var p = src.Substring(4); int c = p.LastIndexOf(':'); string folder = p.Substring(0, c); int of = int.Parse(p.Substring(c + 1));
                    var sus = insts.First(x => x.Program == of).Smps.ToDictionary(s => NoteToken(s.File), s => s);
                    var rel = new List<Smp>();
                    foreach (var f in Directory.GetFiles(folder, "*.wav").OrderBy(x => x))
                    {
                        Smp m; if (!sus.TryGetValue(NoteToken(f) ?? "", out m)) { Console.WriteLine("no sustain partner, skipped: " + Path.GetFileName(f)); continue; }
                        var s = ReadWav(f); s.Root = m.Root; s.Cents = m.Cents; s.Name = "r" + s.Root; rel.Add(s);
                    }
                    insts.Add(new Inst { Name = a[2] + " rel " + of, Program = prog, Smps = rel.OrderBy(s => s.Root).ToList(), Release = 1.0 });
                }
                else insts.Add(new Inst { Name = a[2] + " " + prog, Program = prog, Smps = Measured(src), Release = 0.35 });
                var last = insts[insts.Count - 1];
                Console.WriteLine("preset " + prog + ": " + last.Smps.Count + " recordings, keys " + Name(Lo(last.Smps, 0)) + "-" + Name(Hi(last.Smps, last.Smps.Count - 1)));
            }
            File.WriteAllBytes(a[1], Build(insts, a[2], 30));
            Console.WriteLine("wrote " + a[1]);
            return 0;
        }

        public static int Main(string[] a)
        {
            if (a.Length > 3 && a[0] == "--multi") return Multi(a);   // a[1] out.sf2, a[2] name, a[3..] presets
            if (a.Length < 3) { Console.Error.WriteLine("usage: WavToSf2 <folder> <out.sf2> <preset name> [release s] [velocity range dB]"); return 2; }
            double release = a.Length > 3 ? double.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture) : 0.15;
            double velDb = a.Length > 4 ? double.Parse(a[4], System.Globalization.CultureInfo.InvariantCulture) : 30;
            var smps = new List<Smp>();
            foreach (var f in Directory.GetFiles(a[0], "*.wav").OrderBy(x => x))
            {
                var s = ReadWav(f);
                double hz = Pitch(s);
                double midi = 69 + 12 * Math.Log(hz / 440, 2);
                s.Root = (int)Math.Round(midi); s.Cents = (int)Math.Round((s.Root - midi) * 100); s.Hz = hz;
                s.Name = "h" + s.Root;
                smps.Add(s);
                Console.WriteLine(string.Format("{0,-44} {1,8:0.00} Hz -> key {2,3} ({3}), correction {4,3:+0;-0;0} cents, {5:0.0} s, {6} ch",
                    Path.GetFileName(f), hz, s.Root, Name(s.Root), s.Cents, s.L.Length / (double)s.Rate, s.R == null ? 1 : 2));
            }
            smps = smps.OrderBy(s => s.Root).ToList();
            if (smps.Select(s => s.Root).Distinct().Count() != smps.Count) { Console.Error.WriteLine("two files measured as the same key; check the recordings"); return 1; }
            File.WriteAllBytes(a[1], Build(new List<Inst> { new Inst { Name = a[2], Program = 0, Smps = smps, Release = release } }, a[2], velDb));
            Console.WriteLine("wrote " + a[1] + ": keys " + Name(Lo(smps, 0)) + "-" + Name(Hi(smps, smps.Count - 1)));
            return 0;
        }

        static string Name(int k) { return new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" }[k % 12] + (k / 12 - 1); }
        // each recording covers the keys from halfway to its lower neighbour up to halfway to its upper one; the ends reach 5 keys further
        static int Lo(List<Smp> s, int i) { return i == 0 ? Math.Max(0, s[0].Root - 5) : (s[i - 1].Root + s[i].Root) / 2 + 1; }
        static int Hi(List<Smp> s, int i) { return i == s.Count - 1 ? Math.Min(127, s[i].Root + 5) : (s[i].Root + s[i + 1].Root) / 2; }

        static Smp ReadWav(string f)
        {
            var b = File.ReadAllBytes(f);
            int ch = 0, rate = 0, bits = 0, pos = 12, data = -1, len = 0;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4); int sz = BitConverter.ToInt32(b, pos + 4);
                if (id == "fmt ") { ch = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22); }
                if (id == "data") { data = pos + 8; len = Math.Min(sz, b.Length - data); break; }
                pos += 8 + sz + (sz & 1);
            }
            if (data < 0 || (bits != 16 && bits != 24) || ch < 1 || ch > 2) throw new Exception(f + ": need 16- or 24-bit mono/stereo PCM");
            int bps = bits / 8, frames = len / (bps * ch);
            var s = new Smp { File = f, Rate = rate, L = new short[frames], R = ch == 2 ? new short[frames] : null };
            for (int i = 0; i < frames; i++)
                for (int c = 0; c < ch; c++)
                {
                    int o = data + (i * ch + c) * bps;
                    int v = bits == 16 ? BitConverter.ToInt16(b, o) : (b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16)) >> 8;   // 24 -> 16 bit
                    if (c == 0) s.L[i] = (short)v; else s.R[i] = (short)v;
                }
            return s;
        }

        // normalised autocorrelation over 0.15-0.65 s of the mono mix, 25-2000 Hz, first peak within 90% of the best
        static double Pitch(Smp s)
        {
            int n = s.L.Length, st = Math.Min(n / 4, (int)(0.15 * s.Rate)), w = Math.Min(n - st - 1, (int)(0.5 * s.Rate));
            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = (s.L[i] + (s.R != null ? s.R[i] : s.L[i])) / 65536.0;
            int minLag = s.Rate / 2000, maxLag = Math.Min(w / 2, s.Rate / 25);
            int win = w - maxLag;
            var r = new double[maxLag + 2];
            for (int lag = minLag; lag <= maxLag + 1; lag++)
            {
                double num = 0, e1 = 0, e2 = 0;
                for (int i = 0; i < win; i++) { double p = x[st + i], q = x[st + i + lag]; num += p * q; e1 += p * p; e2 += q * q; }
                r[lag] = num / Math.Sqrt(e1 * e2 + 1e-12);
            }
            double top = 0; for (int lag = minLag + 1; lag <= maxLag; lag++) top = Math.Max(top, r[lag]);
            int bl = -1;
            for (int lag = minLag + 1; lag <= maxLag; lag++) if (r[lag] > r[lag - 1] && r[lag] >= r[lag + 1] && r[lag] > 0.9 * top) { bl = lag; break; }
            if (bl < 0) throw new Exception(s.File + ": no clear pitch");
            double y0 = r[bl - 1], y1 = r[bl], y2 = r[bl + 1], den = y0 - 2 * y1 + y2;
            return s.Rate / (bl + (den == 0 ? 0 : 0.5 * (y0 - y2) / den));
        }

        // ---------- SF2 writing ----------
        static byte[] Str(string s, int n) { var b = new byte[n]; var t = Encoding.ASCII.GetBytes(s); Array.Copy(t, b, Math.Min(t.Length, n - 1)); return b; }
        static byte[] Chunk(string id, byte[] data)
        {
            var o = new List<byte>(Encoding.ASCII.GetBytes(id)); o.AddRange(BitConverter.GetBytes(data.Length)); o.AddRange(data);
            if ((data.Length & 1) == 1) o.Add(0);
            return o.ToArray();
        }
        static byte[] List(string type, params byte[][] chunks) { var d = new List<byte>(Encoding.ASCII.GetBytes(type)); foreach (var c in chunks) d.AddRange(c); return Chunk("LIST", d.ToArray()); }
        static void U16(List<byte> o, int v) { o.Add((byte)v); o.Add((byte)(v >> 8)); }
        static void Gen(List<byte> o, int op, int amount) { U16(o, op); U16(o, amount); }
        static void GenRange(List<byte> o, int op, int lo, int hi) { U16(o, op); o.Add((byte)lo); o.Add((byte)hi); }

        static byte[] Build(List<Inst> insts, string name, double velDb)
        {
            // sample data: each sample followed by 46 zero points, as the format requires
            var smpl = new List<byte>(); var hdr = new List<byte>();
            var order = new List<Tuple<Smp, bool>>();   // (sample, right channel?)
            foreach (var s in insts.SelectMany(x => x.Smps)) { order.Add(Tuple.Create(s, false)); if (s.R != null) order.Add(Tuple.Create(s, true)); }
            int idx = 0;
            foreach (var t in order)
            {
                var d = t.Item2 ? t.Item1.R : t.Item1.L;
                int start = smpl.Count / 2;
                foreach (var v in d) U16(smpl, v);
                for (int z = 0; z < 46; z++) U16(smpl, 0);
                int end = start + d.Length;
                bool stereo = t.Item1.R != null;
                hdr.AddRange(Str(t.Item1.Name + (stereo ? (t.Item2 ? "R" : "L") : ""), 20));
                hdr.AddRange(BitConverter.GetBytes(start)); hdr.AddRange(BitConverter.GetBytes(end));
                hdr.AddRange(BitConverter.GetBytes(start)); hdr.AddRange(BitConverter.GetBytes(Math.Max(start, end - 1)));   // loop points unused
                hdr.AddRange(BitConverter.GetBytes(t.Item1.Rate));
                hdr.Add((byte)t.Item1.Root); hdr.Add((byte)(sbyte)t.Item1.Cents);
                U16(hdr, stereo ? (t.Item2 ? idx - 1 : idx + 1) : 0);     // linked partner
                U16(hdr, stereo ? (t.Item2 ? 2 : 4) : 1);                 // 1 mono, 2 right, 4 left
                idx++;
            }
            hdr.AddRange(Str("EOS", 20)); hdr.AddRange(new byte[26]);

            // one instrument per preset: a zone per recording (two for stereo, panned hard left/right)
            var ibag = new List<byte>(); var igen = new List<byte>(); var imod = new List<byte>(); var inst = new List<byte>();
            int zones = 0, gens = 0, mods = 0;
            idx = 0;
            foreach (var ins in insts)
            {
                var smps = ins.Smps;
                inst.AddRange(Str(ins.Name, 20)); U16(inst, zones);
                int rel = (int)Math.Round(1200 * Math.Log(ins.Release, 2));
                // global zone: damper-like release, and a gentler velocity curve than the SF2 default (96 dB): a harpsichord barely changes loudness
                U16(ibag, gens); U16(ibag, mods); zones++;
                U16(imod, 0x0502); U16(imod, 48); U16(imod, (int)Math.Round(velDb * 10)); U16(imod, 0); U16(imod, 0); mods++;
                Gen(igen, 38, rel); gens++;
                for (int i = 0; i < smps.Count; i++)
                {
                    var s = smps[i];
                    foreach (var right in s.R != null ? new[] { false, true } : new[] { false })
                    {
                        U16(ibag, gens); U16(ibag, mods); zones++;
                        GenRange(igen, 43, Lo(smps, i), Hi(smps, i)); gens++;
                        if (s.R != null) { Gen(igen, 17, right ? 500 : -500); gens++; }
                        Gen(igen, 53, idx++); gens++;
                    }
                }
            }
            U16(ibag, gens); U16(ibag, mods);   // terminal
            Gen(igen, 0, 0);
            U16(imod, 0); U16(imod, 0); U16(imod, 0); U16(imod, 0); U16(imod, 0);
            inst.AddRange(Str("EOI", 20)); U16(inst, zones);

            // presets in bank 0, each pointing at its instrument
            var phdr = new List<byte>(); var pbag = new List<byte>(); var pgen = new List<byte>();
            for (int k = 0; k < insts.Count; k++)
            {
                phdr.AddRange(Str(insts.Count == 1 ? name : insts[k].Name, 20)); U16(phdr, insts[k].Program); U16(phdr, 0); U16(phdr, k); phdr.AddRange(new byte[12]);
                U16(pbag, k); U16(pbag, 0); Gen(pgen, 41, k);
            }
            phdr.AddRange(Str("EOP", 20)); U16(phdr, 0); U16(phdr, 0); U16(phdr, insts.Count); phdr.AddRange(new byte[12]);
            U16(pbag, insts.Count); U16(pbag, 0); Gen(pgen, 0, 0);
            var pmod = new byte[10];

            var info = List("INFO", Chunk("ifil", new byte[] { 2, 0, 1, 0 }), Chunk("isng", Str("EMU8000", 8)), Chunk("INAM", Str(name, 64)),
                Chunk("ICMT", Str("Built by Abend Orchestra's WavToSf2 from CC0 recordings (Versilian Community Sample Library).", 128)));
            var sdta = List("sdta", Chunk("smpl", smpl.ToArray()));
            var pdta = List("pdta", Chunk("phdr", phdr.ToArray()), Chunk("pbag", pbag.ToArray()), Chunk("pmod", pmod), Chunk("pgen", pgen.ToArray()),
                Chunk("inst", inst.ToArray()), Chunk("ibag", ibag.ToArray()), Chunk("imod", imod.ToArray()), Chunk("igen", igen.ToArray()), Chunk("shdr", hdr.ToArray()));
            var body = new List<byte>(Encoding.ASCII.GetBytes("sfbk")); body.AddRange(info); body.AddRange(sdta); body.AddRange(pdta);
            return Chunk("RIFF", body.ToArray());
        }
    }
}
