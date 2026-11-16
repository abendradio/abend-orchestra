// Song model, tempo map, project file format, MIDI import/export.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Abend
{
    public class NoteEv
    {
        public int Start, Dur, Pitch, Vel = 90;
        // render-time only (not saved): ticks the notation's normal articulation took off the written length (0 for slurred,
        // tenuto and staccato notes), and whether the note is a sustained chord (a "*" chord pattern), which may play legato
        public int Gap; public bool Sustained;
        public int Written;   // score notes only (not saved): the written length, for checks (0 = unknown)
        public NoteEv Clone() { return (NoteEv)MemberwiseClone(); }
    }

    public class Track
    {
        public string Name = "Track";
        public int Program = 48, Channel = 0, Volume = 100, Pan = 64;
        public bool Mute, Solo;
        public List<NoteEv> Notes = new List<NoteEv>();
        public int Bank;                                   // bank select (CC0); only the renderer sets it, for sample libraries
        public string Instrument;                          // a score script's "instrument:" text, if given (not saved; MIDI files have none)
        public List<CtlEv> Controls = new List<CtlEv>();  // controller changes (CC1, CC11) added by the renderer's expression option
        public List<Sym.DynMark> Dyn;                      // render-time only: the part's written dynamics as a resolved curve (absolute ticks)
        public Track Clone()
        {
            var t = (Track)MemberwiseClone();
            t.Notes = Notes.Select(n => n.Clone()).ToList();
            t.Controls = Controls.Select(c => new CtlEv { Tick = c.Tick, Cc = c.Cc, Val = c.Val }).ToList();
            return t;
        }
        public bool IsDrums { get { return Channel == 9; } }
    }

    public class TempoEv { public int Tick; public double Bpm; }
    public class CtlEv { public int Tick, Cc, Val; public const int PitchBend = -1; }   // Cc -1: pitch bend, Val 0-16383 (8192 = none)

    public class Song
    {
        public const int PPQ = 480;
        public string Title = "Untitled";
        public int Num = 4, Den = 4;
        public List<TempoEv> Tempos = new List<TempoEv> { new TempoEv { Tick = 0, Bpm = 100 } };
        public List<Track> Tracks = new List<Track>();

        public int BarTicks { get { return Num * 4 * PPQ / Den; } }
        // concert pitch: frequency of A4 (440 = standard). Other values tune every channel when the MIDI is written.
        public double TuningHz = 440;
        public double TuningCents { get { return 1200 * Math.Log(TuningHz / 440, 2); } }
        public double BaseBpm
        {
            get { return Tempos[0].Bpm; }
            set { Tempos[0].Bpm = value; }
        }

        public Song Clone()
        {
            var s = (Song)MemberwiseClone();
            s.Tempos = Tempos.Select(t => new TempoEv { Tick = t.Tick, Bpm = t.Bpm }).ToList();
            s.Tracks = Tracks.Select(t => t.Clone()).ToList();
            return s;
        }

        public int EndTick()
        {
            int e = 0;
            foreach (var t in Tracks) foreach (var n in t.Notes) e = Math.Max(e, n.Start + n.Dur);
            return e;
        }

        public double TickToMs(int tick)
        {
            double ms = 0; int last = 0; double bpm = Tempos[0].Bpm;
            foreach (var te in Tempos)
            {
                if (te.Tick >= tick) break;
                ms += (te.Tick - last) * 60000.0 / (bpm * PPQ);
                last = te.Tick; bpm = te.Bpm;
            }
            return ms + (tick - last) * 60000.0 / (bpm * PPQ);
        }

        public int MsToTick(double ms)
        {
            double acc = 0; int last = 0; double bpm = Tempos[0].Bpm;
            foreach (var te in Tempos)
            {
                double seg = (te.Tick - last) * 60000.0 / (bpm * PPQ);
                if (acc + seg > ms) break;
                acc += seg; last = te.Tick; bpm = te.Bpm;
            }
            return last + (int)((ms - acc) * bpm * PPQ / 60000.0);
        }

        public int NextFreeChannel()
        {
            for (int c = 0; c < 16; c++)
            {
                if (c == 9) continue;
                if (!Tracks.Any(t => t.Channel == c)) return c;
            }
            return 0;
        }

        // ---------- project files ----------
        public void Save(string path)
        {
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            sb.AppendLine("ABEND 1");
            sb.AppendLine("title " + Title.Replace("\n", " "));
            sb.AppendLine("meter " + Num + " " + Den);
            if (TuningHz != 440) sb.AppendLine("tuning " + TuningHz.ToString("0.###", ci));
            foreach (var t in Tempos) sb.AppendLine("tempo " + t.Tick + " " + t.Bpm.ToString("0.###", ci));
            foreach (var tr in Tracks)
            {
                sb.AppendLine("track " + tr.Program + " " + tr.Channel + " " + tr.Volume + " " + tr.Pan + " " + (tr.Mute ? 1 : 0) + " " + (tr.Solo ? 1 : 0) + " " + tr.Name.Replace("\n", " "));
                foreach (var n in tr.Notes) sb.AppendLine("n " + n.Start + " " + n.Dur + " " + n.Pitch + " " + n.Vel);
            }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public static Song Load(string path)
        {
            var s = new Song(); s.Tempos.Clear();
            Track cur = null;
            var ci = CultureInfo.InvariantCulture;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var p = line.Split(' ');
                switch (p[0])
                {
                    case "ABEND": break;
                    case "title": s.Title = line.Length > 6 ? line.Substring(6) : ""; break;
                    case "meter": s.Num = int.Parse(p[1]); s.Den = int.Parse(p[2]); break;
                    case "tuning": s.TuningHz = double.Parse(p[1], ci); break;
                    case "tempo": s.Tempos.Add(new TempoEv { Tick = int.Parse(p[1]), Bpm = double.Parse(p[2], ci) }); break;
                    case "track":
                        cur = new Track { Program = int.Parse(p[1]), Channel = int.Parse(p[2]), Volume = int.Parse(p[3]), Pan = int.Parse(p[4]), Mute = p[5] == "1", Solo = p[6] == "1" };
                        cur.Name = string.Join(" ", p.Skip(7));
                        s.Tracks.Add(cur); break;
                    case "n":
                        if (cur != null) cur.Notes.Add(new NoteEv { Start = int.Parse(p[1]), Dur = int.Parse(p[2]), Pitch = int.Parse(p[3]), Vel = int.Parse(p[4]) });
                        break;
                }
            }
            if (s.Tempos.Count == 0) s.Tempos.Add(new TempoEv { Tick = 0, Bpm = 100 });
            s.Tempos.Sort((a, b) => a.Tick.CompareTo(b.Tick));
            return s;
        }
    }

    public static class MidiFile
    {
        // ---------- export ----------
        static void VarLen(List<byte> b, int v)
        {
            var st = new Stack<byte>(); st.Push((byte)(v & 0x7F)); v >>= 7;
            while (v > 0) { st.Push((byte)((v & 0x7F) | 0x80)); v >>= 7; }
            b.AddRange(st);
        }
        static byte[] BE(int v, int n) { var r = new byte[n]; for (int i = n - 1; i >= 0; i--) { r[i] = (byte)(v & 0xFF); v >>= 8; } return r; }
        class Ev { public int T, Ord; public byte[] D; }
        static byte[] Meta(int type, byte[] data) { var l = new List<byte> { 0xFF, (byte)type }; VarLen(l, data.Length); l.AddRange(data); return l.ToArray(); }
        static byte[] Chunk(List<Ev> evs)
        {
            var b = new List<byte>(); int last = 0;
            foreach (var e in evs.OrderBy(x => x.T).ThenBy(x => x.Ord)) { VarLen(b, e.T - last); last = e.T; b.AddRange(e.D); }
            VarLen(b, 0); b.AddRange(new byte[] { 0xFF, 0x2F, 0 });
            var c = new List<byte>(Encoding.ASCII.GetBytes("MTrk")); c.AddRange(BE(b.Count, 4)); c.AddRange(b);
            return c.ToArray();
        }

        // Channel tuning by Registered Parameter Numbers: RPN 2 (coarse, semitones) only when the offset passes 100 cents,
        // RPN 1 (fine, 8192 = 0, +-8192 = +-100 cents), then the "null" RPN so later data entry changes nothing.
        // Pitch bend stays free for expression and stretched notes.
        public static List<int[]> TuningMessages(double cents)
        {
            var m = new List<int[]>();
            int coarse = Math.Abs(cents) > 100 ? (int)Math.Round(cents / 100) : 0;
            double fine = cents - 100 * coarse;
            if (coarse != 0) m.AddRange(new[] { new[] { 101, 0 }, new[] { 100, 2 }, new[] { 6, 64 + coarse }, new[] { 38, 0 } });
            int v = Math.Max(0, Math.Min(16383, 8192 + (int)Math.Round(fine / 100 * 8192)));
            m.AddRange(new[] { new[] { 101, 0 }, new[] { 100, 1 }, new[] { 6, v >> 7 }, new[] { 38, v & 127 }, new[] { 101, 127 }, new[] { 100, 127 } });
            return m;
        }

        public static void Write(Song s, string path) { Write(s, path, 0); }

        // tailSeconds: silence kept after the last note so reverb and releases can ring out when rendering
        public static void Write(Song s, string path, double tailSeconds) { Write(s, path, tailSeconds, -1); }

        // endTick: where the piece ends (for the tail); -1 = after its last note. Used when one piece is split into several files.
        public static void Write(Song s, string path, double tailSeconds, int endTick)
        {
            var chunks = new List<byte[]>();
            var t0 = new List<Ev> { new Ev { T = 0, D = Meta(0x03, Encoding.UTF8.GetBytes(s.Title)) } };
            if (tailSeconds > 0)
            {
                int end = endTick >= 0 ? endTick : s.EndTick();
                int tail = s.MsToTick(s.TickToMs(end) + tailSeconds * 1000) - end;
                t0.Add(new Ev { T = end + Math.Max(1, tail), Ord = 9, D = Meta(0x01, Encoding.ASCII.GetBytes("end")) });
            }
            int dd = 0, d = s.Den; while (d > 1) { d >>= 1; dd++; }
            t0.Add(new Ev { T = 0, D = Meta(0x58, new byte[] { (byte)s.Num, (byte)dd, 24, 8 }) });
            foreach (var te in s.Tempos) t0.Add(new Ev { T = te.Tick, D = Meta(0x51, BE((int)Math.Round(60000000.0 / te.Bpm), 3)) });
            chunks.Add(Chunk(t0));
            foreach (var tr in s.Tracks)
            {
                var evs = new List<Ev>();
                byte ch = (byte)tr.Channel;
                evs.Add(new Ev { T = 0, D = Meta(0x03, Encoding.UTF8.GetBytes(tr.Name)) });
                if (tr.Bank != 0) evs.Add(new Ev { T = 0, Ord = 0, D = new byte[] { (byte)(0xB0 | ch), 0, (byte)tr.Bank } });
                if (!tr.IsDrums) evs.Add(new Ev { T = 0, Ord = 1, D = new byte[] { (byte)(0xC0 | ch), (byte)tr.Program } });
                evs.Add(new Ev { T = 0, Ord = 1, D = new byte[] { (byte)(0xB0 | ch), 7, (byte)tr.Volume } });
                evs.Add(new Ev { T = 0, Ord = 1, D = new byte[] { (byte)(0xB0 | ch), 10, (byte)tr.Pan } });
                evs.Add(new Ev { T = 0, Ord = 1, D = new byte[] { (byte)(0xB0 | ch), 91, (byte)(tr.IsDrums ? 30 : 48) } });  // reverb send
                if (!tr.IsDrums && s.TuningHz != 440) foreach (var cc in TuningMessages(s.TuningCents)) evs.Add(new Ev { T = 0, Ord = 1, D = new byte[] { (byte)(0xB0 | ch), (byte)cc[0], (byte)cc[1] } });
                foreach (var c in tr.Controls)
                    evs.Add(new Ev { T = c.Tick, Ord = 1, D = c.Cc == CtlEv.PitchBend ? new byte[] { (byte)(0xE0 | ch), (byte)(c.Val & 0x7F), (byte)((c.Val >> 7) & 0x7F) }
                        : new byte[] { (byte)(0xB0 | ch), (byte)c.Cc, (byte)Math.Max(0, Math.Min(127, c.Val)) } });
                foreach (var n in tr.Notes)
                {
                    evs.Add(new Ev { T = n.Start, Ord = 3, D = new byte[] { (byte)(0x90 | ch), (byte)n.Pitch, (byte)Math.Max(1, Math.Min(127, n.Vel)) } });
                    evs.Add(new Ev { T = n.Start + n.Dur, Ord = 2, D = new byte[] { (byte)(0x80 | ch), (byte)n.Pitch, 0 } });
                }
                chunks.Add(Chunk(evs));
            }
            var f = new List<byte>(Encoding.ASCII.GetBytes("MThd"));
            f.AddRange(BE(6, 4)); f.AddRange(BE(1, 2)); f.AddRange(BE(chunks.Count, 2)); f.AddRange(BE(Song.PPQ, 2));
            foreach (var c in chunks) f.AddRange(c);
            File.WriteAllBytes(path, f.ToArray());
        }

        // ---------- import ----------
        public static Song Read(string path)
        {
            var b = File.ReadAllBytes(path);
            int pos = 0;
            Func<int, int> rd = n => { int v = 0; for (int i = 0; i < n; i++) v = (v << 8) | b[pos++]; return v; };
            if (Encoding.ASCII.GetString(b, 0, 4) != "MThd") throw new Exception("Not a standard MIDI file.");
            pos = 4; int hl = rd(4); int fmt = rd(2); int ntr = rd(2); int div = rd(2);
            pos = 8 + hl;
            if ((div & 0x8000) != 0) throw new Exception("SMPTE-timed MIDI files are not supported.");
            double scale = (double)Song.PPQ / div;
            var song = new Song { Title = Path.GetFileNameWithoutExtension(path) };
            song.Tempos.Clear();
            // key: (track index, channel) -> track
            var map = new Dictionary<long, Track>();
            for (int ti = 0; ti < ntr && pos + 8 <= b.Length; ti++)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4); pos += 4;
                int len = rd(4); int end = pos + len;
                if (id != "MTrk") { pos = end; continue; }
                int tick = 0, status = 0; string tname = null;
                var progs = new int[16]; var vols = new int[16]; var pans = new int[16];
                for (int i = 0; i < 16; i++) { progs[i] = -1; vols[i] = 100; pans[i] = 64; }
                var open = new Dictionary<int, Stack<NoteEv>>();
                Func<int, Track> trackFor = ch =>
                {
                    long key = ti * 16L + ch; Track t;
                    if (!map.TryGetValue(key, out t)) { t = new Track { Channel = ch, Program = Math.Max(0, progs[ch]), Name = tname ?? ("Track " + (ti + 1)) }; map[key] = t; }
                    return t;
                };
                while (pos < end)
                {
                    int delta = 0; byte c;
                    do { c = b[pos++]; delta = (delta << 7) | (c & 0x7F); } while ((c & 0x80) != 0);
                    tick += delta;
                    int st = b[pos];
                    if (st >= 0x80) { pos++; if (st < 0xF0) status = st; } else st = status;
                    int t = (int)Math.Round(tick * scale);
                    if (st == 0xFF)
                    {
                        int type = b[pos++]; int ml = 0;
                        do { c = b[pos++]; ml = (ml << 7) | (c & 0x7F); } while ((c & 0x80) != 0);
                        if (type == 0x51 && ml == 3) song.Tempos.Add(new TempoEv { Tick = t, Bpm = 60000000.0 / ((b[pos] << 16) | (b[pos + 1] << 8) | b[pos + 2]) });
                        else if (type == 0x58 && ml >= 2 && t == 0) { song.Num = b[pos]; song.Den = 1 << b[pos + 1]; }
                        else if (type == 0x03) { tname = Encoding.UTF8.GetString(b, pos, ml).Trim(); if (ti == 0 && fmt == 1 && tname.Length > 0) song.Title = tname; }
                        pos += ml;
                        continue;
                    }
                    if (st == 0xF0 || st == 0xF7)
                    {
                        int ml = 0; do { c = b[pos++]; ml = (ml << 7) | (c & 0x7F); } while ((c & 0x80) != 0);
                        pos += ml; continue;
                    }
                    int kind = st & 0xF0, ch2 = st & 0x0F;
                    int d1 = b[pos++];
                    int d2 = (kind == 0xC0 || kind == 0xD0) ? 0 : b[pos++];
                    if (kind == 0x90 && d2 > 0)
                    {
                        var tr = trackFor(ch2);
                        var n = new NoteEv { Start = t, Pitch = d1, Vel = d2, Dur = 0 };
                        tr.Notes.Add(n);
                        int k = ch2 * 128 + d1; Stack<NoteEv> s;
                        if (!open.TryGetValue(k, out s)) open[k] = s = new Stack<NoteEv>();
                        s.Push(n);
                    }
                    else if (kind == 0x80 || kind == 0x90)
                    {
                        int k = ch2 * 128 + d1; Stack<NoteEv> s;
                        if (open.TryGetValue(k, out s) && s.Count > 0) { var n = s.Pop(); n.Dur = Math.Max(10, t - n.Start); }
                    }
                    else if (kind == 0xC0) { if (progs[ch2] < 0) { progs[ch2] = d1; Track tr; if (map.TryGetValue(ti * 16L + ch2, out tr) && tr.Notes.Count == 0) tr.Program = d1; } }
                    else if (kind == 0xB0 && d1 == 7) vols[ch2] = d2;
                    else if (kind == 0xB0 && d1 == 10) pans[ch2] = d2;
                }
                foreach (var kv in map.Where(k => k.Key / 16 == ti))
                {
                    int ch = kv.Value.Channel;
                    if (progs[ch] >= 0) kv.Value.Program = progs[ch];
                    kv.Value.Volume = vols[ch]; kv.Value.Pan = pans[ch];
                    if (tname != null) kv.Value.Name = tname;
                    foreach (var n in kv.Value.Notes) if (n.Dur == 0) n.Dur = Song.PPQ / 2;
                }
                pos = end;
            }
            if (song.Tempos.Count == 0) song.Tempos.Add(new TempoEv { Tick = 0, Bpm = 120 });
            song.Tempos = song.Tempos.OrderBy(x => x.Tick).ToList();
            if (song.Tempos[0].Tick != 0) song.Tempos.Insert(0, new TempoEv { Tick = 0, Bpm = song.Tempos[0].Bpm });
            song.Tracks = map.OrderBy(k => k.Key).Select(k => k.Value).Where(t => t.Notes.Count > 0).ToList();
            foreach (var t in song.Tracks) t.Notes.Sort((x, y) => x.Start.CompareTo(y.Start));
            return song;
        }
    }

    public static class GM
    {
        public static readonly string[] Names = {
            "Acoustic Grand Piano","Bright Acoustic Piano","Electric Grand Piano","Honky-tonk Piano","Electric Piano 1","Electric Piano 2","Harpsichord","Clavinet",
            "Celesta","Glockenspiel","Music Box","Vibraphone","Marimba","Xylophone","Tubular Bells","Dulcimer",
            "Drawbar Organ","Percussive Organ","Rock Organ","Church Organ","Reed Organ","Accordion","Harmonica","Tango Accordion",
            "Nylon Guitar","Steel Guitar","Jazz Guitar","Clean Guitar","Muted Guitar","Overdriven Guitar","Distortion Guitar","Guitar Harmonics",
            "Acoustic Bass","Finger Bass","Pick Bass","Fretless Bass","Slap Bass 1","Slap Bass 2","Synth Bass 1","Synth Bass 2",
            "Violin","Viola","Cello","Contrabass","Tremolo Strings","Pizzicato Strings","Orchestral Harp","Timpani",
            "String Ensemble 1","String Ensemble 2","Synth Strings 1","Synth Strings 2","Choir Aahs","Voice Oohs","Synth Voice","Orchestra Hit",
            "Trumpet","Trombone","Tuba","Muted Trumpet","French Horn","Brass Section","Synth Brass 1","Synth Brass 2",
            "Soprano Sax","Alto Sax","Tenor Sax","Baritone Sax","Oboe","English Horn","Bassoon","Clarinet",
            "Piccolo","Flute","Recorder","Pan Flute","Blown Bottle","Shakuhachi","Whistle","Ocarina",
            "Square Lead","Sawtooth Lead","Calliope Lead","Chiff Lead","Charang Lead","Voice Lead","Fifths Lead","Bass + Lead",
            "New Age Pad","Warm Pad","Polysynth Pad","Choir Pad","Bowed Pad","Metallic Pad","Halo Pad","Sweep Pad",
            "Rain FX","Soundtrack FX","Crystal FX","Atmosphere FX","Brightness FX","Goblins FX","Echoes FX","Sci-fi FX",
            "Sitar","Banjo","Shamisen","Koto","Kalimba","Bagpipe","Fiddle","Shanai",
            "Tinkle Bell","Agogo","Steel Drums","Woodblock","Taiko Drum","Melodic Tom","Synth Drum","Reverse Cymbal",
            "Guitar Fret Noise","Breath Noise","Seashore","Bird Tweet","Telephone Ring","Helicopter","Applause","Gunshot" };

        // the orchestra used by the symphony, for "Add orchestra"
        public static readonly int[][] Orchestra = {
            new[]{73}, new[]{68}, new[]{71}, new[]{70}, new[]{60}, new[]{56}, new[]{57}, new[]{58}, new[]{47}, new[]{46},
            new[]{48}, new[]{48}, new[]{41}, new[]{42}, new[]{43} };
        public static readonly string[] OrchestraNames = { "Flutes", "Oboes", "Clarinets", "Bassoons", "Horns", "Trumpets", "Trombones", "Tuba", "Timpani", "Harp", "Violins I", "Violins II", "Violas", "Cellos", "Basses" };
        public static readonly int[][] OrchestraRange = {
            new[]{60,98}, new[]{58,89}, new[]{50,94}, new[]{34,76}, new[]{35,77}, new[]{54,84}, new[]{40,77}, new[]{26,65}, new[]{40,57}, new[]{24,103},
            new[]{55,100}, new[]{55,96}, new[]{48,88}, new[]{36,81}, new[]{28,55} };

        public static string NoteName(int p)
        {
            string[] n = { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };
            return n[p % 12] + (p / 12 - 1);
        }
    }
}
