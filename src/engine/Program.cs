// Build driver: finds Sym.Mvt* classes, renders each to MIDI + JSON, prints a report.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Sym
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : "out";
            Directory.CreateDirectory(outDir);
            var types = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.Namespace == "Sym" && t.Name.StartsWith("Mvt") && t.GetMethod("Build") != null)
                .OrderBy(t => t.Name).ToList();
            int rc = 0;
            foreach (var ty in types)
            {
                try
                {
                    var mv = (Movement)ty.GetMethod("Build").Invoke(null, null);
                    Render.Finalize(mv);
                    string baseName = Path.Combine(outDir, ty.Name.ToLower());
                    Midi.Write(mv, baseName + ".mid");
                    Json.Write(mv, baseName + ".json");
                    Report.Print(mv);
                }
                catch (TargetInvocationException ex)
                {
                    Console.WriteLine("ERROR in " + ty.Name + ": " + ex.InnerException.Message);
                    Console.WriteLine(ex.InnerException.StackTrace);
                    rc = 1;
                }
            }
            return rc;
        }
    }

    public static class Render
    {
        public static double LevelAt(List<DynMark> curve, int t)
        {
            // curve already resolved: points with Level>=0, Ramp means linear to next
            double lv = 3; // default p? use mf-ish start if none
            if (curve.Count == 0) return 4.5;
            if (t < curve[0].Time) return curve[0].Level;
            for (int i = 0; i < curve.Count; i++)
            {
                if (curve[i].Time <= t)
                {
                    lv = curve[i].Level;
                    if (curve[i].Ramp && i + 1 < curve.Count && curve[i + 1].Time > t)
                    {
                        double f = (double)(t - curve[i].Time) / (curve[i + 1].Time - curve[i].Time);
                        lv = curve[i].Level + (curve[i + 1].Level - curve[i].Level) * f;
                    }
                }
                else break;
            }
            return lv;
        }

        public static List<DynMark> Resolve(List<DynMark> marks)
        {
            var sorted = marks.OrderBy(m => m.Time).ThenBy(m => m.Level < 0 ? 1 : 0).ToList();
            var res = new List<DynMark>();
            double cur = 4.5;
            foreach (var m in sorted)
            {
                if (m.Level < 0)
                {
                    // ramp starts from current level; if a point already exists at this time, flag it
                    if (res.Count > 0 && res[res.Count - 1].Time == m.Time) res[res.Count - 1].Ramp = true;
                    else res.Add(new DynMark { Time = m.Time, Level = cur, Ramp = true });
                }
                else
                {
                    if (res.Count > 0 && res[res.Count - 1].Time == m.Time && !res[res.Count - 1].Ramp) res[res.Count - 1].Level = m.Level;
                    else if (res.Count > 0 && res[res.Count - 1].Time == m.Time && res[res.Count - 1].Ramp)
                    {
                        // a dynamic and a hairpin at the same instant: the dynamic is the ramp's start
                        res[res.Count - 1].Level = m.Level;
                    }
                    else res.Add(new DynMark { Time = m.Time, Level = m.Level });
                    cur = m.Level;
                }
            }
            return res;
        }

        public static void Finalize(Movement mv)
        {
            foreach (var p in mv.Parts.Values)
            {
                // timpani rolls -> repeated strokes
                if (p.Id == "ti")
                {
                    var outN = new List<Note>();
                    foreach (var n in p.Notes)
                    {
                        if (n.Art == "tr")
                        {
                            int step = 80; // 1/24 note
                            for (int t = 0; t < n.Dur; t += step)
                            {
                                var c = n.Clone(); c.Art = ""; c.Start = n.Start + t; c.Dur = Math.Min(step, n.Dur - t); c.Accent = n.Accent && t == 0;
                                outN.Add(c);
                            }
                        }
                        else outN.Add(n);
                    }
                    p.Notes = outN;
                }
                p.Notes = p.Notes.OrderBy(n => n.Start).ThenBy(n => n.Midi).ToList();
                // remove exact duplicates (same start + pitch), keep the longer one
                var dedup = new List<Note>();
                var seen = new Dictionary<long, Note>();
                foreach (var n in p.Notes)
                {
                    long key = (long)n.Start * 1000 + n.Midi;
                    Note ex;
                    if (seen.TryGetValue(key, out ex)) { if (n.Dur > ex.Dur) ex.Dur = n.Dur; ex.Accent |= n.Accent; continue; }
                    seen[key] = n; dedup.Add(n);
                }
                p.Notes = dedup;
                p.Dyn = Resolve(p.Dyn);
                bool rolled = p.Id == "ti";
                int idx = 0;
                foreach (var n in p.Notes)
                {
                    double lv = LevelAt(p.Dyn, n.Start);
                    double v = 16 + lv * 13;
                    if (n.Accent) v += 14;
                    if (n.Sfz) v += 10;
                    if (rolled && !n.Accent) v -= 8 + (idx % 3) * 3;
                    n.Vel = Math.Max(8, Math.Min(127, (int)Math.Round(v)));
                    idx++;
                }
            }
            mv.BuildTimeMap(mv.EndTick() + T.W * 2);
        }

        public static int Gate(Note n, Part p)
        {
            if (n.Art == "pz") return Math.Min(n.Dur, T.E);
            if (n.Staccato) return Math.Max(40, (int)(n.Dur * 0.45));
            if (n.Legato || n.Tenuto) return n.Dur;
            return (int)(n.Dur * 0.9);
        }
    }

    public static class Midi
    {
        static void VarLen(List<byte> b, int v)
        {
            var st = new Stack<byte>();
            st.Push((byte)(v & 0x7F));
            v >>= 7;
            while (v > 0) { st.Push((byte)((v & 0x7F) | 0x80)); v >>= 7; }
            b.AddRange(st);
        }
        class Ev { public int T; public int Ord; public byte[] D; }

        static byte[] Track(List<Ev> evs)
        {
            var b = new List<byte>();
            int last = 0;
            foreach (var e in evs.OrderBy(x => x.T).ThenBy(x => x.Ord))
            {
                VarLen(b, e.T - last); last = e.T; b.AddRange(e.D);
            }
            VarLen(b, 0); b.AddRange(new byte[] { 0xFF, 0x2F, 0x00 });
            var trk = new List<byte>(Encoding.ASCII.GetBytes("MTrk"));
            trk.AddRange(BE(b.Count, 4)); trk.AddRange(b);
            return trk.ToArray();
        }
        static byte[] BE(int v, int n) { var r = new byte[n]; for (int i = n - 1; i >= 0; i--) { r[i] = (byte)(v & 0xFF); v >>= 8; } return r; }
        static byte[] Meta(int type, byte[] data) { var l = new List<byte> { 0xFF, (byte)type }; var vl = new List<byte>(); VarLen(vl, data.Length); l.AddRange(vl); l.AddRange(data); return l.ToArray(); }

        public static void Write(Movement mv, string path)
        {
            var tracks = new List<byte[]>();
            int end = mv.EndTick();
            var t0 = new List<Ev>();
            t0.Add(new Ev { T = 0, D = Meta(0x03, Encoding.UTF8.GetBytes(mv.Title)) });
            foreach (var kv in mv.TempoEvents(end))
            {
                int us = (int)Math.Round(60000000.0 / kv.Value);
                t0.Add(new Ev { T = kv.Key, D = Meta(0x51, BE(us, 3)) });
            }
            foreach (var m in mv.Meters)
            {
                int den = 0, d = m[2]; while (d > 1) { d >>= 1; den++; }
                t0.Add(new Ev { T = mv.Pos(m[0], 0), D = Meta(0x58, new byte[] { (byte)m[1], (byte)den, 24, 8 }) });
            }
            foreach (var mk in mv.Markers) t0.Add(new Ev { T = mk.Time, D = Meta(0x06, Encoding.UTF8.GetBytes(mk.Text)) });
            tracks.Add(Track(t0));

            foreach (var id in mv.Order)
            {
                var p = mv.Parts[id];
                if (p.Notes.Count == 0) continue;
                var evs = new List<Ev>();
                byte ch = (byte)p.Channel;
                evs.Add(new Ev { T = 0, D = Meta(0x03, Encoding.UTF8.GetBytes(p.Name)) });
                evs.Add(new Ev { T = 0, Ord = 1, D = new byte[] { (byte)(0xC0 | ch), (byte)p.Program } });
                evs.Add(new Ev { T = 0, Ord = 1, D = new byte[] { (byte)(0xB0 | ch), 7, 100 } });
                int pan = PanFor(id);
                evs.Add(new Ev { T = 0, Ord = 1, D = new byte[] { (byte)(0xB0 | ch), 10, (byte)pan } });
                evs.Add(new Ev { T = 0, Ord = 1, D = new byte[] { (byte)(0xB0 | ch), 91, 60 } });
                // expression curve
                int lastCC = -1;
                for (int t = 0; t <= end; t += 60)
                {
                    int cc = (int)Math.Round(36 + Render.LevelAt(p.Dyn, t) * 11);
                    cc = Math.Max(0, Math.Min(127, cc));
                    if (cc != lastCC) { evs.Add(new Ev { T = t, Ord = 2, D = new byte[] { (byte)(0xB0 | ch), 11, (byte)cc } }); lastCC = cc; }
                }
                // notes with articulation program switches for strings
                bool str = id == "v1" || id == "v2" || id == "va" || id == "vc" || id == "cb";
                int curProg = p.Program;
                var sorted = p.Notes.OrderBy(n => n.Start).ToList();
                // prevent same-pitch overlaps
                var lastOff = new Dictionary<int, Ev>();
                foreach (var n in sorted)
                {
                    if (str)
                    {
                        int want = n.Art == "pz" ? 45 : n.Art == "tr" ? 44 : p.Program;
                        if (want != curProg) { evs.Add(new Ev { T = n.Start, Ord = 3, D = new byte[] { (byte)(0xC0 | ch), (byte)want } }); curProg = want; }
                    }
                    int g = Render.Gate(n, p);
                    Ev prevOff;
                    if (lastOff.TryGetValue(n.Midi, out prevOff) && prevOff.T > n.Start) prevOff.T = n.Start;
                    evs.Add(new Ev { T = n.Start, Ord = 5, D = new byte[] { (byte)(0x90 | ch), (byte)n.Midi, (byte)n.Vel } });
                    var off = new Ev { T = n.Start + g, Ord = 4, D = new byte[] { (byte)(0x80 | ch), (byte)n.Midi, 0 } };
                    evs.Add(off); lastOff[n.Midi] = off;
                }
                tracks.Add(Track(evs));
            }
            var file = new List<byte>(Encoding.ASCII.GetBytes("MThd"));
            file.AddRange(BE(6, 4)); file.AddRange(BE(1, 2)); file.AddRange(BE(tracks.Count, 2)); file.AddRange(BE(T.PPQ, 2));
            foreach (var tr in tracks) file.AddRange(tr);
            File.WriteAllBytes(path, file.ToArray());
        }
        public static int PanFor(string id)
        {
            switch (id)
            {
                case "v1": return 34; case "v2": return 50; case "va": return 74; case "vc": return 86; case "cb": return 96;
                case "fl": return 56; case "ob": return 70; case "cl": return 54; case "bn": return 72;
                case "hn": return 44; case "tp": return 76; case "tb": return 88; case "tu": return 96; case "ti": return 60; case "hp": return 28;
            }
            return 64;
        }
    }

    public static class Json
    {
        static string F(double d) { return d.ToString("0.###", CultureInfo.InvariantCulture); }
        public static void Write(Movement mv, string path)
        {
            var sb = new StringBuilder();
            int end = mv.EndTick();
            sb.Append("{\"title\":\"" + mv.Title.Replace("\"", "'") + "\",\"tempo\":\"" + mv.Tempo + "\",\"duration\":" + F(mv.Sec(end)) + ",");
            sb.Append("\"parts\":[" + string.Join(",", mv.Order.Select(id => "\"" + id + "\"")) + "],");
            sb.Append("\"markers\":[" + string.Join(",", mv.Markers.OrderBy(m => m.Time).Select(m => "[" + F(mv.Sec(m.Time)) + ",\"" + m.Text.Replace("\"", "'") + "\"]")) + "],");
            sb.Append("\"curves\":{");
            bool first = true;
            foreach (var id in mv.Order)
            {
                var p = mv.Parts[id];
                if (!first) sb.Append(","); first = false;
                sb.Append("\"" + id + "\":[");
                sb.Append(string.Join(",", p.Dyn.Select(d => "[" + F(mv.Sec(d.Time)) + "," + F(d.Level) + "," + (d.Ramp ? 1 : 0) + "]")));
                sb.Append("]");
            }
            sb.Append("},\"notes\":[");
            var all = new List<string>();
            for (int pi = 0; pi < mv.Order.Count; pi++)
            {
                var p = mv.Parts[mv.Order[pi]];
                foreach (var n in p.Notes)
                {
                    double s = mv.Sec(n.Start), e = mv.Sec(n.Start + Render.Gate(n, p));
                    int art = n.Art == "pz" ? 1 : n.Art == "tr" ? 2 : n.Staccato ? 3 : n.Legato ? 4 : 0;
                    all.Add("[" + F(s) + "," + F(e - s) + "," + n.Midi + "," + pi + "," + art + "," + n.Vel + "]");
                }
            }
            sb.Append(string.Join(",", all));
            sb.Append("]}");
            File.WriteAllText(path, sb.ToString());
        }
    }

    public static class Report
    {
        public static void Print(Movement mv)
        {
            int end = mv.EndTick();
            int off; int bars = mv.BarAt(end - 1, out off);
            double secs = mv.Sec(end);
            Console.WriteLine("== " + mv.Title + " : " + bars + " bars, " + ((int)secs / 60) + ":" + ((int)secs % 60).ToString("00") + ", notes " + mv.Parts.Values.Sum(p => p.Notes.Count));
            foreach (var id in mv.Order)
            {
                var p = mv.Parts[id];
                if (p.Notes.Count == 0) continue;
                var bad = p.Notes.Where(n => n.Midi < p.Lo || n.Midi > p.Hi).ToList();
                string line = "  " + id.PadRight(3) + " notes " + p.Notes.Count.ToString().PadLeft(5) + "  range " + Theory.NoteName(p.Notes.Min(n => n.Midi)) + "-" + Theory.NoteName(p.Notes.Max(n => n.Midi));
                if (bad.Count > 0)
                {
                    line += "  OUT OF RANGE x" + bad.Count + ": " + string.Join(", ", bad.Take(6).Select(n => { int o; int b = mv.BarAt(n.Start, out o); return Theory.NoteName(n.Midi) + "@" + b; }));
                }
                Console.WriteLine(line);
            }
        }
    }
}
