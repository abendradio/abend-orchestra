// Sample libraries for audio renders (abendc render --library sonatina): maps each track's instrument to a
// preset of the library, loaded into FluidSynth next to the default soundfont under bank 100. A track keeps its
// default sound (and is reported) when the library has no such instrument or no preset that covers all its notes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Abend
{
    public static class Library
    {
        public const int Bank = 100;

        // Sonatina Symphonic Orchestra SF2 (MuseScore conversion): preset number, lowest and highest key it sounds well,
        // and a transposition (this conversion's flutes and piccolo sound an octave too high). Ranges leave out keys where
        // the conversion stretches one recording far from its pitch: Flute Solo keys 36-72 all play ONE low recording
        // (up to 4 octaves up: a fast artificial warble), Cello Section keys above 76 its C5 recording (Sf2Info.Stretch)
        class Pr { public int P, Lo, Hi, Tr; public string Name; public Pr(int p, string n, int lo, int hi, int tr) { P = p; Name = n; Lo = lo; Hi = hi; Tr = tr; } }
        static readonly Pr Piano = new Pr(0, "Steinway grand", 24, 108, 0), Harp = new Pr(1, "Concert harp", 36, 96, 0),
            Vln = new Pr(2, "1st violins", 55, 96, 0), Vln2 = new Pr(4, "2nd violins", 55, 96, 0), VlnPz = new Pr(3, "violins pizz.", 55, 96, 0), Vla = new Pr(6, "violas", 48, 84, 0), VlaPz = new Pr(7, "violas pizz.", 48, 84, 0),
            Vc = new Pr(8, "cellos", 36, 76, 0), VcPz = new Pr(9, "cellos pizz.", 36, 72, 0), Cb = new Pr(10, "basses", 24, 60, 0), CbPz = new Pr(11, "basses pizz.", 24, 60, 0),
            VlnSolo = new Pr(12, "solo violin", 55, 96, 0), VcSolo = new Pr(13, "solo cello", 36, 84, 0),
            Fl = new Pr(14, "solo flute", 73, 84, -12), Fls = new Pr(15, "flutes", 48, 81, -12), Ob = new Pr(16, "solo oboe", 58, 84, 0), Obs = new Pr(17, "oboes", 58, 88, 0),
            Picc = new Pr(18, "piccolo", 60, 91, -12), EH = new Pr(19, "english horn", 53, 77, 0), Cl = new Pr(20, "solo clarinet", 50, 86, 0), Cls = new Pr(21, "clarinets", 50, 86, 0),
            BCl = new Pr(22, "bass clarinet", 38, 74, 0), Bn = new Pr(23, "solo bassoon", 34, 74, 0), Bns = new Pr(24, "bassoons", 34, 76, 0), CBn = new Pr(25, "contrabassoon", 22, 58, 0),
            Tp = new Pr(27, "solo trumpet", 52, 88, 0), Tps = new Pr(28, "trumpets", 52, 88, 0), Hn = new Pr(29, "solo horn", 40, 76, 0), Hns = new Pr(30, "horns", 40, 72, 0),
            Tb = new Pr(31, "solo trombone", 40, 71, 0), Tbs = new Pr(32, "trombones", 40, 76, 0), BTb = new Pr(33, "bass trombone", 28, 67, 0), Tu = new Pr(34, "tuba", 28, 62, 0),
            Choir = new Pr(35, "mixed choir", 43, 84, 0), Glock = new Pr(39, "glockenspiel", 48, 84, 0), Timp = new Pr(40, "timpani", 24, 108, 0),
            Xyl = new Pr(42, "xylophone", 43, 82, 0), Chimes = new Pr(43, "chimes", 48, 84, 0);

        // General MIDI program -> candidates for a solo part and for a section part (first that covers every note wins)
        static Pr[][] Candidates(int program)
        {
            switch (program)
            {
                case 0: case 1: return Two(new[] { Piano });
                case 9: return Two(new[] { Glock });
                case 13: return Two(new[] { Xyl });
                case 14: return Two(new[] { Chimes });
                case 40: return new[] { new[] { VlnSolo, Vln }, new[] { Vln, VlnSolo } };
                case 41: return Two(new[] { Vla });
                case 42: return new[] { new[] { VcSolo, Vc }, new[] { Vc, VcSolo } };
                case 43: return Two(new[] { Cb, Vc });
                case 45: return Two(new[] { VlnPz, VlaPz, VcPz, CbPz });
                case 46: return Two(new[] { Harp });
                case 47: return Two(new[] { Timp });
                case 48: case 49: return Two(new[] { Vln, Vla, Vc, Cb });   // string ensemble: the highest section that covers the part
                case 52: case 53: case 54: return Two(new[] { Choir });
                case 56: return new[] { new[] { Tp, Tps }, new[] { Tps, Tp } };
                case 57: return new[] { new[] { Tb, Tbs, BTb }, new[] { Tbs, Tb, BTb } };
                case 58: return Two(new[] { Tu, BTb });
                case 60: return new[] { new[] { Hn, Hns }, new[] { Hns, Hn } };
                case 68: return new[] { new[] { Ob, Obs }, new[] { Obs, Ob } };
                case 69: return Two(new[] { EH });
                case 70: return new[] { new[] { Bn, Bns, CBn }, new[] { Bns, Bn, CBn } };
                case 71: return new[] { new[] { Cl, Cls, BCl }, new[] { Cls, Cl, BCl } };
                case 72: return Two(new[] { Picc, Fl });
                // the section patch first even for a solo flute: Flute Solo below written C#6 is one stretched recording (see above)
                case 73: return new[] { new[] { Fls, Fl, Picc }, new[] { Fls, Fl, Picc } };
                default: return null;
            }
        }
        static Pr[][] Two(Pr[] c) { return new[] { c, c }; }
        static Pr NamedSound(string instrument)
        {
            switch ((instrument ?? "").Trim().ToLowerInvariant())
            {
                case "bass clarinet": return BCl;
                case "contrabassoon": case "contra bassoon": return CBn;
                case "bass trombone": return BTb;
                default: return null;
            }
        }

        // "Violins", "Horns", "Cello section", "Celli" -> a section; "Cello", "Solo Cello", "Bass" -> a soloist
        public static bool IsSection(string name)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            if (n.Contains("solo")) return false;
            if (n.Contains("section") || n.Contains("celli") || n.Contains("tutti") || n.Contains("ens")) return true;
            var last = n.Split(' ').LastOrDefault(w => w.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(w, @"^([ivx]+|\d+)$")) ?? "";
            return last.EndsWith("s") && !last.EndsWith("ss");
        }
        // a part is a section when its name or its "instrument:" says so ("track Oboe | instrument: oboes");
        // "solo" in the name always means the soloist
        public static bool IsSection(Track t)
        {
            if ((t.Name ?? "").ToLowerInvariant().Contains("solo")) return false;
            return IsSection(t.Name) || (t.Instrument != null && IsSection(t.Instrument));
        }

        public static string Find(string name, string toolsDir)
        {
            if (!string.Equals(name, "sonatina", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (var r in AudioRender.SearchRoots(toolsDir))
            {
                var d = Path.Combine(r, "soundfonts", "Sonatina");
                if (Directory.Exists(d)) { var f = Directory.GetFiles(d, "*.sf2").OrderBy(x => x).FirstOrDefault(); if (f != null) return f; }
            }
            return null;
        }

        // the harpsichord built from VCSL's CC0 recordings (src/tools/WavToSf2.cs), loaded beside Sonatina under its own bank
        public const int HarpsichordBank = 101, HarpsichordLo = 29, HarpsichordHi = 93;   // F1-A6
        public static double HarpsichordVolume = 1.0;   // level match with the default harpsichord (CC7 scale)
        // the VCSL Flemish harpsichord (8' stop + key-up noises) when present, else the VCSL English one;
        // the English one stays reachable with --harpsichord VCSL-English-Harpsichord.sf2
        static readonly string[] HarpsichordFolders = { "VCSL-Flemish-Harpsichord", "VCSL-Harpsichord" };
        public static string FindHarpsichord(string toolsDir)
        {
            foreach (var r in AudioRender.SearchRoots(toolsDir))
                foreach (var name in HarpsichordFolders)
                {
                    var d = Path.Combine(r, "soundfonts", name);
                    if (Directory.Exists(d)) { var f = Directory.GetFiles(d, "*.sf2").OrderBy(x => x).FirstOrDefault(); if (f != null) return f; }
                }
            return null;
        }

        // whether a song has a part the harpsichord soundfont may play (its render key then names that soundfont, so changing
        // the harpsichord re-renders only the pieces that use it)
        public static bool PlaysHarpsichord(Song s)
        {
            return s != null && s.Tracks.Any(t => !t.IsDrums && t.Program == 6 && t.Notes.Count > 0);
        }

        // two extra instruments chosen by listening tests, both CC0, converted into one SF2 (soundfonts/Abend-Extras) and
        // loaded under their own bank only for a piece that uses them: preset 0 the VCSL pipe organ (instrument: pipe organ),
        // 1 its pedal stops (no longer used), 2 the FreePats Hohner button accordion (instrument: accordion).
        // "organ" and "church organ" keep the default sound, so existing organ pieces sound as before. "pipe organ pedal" plays
        // the default church organ too, at any range: in listening tests its depth beat the VCSL pedal's.
        public const int ExtrasBank = 103;
        static readonly string[] ExtrasNames = { "VCSL pipe organ", "VCSL pipe organ pedal", "Hohner accordion" };
        static readonly int[][] ExtrasRange = { new[] { 36, 97 }, new[] { 36, 67 }, new[] { 43, 84 } };   // C2-C#7, C2-G4, G2-C6
        public static double[] ExtrasVolume = { 1.135, 1.162, 1.0 };   // level match (CC7 scale) per preset: the rest of the gain the samples could take without clipping
        public static string FindExtras(string toolsDir)
        {
            foreach (var r in AudioRender.SearchRoots(toolsDir))
            {
                var d = Path.Combine(r, "soundfonts", "Abend-Extras");
                if (Directory.Exists(d)) { var f = Directory.GetFiles(d, "*.sf2").OrderBy(x => x).FirstOrDefault(); if (f != null) return f; }
            }
            return null;
        }
        public static int ExtrasPreset(Track t)
        {
            if (t.IsDrums) return -1;
            string i = (t.Instrument ?? "").Trim().ToLowerInvariant();
            if (i == "pipe organ") return 0;
            return t.Program == 21 ? 2 : -1;
        }
        // whether a song has a part the extras may play (its render key then names that soundfont, as for the harpsichord)
        public static bool PlaysExtras(Song s) { return s != null && s.Tracks.Any(t => t.Notes.Count > 0 && ExtrasPreset(t) >= 0); }

        // --harpsichord given as a bare file name ("VCSL-Flemish-Harpsichord.sf2"): look for it anywhere under soundfonts\
        public static string FindHarpsichord(string toolsDir, string name)
        {
            if (File.Exists(name)) return Path.GetFullPath(name);
            if (Path.GetFileName(name) != name || name.IndexOfAny(new[] { '*', '?' }) >= 0) return null;
            foreach (var r in AudioRender.SearchRoots(toolsDir))
            {
                var d = Path.Combine(r, "soundfonts");
                if (Directory.Exists(d)) { var f = Directory.GetFiles(d, name, SearchOption.AllDirectories).OrderBy(x => x).FirstOrDefault(); if (f != null) return f; }
            }
            return null;
        }

        // within the preset's range AND on a key that has a recording (Sonatina's timpani, for one, has holes: C#2-B2 and
        // C#4-B4 have none, so those notes would be silent); "cover" is the soundfont being mapped to, when known
        [ThreadStatic] static Sf2Info cover;
        static bool Fits(Pr p, NoteEv n) { return n.Pitch + p.Tr >= p.Lo && n.Pitch + p.Tr <= p.Hi && (cover == null || cover.Covers(0, p.P, n.Pitch + p.Tr, n.Vel)); }

        // "Violins II", "2nd violins", "Second Violins": Sonatina's separate 2nd-violin recordings, so the two violin
        // sections never play identical samples (in unison those would phase)
        static Pr Second(Track t, Pr p)
        {
            if (p != Vln) return p;
            string n = " " + (t.Name ?? "").ToLowerInvariant() + " ";
            return System.Text.RegularExpressions.Regex.IsMatch(n, @"\b(ii|2|2nd|second)\b") ? Vln2 : p;
        }
        static readonly Pr[] StringSections = { Vln, Vla, Vc, Cb };

        public static Song Apply(Song song, List<string> used, List<string> kept) { return Apply(song, used, kept, false); }

        // Logical MIDI channels: a synthesizer has 16, so the renderer plays channels 0-15 in one FluidSynth pass and
        // 16-31, 32-47 in further passes, then adds them up (the mix and reverb are linear, so this sounds the same as
        // one big synthesizer). Big scores can therefore use spare channels freely; 9, 25 and 41 are each pass's drums.
        public const int MaxChannels = 48;
        static IEnumerable<int> FreeChannels(Song s) { return Enumerable.Range(0, MaxChannels).Where(ch => ch % 16 != 9 && !s.Tracks.Any(x => x.Channel == ch)); }

        // ---------- notes longer than their recording ----------
        // Only Sonatina's string sections are looped; its winds, brass, choir and solo strings are single recordings that
        // stop when they end (2-9 s). A held note that outlasts its recording is continued: just before the recording
        // runs out, the same note starts again on a spare "held notes" channel with a slow attack (FadeSec), overlapping while
        // the old one releases, alternating two spare channels for very long notes. Where the recording is too short to
        // crossfade (a few faulty Sonatina keys), that note plays on the default soundfont's looped sound instead.
        // Overlap: how early the next piece starts; it must stay below half the usable length, or a piece on one channel would
        // start before the one two pieces back (same key, same channel) has ended, and that note-off would cut it
        const double EndMargin = 0.4, FadeSec = 0.6, Overlap = 1.2, MinOverlap = 0.6, MinCrossfade = EndMargin + 2 * MinOverlap + 0.1;
        public static bool IsHeldSound(int program) { return HeldSounds.Contains(program); }
        static readonly HashSet<int> HeldSounds = new HashSet<int> { 2, 4, 6, 8, 10, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35 };

        // a "held notes" channel only plays continuations, so it can have a slow attack (NRPN on generator 34, attack time):
        // each new piece fades in by itself. (A CC11 ramp would act on the whole channel and dip the other notes of a chord.)
        static void AddAttack(Track t, Sf2Info sounds, double seconds)
        {
            double now = Math.Max(0.001, sounds.LongestAttack(0, t.Program));
            int cents = (int)Math.Round(1200 * Math.Log(seconds / now, 2));
            if (cents <= 0) return;
            int v = Math.Min(16383, 8192 + cents / 2);   // 2 timecents per step
            t.Controls.InsertRange(0, new[] { new[] { 99, 120 }, new[] { 98, 34 }, new[] { 38, v & 127 }, new[] { 6, v >> 7 } }.Select(c => new CtlEv { Tick = 0, Cc = c[0], Val = c[1] }));
        }

        static void HoldLongNotes(Song s, Sf2Info sounds, Dictionary<Track, int> gm, List<string> used)
        {
            Func<int, double> sec = tk => s.TickToMs(tk) / 1000;
            Func<double, int> tick = sc => s.MsToTick(sc * 1000);
            Func<Track, NoteEv, double> room = (t, n) => sounds.Sustain(0, t.Program, n.Pitch, n.Vel);   // the file's own bank; FluidSynth adds the offset
            // first, notes whose recording is too short even to crossfade move to the same instrument's other Sonatina sound
            // (solo <-> section) when that one is long enough; the crossfades below then work on both
            var twin = new Dictionary<int, int> { { 16, 17 }, { 17, 16 }, { 20, 21 }, { 21, 20 }, { 23, 24 }, { 24, 23 }, { 27, 28 }, { 28, 27 }, { 29, 30 }, { 30, 29 }, { 31, 32 }, { 32, 31 }, { 12, 2 }, { 13, 8 }, { 14, 15 } };
            foreach (var t in s.Tracks.Where(t => t.Bank == Bank && twin.ContainsKey(t.Program)).ToList())
            {
                int alt = twin[t.Program];
                var move = t.Notes.Where(n => sec(n.Start + n.Dur) - sec(n.Start) > room(t, n) - 0.1 && room(t, n) < MinCrossfade && sounds.Sustain(0, alt, n.Pitch, n.Vel) >= MinCrossfade && sounds.Stretch(0, alt, n.Pitch, n.Vel) <= 4).ToList();
                int ch = FreeChannels(s).DefaultIfEmpty(-1).First();
                if (move.Count == 0 || ch < 0) continue;
                var other = t.Clone(); other.Name = t.Name + " (long notes, " + sounds.Names[alt].ToLowerInvariant() + ")"; other.Channel = ch; other.Program = alt; other.Notes = move;
                t.Notes = t.Notes.Except(move).ToList();
                if (gm.ContainsKey(t)) gm[other] = gm[t];
                s.Tracks.Insert(s.Tracks.IndexOf(t) + 1, other);
                used.Add(t.Name + ": " + move.Count + " long note(s) on " + sounds.Names[alt].ToLowerInvariant() + " (its own recording is too short there)");
            }
            var work = s.Tracks.Where(t => t.Bank == Bank && HeldSounds.Contains(t.Program))
                .Select(t => new { t, notes = t.Notes.Where(n => sec(n.Start + n.Dur) - sec(n.Start) > room(t, n) - 0.1).ToList() }).Where(w => w.notes.Count > 0).ToList();
            if (work.Count == 0) return;
            var free = new Queue<int>(FreeChannels(s));
            int each = free.Count >= 2 * work.Count ? 2 : 1;
            foreach (var w in work)
            {
                var t = w.t;
                var cont = new List<Track>();
                for (int i = 0; i < each && free.Count > 0; i++)
                {
                    var c = t.Clone(); c.Name = t.Name + " (held notes " + (i + 1) + ")"; c.Channel = free.Dequeue(); c.Notes = new List<NoteEv>();
                    c.Controls = t.Controls.Where(x => x.Cc == 11).Select(x => new CtlEv { Tick = x.Tick, Cc = 11, Val = x.Val }).ToList();
                    AddAttack(c, sounds, FadeSec);
                    s.Tracks.Insert(s.Tracks.IndexOf(t) + 1 + i, c); cont.Add(c);
                }
                Track fallback = null;
                int faded = 0, moved = 0, retrig = 0;
                Func<int, int> levelAt = tk => { var l = t.Controls.Where(c => c.Cc == 11 && c.Tick <= tk).OrderBy(c => c.Tick).LastOrDefault(); return l == null ? 127 : l.Val; };
                foreach (var n in w.notes)
                {
                    double A = room(t, n), start = sec(n.Start), end = sec(n.Start + n.Dur);
                    if (A < MinCrossfade)
                    {
                        // too short to crossfade: the default soundfont's looped sound plays this note, at the part's level
                        if (fallback == null && free.Count > 0)
                        {
                            fallback = t.Clone(); fallback.Name = t.Name + " (long notes, default sound)"; fallback.Channel = free.Dequeue(); fallback.Bank = 0; fallback.Program = gm.ContainsKey(t) ? gm[t] : t.Program;
                            fallback.Notes = new List<NoteEv>(); s.Tracks.Insert(s.Tracks.IndexOf(t) + 1, fallback);
                        }
                        if (fallback != null) { t.Notes.Remove(n); fallback.Notes.Add(n); moved++; continue; }
                    }
                    double segEnd = start + A - EndMargin;
                    if (A < MinCrossfade || cont.Count == 0)
                    {
                        // no spare channel: start the note again on its own channel as the recording ends (audible, but never silent)
                        var cur = n;
                        while (segEnd < end - 0.05)
                        {
                            var r = n.Clone(); r.Start = tick(segEnd); r.Dur = tick(end) - r.Start;
                            cur.Dur = r.Start - cur.Start; t.Notes.Add(r); cur = r; retrig++;
                            segEnd = sec(r.Start) + Math.Max(0.5, A - EndMargin);
                        }
                        continue;
                    }
                    n.Dur = tick(segEnd) - n.Start;
                    int k = 0;
                    while (segEnd < end - 0.05)
                    {
                        // the next piece starts FadeSec before the previous one is released; with one spare channel, every other piece goes back to the part's own channel
                        double ov = Math.Min(Overlap, (A - EndMargin) / 2 - 0.1);
                        double s2 = segEnd - ov, e2 = Math.Min(end, s2 + A - EndMargin);
                        var on = cont.Count >= 2 ? cont[k % 2] : (k % 2 == 0 ? cont[0] : t);
                        var piece = n.Clone(); piece.Start = tick(s2); piece.Dur = tick(e2) - piece.Start; on.Notes.Add(piece);
                        segEnd = e2; k++;
                    }
                    faded++;
                }
                foreach (var c in cont) Sym.Stable.Sort(c.Notes, (a, b) => a.Start.CompareTo(b.Start));
                Sym.Stable.Sort(t.Notes, (a, b) => a.Start.CompareTo(b.Start));
                s.Tracks.RemoveAll(x => cont.Contains(x) && x.Notes.Count == 0);
                used.Add(t.Name + ": " + string.Join(", ", new[] { faded > 0 ? faded + " long note(s) continued with crossfades" : null, moved > 0 ? moved + " on the default sound (recording too short)" : null,
                    retrig > 0 ? retrig + " restarted (no spare channel)" : null }.Where(x => x != null)));
            }
        }

        // returns a copy that plays from the library where it can; 'used' and 'kept' describe each track for the log
        // a --harpsichord soundfont's extras: preset 1 = a 4' stop (recorded at sounding pitch, so played an octave up),
        // presets 10 and 11 = key-up noises of presets 0 and 1 (the jack falling back), struck as each note ends
        static void HarpsichordExtras(Song s, Track t, Sf2Info h, List<string> used)
        {
            var extras = new List<string>();
            Func<Track, string, int, int, Track> add = (src, label, program, shift) =>
            {
                int ch = FreeChannels(s).DefaultIfEmpty(-1).First();
                if (ch < 0) return null;
                var x = src.Clone(); x.Name = t.Name + " (" + label + ")"; x.Channel = ch; x.Program = program;
                x.Notes = src.Notes.Where(n => h.Covers(0, program, n.Pitch + shift, n.Vel)).Select(n => { var m = n.Clone(); m.Pitch += shift; return m; }).ToList();
                s.Tracks.Insert(s.Tracks.IndexOf(t) + 1, x);
                return x;
            };
            Track four = null;
            if (h.Has(0, 1)) { four = add(t, "4' stop", 1, 12); if (four != null) { four.Volume = t.Volume * 70 / 100; extras.Add("4' stop"); } }
            foreach (var pair in new[] { Tuple.Create(t, 10), Tuple.Create(four, 11) })
            {
                if (pair.Item1 == null || !h.Has(0, pair.Item2)) continue;
                var src = pair.Item1.Clone();
                foreach (var n in src.Notes) { int end = n.Start + n.Dur; n.Start = end; n.Dur = Math.Max(1, s.MsToTick(s.TickToMs(end) + 600) - end); n.Pitch -= pair.Item1 == four ? 12 : 0; }
                var r = add(src, pair.Item2 == 10 ? "key-up" : "4' key-up", pair.Item2, pair.Item1 == four ? 12 : 0);
                if (r != null && !extras.Contains("key-up noises")) extras.Add("key-up noises");
            }
            if (extras.Count > 0) used[used.Count - 1] += ", with " + string.Join(" and ", extras);
        }

        public static Song Apply(Song song, List<string> used, List<string> kept, bool harpsichord, Sf2Info sounds = null, Sf2Info hpsi = null, string hpsiName = null, bool percussion = false, Sf2Info extras = null)
        {
            var s = song.Clone();
            cover = sounds;
            var gm = s.Tracks.ToDictionary(x => x, x => x.Program);   // General MIDI program before mapping
            foreach (var t in s.Tracks.ToList())
            {
                if (t.IsDrums || t.Notes.Count == 0) continue;
                if (percussion && t.Program == 47) continue;   // timpani: Percussion.Apply below
                if (harpsichord && t.Program == 6 && t.Notes.All(n => hpsi != null ? hpsi.Covers(0, 0, n.Pitch, n.Vel) : n.Pitch >= HarpsichordLo && n.Pitch <= HarpsichordHi))
                {
                    t.Bank = HarpsichordBank; t.Program = 0;
                    t.Volume = Math.Min(127, (int)Math.Round(t.Volume * HarpsichordVolume));
                    used.Add(t.Name + (hpsi == null ? " = VCSL harpsichord" : " = " + (hpsiName ?? "harpsichord from --harpsichord")));
                    if (hpsi != null) HarpsichordExtras(s, t, hpsi, used);
                    continue;
                }
                int ep = extras != null ? ExtrasPreset(t) : -1;
                if (ep >= 0 && t.Notes.All(n => n.Pitch >= ExtrasRange[ep][0] && n.Pitch <= ExtrasRange[ep][1] && extras.Covers(0, ep, n.Pitch, n.Vel)))
                {
                    t.Bank = ExtrasBank; t.Program = ep;
                    t.Volume = Math.Min(127, (int)Math.Round(t.Volume * ExtrasVolume[ep]));
                    used.Add(t.Name + " = " + ExtrasNames[ep]);
                    continue;
                }
                var c = Candidates(t.Program);
                Pr pick = null;
                if (c != null)
                    foreach (var p in c[IsSection(t) ? 1 : 0])
                        if (t.Notes.All(n => Fits(p, n))) { pick = p; break; }
                // a part asking for one of the family's own recordings by name gets it (instrument: bass clarinet, contrabassoon,
                // bass trombone; their General MIDI program is the clarinet's, bassoon's or trombone's)
                Pr byName = NamedSound(t.Instrument);
                if (byName != null && t.Notes.All(n => Fits(byName, n))) pick = byName;

                // a string-ensemble part ("Strings", "Violins II", a pad) is shared the way an orchestra's strings would share it:
                // every note goes to the highest section that can play it (violins down to G3, then violas, cellos, basses),
                // each section on its own channel, so a strings melody stays in the violins and only its low notes go lower.
                // A part named after one section (violas, cellos, basses) keeps that section when it can play everything.
                if (t.Program == 48 || t.Program == 49)
                {
                    string nm = (t.Name ?? "").ToLowerInvariant();
                    Pr named = nm.Contains("viola") ? Vla : (nm.Contains("cello") || nm.Contains("celli")) ? Vc : nm.Contains("bass") ? Cb : null;
                    if (named != null && t.Notes.All(n => Fits(named, n))) pick = named;
                    else
                    {
                        var groups = t.Notes.GroupBy(n => StringSections.FirstOrDefault(p => Fits(p, n))).OrderBy(g => Array.IndexOf(StringSections, g.Key)).ToList();
                        if (groups.All(g => g.Key != null) && FreeChannels(s).Count() >= groups.Count - 1)
                        {
                            var parts = new List<string>();
                            for (int i = 0; i < groups.Count; i++)
                            {
                                var tr = t;
                                if (i > 0) { tr = t.Clone(); tr.Name = t.Name + " (" + groups[i].Key.Name + ")"; tr.Channel = FreeChannels(s).First(); s.Tracks.Insert(s.Tracks.IndexOf(t) + i, tr); }
                                tr.Notes = groups[i].ToList(); tr.Bank = Bank; tr.Program = Second(t, groups[i].Key).P;
                                parts.Add(Second(t, groups[i].Key).Name + (groups.Count > 1 ? " " + tr.Notes.Count : ""));
                            }
                            used.Add(t.Name + " = " + (groups.Count == 1 ? parts[0] : "strings by register: " + string.Join(", ", parts) + " notes"));
                            continue;
                        }
                    }
                }

                // a few notes just beyond the preset's range (e.g. a harp's low A-flat): play them from the edge key, bent to pitch on a
                // spare channel, so the whole part keeps the library's sound (samples stretched at most 5 semitones)
                if (pick == null && c != null)
                {
                    int free = FreeChannels(s).DefaultIfEmpty(-1).First();
                    foreach (var p in c[IsSection(t) ? 1 : 0])
                    {
                        var outl = t.Notes.Where(n => !Fits(p, n)).ToList();
                        if (free < 0 || outl.Count > t.Notes.Count / 10 || outl.Any(n => n.Pitch + p.Tr < p.Lo - 5 || n.Pitch + p.Tr > p.Hi + 5 || (n.Pitch + p.Tr >= p.Lo && n.Pitch + p.Tr <= p.Hi))) continue;   // only notes beyond the ends, not in a hole
                        if (outl.Any(a => outl.Any(b => b != a && b.Start < a.Start + a.Dur && a.Start < b.Start + b.Dur))) continue;   // one bend at a time
                        var st = t.Clone(); st.Name = t.Name + " (stretched notes)"; st.Channel = free; st.Bank = Bank; st.Program = p.P;
                        st.Notes = new List<NoteEv>();
                        foreach (var cc in new[] { new[] { 101, 0 }, new[] { 100, 0 }, new[] { 38, 0 }, new[] { 6, 12 } }) st.Controls.Add(new CtlEv { Tick = 0, Cc = cc[0], Val = cc[1] });   // bend range 12
                        foreach (var n in outl)
                        {
                            int target = n.Pitch + p.Tr, key = target < p.Lo ? p.Lo : p.Hi;
                            st.Controls.Add(new CtlEv { Tick = n.Start, Cc = CtlEv.PitchBend, Val = Math.Max(0, Math.Min(16383, 8192 + (int)Math.Round((target - key) * 8192 / 12.0))) });
                            var k = n.Clone(); k.Pitch = key; st.Notes.Add(k);
                        }
                        t.Notes = t.Notes.Where(n => Fits(p, n)).ToList();
                        t.Bank = Bank; t.Program = p.P;
                        if (p.Tr != 0) foreach (var n in t.Notes) n.Pitch += p.Tr;
                        s.Tracks.Insert(s.Tracks.IndexOf(t) + 1, st);
                        used.Add(t.Name + " = " + p.Name + ", with " + outl.Count + " note(s) just outside its range stretched");
                        pick = p;
                        break;
                    }
                    if (pick != null) continue;
                }
                if (pick == null)
                {
                    // the default harpsichord is bright and buzzy next to real samples: gentle low-pass (SF2 NRPN, filter
                    // cutoff -1800 cents), a little quieter, a little more reverb
                    if (t.Program == 6)
                    {
                        int v = 8192 - 1800 / 2;   // 2 cents per step
                        foreach (var cc in new[] { new[] { 99, 120 }, new[] { 98, 8 }, new[] { 38, v & 127 }, new[] { 6, v >> 7 }, new[] { 91, 64 } }) t.Controls.Add(new CtlEv { Tick = 0, Cc = cc[0], Val = cc[1] });
                        t.Volume = t.Volume * 85 / 100;
                    }
                    kept.Add(t.Name + " (" + GM.Names[t.Program].ToLower() + (c == null ? ", not in the library" : ", notes outside the library's range") + (t.Program == 6 ? "; softened: low-pass, -15% volume, more reverb" : "") + ")");
                    continue;
                }
                t.Bank = Bank; t.Program = Second(t, pick).P;
                if (pick.Tr != 0) foreach (var n in t.Notes) n.Pitch += pick.Tr;
                used.Add(t.Name + " = " + Second(t, pick).Name);

                // solo violin and cello samples carry a wide recorded vibrato (up to 40-46 cents on high notes); the section
                // patch playing the same line about 9 dB underneath narrows it by a quarter to a third without hiding the soloist
                var under = pick == VlnSolo ? Vln : pick == VcSolo ? Vc : null;
                int spare = FreeChannels(s).DefaultIfEmpty(-1).First();
                if (under != null && spare >= 0 && t.Notes.All(n => Fits(under, n)))
                {
                    var dbl = t.Clone(); dbl.Name = t.Name + " (section underneath)"; dbl.Channel = spare; dbl.Program = under.P;
                    dbl.Volume = t.Volume * 60 / 100;
                    // where another part already plays the same note, leave it out: on the same section sound the identical samples
                    // a few milliseconds apart would phase (a flanging wobble)
                    dbl.Notes = dbl.Notes.Where(n => !s.Tracks.Any(o => o != t && !o.IsDrums && o.Notes.Any(m => m.Pitch == n.Pitch && m.Start < n.Start + n.Dur && n.Start < m.Start + m.Dur))).ToList();
                    s.Tracks.Insert(s.Tracks.IndexOf(t) + 1, dbl);
                    used[used.Count - 1] += ", doubled softly by " + under.Name;
                }
            }
            if (percussion) Percussion.Apply(s, used, kept, sounds, () => FreeChannels(s).DefaultIfEmpty(-1).First());
            if (sounds != null) HoldLongNotes(s, sounds, gm, used);
            return s;
        }
    }
}
