// Score scripts (.abscore): a whole piece as one text file — header, then track and chord blocks.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Abend
{
    public class ScoreResult
    {
        public Song Song;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public string RepairedText;   // set when lost line breaks were restored
        public string Composer;       // from the composer: header
        public bool TempoLineAfterRamp;   // a tempo line where a rit/accel ends changed the speed (renders key on it)
        public string Cover;          // from the cover: header (image path, relative to the script)
        public string Album;          // from the album: header (movements sharing it get one loudness gain in audio renders)
        public double? Gap;           // from the gap: header (seconds of silence after the track in audio renders; null = the --gap setting)
        public string Percussion;     // from the percussion: header (sonatina or generaluser; null = the --percussion setting)
        public bool Ok { get { return Errors.Count == 0; } }
        public string Summary()
        {
            if (Song == null) return "";
            int end = Song.EndTick();
            double sec = Song.TickToMs(end) / 1000.0;
            return string.Format("\"{0}\": {1} tracks, {2} notes, {3} bars, {4}:{5:00}", Song.Title, Song.Tracks.Count, Song.Tracks.Sum(t => t.Notes.Count),
                end == 0 ? 0 : (end - 1) / Song.BarTicks + 1, (int)sec / 60, (int)sec % 60);
        }
    }

    public static class ScoreScript
    {
        // instrument name -> GM program (-1 = drums)
        static readonly Dictionary<string, int> Instruments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            {"piano",0},{"grand piano",0},{"electric piano",4},{"harpsichord",6},{"celesta",8},{"glockenspiel",9},{"music box",10},{"vibraphone",11},{"marimba",12},{"xylophone",13},{"bells",14},{"tubular bells",14},{"chimes",14},
            {"bass clarinet",71},{"contrabassoon",70},{"contra bassoon",70},{"bass trombone",57},   // the library plays its own recordings of these (Library.NamedSound)
            {"pipe organ",19},{"pipe organ pedal",19},   // the library's real pipe organ (Library.ExtrasPreset); "organ" keeps the default sound
            {"organ",19},{"church organ",19},{"accordion",21},{"harmonica",22},{"guitar",24},{"nylon guitar",24},{"acoustic guitar",25},{"steel guitar",25},{"electric guitar",27},{"jazz guitar",26},{"distortion guitar",30},
            {"acoustic bass",32},{"bass guitar",33},{"electric bass",33},{"fretless bass",35},{"synth bass",38},
            {"violin",40},{"violins",48},{"violins i",48},{"violins ii",48},{"first violins",48},{"second violins",48},{"viola",41},{"violas",48},{"cello",42},{"cellos",42},
            {"contrabass",43},{"double bass",43},{"basses",43},{"tremolo strings",44},{"pizzicato",45},{"pizzicato strings",45},{"harp",46},{"timpani",47},
            {"strings",48},{"string ensemble",48},{"slow strings",49},{"synth strings",50},{"choir",52},{"voices",52},{"aahs",52},{"oohs",53},
            {"trumpet",56},{"trumpets",56},{"trombone",57},{"trombones",57},{"tuba",58},{"muted trumpet",59},{"horn",60},{"horns",60},{"french horn",60},{"brass",61},{"brass section",61},
            {"soprano sax",64},{"alto sax",65},{"saxophone",65},{"tenor sax",66},{"baritone sax",67},{"oboe",68},{"oboes",68},{"english horn",69},{"cor anglais",69},{"bassoon",70},{"bassoons",70},
            {"clarinet",71},{"clarinets",71},{"piccolo",72},{"flute",73},{"flutes",73},{"recorder",74},{"pan flute",75},{"whistle",78},{"ocarina",79},
            {"square lead",80},{"saw lead",81},{"synth lead",81},{"warm pad",89},{"pad",89},{"choir pad",91},{"sitar",104},{"banjo",105},{"kalimba",108},{"bagpipe",109},{"fiddle",110},
            {"steel drums",114},{"taiko",116},{"drums",-1},{"drum kit",-1},{"percussion",-1}
        };

        // comfortable sounding ranges for warnings
        static readonly Dictionary<int, int[]> Ranges = new Dictionary<int, int[]>
        {
            {40,new[]{55,103}},{41,new[]{48,91}},{42,new[]{36,84}},{43,new[]{28,67}},{44,new[]{36,100}},{45,new[]{28,96}},{46,new[]{23,104}},{47,new[]{38,60}},
            {48,new[]{28,103}},{56,new[]{54,84}},{57,new[]{40,77}},{58,new[]{26,65}},{60,new[]{34,77}},{68,new[]{58,91}},{69,new[]{52,81}},{70,new[]{34,76}},
            {71,new[]{50,94}},{72,new[]{74,108}},{73,new[]{60,98}}
        };

        // instruments that share a General MIDI program with their family but have their own range
        static readonly Dictionary<string, int[]> NamedRanges = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            {"bass clarinet",new[]{38,77}},{"contrabassoon",new[]{22,58}},{"contra bassoon",new[]{22,58}},{"bass trombone",new[]{28,67}},
            {"pipe organ",new[]{36,97}},{"pipe organ pedal",new[]{24,67}},{"accordion",new[]{43,84}}
        };

        public static int ParseInstrument(string s, out bool ok)
        {
            ok = true;
            s = (s ?? "").Trim();
            int n;
            if (int.TryParse(s, out n) && n >= 1 && n <= 128) return n - 1;
            if (s.StartsWith("gm", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Substring(2).Trim(' ', ':'), out n) && n >= 1 && n <= 128) return n - 1;
            int p;
            if (Instruments.TryGetValue(s, out p)) return p;
            for (int i = 0; i < 128; i++) if (string.Equals(GM.Names[i], s, StringComparison.OrdinalIgnoreCase)) return i;
            ok = false; return 0;
        }

        class Block { public string Kind, Name; public Dictionary<string, string> Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); public int Line; public List<KeyValuePair<int, string>> Body = new List<KeyValuePair<int, string>>(); }

        public static ScoreResult Parse(string text)
        {
            var res = new ScoreResult();
            var song = new Song { Title = "Untitled" };
            song.Tracks.Clear();
            var blocks = new List<Block>();
            var tempoOps = new List<string[]>(); // kind, bar, beat, bar2, beat2, value, line
            Block cur = null;
            string keyName = null;
            int bare = 0, bareLine = 0;
            text = RepairCollapsed(text.Replace("\r\n", "\n").Replace('\r', '\n'), res);
            var lines = text.Split('\n');
            var head = new Regex(@"^(track|chords)\s+(.+)$", RegexOptions.IgnoreCase);
            var tempoAt = new Regex(@"^(tempo|rit|ritardando|rall|accel|accelerando|fermata)\s*@\s*(\d+)(?:\.(\d+))?(?:\s*-\s*@?\s*(\d+)(?:\.(\d+))?)?\s*(?::\s*([\d.]+))?\s*$", RegexOptions.IgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i], line = raw.Trim();
                int ln = i + 1;
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                if (line.StartsWith("```")) continue; // tolerate Markdown fences pasted from a chat
                var tm = tempoAt.Match(line);
                if (tm.Success)
                {
                    tempoOps.Add(new[] { tm.Groups[1].Value.ToLower(), tm.Groups[2].Value, tm.Groups[3].Value, tm.Groups[4].Value, tm.Groups[5].Value, tm.Groups[6].Value, ln.ToString() });
                    continue;
                }
                if (Regex.IsMatch(line, @"^(tempo|rit|ritardando|rall|accel|accelerando|fermata)\s*@", RegexOptions.IgnoreCase))
                {
                    res.Errors.Add("line " + ln + ": could not read \"" + line + "\". Examples: tempo @17: 108   rit @21-24: 60   fermata @24.3: 2.5");
                    continue;
                }
                var hm = head.Match(line);
                if (hm.Success)
                {
                    cur = new Block { Kind = hm.Groups[1].Value.ToLower(), Line = ln };
                    var parts = hm.Groups[2].Value.Split('|');
                    cur.Name = parts[0].Trim();
                    // fields are "key: value"; anything after them on the same line is the start of the music
                    string bodyStart = null;
                    for (int k = 1; k < parts.Length; k++)
                    {
                        string p = parts[k];
                        if (bodyStart != null) { bodyStart += "|" + p; continue; }
                        int c = p.IndexOf(':');
                        string key = c > 0 ? p.Substring(0, c).Trim().ToLower() : "";
                        if (c < 0 || !FieldNames.Contains(key))
                        {
                            if (c > 0 && Regex.IsMatch(key, @"^[a-z ]+$")) { res.Warnings.Add("line " + ln + ": unknown field \"" + key + "\" ignored."); continue; }
                            bodyStart = p; continue;
                        }
                        string val = p.Substring(c + 1);
                        int cut = val.IndexOfAny(new[] { '\\', '@' });
                        if (cut >= 0) { bodyStart = val.Substring(cut); val = val.Substring(0, cut); }
                        cur.Fields[key] = val.Trim();
                    }
                    if (bodyStart != null && bodyStart.Trim().Length > 0) cur.Body.Add(new KeyValuePair<int, string>(ln, BareHairpins(bodyStart, ln, ref bare, ref bareLine)));
                    blocks.Add(cur);
                    continue;
                }
                if (cur == null)
                {
                    int c = line.IndexOf(':');
                    if (c < 0) { res.Errors.Add("line " + ln + ": expected a header line such as \"title: My Song\" or a \"track\" block."); continue; }
                    string key = line.Substring(0, c).Trim().ToLower(), val = line.Substring(c + 1).Trim();
                    try
                    {
                        switch (key)
                        {
                            case "title": song.Title = val; break;
                            case "tempo": song.BaseBpm = double.Parse(val.Split(' ')[0], CultureInfo.InvariantCulture); break;
                            case "meter": case "time": case "time signature":
                                var mp = val.Split('/'); song.Num = int.Parse(mp[0].Trim()); song.Den = int.Parse(mp[1].Trim());
                                if (song.Den != 2 && song.Den != 4 && song.Den != 8 && song.Den != 16) throw new Exception("the beat unit must be 2, 4, 8 or 16");
                                break;
                            case "composer": res.Composer = val; break;
                            case "cover": res.Cover = val; break;
                            case "tuning":
                                song.TuningHz = double.Parse(Regex.Replace(val, @"(?i)\s*hz\s*$", ""), CultureInfo.InvariantCulture);
                                if (song.TuningHz < 415 || song.TuningHz > 466) throw new Exception("tuning is the frequency of A4 in Hz, between 415 and 466 (440 is standard)");
                                break;
                            case "album": res.Album = val.Length > 0 ? val : null; break;
                            case "gap":
                                res.Gap = double.Parse(Regex.Replace(val, @"(?i)\s*(s|sec|seconds?)\s*$", ""), CultureInfo.InvariantCulture);
                                if (res.Gap < 0 || res.Gap > 10) throw new Exception("gap is the silence after the piece in seconds, between 0 and 10");
                                break;
                            case "percussion":
                                res.Percussion = val.ToLowerInvariant();
                                if (res.Percussion != "sonatina" && res.Percussion != "generaluser") throw new Exception("percussion is sonatina (orchestral drums and timpani, the default) or generaluser (the default soundfont's kit)");
                                break;
                            case "key":
                                keyName = val;
                                if (KeyTonic(val) < 0) res.Warnings.Add("line " + ln + ": key \"" + val + "\" is not a key name I know (it is only used to check that the piece ends on its home chord). Write it like  key: D minor,  key: Bb major  or  key: F#m.");
                                break;
                            case "style": case "description": break;
                            default: res.Warnings.Add("line " + ln + ": unknown header \"" + key + "\" ignored."); break;
                        }
                    }
                    catch (Exception ex) { res.Errors.Add("line " + ln + ": could not read \"" + line + "\" (" + ex.Message + ")."); }
                    continue;
                }
                cur.Body.Add(new KeyValuePair<int, string>(ln, BareHairpins(raw, ln, ref bare, ref bareLine)));
            }
            if (bare > 0) res.Warnings.Add(bare + " bare < or > read as \\< or \\> (a crescendo or diminuendo), first on line " + bareLine + ". Write \\< and \\> for hairpins.");
            if (blocks.Count == 0) res.Errors.Add("No \"track\" blocks were found. A score needs at least one line like: track Melody | instrument: flute");

            ApplyTempoOps(song, tempoOps, res);

            var byName = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var melodic = new HashSet<NoteEv>();   // notes written in "track" blocks (not chords or patterns), for the scale-run check
            foreach (var b in blocks)
            {
                string where = "line " + b.Line + " (" + b.Kind + " " + b.Name + ")";
                Track tr;
                if (!byName.TryGetValue(b.Name, out tr))
                {
                    tr = new Track { Name = b.Name };
                    string inst; bool ok = true; int prog;
                    if (b.Fields.TryGetValue("instrument", out inst)) { prog = ParseInstrument(inst, out ok); tr.Instrument = inst; }
                    else prog = ParseInstrument(b.Name, out ok);
                    if (!ok)
                    {
                        if (b.Fields.ContainsKey("instrument")) res.Warnings.Add(where + ": unknown instrument \"" + inst + "\", using piano. Use a name like violin, flute, horn, or a General MIDI number 1-128.");
                        prog = 0;
                    }
                    if (prog < 0) { tr.Channel = 9; tr.Program = 0; }
                    else { tr.Program = prog; tr.Channel = song.NextFreeChannel(); }
                    string v;
                    if (b.Fields.TryGetValue("volume", out v)) { int vv; if (int.TryParse(v, out vv)) tr.Volume = Math.Max(0, Math.Min(127, vv)); }
                    if (b.Fields.TryGetValue("pan", out v)) { int pv; if (int.TryParse(v, out pv)) tr.Pan = Math.Max(0, Math.Min(127, 64 + pv * 63 / 100)); }
                    byName[b.Name] = tr; song.Tracks.Add(tr);
                }
                int had = tr.Notes.Count, hadEnd = had == 0 ? 0 : tr.Notes.Max(n => n.Start + n.Dur);
                try
                {
                    if (b.Kind == "track") RunNotation(song, tr, b, res);
                    else RunChords(song, tr, b, res);
                }
                catch (Exception ex) { res.Errors.Add(where + ": " + Help.Friendly(ex.Message)); }
                if (b.Kind == "track") foreach (var n in tr.Notes.Skip(had)) melodic.Add(n);
                // a second block of the same kind without @ starts again at bar 1, on top of the first one
                string seenKey = b.Kind + "\n" + b.Name;
                if (seen.Contains(seenKey) && had > 0 && tr.Notes.Count > had && !Chunks(b)[0].Item1.HasValue && tr.Notes.Skip(had).Min(n => n.Start) < hadEnd)
                {
                    int from = (hadEnd - 1) / song.BarTicks + 2;
                    res.Warnings.Add(where + ": this block has no @bar, so it starts at bar 1 and plays on top of the earlier " + b.Name + " block (which runs to bar " + (from - 1) +
                        "). If it should follow on, start it with @" + from + ".");
                }
                seen.Add(seenKey);
            }
            if (song.Tracks.Count(t => !t.IsDrums) > 15) res.Warnings.Add("More than 15 instrument tracks: in the .mid file some share a MIDI channel and sound alike (abendc render gives each its own).");

            // range warnings
            foreach (var t in song.Tracks)
            {
                int[] r;
                if (t.IsDrums || !(NamedRanges.TryGetValue((t.Instrument ?? "").Trim(), out r) || Ranges.TryGetValue(t.Program, out r))) continue;
                var bad = t.Notes.Where(n => n.Pitch < r[0] || n.Pitch > r[1]).ToList();
                if (bad.Count > 0)
                    res.Warnings.Add(t.Name + ": " + bad.Count + " note(s) outside the instrument's comfortable range " + Spell(r[0]) + "-" + Spell(r[1]) + ", first " + Spell(bad[0].Pitch) + " in bar " + (bad[0].Start / song.BarTicks + 1) + ".");
            }
            // a track written as notes with no dynamic at all plays at the default level
            foreach (var t in song.Tracks)
                if (!t.IsDrums && (t.Dyn == null || t.Dyn.Count == 0) && t.Notes.Any(n => melodic.Contains(n)))
                    res.Warnings.Add(t.Name + ": the track has no dynamic, so it plays at a default level. Start it with one, e.g. \\mf c5/4 ...");
            foreach (var t in song.Tracks) Sym.Stable.Sort(t.Notes, (a, b) => a.Start.CompareTo(b.Start));
            if (res.Ok) res.Warnings.AddRange(MusicChecks(song, keyName));
            // tunes made mostly of scales: only notes written in "track" blocks (chord and pattern blocks, bass lines included, are left out)
            if (res.Ok && Lint) res.Warnings.AddRange(ScaleRuns(song, song.Tracks.Where(t => !t.IsDrums).ToList(), melodic));
            res.Song = song;
            return res;
        }

        // style hints (--lint, off by default): currently one, a tune made mostly of scale runs
        public static bool Lint = false;

        // ---------- scale runs: a style hint for tunes that are mostly scales ----------
        // A run is 5 or more notes in a row, each a half or whole step on from the last, going the same way.
        // The line is the top note at each onset (ties merged, repeats
        // expanded); a rest breaks a run; a turning note counts only in the run it ends; the share is over note onsets. A part warns when at least 25% of its notes (of 40 or
        // more) sit in runs: ordinary passagework stays under that (in tested scores: median 0%, 90% under 12%);
        // a tune of about 40% or more reads as scales rather than a melody.
        public const int ScaleRunNotes = 5, ScaleMinNotes = 40;
        public const double ScaleWarnShare = 0.25;
        public static List<string> ScaleRuns(Song song, List<Track> parts, HashSet<NoteEv> melodic)
        {
            var w = new List<string>();
            foreach (var t in parts)
            {
                var groups = t.Notes.Where(n => melodic.Contains(n)).GroupBy(n => n.Start).OrderBy(g => g.Key).ToList();
                var line = groups.Select(g => g.OrderByDescending(n => n.Pitch).First()).ToList();
                // where each onset's written notes end (staccato and the normal articulation shorten the sounding note)
                var ends = groups.Select(g => g.Max(n => n.Start + (n.Written > 0 ? n.Written : n.Dur))).ToList();
                if (line.Count < ScaleMinNotes) continue;
                var runs = new List<int[]>();   // first index, last index
                int from = 0, dir = 0;
                for (int i = 1; i <= line.Count; i++)
                {
                    int d = i < line.Count ? line[i].Pitch - line[i - 1].Pitch : 0;
                    int sgn = Math.Sign(d);
                    bool step = (Math.Abs(d) == 1 || Math.Abs(d) == 2) && line[i].Start <= ends[i - 1] + Song.PPQ / 32;   // a rest breaks a run
                    if (step && (dir == 0 || sgn == dir)) { dir = sgn; continue; }
                    if (i - from >= ScaleRunNotes) runs.Add(new[] { from, i - 1 });
                    // the next run starts after this note: a turning note counts only in the run it ends
                    from = i; dir = 0;
                }
                if (runs.Count == 0) continue;
                var inRun = new HashSet<int>();
                foreach (var r in runs) for (int i = r[0]; i <= r[1]; i++) inRun.Add(i);
                double share = inRun.Count / (double)line.Count;
                if (share < ScaleWarnShare) continue;
                var eg = runs.OrderByDescending(r => r[1] - r[0]).Take(3).OrderBy(r => r[0])
                    .Select(r => { int a = line[r[0]].Start / song.BarTicks + 1, b = line[r[1]].Start / song.BarTicks + 1; return (a == b ? "bar " + a : "bars " + a + "-" + b) + " (" + (r[1] - r[0] + 1) + " notes)"; });
                w.Add(t.Name + ": " + Math.Round(share * 100) + "% of its notes are in scale runs (5 or more notes stepping the same way), e.g. " + string.Join(", ", eg) +
                    ". A tune made mostly of scales (about 40% or more) reads as an exercise rather than a melody; runs as decoration or a run-up are fine.");
            }
            return w;
        }

        // ---------- musical-sense warnings: the bars add up, but do the parts fit together? ----------
        public static List<string> MusicChecks(Song song, string keyName)
        {
            var w = new List<string>();
            int bt = song.BarTicks, beatT = Song.PPQ * 4 / song.Den;
            Func<int, int> barOf = tick => tick / bt + 1;
            var tracks = song.Tracks.Where(t => t.Notes.Count > 0).ToList();
            if (tracks.Count == 0) return w;

            // 1. tracks that end at different bars; the bar most tracks end on counts as the end of the piece
            var endBar = new Dictionary<Track, int>();
            foreach (var t in tracks) endBar[t] = barOf(t.Notes.Max(n => n.Start + n.Dur) - 1);
            if (tracks.Count >= 2)
            {
                int common = endBar.Values.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
                var late = tracks.Where(t => endBar[t] > common).ToList();
                var together = tracks.Where(t => endBar[t] == common).Select(t => t.Name).ToList();
                if (late.Count > 0)
                    w.Add(Names(late.Select(t => t.Name + (late.Select(x => endBar[x]).Distinct().Count() > 1 ? " (bar " + endBar[t] + ")" : "")).ToList()) +
                        (late.Count == 1 ? " runs" : " run") + " to bar " + late.Max(t => endBar[t]) + " but " + Names(together) + (together.Count == 1 ? " ends" : " end") + " at bar " + common +
                        ", so " + (late.Count == 1 ? "it plays" : "they play") + " alone at the end. If that is not intended, check the bar count of " + (late.Count == 1 ? "that track" : "those tracks") + " or make the parts end together.");
                // parts that already rest for two bars or more somewhere are orchestration, not a miscount
                Func<Track, bool> dropsOut = t =>
                {
                    int prev = 0;
                    foreach (var n in t.Notes) { if (n.Start - prev >= 2 * bt) return true; prev = Math.Max(prev, n.Start + n.Dur); }
                    return false;
                };
                foreach (var t in tracks.Where(t => endBar[t] <= common - 2 && !dropsOut(t)))
                    w.Add(t.Name + " stops at bar " + endBar[t] + ", " + (common - endBar[t]) + " bars before the other parts end (bar " + common + "). If it should play to the end, check its bar count.");
            }

            // 2. melody notes on strong beats that clash by a semitone with a held note in another part
            var pitched = tracks.Where(t => !t.IsDrums).ToList();
            foreach (var a in pitched)
            {
                int count = 0; string first = null;
                foreach (var n in a.Notes)
                {
                    int off = n.Start % bt;
                    if (!(off == 0 || (song.Num >= 4 && song.Num % 2 == 0 && off == bt / 2))) continue;
                    if (n.Dur < Song.PPQ / 2) continue;
                    // only melody notes: nothing else in this track sounds when it starts
                    if (a.Notes.Any(o => o != n && o.Start <= n.Start && o.Start + o.Dur > n.Start)) continue;
                    int pc = n.Pitch % 12;
                    bool chordTone = false; NoteEv held = null; Track heldIn = null;
                    foreach (var b in pitched)
                    {
                        if (b == a) continue;
                        foreach (var o in b.Notes)
                        {
                            if (o.Start > n.Start || o.Start + o.Dur <= n.Start) continue;
                            if (o.Pitch % 12 == pc) chordTone = true;
                            int ic = ((o.Pitch - n.Pitch) % 12 + 12) % 12;
                            if ((ic == 1 || ic == 11) && o.Dur >= Song.PPQ && o.Start + o.Dur - n.Start >= Song.PPQ / 2 && held == null) { held = o; heldIn = b; }
                        }
                    }
                    if (chordTone || held == null) continue;
                    count++;
                    if (first == null) first = Spell(n.Pitch) + " against " + PcName(held.Pitch) + " in " + heldIn.Name + " in bar " + barOf(n.Start) + (off == 0 ? "" : " beat " + (off / beatT + 1));
                }
                if (count > 0)
                    w.Add(a.Name + ": " + count + " note(s) on strong beats clash by a semitone with a held note in another part, e.g. " + first + ". Melody notes on strong beats usually belong to the chord.");
            }

            // 3. the ending: the lowest note of the final chord should be the home note
            if (pitched.Count > 0 && barOf(song.EndTick() - 1) >= 4)
            {
                var all = pitched.SelectMany(t => t.Notes).ToList();
                int lastOn = all.Max(n => n.Start);
                int endBass = all.Where(n => n.Start <= lastOn && n.Start + n.Dur > lastOn).Min(n => n.Pitch) % 12;
                int tonic = KeyTonic(keyName);
                if (tonic >= 0)
                {
                    if (endBass != tonic)
                        w.Add("The piece ends with " + PcName(endBass) + " in the bass, but its key is " + keyName.Trim() + ": it ends off the home chord (" + PcName(tonic) + "), so make sure the ending still sounds final.");
                }
                else
                {
                    // without a key: the bass of the first bar where two or more parts play (one part if there is only one)
                    int need = Math.Min(2, pitched.Count), home = -1;
                    for (int bar = 0; bar * bt < lastOn + 1 && home < 0; bar++)
                    {
                        var inBar = pitched.Where(t => t.Notes.Any(n => n.Start >= bar * bt && n.Start < (bar + 1) * bt)).ToList();
                        if (inBar.Count >= need) home = inBar.SelectMany(t => t.Notes).Where(n => n.Start >= bar * bt && n.Start < (bar + 1) * bt).Min(n => n.Pitch) % 12;
                    }
                    if (home >= 0 && endBass != home)
                        w.Add("The piece starts with " + PcName(home) + " in the bass but ends with " + PcName(endBass) + ": it ends off the home chord, so make sure the ending still sounds final.");
                }
            }
            return w;
        }

        static string PcName(int p) { return Spell(60 + ((p % 12) + 12) % 12).TrimEnd('4'); }
        // a pitch spelled the way the notation writes it (bes4, fis3, c5), so a warning can be copied straight into a score
        internal static string Spell(int p) { string[] n = { "c", "cis", "d", "ees", "e", "f", "fis", "g", "aes", "a", "bes", "b" }; return n[((p % 12) + 12) % 12] + (p / 12 - 1); }

        static string Names(List<string> n)
        {
            if (n.Count <= 1) return n.Count == 0 ? "" : n[0];
            return string.Join(", ", n.Take(n.Count - 1)) + " and " + n[n.Count - 1];
        }

        // "C major", "E-flat minor", "Eb", "F# minor", "bes major" -> pitch class of the home note, or -1
        public static int KeyTonic(string key)
        {
            if (key == null) return -1;
            var m = Regex.Match(key.Trim(), @"^([A-Ga-g])(?:\s*-?\s*(flat|sharp)|(♭|♯|#|b|es|is|s))?(?:m|min|maj)?(?=$|[\s,;()-])", RegexOptions.IgnoreCase);   // "D minor", "Bb major", "F#m", "Dm"
            if (!m.Success) return -1;
            int pc = "C D EF G A B".IndexOf(char.ToUpper(m.Groups[1].Value[0]));
            string acc = (m.Groups[2].Value + m.Groups[3].Value).ToLower();
            if (acc == "flat" || acc == "♭" || acc == "b" || acc == "es" || acc == "s") pc--;
            else if (acc.Length > 0) pc++;
            return (pc + 12) % 12;
        }

        internal static bool TempoLineAfterRamp = true;

        // tempo @bar[.beat]: bpm      rit|accel @bar[.beat]-bar[.beat]: bpm      fermata @bar.beat[: factor]
        static void ApplyTempoOps(Song song, List<string[]> ops, ScoreResult res)
        {
            int beatT = Song.PPQ * 4 / song.Den;
            Func<string, string, int> at = (b, bt) => (int.Parse(b) - 1) * song.BarTicks + (bt.Length > 0 ? int.Parse(bt) - 1 : 0) * beatT;
            Func<int, double> bpmAt = tick => { double v = song.Tempos[0].Bpm; foreach (var t in song.Tempos) if (t.Tick <= tick) v = t.Bpm; return v; };
            Action<int, double> set = (tick, v) => { song.Tempos.RemoveAll(t => t.Tick == tick); song.Tempos.Add(new TempoEv { Tick = tick, Bpm = v }); Sym.Stable.Sort(song.Tempos, (a, b) => a.Tick.CompareTo(b.Tick)); };
            var ci = CultureInfo.InvariantCulture;
            var tempoLine = new Dictionary<int, double>();
            foreach (var o in ops.Where(x => x[0] == "tempo"))
            {
                if (o[5].Length == 0) { res.Errors.Add("line " + o[6] + ": a tempo change needs a speed, e.g. tempo @17: 108"); continue; }
                int tick = at(o[1], o[2]); double v = double.Parse(o[5], ci);
                set(tick, v); tempoLine[tick] = v;
            }
            foreach (var o in ops.Where(x => x[0] != "tempo" && x[0] != "fermata"))
            {
                if (o[3].Length == 0 || o[5].Length == 0) { res.Errors.Add("line " + o[6] + ": write a gradual change as " + o[0] + " @21-24: 60 (from the start of bar 21 to the start of bar 24, ending at 60)"); continue; }
                int a = at(o[1], o[2]), b = at(o[3], o[4]);
                if (b <= a) { res.Errors.Add("line " + o[6] + ": the end of the " + o[0] + " must come after its start."); continue; }
                double from = bpmAt(a), to = double.Parse(o[5], ci), after = bpmAt(b);
                song.Tempos.RemoveAll(t => t.Tick > a && t.Tick < b);
                for (int t = a; t < b; t += Song.PPQ / 4) set(t, from + (to - from) * (t - a) / (double)(b - a));
                // a tempo line exactly where the ramp ends takes over there (it used to be overwritten by the ramp's target).
                // Chosen after listening tests of both versions.
                double end;
                bool line = TempoLineAfterRamp && tempoLine.TryGetValue(b, out end) && end != to;
                if (line) res.TempoLineAfterRamp = true;
                set(b, line ? tempoLine[b] : to);
            }
            foreach (var o in ops.Where(x => x[0] == "fermata"))
            {
                int a = at(o[1], o[2]);
                double f = o[5].Length > 0 ? double.Parse(o[5], ci) : 2.0;
                if (f < 1 || f > 8) { res.Errors.Add("line " + o[6] + ": a fermata factor should be between 1 and 8 (2 = twice as long)."); continue; }
                int b = o[3].Length > 0 ? at(o[3], o[4]) : a + beatT;
                double resume = bpmAt(b);
                var inside = song.Tempos.Where(t => t.Tick >= a && t.Tick < b).Select(t => new TempoEv { Tick = t.Tick, Bpm = t.Bpm }).ToList();
                double first = bpmAt(a);
                set(a, first / f);
                foreach (var t in inside) if (t.Tick > a) set(t.Tick, t.Bpm / f);
                set(b, resume);
            }
        }

        static readonly HashSet<string> FieldNames = new HashSet<string> { "instrument", "volume", "pan", "transpose", "voices", "range", "bass", "velocity", "pattern" };

        // Copying out of some chat windows loses the line breaks. Detect that and put them back.
        static readonly Regex GluedHead = new Regex(@"(?<=\S)(?=(?:track|chords)\s+[^|\n]{1,60}?\|\s*(?:instrument|voices|range|pattern|bass|velocity|volume|pan|transpose)\s*:)", RegexOptions.IgnoreCase);
        static readonly Regex GluedHeader = new Regex(@"(?<=\S)(?=(?:title|composer|cover|tuning|gap|album|tempo|meter|key|style|description)\s*:\s*\S)", RegexOptions.IgnoreCase);
        static readonly Regex GluedComment = new Regex(@"(?<=[|)\d.])(?=#\s+\w)");
        static readonly Regex GluedTempo = new Regex(@"(?<=\S)(?=(?:tempo|rit|accel|fermata)\s*@\s*\d)", RegexOptions.IgnoreCase);
        static string RepairCollapsed(string text, ScoreResult res)
        {
            bool glued = GluedHead.IsMatch(text) || text.Split('\n').Any(l => GluedHeader.Matches(l).Count > 0 && !l.TrimStart().StartsWith("#") && Regex.IsMatch(l, @"^\s*(title|composer|tempo|meter)\s*:", RegexOptions.IgnoreCase));
            if (!glued) return text;
            var m = Regex.Match(text, @"(?:^|\n|\S)(?=(?:track|chords)\s+[^|\n]{1,60}?\|)", RegexOptions.IgnoreCase);
            int headerEnd = m.Success ? m.Index + 1 : text.Length;
            string head = text.Substring(0, headerEnd), rest = text.Substring(headerEnd);
            head = GluedHeader.Replace(head, "\n");
            head = GluedTempo.Replace(head, "\n");
            text = head + rest;
            text = GluedHead.Replace(text, "\n");
            text = GluedComment.Replace(text, "\n");
            res.Warnings.Add("The line breaks seem to have been lost when the score was copied, so they were restored automatically. If anything looks wrong, copy the AI's answer again using the chat's Copy button for the code block.");
            res.RepairedText = text;
            return text;
        }

        // Measures every bar of a passage, so that all wrong bars can be reported at once.
        static List<string> AuditBars(Song song, int startTick, string text, bool chords)
        {
            var bad = new List<string>();
            int dur = Song.PPQ, pos = startTick, barStart = startTick, barNo = startTick / song.BarTicks + 1;
            int expectFirst = song.BarTicks - startTick % song.BarTicks;
            var toks = Sym.Dsl.Expand(text).Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < toks.Length; i++)
            {
                string tok = toks[i];
                if (tok == "|")
                {
                    int len = pos - barStart, need = barStart == startTick ? expectFirst : song.BarTicks;
                    if (len != need) bad.Add("bar " + barNo + " adds up to " + Sym.Dsl.DurName(len) + " but a bar needs " + Sym.Dsl.DurName(need));
                    barStart = pos; barNo++;
                    continue;
                }
                if (tok.StartsWith("\\") || tok == "(" || tok == ")" || tok.StartsWith("@")) continue;
                string durPart = tok;
                if (!chords && tok.StartsWith("<"))
                {
                    var sb = new System.Text.StringBuilder(tok);
                    while (!Regex.IsMatch(sb.ToString(), @"\d>") && i + 1 < toks.Length) { i++; sb.Append(' ').Append(toks[i]); }
                    var all = sb.ToString(); var cm = Regex.Match(all, @"\d>");
                    durPart = cm.Success ? all.Substring(cm.Index + 2) : "";
                }
                var m = chords ? DurEnd.Match(durPart) : Regex.Match(durPart, @"/\d+\.*");
                if (m.Success) { try { int ii = 0; dur = Sym.Theory.ParseDur(m.Value, ref ii); } catch { } }
                pos += dur;
            }
            return bad;
        }


        // A "<" or ">" standing alone between spaces is a hairpin with its backslash lost (ChatGPT writes them so, and shell
        // tools strip backslashes): read as \< and \>. A bare ">" was never valid. A bare "<" still opens a chord when what
        // follows on the line is chord pitches and a closing "e4>" ("< c4 e4>/2"); a note with a duration or a bar line
        // after it means a hairpin.
        static readonly Regex BareTok = new Regex(@"(?<=^|\s)[<>](?=\s|$)");
        internal static string BareHairpins(string line, int ln, ref int count, ref int first)
        {
            if (!BareTok.IsMatch(line)) return line;
            int n = 0;
            string res = BareTok.Replace(line, m =>
            {
                if (m.Value == "<" && OpensChord(line.Substring(m.Index + 1))) return m.Value;
                n++; return "\\" + m.Value;
            });
            if (n > 0) { if (count == 0) first = ln; count += n; }
            return res;
        }

        static bool OpensChord(string after)
        {
            var toks = after.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (toks.Length == 0) return false;   // "<" ending a line: a hairpin
            foreach (var tok in toks)
            {
                int gt = tok.IndexOf('>');
                string pitch = gt > 0 && char.IsDigit(tok[gt - 1]) ? tok.Substring(0, gt) : tok;
                int m, used;
                if (!Sym.Theory.TryParsePitch(pitch, out m, out used) || used != pitch.Length) return false;
                if (gt > 0 && char.IsDigit(tok[gt - 1])) return true;
            }
            return true;   // only chord pitches up to the end of the line: it may close on the next line, so as before
        }

        static readonly Regex DurEnd = new Regex(@"/\d+\.*$");
        static readonly Regex DynTok =new Regex(@"^\\(ppp|pp|p|mp|mf|f|ff|fff|sf|fp|<|>|!)$");

        // split a block body into (bar, text) chunks at "@N" marks; chunks without a mark continue after the previous one.
        // Item4 maps the chunk's text back to the score: {offset in the text, source line} for each piece of a line.
        static List<Tuple<int?, int, string, List<int[]>>> Chunks(Block b)
        {
            var res = new List<Tuple<int?, int, string, List<int[]>>>();
            int? bar = null; int line = b.Body.Count > 0 ? b.Body[0].Key : b.Line;
            var sb = new System.Text.StringBuilder();
            var offs = new List<int[]>();
            var at = new Regex(@"(?<![\w])@\s*(\d+)");
            foreach (var kv in b.Body)
            {
                string s = kv.Value;
                int pos = 0;
                foreach (Match mm in at.Matches(s))
                {
                    sb.Append(' '); offs.Add(new[] { sb.Length, kv.Key }); sb.Append(s.Substring(pos, mm.Index - pos));
                    if (sb.ToString().Trim().Length > 0) res.Add(Tuple.Create(bar, line, sb.ToString(), offs));
                    sb.Clear(); offs = new List<int[]>();
                    bar = int.Parse(mm.Groups[1].Value); line = kv.Key; pos = mm.Index + mm.Length;
                }
                sb.Append('\n'); offs.Add(new[] { sb.Length, kv.Key }); sb.Append(s.Substring(pos));
            }
            if (sb.ToString().Trim().Length > 0 || res.Count == 0) res.Add(Tuple.Create(bar, line, sb.ToString(), offs));
            // each "@N" applies only to its own chunk; following chunks without a mark keep bar=null
            return res;
        }

        // ---------- errors with the right source line, and more than one per passage ----------
        // the source line of a position in a chunk's text
        static int LineAt(List<int[]> offs, int pos, int fallback)
        {
            int line = fallback;
            foreach (var o in offs) { if (o[0] <= pos) line = o[1]; else break; }
            return line;
        }

        // the bar an engine error is about ("bar check failed: bar 3 is too short" or "... bar 4+240 token 'x': ...")
        static int ErrorBar(string msg)
        {
            var m = Regex.Match(msg, @"bar check failed: bar (\d+)");
            if (!m.Success) m = Regex.Match(msg, @"\bbar (\d+)\+\d+ token");
            int b; return m.Success && int.TryParse(m.Groups[1].Value, out b) ? b : -1;
        }

        static string ErrorToken(string msg)
        {
            if (msg.Contains("bar check failed")) return null;
            var m = Regex.Match(msg, @"token '(.*?)': ");
            return m.Success && m.Groups[1].Value != "|" ? m.Groups[1].Value : null;
        }

        // positions of the bar lines in a passage's text
        static List<int> BarLines(string text)
        {
            var res = new List<int>();
            for (int i = 0; i < text.Length; i++) if (text[i] == '|') res.Add(i);
            return res;
        }

        // the source line of an error in bar `errBar` of a passage that starts at bar `firstBar` (text from `from` on)
        static int ErrorLine(Tuple<int?, int, string, List<int[]>> ch, List<int> bars, int from, int firstBar, int errBar, string token)
        {
            string text = ch.Item3;
            var inside = bars.Where(p => p >= from).ToList();
            int i = errBar - firstBar;
            int seg = i <= 0 ? from : (i - 1 < inside.Count ? inside[i - 1] + 1 : -1);
            if (seg < 0) return ch.Item2;
            int pos = seg;
            if (token != null) { int t = text.IndexOf(token, seg, StringComparison.Ordinal); if (t >= 0) pos = t; }
            while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
            return LineAt(ch.Item4, pos, ch.Item2);
        }

        // reports the errors of a passage that failed: each with its own source line; after a bad bar, checking goes on at
        // the next bar line (notation only, and not inside [ ]*N repeats, whose bars do not line up with the text)
        static void PassageErrors(ScoreResult res, Song song, string kind, string name, Tuple<int?, int, string, List<int[]>> ch, int start,
            Exception first, Func<int, string, Exception> retry)
        {
            string text = ch.Item3;
            var bars = BarLines(text);
            int firstBar = start / song.BarTicks + 1, from = 0, reported = 0;
            bool resume = retry != null && text.IndexOf('[') < 0;
            Exception ex = first;
            while (true)
            {
                int errBar = ErrorBar(ex.Message);
                int errLine = ch.Item2;
                if (errBar > 0) errLine = ErrorLine(ch, bars, from, firstBar, errBar, ErrorToken(ex.Message));
                else
                {
                    // chord errors name the symbol, not the bar: find the symbol in the text
                    var sm = Regex.Match(ex.Message, @"(?:bad chord root in|' in) (\S+)\s*$");
                    if (!sm.Success) sm = Regex.Match(ex.Message, @"bad chord pitch '(.+?)'");
                    if (sm.Success) { int p = text.IndexOf(sm.Groups[1].Value, from, StringComparison.Ordinal); if (p >= 0) errLine = LineAt(ch.Item4, p, ch.Item2); }
                }
                string where = "line " + errLine + " (" + kind + " " + name + ")";
                if (ex.Message.Contains("bar check failed") && from == 0)
                {
                    var bad = AuditBars(song, start, text, kind == "chords");
                    if (bad.Count > 1)
                    {
                        string head = " (" + kind + " " + name + ")";
                        foreach (var b in bad.Take(16))
                        {
                            var bm = Regex.Match(b, @"^bar (\d+)");
                            int bl = bm.Success ? ErrorLine(ch, bars, 0, firstBar, int.Parse(bm.Groups[1].Value), null) : ch.Item2;
                            res.Errors.Add("line " + bl + head + ": " + b);
                        }
                        if (bad.Count > 16) res.Errors.Add("line " + ch.Item2 + head + ": ... and " + (bad.Count - 16) + " more bars like these");
                        string half = "adds up to " + Sym.Dsl.DurName(song.BarTicks / 2) + " ";
                        if (bad.Count(b => b.Contains(half)) >= 3)
                            res.Errors.Add("line " + ch.Item2 + head + ": hint: many bars hold exactly HALF a bar. In " + song.Num + "/" + song.Den + " a bar lasts " + Sym.Dsl.DurName(song.BarTicks) +
                                (song.Den == 8 && song.Num % 3 == 0 ? ", that is " + (song.Num / 3) + " dotted-quarter beats: put a bar line after every " + (song.Num / 3) + " dotted quarters, not after each one." : "; join each pair of half bars into one bar."));
                        return;
                    }
                }
                res.Errors.Add(where + ": " + Help.Friendly(ex.Message));
                reported++;
                var inside = bars.Where(p => p >= from).ToList();
                int i = errBar - firstBar;
                bool more = resume && errBar > 0 && i >= 0 && i < inside.Count && reported < 16 && inside[i] + 1 < text.Length
                    && text.Substring(inside[i] + 1).Replace("|", "").Trim().Length > 0;
                if (!more)
                {
                    if (!resume || reported >= 16)
                        res.Errors.Add(where + ": checking of this " + (kind == "chords" ? "block" : "passage") + " stopped here; there may be more errors after it.");
                    return;
                }
                from = inside[i] + 1; firstBar = errBar + 1;
                ex = retry(firstBar, text.Substring(from));
                if (ex == null) return;
            }
        }

        static Sym.Movement Scratch(Song s) { var mv = new Sym.Movement("score"); mv.Meter(1, s.Num, s.Den); return mv; }

        static void RunNotation(Song song, Track tr, Block b, ScoreResult res)
        {
            int transpose = 0; string v;
            if (b.Fields.TryGetValue("transpose", out v)) int.TryParse(v, out transpose);
            int nextTick = 0;
            foreach (var ch in Chunks(b))
            {
                int start = ch.Item1.HasValue ? (ch.Item1.Value - 1) * song.BarTicks : nextTick;
                if (start % song.BarTicks != 0) throw new Exception("the previous passage ends in the middle of a bar; finish the bar with rests or start the next passage with @bar.");
                int bar = start / song.BarTicks + 1;
                try
                {
                    var mv = Scratch(song);
                    var ph = mv.P(bar, ch.Item3);
                    var dyn = new List<Sym.DynMark>();
                    var notes = Engine.Notation(song, bar, ch.Item3, transpose, dyn);
                    (tr.Dyn ?? (tr.Dyn = new List<Sym.DynMark>())).AddRange(dyn);
                    tr.Notes.AddRange(notes);
                    WarnLateStart(song, res, "line " + ch.Item2 + " (track " + tr.Name + ")", ch.Item1, notes);
                    nextTick = start + ph.Length;
                }
                catch (Exception ex)
                {
                    PassageErrors(res, song, "track", tr.Name, ch, start, ex, (bar2, rest) =>
                    {
                        try { Engine.Notation(song, bar2, rest, transpose); return null; } catch (Exception e2) { return e2; }
                    });
                    nextTick = start;
                }
            }
        }

        // "@5 r/1 | ..." really enters in bar 6: point it out in case that was not meant
        static void WarnLateStart(Song song, ScoreResult res, string where, int? atBar, List<NoteEv> notes)
        {
            if (!atBar.HasValue || atBar.Value <= 1 || notes.Count == 0) return;
            int firstBar = notes.Min(n => n.Start) / song.BarTicks + 1;
            if (firstBar > atBar.Value)
                res.Warnings.Add(where + ": the passage marked @" + atBar.Value + " has its first note in bar " + firstBar + ", not bar " + atBar.Value +
                    ". If the part should come in at bar " + atBar.Value + ", remove the rest bar(s) at its start; if it should come in at bar " + firstBar + ", write @" + firstBar + " instead.");
        }

        static void RunChords(Song song, Track tr, Block b, ScoreResult res)
        {
            string v;
            int voices = 3, vel = 70, lo = 55, hi = 72;
            if (b.Fields.TryGetValue("voices", out v)) voices = Math.Max(1, Math.Min(6, int.Parse(v)));
            if (b.Fields.TryGetValue("velocity", out v)) vel = Math.Max(1, Math.Min(127, int.Parse(v)));
            if (b.Fields.TryGetValue("range", out v))
            {
                var r = v.Replace(" to ", "-").Split('-');
                if (r.Length != 2 || !Engine.TryParseNoteName(r[0], out lo) || !Engine.TryParseNoteName(r[1], out hi) || hi <= lo)
                    throw new Exception("range should look like C3-G4 (low then high).");
            }
            bool bass = b.Fields.TryGetValue("bass", out v) && (v.StartsWith("y", StringComparison.OrdinalIgnoreCase) || v == "true" || v == "1");
            string pattern = b.Fields.TryGetValue("pattern", out v) ? v : "*: c";
            if (!pattern.Contains(":")) throw new Exception("pattern needs a note length and tokens, for example \"8: c c c c\".");
            int nextTick = 0;
            var made = new List<NoteEv>();
            var marks = new List<Sym.DynMark>();
            string lastDur = null, lastChord = null;
            int lastChordStart = 0;
            foreach (var ch in Chunks(b))
            {
                int start = ch.Item1.HasValue ? (ch.Item1.Value - 1) * song.BarTicks : nextTick;
                int t = start, before = made.Count;
                try
                {
                    // dynamics may appear between chords: split the passage at them
                    var seg = new System.Text.StringBuilder();
                    Action flush = () =>
                    {
                        string s = seg.ToString(); seg.Clear();
                        if (s.Replace("|", "").Trim().Length == 0) return;
                        // a chord duration carries on across dynamics and @ jumps, as it does within a passage
                        var toks = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        int fi = toks.FindIndex(x => x != "|");
                        if (fi >= 0 && lastDur != null && !DurEnd.IsMatch(toks[fi])) toks[fi] += lastDur;
                        foreach (var x in toks) { var dmm = DurEnd.Match(x); if (dmm.Success) lastDur = dmm.Value; }
                        // "=" straight after a dynamic or hairpin ("\> = |") holds the chord before it: a sustained chord ("*"
                        // pattern) is lengthened, as "=" does within a passage; a rhythmic pattern carries on with the same chord
                        while (fi >= 0 && fi < toks.Count && (toks[fi] == "=" || toks[fi].StartsWith("=/")))
                        {
                            if (lastChord == null) throw new Exception("\"=\" holds the chord before it, but there is no chord before it yet.");
                            var dm2 = DurEnd.Match(toks[fi]); int di = 0;
                            int d = dm2.Success ? Sym.Theory.ParseDur(dm2.Value, ref di) : Song.PPQ;
                            if (pattern.Split(':')[0].Trim() != "*") { toks[fi] = lastChord + (dm2.Success ? dm2.Value : ""); break; }
                            foreach (var n in made.Where(n => n.Start >= lastChordStart))
                            {
                                int written = n.Dur + n.Gap + d;
                                int dur = n.Gap > 0 ? Math.Max(20, (int)(written * 0.95)) : written;
                                n.Dur = dur; n.Gap = written - dur;
                            }
                            t += d; toks.RemoveAt(fi); fi = toks.FindIndex(x => x != "|");
                        }
                        s = string.Join(" ", toks);
                        if (s.Replace("|", "").Trim().Length == 0 && !s.Contains("|")) return;
                        var mv = Scratch(song);
                        var h = mv.H(t / song.BarTicks + 1, (t % song.BarTicks) / (double)Song.PPQ, s);
                        made.AddRange(Engine.HarmonyAt(song, t, s, voices, lo, hi, bass, pattern, vel));
                        if (h.Spans.Count > 0)
                        {
                            t = h.Spans.Max(x => x.Start + x.Dur);
                            lastChordStart = h.Spans.Last().Start;
                            var sym = toks.LastOrDefault(x => x != "|" && x != "=" && !x.StartsWith("=/"));
                            if (sym != null) lastChord = DurEnd.Replace(sym, "");
                        }
                    };
                    // write out [ ... ]*N repeats first, so dynamics before or inside them do not cut the brackets apart;
                    // Expand also spaces out bar lines that touch a dynamic ("|\mf Am"), e.g. after lost line breaks
                    foreach (var tok in Sym.Dsl.Expand(ch.Item3).Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var dm = DynTok.Match(tok);
                        if (!dm.Success) { seg.Append(' ').Append(tok); continue; }
                        flush();
                        string d = dm.Groups[1].Value;
                        if (d == "!") continue;
                        if (d == "<" || d == ">") marks.Add(new Sym.DynMark { Time = t, Level = -1, Ramp = true });
                        else marks.Add(new Sym.DynMark { Time = t, Level = Sym.Theory.Levels[d] });
                    }
                    flush();
                    WarnLateStart(song, res, "line " + ch.Item2 + " (chords " + tr.Name + ")", ch.Item1, made.Skip(before).ToList());
                    nextTick = t;
                }
                catch (Exception ex) { PassageErrors(res, song, "chords", tr.Name, ch, start, ex, null); nextTick = t; }
            }
            if (marks.Count > 0)
            {
                // dynamics in the passage set the loudness; accents from the pattern stay on top
                var curve = Sym.Render.Resolve(marks);
                (tr.Dyn ?? (tr.Dyn = new List<Sym.DynMark>())).AddRange(curve);
                foreach (var n in made)
                {
                    int accent = n.Vel - vel;
                    n.Vel = Math.Max(1, Math.Min(127, (int)Math.Round(16 + Sym.Render.LevelAt(curve, n.Start) * 13) + accent));
                }
            }
            tr.Notes.AddRange(made);
        }
    }
}
