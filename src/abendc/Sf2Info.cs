// Reads a SoundFont's preset/instrument/sample headers (not the audio) to answer: how long can a key of a preset
// sound? Looped samples sustain forever; unlooped ones stop when the recording ends (shorter when played higher).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Abend
{
    public class Sf2Info
    {
        class Zone { public int Lo, Hi, VLo, VHi, Root, Start, End, Rate; public bool Loop; public double Seconds, Attack, Usable = -1; }
        readonly string file; long smplPos = -1;   // where the 16-bit sample points start in the file
        readonly Dictionary<int, List<Zone>> presets = new Dictionary<int, List<Zone>>();   // key: bank * 128 + program
        public readonly Dictionary<int, string> Names = new Dictionary<int, string>();

        static readonly Dictionary<string, Sf2Info> cache = new Dictionary<string, Sf2Info>(StringComparer.OrdinalIgnoreCase);
        public static Sf2Info Load(string path)
        {
            Sf2Info i;
            lock (cache) if (!cache.TryGetValue(path, out i)) cache[path] = i = new Sf2Info(path);
            return i;
        }

        Sf2Info(string path)
        {
            file = path;
            var ch = new Dictionary<string, byte[]>();
            using (var fs = File.OpenRead(path))
            {
                var br = new BinaryReader(fs);
                fs.Position = 12;
                while (fs.Position < fs.Length - 8)
                {
                    string id = Encoding.ASCII.GetString(br.ReadBytes(4)); int len = br.ReadInt32();
                    if (id == "LIST")
                    {
                        string t = Encoding.ASCII.GetString(br.ReadBytes(4));
                        if (t == "pdta") { long end = fs.Position + len - 4; while (fs.Position < end) { string c = Encoding.ASCII.GetString(br.ReadBytes(4)); int cl = br.ReadInt32(); ch[c] = br.ReadBytes(cl); if ((cl & 1) == 1) fs.Position++; } }
                        else if (t == "sdta")
                        {
                            long end = fs.Position + len - 4;   // note where the sample points are; they are only read when needed
                            while (fs.Position < end) { string c = Encoding.ASCII.GetString(br.ReadBytes(4)); int cl = br.ReadInt32(); if (c == "smpl") smplPos = fs.Position; fs.Position += cl + (cl & 1); }
                        }
                        else fs.Position += len - 4;
                    }
                    else fs.Position += len + (len & 1);
                }
            }
            byte[] ph = ch["phdr"], pb = ch["pbag"], pg = ch["pgen"], ins = ch["inst"], ib = ch["ibag"], ig = ch["igen"], sh = ch["shdr"];
            Func<byte[], int, int> u = (b, o) => b[o] | (b[o + 1] << 8);
            Func<byte[], int, int> s16 = (b, o) => (short)(b[o] | (b[o + 1] << 8));
            for (int p = 0; p < ph.Length / 38 - 1; p++)
            {
                int key = u(ph, p * 38 + 22) * 128 + u(ph, p * 38 + 20);
                Names[key] = Encoding.ASCII.GetString(ph, p * 38, 20).TrimEnd('\0');
                var zones = new List<Zone>();
                for (int z = u(ph, p * 38 + 24); z < u(ph, (p + 1) * 38 + 24); z++)
                {
                    int inst = -1, plo = 0, phi = 127;
                    for (int g = u(pb, z * 4); g < u(pb, (z + 1) * 4); g++)
                    {
                        int op = u(pg, g * 4);
                        if (op == 41) inst = u(pg, g * 4 + 2);
                        if (op == 43) { plo = pg[g * 4 + 2]; phi = pg[g * 4 + 3]; }
                    }
                    if (inst < 0) continue;
                    int gMode = 0; double gAttack = -12000;   // the instrument's global zone (no sample) sets defaults
                    for (int iz = u(ins, inst * 22 + 20); iz < u(ins, (inst + 1) * 22 + 20); iz++)
                    {
                        int smp = -1, mode = gMode, lo = 0, hi = 127, vlo = 0, vhi = 127, root = -1, coarse = 0; double attack = gAttack;
                        for (int g = u(ib, iz * 4); g < u(ib, (iz + 1) * 4); g++)
                        {
                            int op = u(ig, g * 4);
                            if (op == 53) smp = u(ig, g * 4 + 2);
                            else if (op == 54) mode = u(ig, g * 4 + 2) & 3;
                            else if (op == 43) { lo = ig[g * 4 + 2]; hi = ig[g * 4 + 3]; }
                            else if (op == 44) { vlo = ig[g * 4 + 2]; vhi = ig[g * 4 + 3]; }
                            else if (op == 58) root = u(ig, g * 4 + 2);
                            else if (op == 51) coarse = s16(ig, g * 4 + 2);
                            else if (op == 34) attack = s16(ig, g * 4 + 2);
                        }
                        if (smp < 0) { gMode = mode; gAttack = attack; continue; }
                        int o = smp * 46;
                        int start = BitConverter.ToInt32(sh, o + 20), end = BitConverter.ToInt32(sh, o + 24), rate = BitConverter.ToInt32(sh, o + 36);
                        zones.Add(new Zone { Lo = Math.Max(lo, plo), Hi = Math.Min(hi, phi), VLo = vlo, VHi = vhi, Root = (root >= 0 ? root : sh[o + 40]) - coarse,
                            Loop = mode == 1 || mode == 3, Seconds = (end - start) / (double)Math.Max(1, rate), Attack = Math.Pow(2, attack / 1200.0), Start = start, End = end, Rate = rate });
                    }
                }
                presets[key] = zones;
            }
        }

        public bool Has(int bank, int program) { return presets.ContainsKey(bank * 128 + program); }

        // seconds a key can sound at this velocity: infinity when every sounding zone loops; otherwise the shortest
        // recording among the zones that sound (stereo pairs stop together), shortened when played above its root
        public double Sustain(int bank, int program, int key, int vel)
        {
            List<Zone> zs;
            if (!presets.TryGetValue(bank * 128 + program, out zs)) return double.PositiveInfinity;
            var hit = zs.Where(z => key >= z.Lo && key <= z.Hi && vel >= z.VLo && vel <= z.VHi).ToList();
            if (hit.Count == 0 || hit.All(z => z.Loop)) return double.PositiveInfinity;
            return hit.Where(z => !z.Loop).Min(z => UsableSeconds(z) / Math.Pow(2, (key - z.Root) / 12.0));
        }

        // An unlooped recording ends with the note dying away, so the part that sounds "held" is shorter than the file:
        // the time until its level (50 ms RMS) falls 3 dB below the held level (median of 15-60% of the recording) for good.
        double UsableSeconds(Zone z)
        {
            if (z.Usable >= 0) return z.Usable;
            z.Usable = z.Seconds;
            if (smplPos < 0) return z.Usable;
            try
            {
                int n = z.End - z.Start; var b = new byte[n * 2];
                using (var fs = File.OpenRead(file)) { fs.Position = smplPos + 2L * z.Start; fs.Read(b, 0, b.Length); }
                int frame = Math.Max(1, z.Rate / 20), frames = n / frame;
                if (frames < 10) return z.Usable;
                var env = new double[frames];
                for (int f = 0; f < frames; f++) { double s = 0; for (int i = f * frame; i < (f + 1) * frame; i++) { double v = BitConverter.ToInt16(b, i * 2) / 32768.0; s += v * v; } env[f] = 10 * Math.Log10(s / frame + 1e-12); }
                double held = env.Skip(frames * 15 / 100).Take(Math.Max(1, frames * 45 / 100)).OrderBy(v => v).ElementAt(Math.Max(1, frames * 45 / 100) / 2);
                int last = frames - 1;
                while (last > frames * 15 / 100 && env[last] < held - 3) last--;
                z.Usable = (last + 1) * frame / (double)z.Rate;
            }
            catch { }
            return z.Usable;
        }

        // whether any recording sounds for this key at this velocity (some presets have holes, e.g. Sonatina's timpani)
        public bool Covers(int bank, int program, int key, int vel)
        {
            List<Zone> zs;
            return presets.TryGetValue(bank * 128 + program, out zs) && zs.Any(z => key >= z.Lo && key <= z.Hi && vel >= z.VLo && vel <= z.VHi);
        }

        // how far (in semitones) the recording that plays this key is transposed from its own pitch
        public int Stretch(int bank, int program, int key, int vel)
        {
            List<Zone> zs;
            if (!presets.TryGetValue(bank * 128 + program, out zs)) return 0;
            var hit = zs.Where(z => key >= z.Lo && key <= z.Hi && vel >= z.VLo && vel <= z.VHi).ToList();
            return hit.Count == 0 ? 0 : hit.Max(z => Math.Abs(key - z.Root));
        }

        public bool Loops(int bank, int program)
        {
            List<Zone> zs;
            return presets.TryGetValue(bank * 128 + program, out zs) && zs.Count > 0 && zs.All(z => z.Loop);
        }

        public double LongestAttack(int bank, int program)
        {
            List<Zone> zs;
            return presets.TryGetValue(bank * 128 + program, out zs) && zs.Count > 0 ? zs.Max(z => z.Attack) : 0;
        }
    }
}
