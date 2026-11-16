// Renders score scripts and MIDI files to loudness-matched MP3s with FluidSynth (soundfont synth) and ffmpeg.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Abend
{
    public class RenderOptions
    {
        public string OutDir, SoundFont, ToolsDir;
        public double Lufs = -14, TruePeak = -1.5, Lra = 15;   // EBU R128 targets; LRA = how much loud/soft contrast to keep
        public int Bitrate = 192;
        public bool Force;
        public bool DryRun;                                      // --dry-run: only report which files would be rendered
        public double GapSeconds = 1.5;                          // silence after each track
        public double? MovementGapSeconds;                       // after each movement of an album but the last (null = GapSeconds)
        public bool AlbumGiven;                                  // --album on the command line: each folder's files (without an album: header) are one work's movements
        public double BassDb = 0;                                // low-shelf lift around 100 Hz for subwoofers, applied before loudness matching
        public string Album = "";                                // album tag when a script has no album: header (empty = none)
        public string Artist = "Abend Orchestra";                 // used when a script has no real composer: line
        public string Cover;                                     // --cover: image used when a file has no cover of its own
        public string Library;                                   // --library sonatina: play what it can from a sample library (null = default soundfont only)
        public double? Tuning;                                   // --tuning: concert A in Hz for everything in the run (overrides a score's tuning: header)
        public bool Expression;                                  // --expression: swells, legato, vibrato and small human variations
        public string Harpsichord;                               // --harpsichord file.sf2: another harpsichord instead of VCSL's (null = VCSL's)
        public string Percussion;                                // --percussion sonatina|generaluser (null = sonatina with --library)
        public string LibraryFile, HarpsichordFile, ExtrasFile;  // found by CheckSetup (the harpsichord and the extras are optional)
        // album loudness (scores with an album: header), used inside one RenderPath run
        internal bool AlbumMeasure;                              // stop after finding the track's own gain
        internal Dictionary<string, double> AlbumGains;          // source -> the gain to use instead
        internal Dictionary<string, string> WavCache;            // source -> its synthesized WAV, so the second pass skips FluidSynth
        internal string WavCacheDir;
        internal HashSet<string> InnerMovements;                 // album movements followed by another (they get MovementGapSeconds)
        public RenderOptions Copy() { return (RenderOptions)MemberwiseClone(); }
        // drums and timpani from the library's own percussion: the default with --library;
        // "generaluser" (--percussion, or a score's percussion: header) keeps the default soundfont's kit and timpani
        public bool SonatinaPercussion { get { return Library != null && (Percussion ?? "sonatina") == "sonatina"; } }
    }

    public class RenderResult { public double Gain = double.NaN; public string Album; public string Source, Output, Status, Detail, Key; public double Seconds; public bool Ok, Compressed, UsesExtras; public List<string> Warnings = new List<string>(), Notes = new List<string>(); }

    public static class AudioRender
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        // ---------- finding the tools ----------
        // a tool's file name on this system: "ffmpeg.exe" on Windows, "ffmpeg" on Mac and Linux
        public static string Exe(string name) { return Path.DirectorySeparatorChar == '\\' ? name + ".exe" : name; }

        public static string Md5(string file)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (var s = File.OpenRead(file))
                return BitConverter.ToString(md5.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        public static IEnumerable<string> SearchRoots(string toolsDir)
        {
            var roots = new List<string>();
            if (!string.IsNullOrEmpty(toolsDir)) roots.Add(toolsDir);
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            roots.Add(exeDir);
            roots.Add(Path.Combine(exeDir, ".."));
            roots.Add(Environment.CurrentDirectory);
            string saved = Settings.Get("radio");
            if (saved != null) roots.Add(saved);
            return roots.Where(Directory.Exists).Select(Path.GetFullPath).Distinct();
        }

        public static string FindTool(string exe, string toolsDir)
        {
            foreach (var r in SearchRoots(toolsDir))
            {
                var direct = Path.Combine(r, exe);
                if (File.Exists(direct)) return direct;
                var td = Path.Combine(r, "tools");
                if (Directory.Exists(td))
                {
                    var hit = Directory.GetFiles(td, exe, SearchOption.AllDirectories).FirstOrDefault();
                    if (hit != null) return hit;
                }
            }
            foreach (var p in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { var f = Path.Combine(p.Trim(), exe); if (File.Exists(f)) return f; } catch { }
            }
            return null;
        }

        public static string FindSoundFont(string given, string toolsDir)
        {
            if (!string.IsNullOrEmpty(given)) return File.Exists(given) ? Path.GetFullPath(given) : null;
            foreach (var r in SearchRoots(toolsDir))
            {
                var sd = Path.Combine(r, "soundfonts");
                if (Directory.Exists(sd))
                {
                    var hit = Directory.GetFiles(sd, "*.sf2").OrderBy(f => f).FirstOrDefault();
                    if (hit != null) return hit;
                }
            }
            return null;
        }

        public static string CheckSetup(RenderOptions o, out string fluid, out string ffmpeg, out string sf2)
        {
            fluid = FindTool(Exe("fluidsynth"), o.ToolsDir);
            ffmpeg = FindTool(Exe("ffmpeg"), o.ToolsDir);
            sf2 = FindSoundFont(o.SoundFont, o.ToolsDir);
            var missing = new List<string>();
            if (fluid == null) missing.Add("FluidSynth (" + Exe("fluidsynth") + ")");
            if (ffmpeg == null) missing.Add("ffmpeg (" + Exe("ffmpeg") + ")");
            if (sf2 == null) missing.Add(o.SoundFont != null ? "the soundfont " + o.SoundFont : "a .sf2 soundfont in a soundfonts folder");
            if (o.Library != null && o.Harpsichord == null) o.HarpsichordFile = Library.FindHarpsichord(o.ToolsDir);
            if (o.Library != null && o.Harpsichord != null) { if ((o.HarpsichordFile = Library.FindHarpsichord(o.ToolsDir, o.Harpsichord)) == null) missing.Add("the harpsichord soundfont " + o.Harpsichord); }
            if (o.Library != null) o.ExtrasFile = Library.FindExtras(o.ToolsDir);
            if (o.Library != null && (o.LibraryFile = Library.Find(o.Library, o.ToolsDir)) == null) missing.Add("the " + o.Library + " library (soundfonts/Sonatina/*.sf2)");
            return missing.Count == 0 ? null : "Could not find " + string.Join(", ", missing) + ". Install FluidSynth and ffmpeg (or put them in a tools folder next to abendc), run the setup script for the sounds (a soundfonts folder next to abendc), or pass --tools and --soundfont.";
        }

        // ---------- batch ----------
        public static List<RenderResult> RenderPath(string input, RenderOptions o, Action<string> say)
        {
            string fluid, ffmpeg, sf2;
            var setup = CheckSetup(o, out fluid, out ffmpeg, out sf2);
            if (setup != null) throw new Exception(setup);
            var files = new List<string>();
            if (Directory.Exists(input))
                files.AddRange(Directory.GetFiles(input, "*", SearchOption.AllDirectories).Where(f => Regex.IsMatch(f, @"\.(abscore|txt|mid|midi)$", RegexOptions.IgnoreCase) && !f.EndsWith(".title.txt", StringComparison.OrdinalIgnoreCase) && (!f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || IsScoreText(f))).OrderBy(f => f));
            else if (File.Exists(input)) files.Add(input);
            else throw new Exception("Nothing found at " + input);
            if (o.OutDir == null) o.OutDir = Directory.Exists(input) ? input : Path.GetDirectoryName(Path.GetFullPath(input));
            Directory.CreateDirectory(o.OutDir);
            say("Soundfont: " + Path.GetFileName(sf2) + "   target " + o.Lufs.ToString("0.#", CI) + " LUFS, " + o.Bitrate + " kbps" + (o.BassDb != 0 ? ", bass " + o.BassDb.ToString("+0.#;-0.#", CI) + " dB" : "")+ (o.Library != null ? ", library " + o.Library : "") + (o.Expression ? ", expression" : "") + "   output: " + o.OutDir);
            var results = new List<RenderResult>();
            string rootOut = o.OutDir, rootIn = Directory.Exists(input) ? Path.GetFullPath(input).TrimEnd('\\', '/') : null;
            // scores in one folder that share an album: header are rendered together with one loudness gain
            var albumOf = files.ToDictionary(f => f, f => AlbumOf(f));
            var albumsDone = new HashSet<string>();
            // --album names a work rendered from a folder of movements (e.g. MIDI files): all but the last file of each folder
            // get the shorter pause between movements; their loudness stays per track
            if (o.AlbumGiven && o.MovementGapSeconds.HasValue)
            {
                o.InnerMovements = new HashSet<string>();
                foreach (var g in files.Where(f => albumOf[f] == null).GroupBy(f => Path.GetDirectoryName(Path.GetFullPath(f)).ToLowerInvariant()))
                    foreach (var f in g.Take(g.Count() - 1)) o.InnerMovements.Add(f);
            }
            foreach (var f in files)
            {
                // scores/symphonies/<work>/x.abscore -> out/symphonies/<work>/x.mp3
                string sub = rootIn == null ? "" : Path.GetDirectoryName(Path.GetFullPath(f)).Substring(rootIn.Length).TrimStart('\\', '/');
                o.OutDir = sub.Length == 0 ? rootOut : Path.Combine(rootOut, sub);
                Directory.CreateDirectory(o.OutDir);
                List<RenderResult> rs;
                string album = albumOf[f], albumKey = album == null ? null : Path.GetDirectoryName(Path.GetFullPath(f)).ToLowerInvariant() + "|" + album;
                if (album == null) rs = new List<RenderResult> { RenderFile(f, o, fluid, ffmpeg, sf2) };
                else if (!albumsDone.Add(albumKey)) continue;
                else rs = RenderAlbum(files.Where(g => albumOf[g] == album && Path.GetDirectoryName(Path.GetFullPath(g)).ToLowerInvariant() + "|" + album == albumKey).ToList(), album, o, fluid, ffmpeg, sf2);
                foreach (var r in rs)
                {
                    results.Add(r);
                    say(string.Format("{0,-8} {1}{2}", r.Status, (sub.Length > 0 ? sub + Path.DirectorySeparatorChar : "") + Path.GetFileName(r.Source), r.Detail == null ? "" : "  - " + r.Detail));
                    foreach (var w in r.Warnings) say("         warning: " + w);
                    foreach (var n in r.Notes) say("         " + n);
                }
            }
            o.OutDir = rootOut;
            if (!o.DryRun) WriteLog(o.OutDir, results);
            return results;
        }

        // A .txt in a render folder is a score only if it has a track or chords block; render-log.txt, notes and READMEs
        // are passed over quietly (the folder's own render-log.txt used to be listed as SKIPPED, "No track blocks")
        internal static bool IsScoreText(string f)
        {
            if (Path.GetFileName(f).Equals("render-log.txt", StringComparison.OrdinalIgnoreCase)) return false;
            try { return File.ReadLines(f).Any(l => Regex.IsMatch(l, @"^\s*(track|chords)\s+\S|(track|chords)\s+[^|]{1,60}\|\s*(instrument|voices)\s*:", RegexOptions.IgnoreCase)); } catch { return false; }   // the second form: lines run together
        }

        static string AlbumOf(string f)
        {
            if (!f.EndsWith(".abscore", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return null;
            try { return ScoreScript.Parse(File.ReadAllText(f)).Album; } catch { return null; }
        }

        // Album loudness: per-track normalisation brings every movement of a work to -14 LUFS, which flattens it (a finale's
        // climax ends up no louder than a quiet movement's). The movements of an album are measured first; then all of them
        // get the gain that brings the LOUDEST movement to the target, so they keep their loudness relative to each other.
        // A soft movement is lifted where needed so it lands at most AlbumMaxDrop dB below the target. The peak limiter
        // still works per track. If one movement is out of date, the whole album is rendered again (its gain may change).
        public const double AlbumMaxDrop = 3.0;
        static List<RenderResult> RenderAlbum(List<string> files, string album, RenderOptions o, string fluid, string ffmpeg, string sf2)
        {
            // movements play in file-name order (I., II., ...); all but the last get the shorter pause, when one is set
            if (o.MovementGapSeconds.HasValue && files.Count > 1) { o = o.Copy(); o.InnerMovements = new HashSet<string>(files.Take(files.Count - 1)); }
            var check = o.Copy(); check.DryRun = true;
            var status = files.Select(f => RenderFile(f, check, fluid, ffmpeg, sf2)).ToList();
            if (status.All(r => r.Status == "UP-TO-DATE")) return status;
            if (o.DryRun)
            {
                foreach (var r in status.Where(r => r.Ok)) { r.Status = "TO-DO"; r.Detail = "album: " + album + (r.Detail != null ? ", " + r.Detail : ""); }
                return status;
            }
            if (o.Library == null && !o.Expression)
            {
                // plain renders use loudnorm, which has no fixed gain to share
                var plain = files.Select(f => RenderFile(f, o, fluid, ffmpeg, sf2)).ToList();
                foreach (var r in plain) r.Warnings.Add("album loudness needs --library or --expression; rendered per track");
                return plain;
            }
            var m = o.Copy(); m.Force = true; m.AlbumMeasure = true; m.WavCache = new Dictionary<string, string>();
            m.WavCacheDir = Path.Combine(Path.GetTempPath(), "abend-album-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(m.WavCacheDir);
            try
            {
                var measured = files.Select(f => RenderFile(f, m, fluid, ffmpeg, sf2)).ToList();
                var good = measured.Where(r => r.Ok && !double.IsNaN(r.Gain)).ToList();
                if (good.Count == 0) return measured;
                double albumGain = good.Min(r => r.Gain);
                var p = m.Copy(); p.AlbumMeasure = false; p.AlbumGains = new Dictionary<string, double>();
                foreach (var r in good) p.AlbumGains[r.Source] = Math.Max(albumGain, r.Gain - AlbumMaxDrop);
                var results = new List<RenderResult>();
                foreach (var f in files)
                {
                    var own = measured.First(r => r.Source == f);
                    if (!own.Ok || double.IsNaN(own.Gain)) { results.Add(own); continue; }
                    var r2 = RenderFile(f, p, fluid, ffmpeg, sf2);
                    r2.Notes.Add(string.Format(CI, "album \"{0}\": gain {1:+0.0;-0.0} dB (on its own {2:+0.0;-0.0} dB){3}", album, p.AlbumGains[f], own.Gain,
                        p.AlbumGains[f] > albumGain + 0.05 ? ", lifted to stay within " + AlbumMaxDrop.ToString(CI) + " dB of the target" : ""));
                    results.Add(r2);
                }
                return results;
            }
            finally { try { Directory.Delete(m.WavCacheDir, true); } catch { } }
        }

        static void WriteLog(string dir, List<RenderResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Render " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            foreach (var r in results)
            {
                sb.AppendLine(string.Format("{0,-8} {1}{2}", r.Status, Path.GetFileName(r.Source), r.Detail == null ? "" : "  - " + r.Detail));
                foreach (var w in r.Warnings) sb.AppendLine("         warning: " + w);
                foreach (var n in r.Notes) sb.AppendLine("         " + n);
            }
            sb.AppendLine();
            try { File.AppendAllText(Path.Combine(dir, "render-log.txt"), sb.ToString()); } catch { }
        }

        static string SafeName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
            s = s.Trim().Trim('.');
            return s.Length == 0 ? "untitled" : s;
        }

        // ---------- cover art ----------
        static readonly string[] CoverExts = { ".png", ".jpg", ".jpeg" };

        // first match wins: cover: header (relative to the script, or absolute), <name>.png/.jpg next to the file,
        // cover.png/.jpg in the file's folder, then --cover. A cover: header that names a missing file gives a warning.
        public static string FindCover(string path, string header, string fallback, List<string> warnings)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(header))
            {
                string h = header.Trim().Trim('"');
                string p = Path.IsPathRooted(h) ? h : Path.Combine(dir, h);
                if (File.Exists(p)) return Path.GetFullPath(p);
                if (warnings != null) warnings.Add("cover: " + h + " was not found next to the script; looking for other covers.");
            }
            foreach (var name in new[] { Path.GetFileNameWithoutExtension(path), "cover" })
                foreach (var ext in CoverExts)
                {
                    string p = Path.Combine(dir, name + ext);
                    if (File.Exists(p)) return p;
                }
            return !string.IsNullOrEmpty(fallback) && File.Exists(fallback) ? Path.GetFullPath(fallback) : null;
        }

        // the MP3 remembers which cover it was made with (a TXXX tag holding the image's MD5), so adding, changing
        // or removing a cover makes the track out of date even when the image file has an old date
        const string CoverTag = "abend_cover";
        public static string CoverKey(string cover) { return cover == null ? "" : Md5(cover); }
        public static string CoverKeyIn(string mp3) { return TagIn(mp3, CoverTag); }

        static string TagIn(string mp3, string tag)
        {
            try
            {
                var buf = new byte[65536];
                int n;
                using (var s = File.OpenRead(mp3)) n = s.Read(buf, 0, buf.Length);
                string head = Encoding.GetEncoding(28591).GetString(buf, 0, n).Replace("\0", "");   // also reads UTF-16 tag text
                var m = Regex.Match(head, tag + @"[^0-9a-f]{0,4}([0-9a-f]{32})");
                return m.Success ? m.Groups[1].Value : "";
            }
            catch { return ""; }
        }

        // The MP3 also remembers the settings it was rendered with, so changing them (or a new version of the sound
        // processing) makes the track out of date without --force. Plain default renders carry no such tag.
        // Bump RenderVersion whenever a change alters the sound of library/expression renders.
        public const string RenderVersion = "2026-10-30";
        const string RenderTag = "abend_render";
        public static string RenderKey(RenderOptions o)
        {
            var d = new RenderOptions();
            if (o.Library == null && !o.Expression && o.Lufs == d.Lufs && o.Lra == d.Lra && o.TruePeak == d.TruePeak && o.BassDb == d.BassDb &&
                o.Bitrate == d.Bitrate && o.GapSeconds == d.GapSeconds && o.Album == d.Album && o.Artist == d.Artist && (!o.Tuning.HasValue || o.Tuning.Value == 440)) return "";
            string s = string.Join("|", RenderVersion, o.Lufs.ToString(CI), o.Lra.ToString(CI), PeakTarget(o).ToString(CI), o.BassDb.ToString(CI), o.Bitrate.ToString(CI),
                o.GapSeconds.ToString(CI), o.Album, o.Artist, o.Library ?? "", o.Expression ? "expr" : "", o.HarpsichordFile != null ? "harpsichord" : "");
            // options added later go in only when set to something other than their default, so adding an option never
            // changes the key of existing renders (that made every library track count as out of date once)
            if (o.Tuning.HasValue && o.Tuning.Value != 440) s += "|A" + o.Tuning.Value.ToString(CI);
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").ToLowerInvariant();
        }
        public static string RenderKeyIn(string mp3) { return TagIn(mp3, RenderTag); }

        // A .mid is titled after its file name. "Song.title.txt" next to "Song.mid" gives it another title (its first
        // line) without renaming the file, which would change the track's identity where it is published.
        public static string SidecarTitle(string midi)
        {
            string f = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(midi)), Path.GetFileNameWithoutExtension(midi) + ".title.txt");
            if (!File.Exists(f)) return null;
            string t = (File.ReadAllLines(f, Encoding.UTF8).FirstOrDefault() ?? "").Trim();
            return t.Length == 0 ? null : t;
        }

        // the settings key of one file: a sidecar title goes in too, so adding or changing one re-renders that file only
        // and so does a score's gap: header (only when the score has one, so scores without it keep their key)
        public static string FileKey(RenderOptions o, string sidecarTitle) { return FileKey(o, sidecarTitle, null, null); }
        public static string FileKey(RenderOptions o, string sidecarTitle, double? gap) { return FileKey(o, sidecarTitle, gap, null); }
        public static string FileKey(RenderOptions o, string sidecarTitle, double? gap, string album) { return FileKey(o, sidecarTitle, gap, album, false); }
        // ... and, for a piece with a harpsichord part, which harpsichord soundfont plays it (since the Flemish one, 2026-10-27)
        public static string FileKey(RenderOptions o, string sidecarTitle, double? gap, string album, bool harpsichordPart) { return FileKey(o, sidecarTitle, gap, album, harpsichordPart, false); }
        // ... and, for a piece with drums or timpani, the library percussion (only when it plays them; the default kit adds nothing)
        public static string FileKey(RenderOptions o, string sidecarTitle, double? gap, string album, bool harpsichordPart, bool percussionPart) { return FileKey(o, sidecarTitle, gap, album, harpsichordPart, percussionPart, false); }
        // ... and, for a piece with a pipe organ or accordion part, the extras soundfont that plays it (since 2026-10-30)
        public static string FileKey(RenderOptions o, string sidecarTitle, double? gap, string album, bool harpsichordPart, bool percussionPart, bool extrasPart) { return FileKey(o, sidecarTitle, gap, album, harpsichordPart, percussionPart, extrasPart, false); }
        // ... and, for a piece with more than 15 instrument tracks, that each part has its own channel (since 2026-11-03)
        public static string FileKey(RenderOptions o, string sidecarTitle, double? gap, string album, bool harpsichordPart, bool percussionPart, bool extrasPart, bool ownChannels) { return FileKey(o, sidecarTitle, gap, album, harpsichordPart, percussionPart, extrasPart, ownChannels, false); }
        // ... and, for a score with a tempo line where a rit or accel ends, that the tempo line now counts (since 2026-11-15)
        public static string FileKey(RenderOptions o, string sidecarTitle, double? gap, string album, bool harpsichordPart, bool percussionPart, bool extrasPart, bool ownChannels, bool tempoLine)
        {
            string key = RenderKey(o), extra = "";
            if (ownChannels) extra += "|channels=own";
            if (tempoLine) extra += "|tempo=line";
            if (extrasPart && o.Library != null && o.ExtrasFile != null) extra += "|extras=" + Path.GetFileName(o.ExtrasFile);
            if (harpsichordPart && o.Library != null && o.HarpsichordFile != null) extra += "|hpsi=" + Path.GetFileName(o.HarpsichordFile);
            if (percussionPart && o.SonatinaPercussion) extra += "|perc=sonatina";
            if (sidecarTitle != null) extra += "|title=" + sidecarTitle;
            if (gap.HasValue) extra += "|gap=" + gap.Value.ToString(CI);
            if (album != null) extra += "|album=" + album + "|drop=" + AlbumMaxDrop.ToString(CI);
            if (extra.Length == 0) return key;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(key + extra))).Replace("-", "").ToLowerInvariant();
        }

        // sample-library renders keep a little more headroom: their MP3s overshoot the true-peak target slightly more
        static double PeakTarget(RenderOptions o) { return o.Library != null ? Math.Min(o.TruePeak, -2.0) : o.TruePeak; }
        // the fixed-gain limiter's ceiling (dBFS): -2.0 for library renders since 2026-10-29 (it was PeakTarget - 1.0 = -3.0;
        // the engineer measured 1.5-2 dB less squeeze on loud moments, stream peaks still -1.6 to -2.2 dBTP)
        static double LimiterCeiling(RenderOptions o) { return o.Library != null ? -2.0 : PeakTarget(o) - 1.0; }

        // expression first (it looks at the General MIDI instruments), then the library's presets
        // The notation ends ordinary notes 5% early, so a 4-bar held chord stops 0.8 s before the next one. A soundfont with a
        // long release (the default one) hides that; Sonatina's short releases turn it into a hole at every chord change.
        // Library and expression renders cap the gap at 50 ms and play sustained chords ("*" patterns, "=") legato.
        internal static Song CloseGaps(Song song) { return CloseGaps(song, false); }

        // continuous: the expression pass will shape dynamics with CC11, so a held pitch may carry on across a change of
        // written velocity
        internal static Song CloseGaps(Song song, bool continuous)
        {
            var s = song.Clone();
            foreach (var t in s.Tracks)
                foreach (var n in t.Notes)
                {
                    if (n.Gap <= 0) continue;
                    int end = n.Start + n.Dur + n.Gap;
                    int keep = n.Sustained ? 0 : Math.Min(n.Gap, Math.Max(1, end - s.MsToTick(Math.Max(0, s.TickToMs(end) - 50))));
                    n.Dur += n.Gap - keep; n.Gap = keep;
                }
            // a sustained chord note that carries on at the same pitch into the next chord (a pedal, a common tone, a pad on
            // one chord for several bars) becomes one held note, like one long bow, instead of being struck again at every
            // bar line: Sonatina's strings and low brass take 0.2-0.3 s to speak, so each new stroke sagged. It is still struck
            // again when the written loudness changes by more than about one dynamic level.
            foreach (var t in s.Tracks)
            {
                var sus = t.Notes.Where(n => n.Sustained).OrderBy(n => n.Start).ToList();
                var byKey = sus.GroupBy(n => n.Start * 128 + n.Pitch).ToDictionary(g => g.Key, g => g.First());
                var gone = new HashSet<NoteEv>();
                foreach (var n in sus)
                {
                    if (gone.Contains(n)) continue;
                    NoteEv next;
                    while (byKey.TryGetValue((n.Start + n.Dur + n.Gap) * 128 + n.Pitch, out next) && !gone.Contains(next) && (Math.Abs(next.Vel - n.Vel) <= 12 || (continuous && t.Dyn != null)))
                    { n.Dur = next.Start + next.Dur - n.Start; n.Gap = next.Gap; gone.Add(next); }
                }
                t.Notes.RemoveAll(gone.Contains);
            }
            // where a held note (half a second or more) moves straight on to a different pitch, it overlaps the next by up to
            // 0.35 s (at most a quarter of its length), like a bow change: Sonatina's strings and low brass take 0.2-0.3 s to
            // speak, which otherwise leaves a dip at every chord change. Repeated pitches are handed over cleanly (the same key
            // on one channel cannot overlap itself).
            foreach (var t in s.Tracks)
            {
                var starts = t.Notes.GroupBy(n => n.Start).ToDictionary(g => g.Key, g => new HashSet<int>(g.Select(n => n.Pitch)));
                foreach (var n in t.Notes.ToList())
                {
                    int end = n.Start + n.Dur + n.Gap; double len = s.TickToMs(end) - s.TickToMs(n.Start); HashSet<int> next;   // written end (after the capped gap)
                    if (len < 500 || !starts.TryGetValue(end, out next) || next.Contains(n.Pitch)) continue;
                    n.Dur = s.MsToTick(s.TickToMs(end) + Math.Min(350, len / 4)) - n.Start; n.Gap = 0;
                }
            }
            return s;
        }

        // Sonatina's sustained sounds stop within about 0.2 s of key-up (a gate); a longer release (NRPN on generator 38,
        // release time, +900 timecents) gives each note a short natural tail: about -25 dB after 0.4 s instead of -40
        const int ReleaseOffsetCents = 900;
        static void AddReleaseTails(Song s)
        {
            int v = 8192 + ReleaseOffsetCents / 2;   // 2 timecents per step
            foreach (var t in s.Tracks.Where(x => x.Bank == Library.Bank && Library.IsHeldSound(x.Program)))
                foreach (var c in new[] { new[] { 99, 120 }, new[] { 98, 38 }, new[] { 38, v & 127 }, new[] { 6, v >> 7 } })
                    t.Controls.Add(new CtlEv { Tick = 0, Cc = c[0], Val = c[1] });
        }

        static Song Prepare(Song song, RenderOptions o, RenderResult r)
        {
            song = OwnChannels(song);
            if (o.Tuning.HasValue) { song = song.Clone(); song.TuningHz = o.Tuning.Value; }
            if (song.TuningHz != 440) r.Notes.Add(TuningText(song.TuningHz));
            var prepared = PrepareSound(song, o, r);
            if (o.Library != null || o.Expression) NoSelfOverlap(prepared);
            return prepared;
        }

        // MIDI cannot sound one key twice on one channel: when a note starts while the previous note of the same pitch in the
        // same part is still on, that earlier note's note-off silences the new one for its whole length. Closed gaps plus
        // human timing (+-8 ms) made this happen to re-struck chords and repeated notes, so as the very last step each note
        // ends where the next note of its pitch begins.
        internal static void NoSelfOverlap(Song s)
        {
            foreach (var t in s.Tracks)
                foreach (var g in t.Notes.GroupBy(n => n.Pitch))
                {
                    var l = g.OrderBy(n => n.Start).ToList();
                    for (int i = 1; i < l.Count; i++)
                    {
                        var prev = l[i - 1];
                        if (prev.Start + prev.Dur <= l[i].Start) continue;
                        if (l[i].Start <= prev.Start) { l[i].Start = prev.Start + 1; if (l[i].Dur > 1) l[i].Dur--; }   // same start: keep both, one tick apart
                        prev.Dur = Math.Max(1, l[i].Start - prev.Start);
                    }
                }
        }

        // A MIDI file has 16 channels, so a score with more than 15 instrument tracks puts the extra ones on channel 0 with
        // the first part, and one channel plays one sound: the later part's sound and expression won for all of them (a
        // glockenspiel sharing the piano's channel played the next part's violin samples above their range: silence).
        // A render has up to 48 channels (one FluidSynth pass per 16), so there each part gets a channel of its own.
        internal static bool SharesChannels(Song s)
        {
            return s != null && s.Tracks.Where(t => !t.IsDrums).GroupBy(t => t.Channel).Any(g => g.Count() > 1);
        }

        internal static Song OwnChannels(Song song)
        {
            if (!SharesChannels(song)) return song;
            var s = song.Clone();
            var seen = new HashSet<int>();
            foreach (var t in s.Tracks.Where(x => !x.IsDrums))
            {
                if (seen.Add(t.Channel)) continue;
                int ch = Enumerable.Range(0, Library.MaxChannels).Where(c => c % 16 != 9 && !s.Tracks.Any(x => x.Channel == c)).DefaultIfEmpty(-1).First();
                if (ch < 0) break;   // more than 45 parts: the check after preparing names the ones left sharing
                t.Channel = ch; seen.Add(ch);
            }
            return s;
        }

        // Self-check after preparing a render: a part with notes that will make no sound. Either it shares a channel with a
        // part of another sound, sits on a drum channel, or its sound has no recording for its keys (Sf2Info zones).
        internal static void CheckAudible(Song s, RenderOptions o, string sf2, RenderResult r)
        {
            Func<int, Sf2Info> font = bank =>
            {
                string f = bank == Library.Bank ? o.LibraryFile : bank == Library.HarpsichordBank ? o.HarpsichordFile : bank == Library.ExtrasBank ? o.ExtrasFile : bank == 0 ? sf2 : null;
                try { return f != null && File.Exists(f) ? Sf2Info.Load(f) : null; } catch { return null; }
            };
            foreach (var t in s.Tracks.Where(x => x.Notes.Count > 0 && !x.IsDrums))
            {
                var other = s.Tracks.FirstOrDefault(x => x != t && x.Channel == t.Channel && !x.IsDrums && (x.Bank != t.Bank || x.Program != t.Program));
                if (other != null) { r.Warnings.Add(t.Name + " shares a MIDI channel with " + other.Name + ", so it will play with the wrong sound or none."); continue; }
                if (t.Channel < 0 || t.Channel % 16 == 9) { r.Warnings.Add(t.Name + " is on a drum channel, so it will play drum sounds or none."); continue; }
                var sf = font(t.Bank);
                if (sf == null) continue;
                const int bank = 0;   // the main soundfont's bank 0, or a library soundfont's bank 0 (loaded as Library.Bank etc.)
                if (!sf.Has(bank, t.Program)) { r.Warnings.Add(t.Name + ": its sound (bank " + t.Bank + ", program " + t.Program + ") is missing from the soundfont, so the part is silent."); continue; }
                var mute = t.Notes.Where(n => !sf.Covers(bank, t.Program, n.Pitch, Math.Max(1, Math.Min(127, n.Vel)))).ToList();
                if (mute.Count == 0) continue;
                string name; if (!sf.Names.TryGetValue(bank * 128 + t.Program, out name)) name = "its sound";
                r.Warnings.Add(mute.Count == t.Notes.Count
                    ? t.Name + ": none of its " + t.Notes.Count + " notes has a recording in " + name.Trim() + ", so the part is silent."
                    : t.Name + ": " + mute.Count + " of its " + t.Notes.Count + " notes have no recording in " + name.Trim() + " and will be silent (e.g. " +
                      string.Join(", ", mute.Select(n => ScoreScript.Spell(n.Pitch)).Distinct().Take(3)) + ").");
            }
        }

        static Song Checked(Song s, RenderOptions o, string sf2, RenderResult r) { CheckAudible(s, o, sf2, r); return s; }

        public static string TuningText(double hz) { return string.Format(CI, "tuning A={0:0.##} ({1:+0.0;-0.0} cents)", hz, 1200 * Math.Log(hz / 440, 2)); }

        static Song PrepareSound(Song song, RenderOptions o, RenderResult r)
        {
            if (o.Library != null || o.Expression) song = CloseGaps(song, o.Expression);
            if (o.Expression) song = Expression.Apply(song);
            if (o.Library != null)
            {
                var used = new List<string>(); var kept = new List<string>();
                song = Library.Apply(song, used, kept, o.HarpsichordFile != null, o.LibraryFile != null ? Sf2Info.Load(o.LibraryFile) : null,
                    o.HarpsichordFile != null ? Sf2Info.Load(o.HarpsichordFile) : null,
                    o.HarpsichordFile != null ? Path.GetFileNameWithoutExtension(o.HarpsichordFile) : null, o.SonatinaPercussion,
                    o.ExtrasFile != null ? Sf2Info.Load(o.ExtrasFile) : null);
                r.UsesExtras = song.Tracks.Any(t => t.Bank == Library.ExtrasBank);
                // library samples carry real recorded vibrato: FluidSynth's added vibrato (8 Hz) would beat against it and sound sour
                foreach (var t in song.Tracks.Where(x => x.Bank >= Library.Bank)) t.Controls.RemoveAll(c => c.Cc == 1);
                AddReleaseTails(song);
                r.Notes.Add(o.Library + ": " + (used.Count == 0 ? "no parts" : string.Join("; ", used)));
                if (kept.Count > 0) r.Notes.Add("default sound kept: " + string.Join("; ", kept));
            }
            return song;
        }

        // ---------- one file ----------
        public static RenderResult RenderFile(string path, RenderOptions o, string fluid, string ffmpeg, string sf2)
        {
            var r = new RenderResult { Source = path };
            string baseName = SafeName(Path.GetFileNameWithoutExtension(path));
            r.Output = Path.Combine(o.OutDir, baseName + ".mp3");
            bool isMidi = Regex.IsMatch(path, @"\.(mid|midi)$", RegexOptions.IgnoreCase);
            ScoreResult res = null;
            string header = null;
            if (!isMidi)
            {
                try { res = ScoreScript.Parse(File.ReadAllText(path)); header = res.Cover; }
                catch (Exception ex) { r.Status = "FAILED"; r.Detail = ex.Message; return r; }
            }
            var coverWarnings = new List<string>();
            string cover = FindCover(path, header, o.Cover, coverWarnings);
            string sidecar = isMidi ? SidecarTitle(path) : null;
            double? gap = res != null ? res.Gap : null;
            if (!gap.HasValue && o.InnerMovements != null && o.InnerMovements.Contains(path)) gap = o.MovementGapSeconds;
            r.Album = res != null ? res.Album : null;
            // a score's percussion: header decides for that score
            if (res != null && res.Percussion != null && res.Percussion != o.Percussion) { o = o.Copy(); o.Percussion = res.Percussion; }
            bool hpsiPart = false, percPart = false, extrasPart = false;
            if (o.Library != null && (o.HarpsichordFile != null || o.SonatinaPercussion || o.ExtrasFile != null))
            {
                Song sg = null;
                if (res != null) sg = res.Song;
                else try { sg = MidiFile.Read(path); } catch { }
                hpsiPart = o.HarpsichordFile != null && Library.PlaysHarpsichord(sg);
                percPart = o.SonatinaPercussion && Percussion.Plays(sg);
                extrasPart = o.ExtrasFile != null && Library.PlaysExtras(sg);
            }
            bool ownChannels = false;
            if (res != null) ownChannels = SharesChannels(res.Song);
            else if (o.Library != null || o.Expression || o.Tuning.HasValue) try { ownChannels = SharesChannels(MidiFile.Read(path)); } catch { }
            r.Key = FileKey(o, sidecar, gap, r.Album, hpsiPart, percPart, extrasPart, ownChannels, res != null && res.TempoLineAfterRamp);
            if (!o.Force && File.Exists(r.Output) && File.GetLastWriteTimeUtc(r.Output) > File.GetLastWriteTimeUtc(path) && CoverKeyIn(r.Output) == CoverKey(cover) && RenderKeyIn(r.Output) == r.Key)
            { r.Status = "UP-TO-DATE"; r.Ok = true; return r; }
            if (o.DryRun)
            {
                if (res != null && !res.Ok) { r.Status = "SKIPPED"; r.Detail = res.Errors.Count + " error(s); first: " + res.Errors[0]; return r; }
                r.Status = "TO-DO"; r.Ok = true;
                var d = new List<string>();
                if (sidecar != null) d.Add("title: " + sidecar);
                if (gap.HasValue) d.Add("gap: " + gap.Value.ToString(CI) + " s");
                r.Detail = d.Count > 0 ? string.Join(", ", d) : null;
                return r;
            }
            r.Warnings.AddRange(coverWarnings);

            string title = sidecar ?? baseName, artist = o.Artist;
            string tmpDir = Path.Combine(Path.GetTempPath(), "abend-render-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmpDir);
            try
            {
                List<string> mids;
                if (isMidi && o.Library == null && !o.Expression && !o.Tuning.HasValue) mids = new List<string> { path };
                else if (isMidi) mids = WritePasses(Checked(Prepare(MidiFile.Read(path), o, r), o, sf2, r), tmpDir, "score");
                else
                {
                    if (!res.Ok)
                    {
                        r.Status = "SKIPPED";
                        r.Detail = res.Errors.Count + " error(s); first: " + res.Errors[0];
                        return r;
                    }
                    r.Warnings.AddRange(res.Warnings);
                    if (!string.IsNullOrWhiteSpace(res.Song.Title) && res.Song.Title != "Untitled") title = res.Song.Title;
                    // placeholders such as "(your name)" or "Test" do not count as a composer
                    string c = (res.Composer ?? "").Trim().Trim('(', ')').Trim();
                    if (c.Length > 0 && !Regex.IsMatch(c, @"^(your name|name|test|unknown|composer|anonymous)$", RegexOptions.IgnoreCase)) artist = c;
                    mids = WritePasses(Checked(Prepare(res.Song, o, r), o, sf2, r), tmpDir, "score");
                }
                return Render(mids, r, o, fluid, ffmpeg, sf2, title, artist, tmpDir, cover, gap ?? o.GapSeconds);
            }
            catch (Exception ex) { r.Status = "FAILED"; r.Detail = ex.Message; return r; }
            finally { try { Directory.Delete(tmpDir, true); } catch { } }
        }

        public static RenderResult RenderSong(Song song, string output, RenderOptions o, string composer)
        {
            string fluid, ffmpeg, sf2;
            var setup = CheckSetup(o, out fluid, out ffmpeg, out sf2);
            if (setup != null) throw new Exception(setup);
            var r = new RenderResult { Source = song.Title, Output = output };
            string tmpDir = Path.Combine(Path.GetTempPath(), "abend-render-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmpDir);
            try
            {
                var mids = WritePasses(Checked(Prepare(song, o, r), o, sf2, r), tmpDir, "song");

                return Render(mids, r, o, fluid, ffmpeg, sf2, song.Title, string.IsNullOrWhiteSpace(composer) ? "Abend Orchestra" : composer, tmpDir, null, o.GapSeconds);
            }
            finally { try { Directory.Delete(tmpDir, true); } catch { } }
        }

        // A synthesizer has 16 MIDI channels. Tracks on logical channels 16-31, 32-47 go into extra files that are rendered
        // separately and added up; all files share the piece's full length so the renders line up.
        internal static List<string> WritePasses(Song song, string dir, string name)
        {
            int passes = song.Tracks.Count == 0 ? 1 : song.Tracks.Max(t => t.Channel) / 16 + 1;
            if (passes == 1) { string one = Path.Combine(dir, name + ".mid"); MidiFile.Write(song, one, 3.0); return new List<string> { one }; }
            int end = song.EndTick();
            var files = new List<string>();
            for (int i = 0; i < passes; i++)
            {
                var part = song.Clone();
                part.Tracks = part.Tracks.Where(t => t.Channel / 16 == i).ToList();
                if (part.Tracks.Count == 0) continue;
                foreach (var t in part.Tracks) t.Channel %= 16;
                string f = Path.Combine(dir, name + i + ".mid");
                MidiFile.Write(part, f, 3.0, end);
                files.Add(f);
            }
            return files;
        }

        // adds up FluidSynth's 32-bit float WAVs sample by sample (padding the shorter ones with silence)
        static void MixFloatWavs(List<string> inputs, string output)
        {
            byte[] head = null; float[] sum = null;
            foreach (var f in inputs)
            {
                var b = File.ReadAllBytes(f);
                int pos = 12, data = -1, len = 0;
                while (pos + 8 <= b.Length)
                {
                    string id = Encoding.ASCII.GetString(b, pos, 4); int sz = BitConverter.ToInt32(b, pos + 4);
                    if (id == "data") { data = pos + 8; len = Math.Min(sz, b.Length - data); break; }
                    pos += 8 + sz + (sz & 1);
                }
                if (data < 0) throw new Exception("FluidSynth produced an unreadable WAV: " + f);
                if (head == null) { head = new byte[data]; Array.Copy(b, head, data); }
                int n = len / 4;
                if (sum == null || sum.Length < n) { var bigger = new float[n]; if (sum != null) Array.Copy(sum, bigger, sum.Length); sum = bigger; }
                for (int i = 0; i < n; i++) sum[i] += BitConverter.ToSingle(b, data + i * 4);
            }
            var outB = new byte[head.Length + sum.Length * 4];
            Array.Copy(head, outB, head.Length);
            Buffer.BlockCopy(sum, 0, outB, head.Length, sum.Length * 4);
            Array.Copy(BitConverter.GetBytes(outB.Length - 8), 0, outB, 4, 4);                // RIFF size
            Array.Copy(BitConverter.GetBytes(sum.Length * 4), 0, outB, head.Length - 4, 4);   // data size
            File.WriteAllBytes(output, outB);
        }

        static RenderResult Render(List<string> mids, RenderResult r, RenderOptions o, string fluid, string ffmpeg, string sf2, string title, string artist, string tmpDir, string cover, double gap)
        {
            string wav = Path.Combine(tmpDir, "raw.wav");
            // 1. synthesize (float output cannot clip; levels are set later)
            // a sample library is loaded next to the soundfont under its own bank, so parts it lacks keep their sound
            string load = "";
            if (o.Library != null && o.LibraryFile != null)
            {
                string cmd = Path.Combine(tmpDir, "load.txt");
                File.WriteAllText(cmd, "load " + Q(o.LibraryFile) + " 1 " + Library.Bank + "\n" +
                    (o.HarpsichordFile != null ? "load " + Q(o.HarpsichordFile) + " 1 " + Library.HarpsichordBank + "\n" : "") +
                    (r.UsesExtras && o.ExtrasFile != null ? "load " + Q(o.ExtrasFile) + " 1 " + Library.ExtrasBank + "\n" : ""));
                load = "-f " + Q(cmd) + " ";
            }
            // one FluidSynth pass per 16 channels (see Library.MaxChannels), added up afterwards
            string cached;
            if (o.WavCache != null && !o.AlbumMeasure && o.WavCache.TryGetValue(r.Source, out cached)) wav = cached;   // album pass 2: already synthesized
            else
            {
                var wavs = new List<string>();
                for (int i = 0; i < mids.Count; i++)
                {
                    string w = mids.Count == 1 ? wav : Path.Combine(tmpDir, "raw" + i + ".wav");
                    var fs = Run(fluid, string.Format("-ni -q -g 0.5 -r 44100 -O float -o synth.reverb.active=1 -o synth.chorus.active=0 {3}-F {0} {1} {2}", Q(w), Q(sf2), Q(mids[i]), load), 600);
                    if (!File.Exists(w) || new FileInfo(w).Length < 10000) { r.Status = "FAILED"; r.Detail = "FluidSynth produced no audio. " + Short(fs); return r; }
                    wavs.Add(w);
                }
                if (wavs.Count > 1) { MixFloatWavs(wavs, wav); r.Notes.Add("rendered in " + wavs.Count + " passes of 16 MIDI channels"); }
                if (o.WavCache != null)
                {
                    string keep = Path.Combine(o.WavCacheDir, o.WavCache.Count + ".wav");
                    File.Copy(wav, keep, true);
                    o.WavCache[r.Source] = keep;
                }
            }

            // trim silence at both ends, keeping a short natural tail
            string trim = "silenceremove=start_periods=1:start_threshold=-60dB,areverse,silenceremove=start_periods=1:start_threshold=-62dB:start_silence=0.3,areverse";
            // bass lift (low shelf) goes before the loudness measurement, so the track still lands on target
            if (o.BassDb != 0) trim += "," + BassFilter(o.BassDb);
            string target = string.Format(CI, "I={0}:TP={1}:LRA={2}", o.Lufs, PeakTarget(o), o.Lra);

            // 2. measure loudness
            var m = Run(ffmpeg, "-hide_banner -nostats -i " + Q(wav) + " -af \"" + trim + ",loudnorm=" + target + ":print_format=json\" -f null -", 600);
            var j1 = LastJson(m);
            if (j1 == null) { r.Status = "FAILED"; r.Detail = "ffmpeg could not measure the loudness. " + Short(m); return r; }

            // a very quiet render (e.g. one soft timpani stroke) measures as -inf: measure it again 30 dB louder; that boost
            // stays in the chain, so the normalisation below then works as usual
            double inI, inLra;
            if (!double.TryParse(J(j1, "input_i"), NumberStyles.Float, CI, out inI))
            {
                trim = "volume=30dB," + trim;   // before the silence trimming, which would otherwise remove all of it
                m = Run(ffmpeg, "-hide_banner -nostats -i " + Q(wav) + " -af \"" + trim + ",loudnorm=" + target + ":print_format=json\" -f null -", 600);
                j1 = LastJson(m);
                if (j1 == null || !double.TryParse(J(j1, "input_i"), NumberStyles.Float, CI, out inI)) { r.Status = "FAILED"; r.Detail = "the rendered audio is (almost) silent: are the parts very soft or outside the instruments' ranges?"; return r; }
            }
            if (!double.TryParse(J(j1, "input_lra"), NumberStyles.Float, CI, out inLra)) inLra = 0;

            // 2b. very wide soft/loud range (orchestral music): compress gently around the average level, then measure again
            bool gentle = o.Library != null || o.Expression;
            if (inLra > o.Lra + 2)
            {
                // library/expression renders: a gentler compressor (slow attack and release, lower ratio) so sudden tutti entries
                // are not hit-then-ducked; plain renders keep the original settings

                double ratio = Math.Min(gentle ? 3.0 : 4.0, 1.0 + (inLra - o.Lra) / 6.0);
                string comp = string.Format(CI, gentle ? "acompressor=threshold={0:0.0}dB:ratio={1:0.0#}:attack=150:release=1500:knee=8:detection=rms" : "acompressor=threshold={0:0.0}dB:ratio={1:0.0#}:attack=40:release=600:knee=6:detection=rms",
                    inI - 2, ratio);
                trim = trim + "," + comp;
                m = Run(ffmpeg, "-hide_banner -nostats -i " + Q(wav) + " -af \"" + trim + ",loudnorm=" + target + ":print_format=json\" -f null -", 600);
                j1 = LastJson(m);
                if (j1 == null) { r.Status = "FAILED"; r.Detail = "ffmpeg could not measure the loudness. " + Short(m); return r; }
                r.Compressed = true;
            }

            // 3. normalize (linear when possible), resample, pad, encode with tags
            string ln = string.Format(CI, "loudnorm={0}:measured_I={1}:measured_TP={2}:measured_LRA={3}:measured_thresh={4}:offset={5}:linear=true:print_format=json",
                target, J(j1, "input_i"), J(j1, "input_tp"), J(j1, "input_lra"), J(j1, "input_thresh"), J(j1, "target_offset"));
            string fixedI = null;
            if (gentle)
            {
                // loudnorm only stays linear when the gain fits under the true-peak target; synth renders have high peaks for
                // their loudness, so it fell back to its DYNAMIC mode every time and rode the gain up and down over a few
                // seconds (uneven volume). Library and expression renders use one fixed gain instead, with a peak limiter at
                // LimiterCeiling that only catches the peaks; one extra measurement corrects the gain for what it took off.
                // Since 2026-10-28 the limiter looks 50 ms ahead and lets go over 600 ms (it was 5/100: its gain wobbled audibly
                // in loud passages); latency=1 takes its look-ahead delay back out, so the music starts and ends where it did.
                double inNow, tpNow;
                double.TryParse(J(j1, "input_i"), NumberStyles.Float, CI, out inNow);
                if (!double.TryParse(J(j1, "input_tp"), NumberStyles.Float, CI, out tpNow)) tpNow = 0;
                double gain = o.Lufs - inNow, albumGain;
                bool forced = o.AlbumGains != null && o.AlbumGains.TryGetValue(r.Source, out albumGain);
                if (forced) gain = o.AlbumGains[r.Source];
                // (a slow compressor in front of the limiter for peaky pieces was tried: it made the level move more, not less)
                string lim = string.Format(CI, "alimiter=limit={0:0.0000}:attack=50:release=600:latency=1:level=false", Math.Pow(10, LimiterCeiling(o) / 20));
                for (int pass = 0; pass < 5; pass++)
                {
                    var mm = Run(ffmpeg, "-hide_banner -nostats -i " + Q(wav) + " -af \"" + trim + string.Format(CI, ",volume={0:0.00}dB,", gain) + lim + ",loudnorm=" + target + ":print_format=json\" -f null -", 600);
                    var jm = LastJson(mm); double got;
                    if (jm == null || !double.TryParse(J(jm, "input_i"), NumberStyles.Float, CI, out got)) break;
                    fixedI = got.ToString("0.00", CI);
                    if (forced || Math.Abs(o.Lufs - got) < 0.15 || pass == 4) break;   // an album gain is kept as it is (up to 5 passes: the slower limiter takes a little more each time)
                    gain += o.Lufs - got;
                }
                if (o.AlbumMeasure) { r.Gain = gain; r.Ok = true; r.Status = "MEASURED"; return r; }
                ln = string.Format(CI, "volume={0:0.00}dB,", gain) + lim;
                r.Notes.Add(string.Format(CI, "loudness: fixed gain {0:+0.0;-0.0} dB, peaks limited by up to {1:0.0} dB, range {2} LU{3}", gain,
                    Math.Max(0, tpNow + gain - LimiterCeiling(o)), J(j1, "input_lra"), r.Compressed ? " after gentle compression" : ""));
            }
            string af = trim + "," + ln + ",aresample=44100,afade=t=in:d=0.02,apad=pad_dur=" + gap.ToString(CI);
            string tmpMp3 = Path.Combine(tmpDir, "out.mp3");
            // cover art: a second input that becomes the ID3v2.3 front cover (at most 1400 px); the audio is mapped and filtered exactly as without it
            string coverIn = "", coverOut = "";
            if (cover != null)
            {
                bool png = cover.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                coverIn = " -i " + Q(cover) + " -map 0:a -map 1:v:0";
                coverOut = " -vf \"scale=w='min(1400\\,iw)':h='min(1400\\,ih)':force_original_aspect_ratio=decrease\"" +
                    (png ? " -c:v png" : " -c:v mjpeg -q:v 2 -pix_fmt yuvj420p") +
                    " -disposition:v:0 attached_pic -metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" -id3v2_version 3 -metadata " + CoverTag + "=" + CoverKey(cover);
            }
            string key = r.Key ?? RenderKey(o), keyTag = key.Length > 0 ? " -metadata " + RenderTag + "=" + key : "";
            var e = Run(ffmpeg, "-hide_banner -nostats -y -i " + Q(wav) + coverIn + " -af \"" + af + "\" -c:a libmp3lame -b:a " + o.Bitrate + "k" + coverOut + keyTag +
                " -metadata title=" + Q(title) + " -metadata artist=" + Q(artist) + (string.IsNullOrEmpty(r.Album ?? o.Album) ? "" : " -metadata album=" + Q(r.Album ?? o.Album)) + " -metadata comment=" + Q("Made with Abend Orchestra") + " " + Q(tmpMp3), 600);
            if (!File.Exists(tmpMp3) || new FileInfo(tmpMp3).Length < 1000) { r.Status = "FAILED"; r.Detail = "ffmpeg could not encode the MP3. " + Short(e); return r; }
            var j2 = LastJson(e);
            File.Copy(tmpMp3, r.Output, true);

            r.Seconds = DurationOf(e);
            if (cover != null)
            {
                // with a cover, ffmpeg's last progress line counts the image stream: read the length from the MP3 instead
                var dm = Regex.Match(Run(ffmpeg, "-hide_banner -i " + Q(tmpMp3), 60), @"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)");
                if (dm.Success) r.Seconds = int.Parse(dm.Groups[1].Value) * 3600 + int.Parse(dm.Groups[2].Value) * 60 + double.Parse(dm.Groups[3].Value, CI);
            }
            string mode = j2 != null ? J(j2, "normalization_type") : "";
            if (!gentle) r.Notes.Add("loudness: " + (mode == "" ? "?" : mode) + " normalisation, range " + J(j1, "input_lra") + " LU" + (r.Compressed ? " after compression" : ""));
            r.Ok = true; r.Status = "OK";
            r.Detail = string.Format(CI, "{0}  {1}:{2:00}  {3} LUFS{4}", Path.GetFileName(r.Output), (int)r.Seconds / 60, (int)r.Seconds % 60,
                fixedI ?? (j2 != null ? J(j2, "output_i") : "?"), (r.Compressed || mode == "dynamic" ? "  (soft/loud range narrowed for radio)" : "") + (cover != null ? "  cover: " + Path.GetFileName(cover) : "") + (o.Library != null ? "  " + o.Library : "") + (o.Expression ? "  expression" : ""));
            return r;
        }

        // ---------- helpers ----------
        public static string BassFilter(double db) { return string.Format(CI, "bass=g={0:0.##}:f=100:w=0.7", db); }

        static string Q(string s) { return "\"" + s.Replace("\"", "'") + "\""; }
        static string Short(string s) { s = (s ?? "").Trim(); var lines = s.Split('\n'); return lines.Length == 0 ? "" : lines[lines.Length - 1].Trim(); }

        static string Run(string exe, string args, int timeoutSec)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                var err = new StringBuilder(); var outp = new StringBuilder();
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (outp) outp.AppendLine(e.Data); };
                p.BeginErrorReadLine(); p.BeginOutputReadLine();
                if (!p.WaitForExit(timeoutSec * 1000)) { try { p.Kill(); } catch { } throw new Exception(Path.GetFileName(exe) + " took longer than " + timeoutSec + " s and was stopped."); }
                p.WaitForExit();
                return outp.ToString() + err.ToString();
            }
        }

        static string LastJson(string s)
        {
            int end = s.LastIndexOf('}'); if (end < 0) return null;
            int start = s.LastIndexOf('{', end); if (start < 0) return null;
            return s.Substring(start, end - start + 1);
        }
        static string J(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "0";
        }
        static double DurationOf(string ffmpegLog)
        {
            var ms = Regex.Matches(ffmpegLog, @"time=(\d+):(\d+):(\d+(?:\.\d+)?)");
            if (ms.Count == 0) return 0;
            var m = ms[ms.Count - 1];
            return int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, CI);
        }
    }

    // tiny per-user settings file (remembers where the tools and soundfonts folder is)
    public static class Settings
    {
        static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AbendComposer", "settings.txt"); } }
        public static string Get(string key)
        {
            try { foreach (var l in File.ReadAllLines(FilePath)) if (l.StartsWith(key + "=")) return l.Substring(key.Length + 1); } catch { }
            return null;
        }
        public static void Set(string key, string value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath).Where(l => !l.StartsWith(key + "=")).ToList() : new List<string>();
                lines.Add(key + "=" + value);
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }
}
