// abendc: command-line converter for score scripts.
using System;
using System.IO;
using System.Linq;

namespace Abend
{
    public static class Cli
    {
        static int RenderCmd(string[] a)
        {
            if (a.Length == 0) { Console.Error.WriteLine("Usage: abendc render <folder or file> [--out dir] [--soundfont f.sf2] [--lufs -14] [--lra 15] [--bass 0] [--bitrate 192] [--cover image] [--library sonatina] [--expression] [--harpsichord f.sf2] [--percussion generaluser] [--lint] [--force] [--dry-run]"); return 2; }
            var o = new RenderOptions();
            string input = null;   // the folder or file; the options may come before or after it
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            bool gapGiven = false;
            try
            {
                for (int i = 0; i < a.Length; i++)
                {
                    if (input == null && !a[i].StartsWith("-")) { input = a[i]; continue; }
                    switch (a[i])
                    {
                        case "--out": o.OutDir = a[++i]; break;
                        case "--soundfont": o.SoundFont = a[++i]; break;
                        case "--tools": o.ToolsDir = a[++i]; break;
                        case "--lufs": o.Lufs = double.Parse(a[++i], ci); break;
                        case "--lra": o.Lra = double.Parse(a[++i], ci); break;
                        case "--bass": o.BassDb = double.Parse(a[++i], ci); if (o.BassDb < -12 || o.BassDb > 12) { Console.Error.WriteLine("--bass takes -12 to 12 (dB)"); return 2; } break;
                        case "--bitrate": o.Bitrate = int.Parse(a[++i]); break;
                        case "--gap": o.GapSeconds = double.Parse(a[++i], ci); gapGiven = true; break;
                        case "--album": o.Album = a[++i]; o.AlbumGiven = true; break;
                        case "--artist": o.Artist = a[++i]; break;
                        case "--cover": o.Cover = a[++i]; if (!File.Exists(o.Cover)) { Console.Error.WriteLine("Cover image not found: " + o.Cover); return 2; } break;
                        case "--force": o.Force = true; break;
                        case "--dry-run": o.DryRun = true; break;
                        case "--tuning": o.Tuning = double.Parse(a[++i], ci); if (o.Tuning < 415 || o.Tuning > 466) { Console.Error.WriteLine("--tuning is the frequency of A4 in Hz, 415 to 466"); return 2; } break;
                        case "--library": o.Library = a[++i].ToLowerInvariant(); if (o.Library != "sonatina") { Console.Error.WriteLine("Known libraries: sonatina"); return 2; } break;
                        case "--expression": o.Expression = true; break;
                        case "--lint": ScoreScript.Lint = true; break;
                        case "--harpsichord": o.Harpsichord = a[++i]; break;
                        case "--percussion": o.Percussion = a[++i].ToLowerInvariant(); if (o.Percussion != "sonatina" && o.Percussion != "generaluser") { Console.Error.WriteLine("Known percussion: sonatina, generaluser"); return 2; } break;
                        default: Console.Error.WriteLine(a[i].StartsWith("-") ? "Unknown option: " + a[i] : "Give one folder or file to render (" + input + ", then " + a[i] + ")"); return 2;
                    }
                }
            }
            catch (Exception) { Console.Error.WriteLine("Missing or bad value after " + a[a.Length - 1]); return 2; }
            if (input == null) { Console.Error.WriteLine("Give a folder or a file to render, e.g.: abendc render examples --out out"); return 2; }
            // library renders pause about 3 s between songs and 2 s between the movements of an album;
            // a score's gap: header or --gap still decides
            if (!gapGiven && o.Library != null) { o.GapSeconds = 3.0; o.MovementGapSeconds = 2.0; }
            try
            {
                var results = AudioRender.RenderPath(input, o, Console.WriteLine);
                if (o.DryRun)
                {
                    Console.WriteLine();
                    Console.WriteLine(results.Count(r => r.Status == "TO-DO") + " would be rendered, " + results.Count(r => r.Status == "UP-TO-DATE") + " already up to date, " + results.Count(r => !r.Ok) + " with problems. Nothing was rendered.");
                    return results.Any(r => !r.Ok) ? 1 : 0;
                }
                int ok = results.Count(r => r.Status == "OK"), same = results.Count(r => r.Status == "UP-TO-DATE"), bad = results.Count(r => !r.Ok);
                Console.WriteLine();
                Console.WriteLine(ok + " rendered, " + same + " already up to date, " + bad + " skipped or failed.");
                return bad > 0 ? 1 : 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
        }

        public const string Version = "1.0.0";

        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length > 0 && (args[0] == "--version" || args[0] == "-v")) { Console.WriteLine("Abend Orchestra " + Version + " (abendc)"); return 0; }
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help" || args[0] == "/?")
            {
                Console.WriteLine(
@"abendc — turns an Abend score script into MIDI

  abendc song.abscore                 check it and write song.mid next to it
  abendc song.abscore -o out.mid      choose the MIDI file name
  abendc song.abscore --abend p.abend also write an Abend Orchestra project
  abendc song.abscore --check         only check; write nothing
  abendc song.abscore --lint          also give style hints (e.g. a tune made mostly of scale runs)
  abendc song.abscore --tuning 444    concert pitch A4 in Hz (415-466; also the score header tuning: 444)
  abendc - < song.abscore             read the script from standard input
  abendc --guide                      print the instructions for AI models
  abendc --version                    print the version

  abendc render <folder or file> [--out dir] [--soundfont file.sf2] [--tools dir]
                [--lufs -14] [--lra 15] [--bass 0] [--bitrate 192] [--gap 1.5] [--artist name] [--album name] [--cover image]
                [--library sonatina] [--expression] [--harpsichord f.sf2] [--percussion generaluser] [--tuning 444] [--lint] [--force] [--dry-run]
      Renders every .abscore and .mid in the folder to a loudness-matched MP3
      using FluidSynth and ffmpeg. Scripts with errors are skipped and listed.
      Up-to-date MP3s are skipped unless --force. A log is kept in render-log.txt.
      --dry-run only lists which files would be rendered; nothing is written.
      --gap is the silence added after each track: 1.5 s, or 3 s with --library
      (2 s after each movement of an album: header work but the last; with --album, after
      each file of a folder but the last).
      A .mid is titled after its file name; Song.title.txt next to Song.mid
      gives it another title (its first line) without renaming the file.
      --bass 4 lifts the bass below about 100 Hz by 4 dB (for subwoofers) before
      the loudness is matched; 0 (the default) leaves the sound unchanged.
      Cover art (PNG or JPEG) is embedded when found, first match wins: a
      'cover: art.png' line in the score script (relative to the script), an image
      named like the file (Song.png next to Song.abscore or Song.mid), cover.png or
      cover.jpg in the same folder (covers all movements of a work), then --cover.
      A new, changed or removed cover makes the track render again.
      --library sonatina plays every part it can with the Sonatina Symphonic
      Orchestra samples (soundfonts/Sonatina), and harpsichord parts with the VCSL
      Flemish harpsichord, with key-up noises (soundfonts/VCSL-Flemish-Harpsichord;
      the VCSL English one in soundfonts/VCSL-Harpsichord if that is missing);
      other parts keep their usual sound. The MP3 remembers its render settings,
      so changing them re-renders the track without --force.
      --expression adds swells on held notes, smooth legato, gentle vibrato and
      small human timing and loudness variations. Both are off by default.
      --harpsichord file.sf2 (with --library) plays harpsichord parts from that
      soundfont instead: preset 0 is the main stop; preset 1, if present, a 4'
      stop added an octave up; presets 10 and 11 key-up noises. A bare file name
      is looked for under soundfonts/, e.g. --harpsichord
      VCSL-English-Harpsichord.sf2.
      With --library, drums and timpani play from the Sonatina orchestra's
      percussion recordings: orchestral bass drum, snare (rolls
      as real rolls), clash cymbals, triangle, tambourine, castanets, and timpani
      at their true pitch. Hi-hat, toms and ride keep the default kit.
      --percussion generaluser keeps the default soundfont's drum kit and timpani
      for the whole run; a score's header line  percussion: generaluser  does the
      same for that score (and  percussion: sonatina  asks for the orchestral set).
      --lint adds style hints to each score's warnings (off by default).
      Abend Radio renders with: --lufs -14 --bass 5 --library sonatina --expression

  abendc --example                    print an example score script

Exit code 0 = built without errors, 1 = errors (listed with line numbers), 2 = bad usage.");
                return args.Length == 0 ? 2 : 0;
            }
            if (args[0] == "render") return RenderCmd(args.Skip(1).ToArray());
            if (args[0] == "--guide") { Console.WriteLine(AiGuide.Text); return 0; }
            if (args[0] == "--example") { Console.Write(AiGuide.Example); return 0; }

            // the options may come before or after the score file
            string input = null, midiOut = null, abendOut = null; bool check = false; double? tuning = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (input == null && (args[i] == "-" || !args[i].StartsWith("-"))) input = args[i];
                else if (args[i] == "-o" && i + 1 < args.Length) midiOut = args[++i];
                else if (args[i] == "--abend" && i + 1 < args.Length) abendOut = args[++i];
                else if (args[i] == "--check") check = true;
                else if (args[i] == "--lint") ScoreScript.Lint = true;
                else if (args[i] == "--tuning" && i + 1 < args.Length)
                {
                    double tv; if (!double.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out tv) || tv < 415 || tv > 466) { Console.Error.WriteLine("--tuning is the frequency of A4 in Hz, 415 to 466"); return 2; }
                    tuning = tv;
                }
                else if (!args[i].StartsWith("-")) { Console.Error.WriteLine("Give one score file at a time (" + input + ", then " + args[i] + "); to render a folder, use: abendc render <folder>"); return 2; }
                else { Console.Error.WriteLine("Unknown option: " + args[i]); return 2; }
            }
            if (input == null) { Console.Error.WriteLine("No score file given, e.g.: abendc song.abscore --check"); return 2; }
            string text;
            try { text = input == "-" ? Console.In.ReadToEnd() : File.ReadAllText(input); }
            catch (Exception ex) { Console.Error.WriteLine("Cannot read " + input + ": " + ex.Message); return 2; }

            var r = ScoreScript.Parse(text);
            foreach (var w in r.Warnings) Console.WriteLine("warning: " + w);
            foreach (var e in r.Errors) Console.WriteLine("error: " + e);
            if (!r.Ok) { Console.WriteLine(r.Errors.Count + " error(s); nothing written."); return 1; }
            if (tuning.HasValue) r.Song.TuningHz = tuning.Value;
            Console.WriteLine("ok: " + r.Summary());
            if (r.Song.TuningHz != 440) Console.WriteLine(AudioRender.TuningText(r.Song.TuningHz));
            if (check) return 0;
            if (midiOut == null) midiOut = input == "-" ? "score.mid" : Path.ChangeExtension(input, ".mid");
            MidiFile.Write(r.Song, midiOut);
            Console.WriteLine("wrote " + midiOut);
            if (abendOut != null) { r.Song.Save(abendOut); Console.WriteLine("wrote " + abendOut); }
            return 0;
        }
    }
}
