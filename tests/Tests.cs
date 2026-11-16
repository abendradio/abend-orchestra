using System; using System.IO; using System.Linq;
namespace Abend { public static class Tests { public static int Main() {
  int fails = 0;
  Action<bool,string> ok = (c, msg) => { Console.WriteLine((c ? "PASS " : "FAIL ") + msg); if (!c) fails++; };
  foreach (var f in Directory.Exists("midi") ? Directory.GetFiles("midi", "*.mid") : new string[0]) {
    var s = MidiFile.Read(f);
    ok(s.Tracks.Count >= 10 && s.Tracks.Sum(t => t.Notes.Count) > 1000, Path.GetFileName(f) + ": " + s.Tracks.Count + " tracks, " + s.Tracks.Sum(t => t.Notes.Count) + " notes, " + s.Tempos.Count + " tempo events, " + (s.TickToMs(s.EndTick())/1000).ToString("0") + " s, meter " + s.Num + "/" + s.Den + ", programs " + string.Join(",", s.Tracks.Select(t => t.Program)));
    MidiFile.Write(s, Path.Combine(Path.GetTempPath(), "abend-rt.mid")); var r = MidiFile.Read(Path.Combine(Path.GetTempPath(), "abend-rt.mid"));
    ok(r.Tracks.Sum(t => t.Notes.Count) == s.Tracks.Sum(t => t.Notes.Count) && Math.Abs(r.TickToMs(r.EndTick()) - s.TickToMs(s.EndTick())) < 50, "  MIDI round trip keeps notes and timing");
    s.Save(Path.Combine(Path.GetTempPath(), "abend-rt.abend")); var a = Song.Load(Path.Combine(Path.GetTempPath(), "abend-rt.abend"));
    ok(a.Tracks.Sum(t => t.Notes.Count) == s.Tracks.Sum(t => t.Notes.Count) && a.Tracks[0].Name == s.Tracks[0].Name && a.Tempos.Count == s.Tempos.Count, "  project save/load round trip (first track: " + a.Tracks[0].Name + ")");
  }
  var song = new Song();
  var n = Engine.Notation(song, 3, @"\mp (d4/2 bes4/2 | a4/4. g4/8 f4/4 e4/4 | cis4/2 d4/2) |", 0);
  ok(n.Count == 8 && n[0].Start == 2 * 1920 && n[0].Pitch == 62 && n[1].Pitch == 70, "notation: motto at bar 3 -> " + n.Count + " notes, first at tick " + n[0].Start + ", vel " + n[0].Vel);
  try { Engine.Notation(song, 1, "c4/4 d4/4 e4/4 | f4/1 |", 0); ok(false, "bar check error expected"); }
  catch (Exception ex) { ok(ex.Message.Contains("bar check"), "notation error message: " + Help.Friendly(ex.Message)); }
  var h = Engine.Harmony(song, 1, "Dm/1 | Gm/2 A7/2 | Dm/1 |", 3, 55, 72, false, "8: c c c c", 70);
  ok(h.Count == 3 * 8 * 3, "harmony: " + h.Count + " notes; bar-2 beat-3 chord " + string.Join(" ", h.Where(x => x.Start == 1920 + 960).Select(x => GM.NoteName(x.Pitch))));
  var b = Engine.Harmony(song, 1, "C/1 | G7/B/1 |", 1, 36, 52, true, "4: B r B r", 70);
  ok(b.Count == 4 && GM.NoteName(b[2].Pitch).StartsWith("B"), "bass pattern follows slash bass: " + string.Join(" ", b.Select(x => GM.NoteName(x.Pitch))));
  int m; ok(Engine.TryParseNoteName("F#3", out m) && m == 54 && Engine.TryParseNoteName("bes2", out m) && m == 46, "note-name parsing");
  // ---------- score scripts ----------
  Func<string, string, ScoreResult> score = (head, body) => ScoreScript.Parse("title: T\ntempo: 120\nmeter: 4/4\n" + head + body);
  Func<ScoreResult, string, bool> warned = (r, s) => r.Warnings.Any(x => x.Contains(s));
  string mel = "track Melody | instrument: flute\n\\mf c5/2 e5/2 | g5/2 e5/2 | d5/2 b4/2 | c5/1 |\n";
  string acc = "chords Strings | instrument: strings | voices: 3 | range: G3-E4\n\\p C/1 | C | G | C |\nchords Bass | instrument: cello | voices: 1 | range: C2-C3 | bass: yes\n\\p C/1 | C | G | C |\n";
  var clean = score("", mel + acc);
  ok(clean.Ok && clean.Warnings.Count == 0 && clean.Song.Tracks.Count == 3, "score: clean piece builds without warnings (" + clean.Summary() + ")" + string.Join(" / ", clean.Warnings));
  var exs = ScoreScript.Parse(AiGuide.Example);
  ok(exs.Ok && exs.Warnings.Count == 0, "score: guide example builds without warnings " + string.Join(" / ", exs.Warnings));

  var bars = score("", "track Melody | instrument: flute\n\\mf c5/2 | d5/1 | e5/2 |\n");
  ok(!bars.Ok && bars.Errors.Any(e => e.Contains("bar 1 adds up to")) && bars.Errors.Any(e => e.Contains("bar 3 adds up to")), "score: every wrong bar is listed: " + string.Join(" / ", bars.Errors));
  var six = ScoreScript.Parse("title: T\ntempo: 120\nmeter: 6/8\ntrack Melody | instrument: flute\n\\mf c5/4. | d5/4. | e5/4. | f5/4. | g5/4. | a5/4. |\n");
  ok(!six.Ok && six.Errors.Any(e => e.Contains("hint") && e.Contains("dotted quarters")), "score: 6/8 half-bar hint: " + six.Errors.LastOrDefault());

  string glued = ("title: T\ntempo: 120\nmeter: 4/4\n" + mel + acc).Replace("\n", "");
  var rep = ScoreScript.Parse(glued);
  ok(rep.Ok && rep.RepairedText != null && rep.Song.Tracks.Sum(t => t.Notes.Count) == clean.Song.Tracks.Sum(t => t.Notes.Count), "score: lost line breaks are repaired (" + rep.Summary() + ")");

  var touch = score("", "chords Strings | instrument: strings\n\\p C/1 | G |\\mf C | C |\n");
  ok(touch.Ok && touch.Song.Tracks[0].Notes.Count == 12, "score: a bar line touching a dynamic in a chord block (|\\mf C) " + string.Join(" / ", touch.Errors));

  // dynamics next to chord-block repeats (Baroque composer's repro, 3/4)
  Func<string, ScoreResult> waltz = body => ScoreScript.Parse("title: T\ntempo: 120\nmeter: 3/4\nchords H | instrument: harpsichord | voices: 3\n" + body);
  var before = waltz("\\mf Gm/2. | \\f [ D | ]*2 G |\n");
  ok(before.Ok && before.Song.Tracks[0].Notes.Count == 4 * 3, "score: dynamic right before a chord repeat " + string.Join(" / ", before.Errors));
  var inside = waltz("\\mf Gm/2. | [ \\f D/2. | \\p G | ]*2\n");
  ok(inside.Ok && inside.Song.Tracks[0].Notes.Count == 5 * 3 && inside.Song.Tracks[0].Notes.Last().Vel < inside.Song.Tracks[0].Notes[4].Vel, "score: dynamics inside a chord repeat " + string.Join(" / ", inside.Errors));

  var tempo = score("tempo @2: 90\nrit @3-4: 60\nfermata @4.1: 2\n", mel + acc);
  ok(tempo.Ok && tempo.Song.Tempos.Any(t => t.Tick == 1920 && t.Bpm == 90) && tempo.Song.Tempos.Any(t => t.Tick == 3 * 1920 && t.Bpm < 60), "score: tempo, rit and fermata make " + tempo.Song.Tempos.Count + " tempo events");
  var badTempo = score("tempo @x: 90\nrit @3: 60\n", mel + acc);
  ok(badTempo.Errors.Count == 2 && badTempo.Errors[1].Contains("gradual"), "score: bad tempo lines are explained: " + string.Join(" / ", badTempo.Errors));

  // musical-sense warnings
  var late = score("", mel.Replace("c5/1 |", "c5/1 | e5/1 |") + acc);
  ok(late.Ok && warned(late, "Melody runs to bar 5 but Strings and Bass end at bar 4"), "warn: track runs past the others: " + late.Warnings.FirstOrDefault());
  var early = score("", "track Melody | instrument: flute\n\\mf c5/2 e5/2 | g5/2 e5/2 |\n" + acc);
  ok(warned(early, "Melody stops at bar 2, 2 bars before"), "warn: track stops early: " + early.Warnings.FirstOrDefault());
  var entry = score("", "track Melody | instrument: flute\n@3 \\mf d5/1 | c5/1 |\n" + acc.Replace("C/1 | C | G | C |", "C/1 | C | G | C | C | C |"));
  ok(entry.Ok && entry.Warnings.Count == 0, "warn: a part that drops in and out is not flagged " + string.Join(" / ", entry.Warnings));
  var startLate = score("", "track Melody | instrument: flute\n@2 \\mf r/1 | c5/1 | c5/1 |\n" + acc);
  ok(warned(startLate, "marked @2 has its first note in bar 3"), "warn: passage starts later than its @bar: " + startLate.Warnings.FirstOrDefault());
  var clash = score("", mel.Replace("c5/2 e5/2 |", "b4/1 |") + acc);
  ok(warned(clash, "B4 against C in Strings in bar 1"), "warn: melody clashes with a held chord: " + clash.Warnings.FirstOrDefault());
  var maj7 = score("", mel.Replace("c5/2 e5/2 |", "b4/1 |") + acc.Replace("voices: 3", "voices: 4").Replace("\\p C/1", "\\p Cmaj7/1"));
  ok(!warned(maj7, "clash"), "warn: a melody note that belongs to the chord (B over Cmaj7) is fine " + string.Join(" / ", maj7.Warnings));
  var ending = score("", mel.Replace("c5/1 |", "b4/1 |") + acc.Replace("G | C |", "C | G |"));
  ok(warned(ending, "starts with C in the bass but ends with G"), "warn: does not end on the home chord: " + ending.Warnings.LastOrDefault());
  string up = "c5/8 d5/8 e5/8 f5/8 g5/8 a5/8 b5/8 c6/8 |", down = "c6/8 b5/8 a5/8 g5/8 f5/8 e5/8 d5/8 c5/8 |", leaps = "c5/8 g5/8 e5/8 c6/8 a5/8 e5/8 g5/8 c5/8 |";
  ScoreScript.Lint = true;
  var scales = score("", "track Melody | instrument: flute\n\\mf " + up + down + up + down + up + down + "\n");
  ok(scales.Ok && warned(scales, "Melody: 100% of its notes are in scale runs") && warned(scales, "bar 1 (8 notes)"), "warn: a tune made of scale runs: " + scales.Warnings.LastOrDefault());
  var tune = score("", "track Melody | instrument: flute\n\\mf " + leaps + up + leaps + leaps + leaps + leaps + "\n");
  ok(tune.Ok && !warned(tune, "scale runs"), "warn: one run in a leaping tune is fine " + string.Join(" / ", tune.Warnings));
  var arps = score("", "chords Harp | instrument: harp | voices: 1 | range: C5-C6 | pattern: 8: 0 0 0 0 0 0 0 0\n\\mf [ C/1 | D | E | F | G | A | ]*1\n");
  ok(arps.Ok && !warned(arps, "scale runs"), "warn: chord and pattern blocks are not checked for scale runs " + string.Join(" / ", arps.Warnings));
  ScoreScript.Lint = false;
  ok(score("", "track Melody | instrument: flute\n\\mf " + up + down + up + down + up + down + "\n").Warnings.Count == 0, "lint: scale-run hints only with --lint");
  int em, eu;
  Func<string, int> pitchOf = sp => Sym.Theory.TryParsePitch(sp, out em, out eu) && eu == sp.Length ? em : -1;
  ok(pitchOf("his4") == 72 && pitchOf("his3") == 60 && pitchOf("h4") == 71 && pitchOf("bis4") == 72 && pitchOf("eis4") == 65 && pitchOf("ces5") == 71 && pitchOf("fes4") == 64 && pitchOf("hes4") == 70 && pitchOf("c5") == 72,
     "pitches: his/h (German B), bis, eis, ces, fes are enharmonic spellings: " + string.Join(",", new[] { "his4", "h4", "eis4", "ces5", "fes4" }.Select(x => pitchOf(x))));
  var hisScore = score("", "track M | instrument: flute\n\\mf his4/4 eis4/4 ces5/4 fes4/4 |\n");
  ok(hisScore.Ok && hisScore.Song.Tracks[0].Notes.Select(x => x.Pitch).SequenceEqual(new[] { 72, 65, 71, 64 }), "score: his4 eis4 ces5 fes4 play C5 F4 B4 E4 " + string.Join(" / ", hisScore.Errors));
  bool okName;
  ok(ScoreScript.ParseInstrument("bass clarinet", out okName) == 71 && okName && ScoreScript.ParseInstrument("Contrabassoon", out okName) == 70 && okName
     && ScoreScript.ParseInstrument("bass trombone", out okName) == 57 && okName && ScoreScript.ParseInstrument("chimes", out okName) == 14 && okName, "instrument names: bass clarinet, contrabassoon, bass trombone, chimes");
  var bcl = score("", "track Low | instrument: bass clarinet\n\\mf d2/1 | f2/1 |\n");
  ok(bcl.Ok && !warned(bcl, "outside the instrument's comfortable range") && !warned(bcl, "unknown instrument"), "bass clarinet: low notes in range, name known " + string.Join(" / ", bcl.Warnings));
  // entries on a new dynamic mark keep their written level even when the human timing moves them just before the mark
  bool stepsOk = true; string stepInfo = "";
  foreach (var ttl in new[] { "Dyn org", "Dyn rev", "A", "B", "C", "Steps 1", "Steps 2", "Steps 3" })
  {
    var dsc = ScoreScript.Parse("title: " + ttl + "\ntempo: 60\nmeter: 4/4\ntrack T | instrument: clarinet\n\\pp <c4 e4 g4>/1 | r/1 |\n\\p <c4 e4 g4>/1 | r/1 |\n\\mf <c4 e4 g4>/1 | r/1 |\n\\f <c4 e4 g4>/1 | r/1 |\n");
    var ex = Expression.Apply(dsc.Song);
    var tops = ex.Tracks[0].Notes.GroupBy(q9 => (int)Math.Round(q9.Start / 3840.0)).OrderBy(g => g.Key).Select(g => g.Average(q9 => q9.Vel)).ToList();
    for (int i = 1; i < tops.Count; i++) if (tops[i] <= tops[i - 1] + 6) { stepsOk = false; stepInfo = ttl + ": " + string.Join(", ", tops.Select(v => v.ToString("0"))); }
  }
  ok(stepsOk, "expression: pp < p < mf < f even when an entry is moved just before its mark " + stepInfo);
  var keyed = score("key: D major\n", mel + acc);
  ok(warned(keyed, "ends with C in the bass, but its key is D major"), "warn: ending checked against the key: header: " + keyed.Warnings.LastOrDefault());
  ok(ScoreScript.KeyTonic("E-flat major") == 3 && ScoreScript.KeyTonic("Eb") == 3 && ScoreScript.KeyTonic("F# minor") == 6 && ScoreScript.KeyTonic("bes major") == 10 &&
     ScoreScript.KeyTonic("B minor") == 11 && ScoreScript.KeyTonic("As") == 8 && ScoreScript.KeyTonic("C") == 0 && ScoreScript.KeyTonic("nonsense") == -1, "key names: E-flat, Eb, F#, bes, B, As, C");
  var twice = score("", mel + acc + "chords Strings | voices: 3\n\\p F/1 | F | C | C |\n");
  ok(warned(twice, "start it with @5"), "warn: second block without @ plays on top of the first: " + twice.Warnings.FirstOrDefault());
  var mixed = score("", "track Piano | instrument: piano\n\\mf c5/2 e5/2 | g5/2 e5/2 | d5/2 b4/2 | c5/1 |\nchords Piano | voices: 3 | range: G3-E4\n\\p C/1 | C | G | C |\n");
  ok(mixed.Ok && mixed.Warnings.Count == 0, "warn: a track block and a chords block of one name may overlap " + string.Join(" / ", mixed.Warnings));

  // the example scores keep building (found from the repo root, wherever the tests run)
  string exDir = null; for (var d = new DirectoryInfo(Environment.CurrentDirectory); d != null && exDir == null; d = d.Parent) if (Directory.Exists(Path.Combine(d.FullName, "examples"))) exDir = Path.Combine(d.FullName, "examples");
  if (exDir == null) Console.WriteLine("SKIP example scores: examples folder not found");
  else foreach (var p in Directory.GetFiles(exDir, "*.abscore", SearchOption.AllDirectories))
  {
    var r = ScoreScript.Parse(File.ReadAllText(p));
    ok(r.Ok, "example score " + Path.GetFileName(p) + ": " + (r.Ok ? r.Summary() : r.Errors[0]) + ", " + r.Warnings.Count + " warning(s)");
  }

  ok(new RenderOptions().BassDb == 0 && AudioRender.BassFilter(4) == "bass=g=4:f=100:w=0.7" && AudioRender.BassFilter(2.5) == "bass=g=2.5:f=100:w=0.7", "render: bass lift off by default; filter " + AudioRender.BassFilter(4));

  // sample library mapping and expression
  ok(Library.IsSection("Violins") && Library.IsSection("Cello section") && Library.IsSection("Horns 1-2") == false && Library.IsSection("Violins II") && Library.IsSection("Celli") &&
     !Library.IsSection("Cello") && !Library.IsSection("Solo Cello") && !Library.IsSection("Bass") && !Library.IsSection("Oboe"), "library: section or soloist from the track name");
  {
    Func<string, string> secMap = head => { var u = new System.Collections.Generic.List<string>(); Library.Apply(ScoreScript.Parse("title: T\ntempo: 120\nmeter: 4/4\n" + head + "\n\\mf c5/1 |\n").Song, u, new System.Collections.Generic.List<string>()); return u[0]; };
    ok(secMap("track Oboe | instrument: oboes") == "Oboe = oboes" && secMap("track Oboes | instrument: oboe") == "Oboes = oboes" &&
       secMap("track Oboe | instrument: oboe") == "Oboe = solo oboe" && secMap("track Solo Oboe | instrument: oboes") == "Solo Oboe = solo oboe" &&
       secMap("track Oboe") == "Oboe = solo oboe", "library: instrument: oboes picks the section, solo in the name the soloist");
  }
  var lib = new Song { Title = "Lib" };
  Func<string, int, int, int, int, Track> trk = (nm, prog, ch, lo, hi) => { var t = new Track { Name = nm, Program = prog, Channel = ch };
    for (int k = 0; k < 4; k++) t.Notes.Add(new NoteEv { Start = k * 1920, Dur = 1920, Pitch = k % 2 == 0 ? lo : hi, Vel = 80 }); return t; };
  lib.Tracks.Add(trk("Solo Cello", 42, 0, 43, 76)); lib.Tracks.Add(trk("Cellos", 42, 1, 36, 60)); lib.Tracks.Add(trk("Strings", 48, 2, 50, 74));
  lib.Tracks.Add(trk("Flute", 73, 3, 62, 91)); lib.Tracks.Add(trk("Harpsichord", 6, 4, 48, 72)); lib.Tracks.Add(trk("Basses", 43, 5, 20, 40));
  lib.Tracks.Add(trk("Drums", 0, 9, 36, 42));
  var used = new System.Collections.Generic.List<string>(); var kept = new System.Collections.Generic.List<string>();
  var mapped = Library.Apply(lib, used, kept);
  Func<string, Track> byName = nm => mapped.Tracks.First(t => t.Name == nm);
  var under = mapped.Tracks.FirstOrDefault(t => t.Name == "Solo Cello (section underneath)");
  ok(byName("Solo Cello").Program == 13 && byName("Cellos").Program == 8 && byName("Strings").Program == 2 && byName("Strings (violas)").Program == 6 && byName("Strings (violas)").Notes.All(q5 => q5.Pitch == 50) && under != null && under.Program == 8 && under.Volume == 60 && under.Notes.Count == 4 &&
     under.Channel != byName("Solo Cello").Channel && mapped.Tracks.Where(t => t.Name != "Harpsichord" && t.Name != "Basses" && t.Name != "Drums").All(t => t.Bank == Library.Bank) && mapped.Tracks.Select(t => t.Channel).Distinct().Count() == mapped.Tracks.Count,
     "library: solo cello (doubled softly by the section), cello section, a strings part shared by register (D5 violins, D3 violas): " + string.Join("; ", used));
  ok(byName("Flute").Program == 15 && byName("Flute").Notes[0].Pitch == 50 && lib.Tracks[3].Notes[0].Pitch == 62, "library: flutes use the section patch (light vibrato), an octave lower in this soundfont (copy only; original untouched)");
  ok(byName("Harpsichord").Bank == 0 && byName("Basses").Bank == 0 && byName("Drums").Bank == 0 && kept.Count == 2 && kept[0].Contains("not in the library") && kept[1].Contains("outside"),
     "library: harpsichord, too-low basses and drums keep their sound: " + string.Join("; ", kept));
  var div = new Song { Title = "Div" };
  div.Tracks.Add(trk("Violins II", 48, 0, 54, 84)); div.Tracks.Add(trk("Contrabass", 43, 1, 40, 50));
  for (int k = 0; k < 8; k++) div.Tracks[0].Notes.Add(new NoteEv { Start = (4 + k) * 1920, Dur = 960, Pitch = 72 + k, Vel = 80 });
  var u2 = new System.Collections.Generic.List<string>(); var dm = Library.Apply(div, u2, new System.Collections.Generic.List<string>());
  ok(dm.Tracks.Count == 3 && dm.Tracks[0].Program == 4 && dm.Tracks[1].Program == 6 && dm.Tracks[1].Notes.Count == 2 && dm.Tracks[1].Notes.All(q3 => q3.Pitch == 54) && dm.Tracks[1].Channel != dm.Tracks[0].Channel &&
     dm.Tracks[1].Channel != 9 && dm.Tracks[2].Program == 10, "library: a Violins II part with low F#3s stays on (2nd) violins, the low notes go to violas: " + string.Join("; ", u2));
  var hp = new Song { Title = "Harp" };
  var harp = new Track { Name = "Harp", Program = 46, Channel = 0 };
  for (int k = 0; k < 20; k++) harp.Notes.Add(new NoteEv { Start = k * 480, Dur = 480, Pitch = k == 5 ? 33 : 48 + k, Vel = 60 });
  hp.Tracks.Add(harp); hp.Tracks.Add(new Track { Name = "Harpsichord", Program = 6, Channel = 1, Volume = 80, Notes = { new NoteEv { Start = 0, Dur = 480, Pitch = 60 } } });
  var u3 = new System.Collections.Generic.List<string>(); var k3 = new System.Collections.Generic.List<string>(); var hm = Library.Apply(hp, u3, k3);
  var stx = hm.Tracks.FirstOrDefault(t => t.Name.Contains("stretched"));
  ok(hm.Tracks[0].Program == 1 && hm.Tracks[0].Notes.Count == 19 && stx != null && stx.Program == 1 && stx.Notes.Single().Pitch == 36 && stx.Channel != 0 && stx.Channel != 1 &&
     stx.Controls.Any(c => c.Cc == CtlEv.PitchBend && c.Tick == 5 * 480 && c.Val == 8192 - 2048), "library: a harp's low A1 plays Sonatina's C2 bent down 3 semitones: " + string.Join("; ", u3));
  var hc = hm.Tracks.First(t => t.Program == 6);
  ok(hc.Bank == 0 && hc.Volume == 68 && hc.Controls.Any(c => c.Cc == 99 && c.Val == 120) && hc.Controls.Any(c => c.Cc == 6 && c.Val == (8192 - 900) >> 7) && k3.Single().Contains("softened"),
     "library: the default harpsichord is softened (low-pass, quieter, more reverb): " + k3.Single());
  var mf = Path.Combine(Path.GetTempPath(), "abend-bend-test.mid"); MidiFile.Write(hm, mf); var back = File.ReadAllBytes(mf); File.Delete(mf);
  ok(Enumerable.Range(0, back.Length - 2).Any(i => (back[i] & 0xF0) == 0xE0 && back[i + 1] == 0 && back[i + 2] == 48), "MIDI writer: pitch bend 6144 written as E0 00 30");
  var ko = new RenderOptions(); string k0 = AudioRender.RenderKey(ko);
  ko.Lufs = -14; ko.BassDb = 5; string k1 = AudioRender.RenderKey(ko);
  ko.Library = "sonatina"; string k2 = AudioRender.RenderKey(ko); ko.Expression = true; string k3x = AudioRender.RenderKey(ko);
  ko.HarpsichordFile = "h.sf2"; string k4 = AudioRender.RenderKey(ko);
  ok(k0 == "" && k1.Length == 32 && new[] { k1, k2, k3x, k4 }.Distinct().Count() == 4 && AudioRender.RenderKey(ko) == k4, "render key: none for default renders; changes with bass, library, expression and the harpsichord");
  var ko2 = new RenderOptions { Lufs = -14, BassDb = 5, Library = "sonatina", Expression = true, HarpsichordFile = "h.sf2" };
  var CIq = System.Globalization.CultureInfo.InvariantCulture; string oldWay;
  using (var md5 = System.Security.Cryptography.MD5.Create())
    oldWay = BitConverter.ToString(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("|", AudioRender.RenderVersion, ko2.Lufs.ToString(CIq), ko2.Lra.ToString(CIq), "-2", ko2.BassDb.ToString(CIq), ko2.Bitrate.ToString(CIq),
      ko2.GapSeconds.ToString(CIq), ko2.Album, ko2.Artist, "sonatina", "expr", "harpsichord")))).Replace("-", "").ToLowerInvariant();
  string kNone = AudioRender.RenderKey(ko2); ko2.Tuning = 440; string k440 = AudioRender.RenderKey(ko2); ko2.Tuning = 444; string k444 = AudioRender.RenderKey(ko2);
  ok(kNone == oldWay && k440 == oldWay && k444 != oldWay, "render key: options at their default (tuning 440) leave the key exactly as before; tuning 444 changes it");
  string scDir = Path.Combine(Path.GetTempPath(), "abend-sidecar-test"); Directory.CreateDirectory(scDir);
  string scMid = Path.Combine(scDir, "Song No1 - I.mid"), scTxt = Path.Combine(scDir, "Song No1 - I.title.txt");
  if (File.Exists(scTxt)) File.Delete(scTxt);
  string scNone = AudioRender.SidecarTitle(scMid);
  File.WriteAllText(scTxt, "Per aspera - I. Andante – Allegro  \r\nsecond line\r\n", System.Text.Encoding.UTF8);   // UTF8 writes a BOM, as Notepad does
  string scT = AudioRender.SidecarTitle(scMid);
  File.WriteAllText(scTxt, "  \r\n"); string scEmpty = AudioRender.SidecarTitle(scMid); File.Delete(scTxt);
  ok(scNone == null && scT == "Per aspera - I. Andante – Allegro" && scEmpty == null, "sidecar title: first line of Song.title.txt (UTF-8, trimmed); none or empty = file name as before");
  ok(AudioRender.FileKey(ko2, null) == AudioRender.RenderKey(ko2) && AudioRender.FileKey(new RenderOptions(), null) == "" && AudioRender.FileKey(ko2, "A") != AudioRender.FileKey(ko2, "B") && AudioRender.FileKey(new RenderOptions(), "A").Length == 32,
    "render key: without a sidecar title the key is unchanged; a sidecar title gives that file its own key");
  Func<string, ScoreResult> gapScore = gh => ScoreScript.Parse("title: T\ntempo: 120\nmeter: 4/4\n" + gh + "track Melody | instrument: flute\nc5/1 |\n");
  var g0 = gapScore("gap: 0\n"); var g25 = gapScore("gap: 2.5 s\n"); var gNone = gapScore(""); var gBad = gapScore("gap: 12\n");
  ok(g0.Ok && g0.Gap == 0 && g25.Gap == 2.5 && gNone.Gap == null && gNone.Warnings.Count == g0.Warnings.Count && !gBad.Ok, "gap: header read (0, 2.5 s); none = null; 12 is an error");
  ok(AudioRender.FileKey(ko2, null, null) == AudioRender.RenderKey(ko2) && AudioRender.FileKey(new RenderOptions(), null, null) == "" && AudioRender.FileKey(ko2, null, 0) != AudioRender.RenderKey(ko2)
     && AudioRender.FileKey(ko2, null, 0) != AudioRender.FileKey(ko2, null, 1.5) && AudioRender.FileKey(new RenderOptions(), null, 0).Length == 32,
    "render key: a score without gap: keeps its key; gap: gives that score its own key");
  var al = gapScore("album: The Quiet House\n");
  ok(al.Ok && al.Album == "The Quiet House" && gNone.Album == null && AudioRender.FileKey(ko2, null, null, null) == AudioRender.RenderKey(ko2) && AudioRender.FileKey(ko2, null, null, "The Quiet House") != AudioRender.RenderKey(ko2),
    "album: header read; the key changes only for scores that have one");
  var ov = new Song(); ov.Tracks.Add(new Track { Name = "pad", Notes = { new NoteEv { Start = 0, Dur = 1925, Pitch = 64 }, new NoteEv { Start = 1915, Dur = 1920, Pitch = 64 }, new NoteEv { Start = 1915, Dur = 1920, Pitch = 67 } } });
  AudioRender.NoSelfOverlap(ov); var on2 = ov.Tracks[0].Notes;
  ok(on2[0].Start + on2[0].Dur == on2[1].Start && on2[2].Dur == 1920, "renderer: a note never outlasts the start of the next note of its pitch (that note-off would silence the new note)");
  var hsd = new Song { Title = "H" }; hsd.Tracks.Add(new Track { Name = "Harpsichord", Program = 6, Volume = 80, Notes = { new NoteEv { Start = 0, Dur = 480, Pitch = 43 } } });
  var u5 = new System.Collections.Generic.List<string>(); var hsm = Library.Apply(hsd, u5, new System.Collections.Generic.List<string>(), true);
  ok(hsm.Tracks[0].Bank == Library.HarpsichordBank && hsm.Tracks[0].Program == 0 && u5.Single().Contains("VCSL"), "library: harpsichord parts use the VCSL harpsichord when it is there: " + u5.Single());
  var xk = new RenderOptions { Library = "sonatina", Expression = true, ExtrasFile = @"x\Abend-Extras.sf2" };
  var pipe = score("", "track Organ | instrument: pipe organ\n\\mf c4/1 |\n").Song; var plainOrgan = score("", "track Organ | instrument: church organ\n\\mf c4/1 |\n").Song;
  var acc2 = score("", "track Acc | instrument: accordion\n\\mf c4/1 |\n").Song;
  var pedal = score("", "track Ped | instrument: pipe organ pedal\n\\mf a1/1 |\n");
  ok(pedal.Ok && Library.ExtrasPreset(pedal.Song.Tracks[0]) == -1 && pedal.Song.Tracks[0].Program == 19 && !pedal.Warnings.Any(x => x.Contains("range")),
     "extras: pipe organ pedal plays the default church organ, low notes in range " + string.Join(" / ", pedal.Warnings));
  ok(Library.PlaysExtras(pipe) && Library.PlaysExtras(acc2) && !Library.PlaysExtras(plainOrgan) && Library.ExtrasPreset(pipe.Tracks[0]) == 0 && Library.ExtrasPreset(acc2.Tracks[0]) == 2
     && AudioRender.FileKey(xk, null, null, null, false, false, false) == AudioRender.RenderKey(xk) && AudioRender.FileKey(xk, null, null, null, false, false, true) != AudioRender.RenderKey(xk),
     "extras: pipe organ and accordion parts (not plain organ) use the extras, and only they change the render key");
  var hk = new RenderOptions { Library = "sonatina", Expression = true, HarpsichordFile = @"x\VCSL-Flemish-Harpsichord.sf2" };
  string hk0 = AudioRender.FileKey(hk, null, null, null), hk1 = AudioRender.FileKey(hk, null, null, null, true);
  hk.HarpsichordFile = @"x\VCSL-English-Harpsichord.sf2";
  ok(hk0 == AudioRender.RenderKey(hk) && AudioRender.FileKey(hk, null, null, null, false) == hk0 && hk1 != hk0 && AudioRender.FileKey(hk, null, null, null, true) != hk1
     && Library.PlaysHarpsichord(hsd) && !Library.PlaysHarpsichord(ov),
     "render key: a new harpsichord soundfont re-renders only pieces with a harpsichord part");
  var pc = new Song { Title = "P" };
  var dr = new Track { Name = "Drums", Channel = 9, Program = 0 };
  foreach (var p in new[] { 36, 38, 38, 49, 42 }) dr.Notes.Add(new NoteEv { Start = dr.Notes.Count * 480, Dur = 120, Pitch = p, Vel = 90 });
  for (int i = 0; i < 8; i++) dr.Notes.Add(new NoteEv { Start = 4800 + i * 60, Dur = 60, Pitch = 38, Vel = 60 + i * 5 });
  var tp = new Track { Name = "Timpani", Channel = 1, Program = 47 };
  foreach (var p in new[] { 38, 45, 50, 57 }) tp.Notes.Add(new NoteEv { Start = tp.Notes.Count * 480, Dur = 240, Pitch = p, Vel = 90 });
  for (int i = 0; i < 8; i++) tp.Notes.Add(new NoteEv { Start = 4800 + i * 60, Dur = 60, Pitch = 38, Vel = 90 });
  pc.Tracks.Add(dr); pc.Tracks.Add(tp);
  var pu = new System.Collections.Generic.List<string>(); var pk = new System.Collections.Generic.List<string>();
  var pm = Library.Apply(pc, pu, pk, false, null, null, null, true);
  var kitT = pm.Tracks.First(t => t.Name == "Drums (Sonatina percussion)"); var rollT = pm.Tracks.First(t => t.Name == "Drums (snare rolls)");
  var timT = pm.Tracks.First(t => t.Name == "Timpani"); var bentT = pm.Tracks.First(t => t.Name == "Timpani (timpani bent +2)");
  ok(kitT.Notes.Select(x => x.Pitch).SequenceEqual(new[] { 37, 40, 38, 45 }) && pm.Tracks.First(t => t.Name == "Drums").Notes.Single().Pitch == 42
     && rollT.Notes.Single().Pitch == 39 && rollT.Controls.Count(c => c.Cc == 11) == 8
     && timT.Bank == Library.Bank && timT.Program == 40 && timT.Notes.Select(x => x.Pitch).SequenceEqual(new[] { 50, 33, 33, 74 })
     && bentT.Notes.Single().Pitch == 60 && bentT.Controls.Any(c => c.Cc == CtlEv.PitchBend && c.Val == 8192 + 1365)
     && Percussion.Plays(pc) && !Percussion.Plays(hsd) && Library.Apply(pc, new System.Collections.Generic.List<string>(), new System.Collections.Generic.List<string>(), false).Tracks.Count == 2,
     "percussion: kit sounds, alternating snare hands and a real roll; timpani at true pitch (bent, octave-moved, rolled); off by default: " + string.Join("; ", pu));
  var pd = new RenderOptions { Library = "sonatina", Expression = true }; var pso = pd.Copy(); pso.Percussion = "sonatina"; var pg = pd.Copy(); pg.Percussion = "generaluser";
  var ph = ScoreScript.Parse("title: P\npercussion: generaluser\ntrack Drums | instrument: drums\nc2/1 |\n"); var pb = ScoreScript.Parse("title: P\npercussion: gm\ntrack Drums | instrument: drums\nc2/1 |\n");
  ok(pd.SonatinaPercussion && !pg.SonatinaPercussion && !new RenderOptions().SonatinaPercussion
     && AudioRender.FileKey(pd, null, null, null, false, true) == AudioRender.FileKey(pso, null, null, null, false, true)
     && AudioRender.FileKey(pd, null, null, null, false, true) != AudioRender.RenderKey(pd)
     && AudioRender.FileKey(pg, null, null, null, false, true) == AudioRender.RenderKey(pg) && AudioRender.FileKey(pd, null, null, null, false, false) == AudioRender.RenderKey(pd)
     && ph.Ok && ph.Percussion == "generaluser" && ph.Warnings.Count == 0 && !pb.Ok,
     "percussion: orchestral by default with --library (same key as --percussion sonatina); generaluser (flag or header) keeps the old key");
  string ssoPath = Library.Find("sonatina", null);
  if (ssoPath == null) Console.WriteLine("SKIP long-note tests: Sonatina not found");
  else
  {
    var info = Sf2Info.Load(ssoPath);
    ok(double.IsInfinity(info.Sustain(0, 2, 79, 90)) && info.Sustain(0, 30, 57, 90) < 4 && info.Stretch(0, 14, 60, 90) > 12 && info.Stretch(0, 15, 60, 90) <= 2,
       "Sf2Info: violins loop, the horn section's A3 lasts " + info.Sustain(0, 30, 57, 90).ToString("0.0") + " s, Flute Solo's middle keys are one stretched recording");
    var ln = new Song { Title = "Long" }; ln.Tempos[0].Bpm = 60;
    ln.Tracks.Add(new Track { Name = "Horns", Program = 60, Channel = 0, Notes = { new NoteEv { Start = 0, Dur = 480 * 10, Pitch = 57, Vel = 80 }, new NoteEv { Start = 480 * 12, Dur = 480, Pitch = 59, Vel = 80 } } });
    var lu = new System.Collections.Generic.List<string>(); var lm = Library.Apply(ln, lu, new System.Collections.Generic.List<string>(), false, info);
    Func<Track, NoteEv, double> secs = (t, nn) => (lm.TickToMs(nn.Start + nn.Dur) - lm.TickToMs(nn.Start)) / 1000;
    var held = lm.Tracks.Where(t => t.Name.Contains("held notes")).ToList();
    bool fits = lm.Tracks.Where(t => t.Bank == Library.Bank).All(t => t.Notes.All(nn => secs(t, nn) <= info.Sustain(0, t.Program, nn.Pitch, nn.Vel)));
    var allA3 = lm.Tracks.SelectMany(t => t.Notes.Where(nn => nn.Pitch == 57)).OrderBy(nn => nn.Start).ToList();
    ok(held.Count >= 1 && fits && allA3.Last().Start + allA3.Last().Dur == 4800 && allA3.Zip(allA3.Skip(1), (a1, b1) => b1.Start < a1.Start + a1.Dur).All(q => q) && held[0].Controls.Any(c => c.Cc == 98 && c.Val == 34),
       "long notes: a 10-second horn A3 is continued in " + allA3.Count + " overlapping, faded-in pieces and still ends on time: " + string.Join("; ", lu));
  }
  var ter = new Song { Title = "Terrace" }; var tt = new Track { Name = "Oboe", Program = 68, Channel = 0 };
  for (int k = 0; k < 8; k++) tt.Notes.Add(new NoteEv { Start = k * 240, Dur = 240, Pitch = 72, Vel = k < 4 ? 94 : 55 });           // f, then p straight away
  for (int k = 0; k < 4; k++) tt.Notes.Add(new NoteEv { Start = 8 * 240 + 960 + k * 240, Dur = 240, Pitch = 72, Vel = k == 0 ? 94 : 94 }); // after a beat's rest: f again
  ter.Tracks.Add(tt); var te = Expression.Apply(ter).Tracks[0].Notes.OrderBy(q4 => q4.Start).ToList();
  ok(te[4].Vel > 68 && te[4].Vel < 82 && te[5].Vel < te[4].Vel && te[7].Vel <= 62 && te[8].Vel >= 88, "expression: f->p without a hairpin eases over a beat (" + string.Join(" ", te.Select(q4 => q4.Vel)) + "); an entry after a rest is not eased");
  // articulation gaps, sustained chords, tenuto in patterns, "=" after a hairpin
  var gs = new Song(); gs.Tempos[0].Bpm = 60;
  var plainN = Engine.Notation(gs, 1, @"\mf c4/1 | d4/1- | (e4/1 | f4/1) |", 0);
  ok(plainN[0].Gap == 96 && plainN[1].Gap == 0 && plainN[2].Gap == 0, "notation: an ordinary whole note at 60 bpm loses 96 ticks (0.2 s); tenuto and slurred notes none");
  var pad = Engine.Harmony(gs, 1, "C/1 | F/1 |", 3, 55, 72, false, "*: c", 70); var ten = Engine.Harmony(gs, 1, "C/1 |", 3, 55, 72, false, "4: c- c- c- c-", 70);
  ok(pad.All(x7 => x7.Sustained && x7.Gap > 0) && ten.All(x7 => x7.Dur == 480 && x7.Gap == 0), "chords: \"*\" patterns are sustained chords; \"c-\" (tenuto) in a pattern plays full length");
  gs.Tracks.Add(new Track { Name = "p", Notes = plainN.Concat(pad).ToList() });
  var sameS = new Song(); sameS.Tempos[0].Bpm = 60; sameS.Tracks.Add(new Track { Name = "s", Notes = Engine.Notation(sameS, 1, @"\mf c4/1 | c4/1 | d4/1 |", 0) }); sameS.Tracks.Add(new Track { Name = "pad", Notes = pad.Select(x7 => x7.Clone()).ToList() });
  var closed = AudioRender.CloseGaps(sameS); var cn = closed.Tracks[0].Notes.Where(x7 => !x7.Sustained).OrderBy(x7 => x7.Start).ToList();
  ok(cn[0].Gap > 0 && cn[0].Gap <= 25 && cn[1].Start + cn[1].Dur > cn[2].Start && cn[1].Start + cn[1].Dur - cn[2].Start <= 170 && closed.Tracks[1].Notes.Where(x7 => x7.Start == 0 && !closed.Tracks[1].Notes.Any(m7 => m7.Start == 1920 && m7.Pitch == x7.Pitch)).All(x7 => x7.Dur > 1920) && closed.Tracks[1].Notes.Count(x7 => x7.Start == 0 && x7.Dur > 1920) >= 1,
     "library renders: a repeated note keeps a 50 ms gap (" + cn[0].Gap + " ticks), a held note overlaps the next pitch (" + (cn[1].Start + cn[1].Dur - cn[2].Start) + " ticks), sustained chords overlap the chord change");
  var hold = ScoreScript.Parse("title: T\ntempo: 60\nmeter: 4/4\nchords Horns | instrument: horn | voices: 3 | range: F3-C5 | pattern: *: c\n\\mf C/1 | \\> = | \\p F/1 |\n");
  ok(hold.Ok && hold.Song.Tracks[0].Notes.Count == 6 && hold.Song.Tracks[0].Notes.Take(3).All(x7 => x7.Dur + x7.Gap == 3840), "chords: \"=\" right after a hairpin holds the chord for another bar " + string.Join(" / ", hold.Errors));
  if (ssoPath != null)
  {
    var sfi = Sf2Info.Load(ssoPath);
    ok(!sfi.Covers(0, 40, 38, 80) && sfi.Covers(0, 40, 50, 80), "Sf2Info: Sonatina's timpani has no recording on D2 (a hole), but does on D3");
    var tim = new Song(); tim.Tracks.Add(new Track { Name = "Timpani", Program = 47, Channel = 0, Notes = { new NoteEv { Start = 0, Dur = 480, Pitch = 38, Vel = 80 } } });
    var tk = new System.Collections.Generic.List<string>(); var tm = Library.Apply(tim, new System.Collections.Generic.List<string>(), tk, false, sfi);
    ok(tm.Tracks[0].Bank == 0 && tk.Count == 1, "library: a timpani part on keys Sonatina can't play keeps the default timpani (not silence): " + tk.FirstOrDefault());
  }
  var hp2 = ScoreScript.Parse("title: T\ntempo: 60\nmeter: 4/4\ntrack Violin | instrument: violin\n\\p \\< c5/1~ | c5/1 | \\f d5/1 |\n");
  var hpe = Expression.Apply(hp2.Song).Tracks[0]; var held2 = hpe.Notes.OrderBy(x8 => x8.Start).First();
  var rise = hpe.Controls.Where(c8 => c8.Cc == 11 && c8.Tick >= held2.Start && c8.Tick < held2.Start + held2.Dur).OrderBy(c8 => c8.Tick).ToList();
  ok(hp2.Ok && held2.Vel > hp2.Song.Tracks[0].Notes[0].Vel + 20 && rise.Count > 8 && rise.Last().Val > rise.First().Val * 1.5,
     "expression: a note held through a crescendo starts at the loud level and CC11 rises across it (" + rise.First().Val + " -> " + rise.Last().Val + ", velocity " + hp2.Song.Tracks[0].Notes[0].Vel + " -> " + held2.Vel + ")");
  // tuning: header, RPN fine tuning on every non-drum channel of every pass, nothing when A=440
  var tun = ScoreScript.Parse("title: T\ntuning: 444\ntempo: 60\nmeter: 4/4\ntrack Flute | instrument: flute\n\\mf c5/1 |\n");
  var badT = ScoreScript.Parse("title: T\ntuning: 500\ntempo: 60\nmeter: 4/4\ntrack Flute | instrument: flute\n\\mf c5/1 |\n");
  ok(tun.Ok && tun.Song.TuningHz == 444 && Math.Abs(tun.Song.TuningCents - 15.67) < 0.01 && !badT.Ok, "tuning: header read (A=444 = +15.67 cents); out of range rejected");
  var tm444 = MidiFile.TuningMessages(15.67); var tm415 = MidiFile.TuningMessages(1200 * Math.Log(415.0 / 440, 2));
  ok(tm444.Count == 6 && tm444[2][1] == 74 && tm444[3][1] == 4 && tm415.Any(x9 => x9[0] == 100 && x9[1] == 2), "tuning: A=444 is RPN 1 data 74/4 (9476); A=415 also needs a coarse step (RPN 2)");
  var big = new Song(); big.TuningHz = 444; for (int k = 0; k < 20; k++) big.Tracks.Add(new Track { Name = "t" + k, Channel = k < 9 ? k : k + 1, Notes = { new NoteEv { Start = 0, Dur = 480, Pitch = 60 } } });
  big.Tracks.Add(new Track { Name = "drums", Channel = 9, Notes = { new NoteEv { Start = 0, Dur = 480, Pitch = 36 } } });
  string td = Path.Combine(Path.GetTempPath(), "abend-tuning-" + Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(td);
  var pfiles = AudioRender.WritePasses(big, td, "x"); int tunedCh = 0;
  foreach (var pf in pfiles) { var bb = File.ReadAllBytes(pf); for (int i = 0; i + 5 < bb.Length; i++) if ((bb[i] & 0xF0) == 0xB0 && bb[i + 1] == 100 && bb[i + 2] == 1) tunedCh++; }
  big.TuningHz = 440; MidiFile.Write(big, Path.Combine(td, "plain.mid")); var plainB = File.ReadAllBytes(Path.Combine(td, "plain.mid"));
  bool noRpn = !Enumerable.Range(0, plainB.Length - 2).Any(i => (plainB[i] & 0xF0) == 0xB0 && plainB[i + 1] == 100 && plainB[i + 2] == 1);
  Directory.Delete(td, true);
  ok(pfiles.Count == 2 && tunedCh == 20 && noRpn, "tuning: all 20 non-drum channels across 2 passes get it, the drums don't; A=440 writes no tuning messages");
  var mv2 = ScoreScript.Parse("title: T\ntempo: 60\nmeter: 4/4\ntrack Violin | instrument: violin\n\\p \\< c5/4 d5 e5 f5 | g5/4 a5 b5 c6 | \\f d6/1 |\n");
  var mve = Expression.Apply(mv2.Song).Tracks[0]; var mvn = mve.Notes.OrderBy(x8 => x8.Start).ToList();
  var mcc = mve.Controls.Where(c8 => c8.Cc == 11 && c8.Tick < 1920 * 2).OrderBy(c8 => c8.Tick).ToList();
  ok(mvn.Max(x8 => x8.Vel) - mvn.Min(x8 => x8.Vel) <= 9 && mcc.First().Val < mcc.Last().Val - 20 && mcc.Zip(mcc.Skip(1), (p8, q8) => q8.Val - p8.Val).All(d8 => d8 > -3),
     "expression: a crescendo over moving notes keeps steady velocities (" + mvn.Min(x8 => x8.Vel) + "-" + mvn.Max(x8 => x8.Vel) + ") and rises in CC11 (" + mcc.First().Val + " -> " + mcc.Last().Val + ")");
  // a single-level change (\p to \mp) in a part that keeps playing eases over two beats; an entry after a rest does not
  var one = ScoreScript.Parse("title: T\ntempo: 60\nmeter: 4/4\ntrack Violin | instrument: violin\n\\p c5/4 d5 e5 f5 | \\mp g5/4 a5 b5 c6 | c6/4 b5 a5 g5 | r/1 | \\p c5/1 | r/1 | \\mp c5/4 d5 e5 f5 |\n");
  var onee = Expression.Apply(one.Song).Tracks[0]; int ob1 = Song.PPQ, obar = Song.PPQ * 4;
  var occ = onee.Controls.Where(c8 => c8.Cc == 11).OrderBy(c8 => c8.Tick).ToList();
  Func<int, int> ccAt = x8 => occ.Where(c8 => c8.Tick <= x8).Last().Val;
  int o0 = ccAt(obar - 1), o1 = ccAt(obar), o2 = ccAt(obar + ob1), o3 = ccAt(obar + 2 * ob1 - 1), o4 = ccAt(obar + 3 * ob1);
  int e0 = ccAt(obar * 6 + ob1 / 4), e1 = ccAt(obar * 6 + 3 * ob1);
  ok(one.Ok && o1 - o0 <= 3 && o2 > o1 + 3 && o3 > o2 + 3 && o4 >= o3 && o4 - o3 <= 3 && Math.Abs(e1 - e0) <= 1,
     "expression: p -> mp in a moving line eases over two beats (CC11 " + o0 + " " + o1 + " " + o2 + " " + o3 + " " + o4 + "); after a rest mp starts at once (" + e0 + " " + e1 + ")");
  var ex1 = Expression.Apply(lib); var ex2 = Expression.Apply(lib);
  bool same = ex1.Tracks.Zip(ex2.Tracks, (ta, tb) => ta.Notes.Zip(tb.Notes, (x, y) => x.Start == y.Start && x.Vel == y.Vel && x.Dur == y.Dur).All(q => q) && ta.Controls.Count == tb.Controls.Count).All(q => q);
  ok(same && lib.Tracks.All(t => t.Controls.Count == 0) && ex1.Tracks[0].Controls.Any(c => c.Cc == 11) && ex1.Tracks[0].Controls.Any(c => c.Cc == 1 && c.Val > 0) &&
     ex1.Tracks[4].Controls.Count == 0 && ex1.Tracks.All(t => t.Notes.All(q2 => q2.Start >= 0 && q2.Vel >= 1)) && ex1.Tracks.Sum(t => t.Notes.Count) == lib.Tracks.Sum(t => t.Notes.Count),
     "expression: repeatable, swells and vibrato on held strings, none on harpsichord, original untouched");

  // cover art: where covers are found
  string cv = Path.Combine(Path.GetTempPath(), "abend-covertest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
  Directory.CreateDirectory(Path.Combine(cv, "art"));
  Action<string, System.Drawing.Color> png = (file, col) => { using (var bmp = new System.Drawing.Bitmap(1600, 1600)) { using (var g = System.Drawing.Graphics.FromImage(bmp)) g.Clear(col); bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png); } };
  try
  {
    string songF = Path.Combine(cv, "Song.abscore"), fb = Path.Combine(cv, "art", "fallback.png");
    File.WriteAllText(songF, "title: Song\ncover: art/front.png\ntempo: 120\nmeter: 4/4\ntrack Melody | instrument: flute\n\\mf c5/2 e5/2 | g5/2 e5/2 | d5/2 b4/2 | c5/1 |\nchords Strings | instrument: strings | voices: 3 | range: G3-E4\n\\p C/1 | C | G | C |\n");
    ok(ScoreScript.Parse(File.ReadAllText(songF)).Cover == "art/front.png" && ScoreScript.Parse("title: Tcover: x.pngtempo: 100\ntrack A | instrument: flute\n\\mf c5/1 |\n").Cover == "x.png", "cover: header read (also after lost line breaks)");
    var w = new System.Collections.Generic.List<string>();
    ok(AudioRender.FindCover(songF, null, null, w) == null, "cover: none found -> no cover");
    png(fb, System.Drawing.Color.Gray);
    ok(AudioRender.FindCover(songF, null, fb, w) == fb, "cover: --cover is the fallback");
    png(Path.Combine(cv, "cover.jpg"), System.Drawing.Color.Blue);
    ok(AudioRender.FindCover(songF, null, fb, w).EndsWith("cover.jpg"), "cover: cover.jpg in the folder beats --cover");
    png(Path.Combine(cv, "Song.png"), System.Drawing.Color.Green);
    ok(AudioRender.FindCover(songF, "art/front.png", fb, w).EndsWith("Song.png") && w.Count == 1, "cover: Song.png beats cover.jpg; a missing cover: file warns: " + w.FirstOrDefault());
    png(Path.Combine(cv, "art", "front.png"), System.Drawing.Color.Red);
    ok(AudioRender.FindCover(songF, "art/front.png", fb, null).EndsWith(Path.Combine("art", "front.png")) && AudioRender.FindCover(songF, fb, null, null) == fb, "cover: the cover: header wins (relative or absolute)");

    // a real render with and without a cover: same audio, cover stream present, re-render when the cover changes
    var ro = new RenderOptions();
    string fl, ff, sf;
    if (AudioRender.CheckSetup(ro, out fl, out ff, out sf) != null) Console.WriteLine("SKIP render tests: FluidSynth/ffmpeg/soundfont not found");
    else
    {
      string probe = Path.Combine(Path.GetDirectoryName(ff), "ffprobe.exe");
      Func<string, string, string> tool = (exe, args) => { var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using (var p = System.Diagnostics.Process.Start(psi)) { var so = p.StandardOutput.ReadToEndAsync(); var se = p.StandardError.ReadToEnd(); p.WaitForExit(); return so.Result + se; } };
      Func<string, string> audioMd5 = f => System.Text.RegularExpressions.Regex.Match(tool(ff, "-v error -i \"" + f + "\" -map 0:a -f md5 -"), "MD5=([0-9a-f]+)").Groups[1].Value;
      Func<string, string> streams = f => tool(probe, "-v error -show_entries stream=codec_name,width,height:stream_disposition=attached_pic -of compact \"" + f + "\"");
      string plain = Path.Combine(cv, "Plain.abscore");
      File.WriteAllText(plain, File.ReadAllText(songF).Replace("cover: art/front.png\n", ""));
      foreach (var x in new[] { "Song.png", "cover.jpg" }) File.Delete(Path.Combine(cv, x));
      ro.OutDir = Path.Combine(cv, "out"); Directory.CreateDirectory(ro.OutDir);
      var r0 = AudioRender.RenderFile(plain, ro, fl, ff, sf);
      var r1 = AudioRender.RenderFile(songF, ro, fl, ff, sf);
      string s0 = streams(r0.Output), s1 = streams(r1.Output);
      ok(r0.Ok && !s0.Contains("attached_pic=1") && !r0.Detail.Contains("cover"), "render: without a cover: " + r0.Detail + " | " + s0.Trim().Replace("\r\n", " "));
      ok(r1.Ok && s1.Contains("codec_name=png") && s1.Contains("width=1400") && s1.Contains("attached_pic=1") && r1.Detail.Contains("cover: front.png"), "render: with a cover: " + r1.Detail + " | " + s1.Trim().Replace("\r\n", " "));
      string l0 = System.Text.RegularExpressions.Regex.Match(r0.Detail, @"\S+ LUFS").Value, l1 = System.Text.RegularExpressions.Regex.Match(r1.Detail, @"\S+ LUFS").Value;
      ok(audioMd5(r0.Output) == audioMd5(r1.Output) && audioMd5(r0.Output).Length == 32 && Math.Abs(r0.Seconds - r1.Seconds) < 0.1 && r1.Seconds > 5 && l0 == l1, "render: the cover does not change the audio (decoded MD5, " + r1.Seconds + " s, " + l1 + ")");
      ok(AudioRender.RenderFile(songF, ro, fl, ff, sf).Status == "UP-TO-DATE", "render: unchanged cover -> up to date");
      png(Path.Combine(cv, "art", "front.png"), System.Drawing.Color.Yellow);
      File.SetLastWriteTimeUtc(Path.Combine(cv, "art", "front.png"), new DateTime(2020, 1, 1));
      ok(AudioRender.RenderFile(songF, ro, fl, ff, sf).Status == "OK", "render: changed cover (even with an old file date) -> rendered again");
      File.Delete(Path.Combine(cv, "art", "front.png")); File.Delete(fb);
      var r3 = AudioRender.RenderFile(songF, ro, fl, ff, sf);
      ok(r3.Status == "OK" && !streams(r3.Output).Contains("attached_pic=1"), "render: removed cover -> rendered again without one");
    }
  }
  finally { try { Directory.Delete(cv, true); } catch { } }


  // ---------- more than 15 instrument tracks: each part gets its own channel in a render ----------
  {
    string many = "title: Many\ntempo: 120\nmeter: 4/4\n";
    for (int k = 0; k < 17; k++) many += "track P" + k + " | instrument: " + (k == 15 ? "glockenspiel" : k == 16 ? "violins" : "flute") + "\n\\mf c5/1 |\n";
    many += "track Drums | instrument: drums\nc2/1 |\n";
    var mr = ScoreScript.Parse(many);
    var own = AudioRender.OwnChannels(mr.Song);
    var mel17 = own.Tracks.Where(t => !t.IsDrums).ToList();
    ok(mr.Ok && AudioRender.SharesChannels(mr.Song) && !AudioRender.SharesChannels(own) && mel17.Count == 17 && mel17.Select(t => t.Channel).Distinct().Count() == 17
       && mel17.All(t => t.Channel % 16 != 9 && t.Channel < Library.MaxChannels) && mr.Song.Tracks[15].Channel == 0 && own.Tracks[15].Channel == 16 && own.Tracks[16].Channel == 17
       && own.Tracks.Sum(t => t.Notes.Count) == mr.Song.Tracks.Sum(t => t.Notes.Count),
       "channels: 17 instrument tracks get 17 channels in a render (the .mid keeps 16): " + string.Join(",", own.Tracks.Select(t => t.Channel)));
    var plain = score("", mel + acc);
    ok(!AudioRender.SharesChannels(plain.Song) && object.ReferenceEquals(AudioRender.OwnChannels(plain.Song), plain.Song), "channels: a piece with 15 or fewer tracks is left exactly as it was");
    var cr = new RenderResult(); AudioRender.CheckAudible(mr.Song, new RenderOptions(), null, cr);
    var cr2 = new RenderResult(); AudioRender.CheckAudible(own, new RenderOptions(), null, cr2);
    ok(cr.Warnings.Any(w => w.StartsWith("P15 shares a MIDI channel")) && cr2.Warnings.Count == 0, "self-check: a part sharing another part's channel is named: " + cr.Warnings.FirstOrDefault());
    var ko8 = new RenderOptions { Library = "sonatina", Expression = true };
    ok(AudioRender.FileKey(ko8, null, null, null, false, false, false, false) == AudioRender.FileKey(ko8, null, null, null, false, false, false)
       && AudioRender.FileKey(ko8, null, null, null, false, false, false, true) != AudioRender.RenderKey(ko8), "channels: only pieces that shared a channel get a new render key");
    string sonF = Library.Find("sonatina", null);
    var sf = sonF;
    if (sf != null)
    {
      var hi = new Song { Title = "Hi" };
      var vt = new Track { Name = "High violins", Bank = Library.Bank, Program = 2, Channel = 0 };
      vt.Notes.Add(new NoteEv { Start = 0, Dur = 480, Pitch = 76, Vel = 80 }); vt.Notes.Add(new NoteEv { Start = 480, Dur = 480, Pitch = 120, Vel = 80 });
      hi.Tracks.Add(vt);
      var cr3 = new RenderResult(); AudioRender.CheckAudible(hi, new RenderOptions { Library = "sonatina", LibraryFile = sf }, null, cr3);
      ok(cr3.Warnings.Count == 1 && cr3.Warnings[0].Contains("1 of its 2 notes have no recording"), "self-check: notes outside a sound's recordings are named: " + cr3.Warnings.FirstOrDefault());
    }
  }

  // ---------- bare hairpins, tempo at a ramp's end, non-score .txt files (2026-11-11) ----------
  {
    Func<ScoreResult, string> notes = r => string.Join(" ", r.Song.Tracks.SelectMany(t => t.Notes).OrderBy(x => x.Start).ThenBy(x => x.Pitch).Select(x => x.Start + ":" + x.Pitch + ":" + x.Dur + ":" + x.Vel));
    string hp1 = "track M | instrument: flute\n\\p c5/4 d5/4 \\< e5/4 f5/4 | \\f g5/2 \\> f5/4 e5/4 | \\p d5/1 |\n";
    var withBs = score("", hp1); var bareHp = score("", hp1.Replace("\\<", "<").Replace("\\>", ">"));
    ok(withBs.Ok && bareHp.Ok && notes(bareHp) == notes(withBs) && warned(bareHp, "2 bare < or > read as") && !warned(withBs, "bare"),
       "dynamics: a lone < and > are read as \\< and \\> (with a warning): " + bareHp.Warnings.FirstOrDefault(w => w.Contains("bare")));
    var chordSp = score("", "track M | instrument: piano\n\\mf < c4 e4 g4>/2 <c4 f4 a4>/2 | c5/4> d5/4 e5/2 |\n");
    var chordTight = score("", "track M | instrument: piano\n\\mf <c4 e4 g4>/2 <c4 f4 a4>/2 | c5/4> d5/4 e5/2 |\n");
    ok(chordSp.Ok && notes(chordSp) == notes(chordTight) && !warned(chordSp, "bare") && chordSp.Song.Tracks[0].Notes.Count == 9,
       "dynamics: < c4 e4 g4>/2 is still a chord and c5/4> still an accent");
    string ramp = "title: T\ntempo: 120\nmeter: 4/4\nrit @2-3: 60\n";
    string mel3 = "track M | instrument: flute\n\\mf c5/1 | d5/1 | e5/1 | f5/1 |\n";
    Func<Song, int, double> bpmAt = (s, tick) => s.Tempos.Where(x => x.Tick <= tick).Last().Bpm;
    var r0 = ScoreScript.Parse(ramp + mel3); var r1 = ScoreScript.Parse(ramp + "tempo @3: 100\n" + mel3); var r2 = ScoreScript.Parse("title: T\ntempo: 120\nmeter: 4/4\ntempo @3: 100\nrit @2-3: 60\n" + mel3);
    var sameT = ScoreScript.Parse(ramp + "tempo @3: 60\n" + mel3);
    ok(ScoreScript.TempoLineAfterRamp && r0.Ok && r1.Ok && bpmAt(r0.Song, 2 * 1920) == 60 && bpmAt(r1.Song, 2 * 1920) == 100 && bpmAt(r2.Song, 2 * 1920) == 100 && bpmAt(r1.Song, 2 * 1920 - 1) < 70
       && r1.TempoLineAfterRamp && r2.TempoLineAfterRamp && !r0.TempoLineAfterRamp && !sameT.TempoLineAfterRamp,
       "tempo: a tempo line where a rit ends takes over there (ramp to 60, then 100), whichever line comes first; only then is the score marked");
    ScoreScript.TempoLineAfterRamp = false;
    var off1 = ScoreScript.Parse(ramp + "tempo @3: 100\n" + mel3);
    ScoreScript.TempoLineAfterRamp = true;
    ok(off1.Ok && bpmAt(off1.Song, 2 * 1920) == 60 && !off1.TempoLineAfterRamp, "tempo: switched off, the rit's speed carries on (the timing before 2026-11-15)");
    var ko9 = new RenderOptions { Library = "sonatina", Expression = true };
    ok(AudioRender.FileKey(ko9, null, null, null, false, false, false, false, false) == AudioRender.FileKey(ko9, null, null, null, false, false, false, false)
       && AudioRender.FileKey(ko9, null, null, null, false, false, false, false, true) != AudioRender.RenderKey(ko9), "tempo: only scores whose timing changed get a new render key");
    string tdx = Path.Combine(Path.GetTempPath(), "abend-txt-" + Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(tdx);
    try
    {
      File.WriteAllText(Path.Combine(tdx, "render-log.txt"), "2026-11-11 OK  track one.abscore\n"); File.WriteAllText(Path.Combine(tdx, "notes.txt"), "some notes\n");
      File.WriteAllText(Path.Combine(tdx, "song.txt"), "title: S\ntrack M | instrument: flute\n\\mf c5/1 |\n");
      File.WriteAllText(Path.Combine(tdx, "glued.txt"), "title: S tempo: 90 track M | instrument: flute \\mf c5/1 |");
      ok(!AudioRender.IsScoreText(Path.Combine(tdx, "render-log.txt")) && !AudioRender.IsScoreText(Path.Combine(tdx, "notes.txt")) && AudioRender.IsScoreText(Path.Combine(tdx, "song.txt")) && AudioRender.IsScoreText(Path.Combine(tdx, "glued.txt")),
         "render: render-log.txt and notes are not scores; .txt scores (even run together) still are");
    }
    finally { Directory.Delete(tdx, true); }
  }

  Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
  return fails;
} } }
