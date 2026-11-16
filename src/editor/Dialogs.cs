// Dialogs: write notation, harmony & accompaniment, song settings, help.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Abend
{
    public class NotationDialog : Form
    {
        public List<NoteEv> Result;
        TextBox text = new TextBox(), guide = new TextBox();
        NumericUpDown bar = new NumericUpDown(), tr = new NumericUpDown();
        Label err = new Label();
        Song song; Track track;
        public static string LastText = @"\mp (d4/2 bes4/2 | a4/4. g4/8 f4/4 e4/4 | cis4/2 d4/2) |";

        public NotationDialog(Song s, Track t, int startBar)
        {
            song = s; track = t;
            Text = "Write notation — " + t.Name;
            Size = new Size(900, 560); StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; Font = new Font("Segoe UI", 9.5f);
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 8, 8, 0) };
            top.Controls.Add(new Label { Text = "Start at bar", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            bar.Minimum = 1; bar.Maximum = 9999; bar.Value = Math.Max(1, startBar); bar.Width = 70; top.Controls.Add(bar);
            top.Controls.Add(new Label { Text = "   Transpose (semitones)", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            tr.Minimum = -48; tr.Maximum = 48; tr.Width = 60; top.Controls.Add(tr);
            text.Multiline = true; text.ScrollBars = ScrollBars.Vertical; text.Font = new Font("Consolas", 12f); text.Dock = DockStyle.Fill; text.AcceptsReturn = true; text.AcceptsTab = false;
            text.Text = LastText;
            guide.Multiline = true; guide.ReadOnly = true; guide.ScrollBars = ScrollBars.Vertical; guide.Dock = DockStyle.Right; guide.Width = 330; guide.Font = new Font("Consolas", 9f);
            guide.Text = Help.NotationGuide.Replace("\n", "\r\n");
            err.Dock = DockStyle.Bottom; err.Height = 44; err.ForeColor = Color.FromArgb(170, 30, 30); err.Padding = new Padding(8, 4, 8, 0);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var ok = new Button { Text = "Insert", Width = 100 }; var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            var prev = new Button { Text = "▶ Preview", Width = 100 };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel); buttons.Controls.Add(prev);
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            body.Controls.Add(text); body.Controls.Add(guide);
            Controls.Add(body); Controls.Add(err); Controls.Add(buttons); Controls.Add(top);
            CancelButton = cancel;
            ok.Click += (s2, e) => { if (Parse()) { LastText = text.Text; DialogResult = DialogResult.OK; Close(); } };
            prev.Click += (s2, e) => { if (Parse()) Preview.Play(song, track, Result); };
            FormClosed += (s2, e) => Preview.Stop();
        }

        bool Parse()
        {
            try
            {
                Result = Engine.Notation(song, (int)bar.Value, text.Text, (int)tr.Value);
                err.ForeColor = Color.FromArgb(30, 110, 60);
                err.Text = Result.Count + " notes, ending in bar " + ((Result.Count == 0 ? 0 : Result.Max(n => n.Start + n.Dur) - 1) / song.BarTicks + 1) + ".";
                return true;
            }
            catch (Exception ex)
            {
                err.ForeColor = Color.FromArgb(170, 30, 30);
                err.Text = Help.Friendly(ex.Message);
                return false;
            }
        }
    }

    public class HarmonyDialog : Form
    {
        public List<NoteEv> Result;
        TextBox prog = new TextBox(), lo = new TextBox(), hi = new TextBox();
        ComboBox pattern = new ComboBox();
        NumericUpDown bar = new NumericUpDown(), voices = new NumericUpDown(), vel = new NumericUpDown();
        CheckBox bass = new CheckBox();
        Label err = new Label();
        Song song; Track track;
        public static string LastProg = "Dm/1 | Gm/2 A7/2 | Bb/2 Gm6/Bb/2 | A7/1 | Dm/1 |";
        static readonly string[][] Presets = {
            new[]{"Sustained chords", "*: c"}, new[]{"Repeated eighths (agitato)", "8: c c c c"}, new[]{"Off-beat chords", "8: r c"},
            new[]{"Broken chord", "8: 0 1 2 1"}, new[]{"Rising arpeggio (sixteenths)", "16: 0 1 2 3"}, new[]{"Alberti bass", "8: 0 2 1 2"},
            new[]{"Bass on the beat", "4: B r B r"}, new[]{"Walking roots and fifths", "4: B F B F"}, new[]{"Waltz (bass + two chords)", "4: B c c"},
            new[]{"Tremolo chords", "*: c^tr"}, new[]{"Pizzicato bass", "4: B^pz r r r"} };

        public HarmonyDialog(Song s, Track t, int startBar)
        {
            song = s; track = t;
            Text = "Harmony & accompaniment — " + t.Name;
            Size = new Size(760, 470); StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; Font = new Font("Segoe UI", 9.5f);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Action<string, Control> row = (label, c) => { grid.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }); c.Dock = DockStyle.Fill; grid.Controls.Add(c); };
            prog.Multiline = true; prog.Height = 90; prog.Font = new Font("Consolas", 12f); prog.Text = LastProg; prog.ScrollBars = ScrollBars.Vertical;
            row("Chords (with durations)", prog);
            bar.Minimum = 1; bar.Maximum = 9999; bar.Value = Math.Max(1, startBar); row("Start at bar", bar);
            voices.Minimum = 1; voices.Maximum = 6; voices.Value = 3; row("Number of voices", voices);
            var range = new FlowLayoutPanel { Height = 30 };
            lo.Width = 60; hi.Width = 60;
            int[] r = RangeFor(t); lo.Text = GM.NoteName(r[0]); hi.Text = GM.NoteName(r[1]);
            range.Controls.Add(lo); range.Controls.Add(new Label { Text = "to", AutoSize = true, Padding = new Padding(4, 6, 4, 0) }); range.Controls.Add(hi);
            row("Range", range);
            bass.Text = "Lowest voice plays the chord's bass note (for bass lines)"; row("", bass);
            pattern.DropDownStyle = ComboBoxStyle.DropDown;
            foreach (var p in Presets) pattern.Items.Add(p[1] + "    ← " + p[0]);
            pattern.Text = Presets[1][1];
            pattern.SelectedIndexChanged += (s2, e) => { var x = pattern.SelectedItem as string; if (x != null) BeginInvoke((Action)(() => pattern.Text = x.Split(new[] { "    ←" }, StringSplitOptions.None)[0])); };
            row("Rhythm pattern", pattern);
            vel.Minimum = 1; vel.Maximum = 127; vel.Value = 70; row("Velocity (loudness)", vel);
            var hint = new Label { AutoSize = true, ForeColor = Color.FromArgb(80, 90, 100), Text = "Chords: Dm  G7  Bbmaj7  F#dim7  Esus4  A/C#  Cm7b5 …   Durations: /1 whole, /2 half, /4 quarter, /8 eighth, dots allowed.\r\n`=` extends the previous chord, `|` checks the bar line. Pattern: unit: tokens — digits pick voices (0 = lowest), c = whole chord,\r\nr = rest, B = bass note, F = fifth above bass; add ' for staccato or > for accent." };
            grid.Controls.Add(new Label()); grid.Controls.Add(hint);
            err.Dock = DockStyle.Bottom; err.Height = 36; err.Padding = new Padding(12, 4, 12, 0); err.ForeColor = Color.FromArgb(170, 30, 30);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var ok = new Button { Text = "Insert", Width = 100 }; var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            var prev = new Button { Text = "▶ Preview", Width = 100 };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel); buttons.Controls.Add(prev);
            Controls.Add(grid); Controls.Add(err); Controls.Add(buttons);
            CancelButton = cancel;
            ok.Click += (s2, e) => { if (Run()) { LastProg = prog.Text; DialogResult = DialogResult.OK; Close(); } };
            prev.Click += (s2, e) => { if (Run()) Preview.Play(song, track, Result); };
            FormClosed += (s2, e) => Preview.Stop();
        }

        static int[] RangeFor(Track t)
        {
            for (int i = 0; i < GM.OrchestraNames.Length; i++) if (GM.OrchestraNames[i] == t.Name) return GM.OrchestraRange[i];
            return new[] { 55, 72 };
        }

        bool Run()
        {
            int l, h;
            if (!Engine.TryParseNoteName(lo.Text, out l) || !Engine.TryParseNoteName(hi.Text, out h) || h <= l)
            { err.ForeColor = Color.FromArgb(170, 30, 30); err.Text = "Write the range as two note names, low then high, for example C3 and G4."; return false; }
            try
            {
                string pat = pattern.Text.Split(new[] { "    ←" }, StringSplitOptions.None)[0].Trim();
                if (!pat.Contains(":")) throw new Exception("The pattern needs a unit and tokens, for example \"8: c c c c\".");
                Result = Engine.Harmony(song, (int)bar.Value, prog.Text, (int)voices.Value, l, h, bass.Checked, pat, (int)vel.Value);
                if (Result.Count == 0) throw new Exception("No notes were produced. Check that the range is wide enough for the number of voices.");
                err.ForeColor = Color.FromArgb(30, 110, 60); err.Text = Result.Count + " notes ready.";
                return true;
            }
            catch (Exception ex) { err.ForeColor = Color.FromArgb(170, 30, 30); err.Text = Help.Friendly(ex.Message); return false; }
        }
    }

    // Paste or write a score script, build it, see errors by line.
    public class ScriptDialog : Form
    {
        public Song Result;
        public string ScriptText { get { return text.Text; } }
        TextBox text = new TextBox();
        ListBox problems = new ListBox();
        Label summary = new Label();

        public ScriptDialog(string initial, string title)
        {
            Text = title; Size = new Size(1000, 720); StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; Font = new Font("Segoe UI", 9.5f);
            var intro = new Label { Dock = DockStyle.Top, Height = 48, Padding = new Padding(10, 8, 10, 0), ForeColor = Color.FromArgb(60, 70, 80),
                Text = "Paste a score script here (for example the answer from another AI), or write one. Build checks every bar and opens the piece.\r\nTo get a score from another AI: Copy AI instructions, paste them into the chat with a description of the piece you want, then paste its answer here." };
            text.Multiline = true; text.ScrollBars = ScrollBars.Both; text.WordWrap = false; text.Font = new Font("Consolas", 11f); text.Dock = DockStyle.Fill; text.AcceptsReturn = true; text.AcceptsTab = true;
            text.Text = (initial ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            problems.Dock = DockStyle.Bottom; problems.Height = 120; problems.Font = new Font("Consolas", 9.5f); problems.HorizontalScrollbar = true;
            problems.DoubleClick += (s, e) => GoToLine();
            summary.Dock = DockStyle.Bottom; summary.Height = 26; summary.Padding = new Padding(10, 5, 10, 0);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var build = new Button { Text = "Build && open", Width = 120 };
            var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
            var check = new Button { Text = "Check", Width = 90 };
            var copy = new Button { Text = "Copy AI instructions", Width = 160 };
            var example = new Button { Text = "Load example", Width = 110 };
            var paste = new Button { Text = "Paste", Width = 80 };
            var copyErr = new Button { Text = "Copy errors for the AI", Width = 170 };
            buttons.Controls.Add(build); buttons.Controls.Add(cancel); buttons.Controls.Add(check); buttons.Controls.Add(copyErr); buttons.Controls.Add(copy); buttons.Controls.Add(example); buttons.Controls.Add(paste);
            copyErr.Click += (s, e) =>
            {
                if (problems.Items.Count == 0) { summary.Text = "There are no errors to copy."; return; }
                var sb = new System.Text.StringBuilder("Abend Orchestra could not build the score. Please fix these and send the whole corrected score again in one code block:\r\n\r\n");
                foreach (var it in problems.Items) sb.Append(it).Append("\r\n");
                try { Clipboard.SetText(sb.ToString()); summary.ForeColor = Color.FromArgb(30, 110, 60); summary.Text = "Errors copied. Paste them into the AI chat that wrote the score."; } catch (Exception ex) { summary.Text = ex.Message; }
            };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 4) }; body.Controls.Add(text);
            Controls.Add(body); Controls.Add(summary); Controls.Add(problems); Controls.Add(buttons); Controls.Add(intro);
            CancelButton = cancel;
            build.Click += (s, e) => { if (Run()) { DialogResult = DialogResult.OK; Close(); } };
            check.Click += (s, e) => Run();
            copy.Click += (s, e) => { try { Clipboard.SetText(AiGuide.Text.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n")); summary.ForeColor = Color.FromArgb(30, 110, 60); summary.Text = "AI instructions copied. Paste them into any AI chat together with what you want it to write."; } catch (Exception ex) { summary.Text = ex.Message; } };
            example.Click += (s, e) => { text.Text = AiGuide.Example.Replace("\r\n", "\n").Replace("\n", "\r\n"); Run(); };
            paste.Click += (s, e) => { if (Clipboard.ContainsText()) { text.Text = Clipboard.GetText().Replace("\r\n", "\n").Replace("\n", "\r\n"); Run(); } };
            Shown += (s, e) => { if (text.Text.Trim().Length > 0) Run(); };
        }

        bool Run()
        {
            var r = ScoreScript.Parse(text.Text);
            if (r.RepairedText != null) text.Text = r.RepairedText.Replace("\n", "\r\n");   // so line numbers match what is shown
            problems.Items.Clear();
            foreach (var er in r.Errors) problems.Items.Add("error: " + er);
            foreach (var w in r.Warnings) problems.Items.Add("warning: " + w);
            if (r.Ok) { summary.ForeColor = Color.FromArgb(30, 110, 60); summary.Text = "Ready: " + r.Summary() + (r.Warnings.Count > 0 ? "  (" + r.Warnings.Count + " warning(s))" : ""); Result = r.Song; }
            else { summary.ForeColor = Color.FromArgb(170, 30, 30); summary.Text = r.Errors.Count + " error(s). Double-click an error to jump to its line, or send the list back to the AI that wrote the score."; Result = null; }
            return r.Ok;
        }

        void GoToLine()
        {
            var s = problems.SelectedItem as string; if (s == null) return;
            var m = System.Text.RegularExpressions.Regex.Match(s, @"line (\d+)");
            if (!m.Success) return;
            int line = int.Parse(m.Groups[1].Value) - 1;
            if (line < 0 || line >= text.Lines.Length) return;
            int pos = text.GetFirstCharIndexFromLine(line);
            text.Focus(); text.Select(pos, text.Lines[line].Length); text.ScrollToCaret();
        }
    }

    public class GuideDialog : Form
    {
        public GuideDialog()
        {
            Text = "Instructions for AI"; Size = new Size(860, 720); StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; Font = new Font("Segoe UI", 9.5f);
            var intro = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 10, 10, 0), Text = "Give these instructions to any AI chat (ChatGPT, Claude, Gemini…) along with the piece you want. Then paste its answer into File > New from score script." };
            var box = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10f), BackColor = Color.White, Text = AiGuide.Text.Replace("\r\n", "\n").Replace("\n", "\r\n") };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var close = new Button { Text = "Close", Width = 90, DialogResult = DialogResult.Cancel };
            var copy = new Button { Text = "Copy to clipboard", Width = 150 };
            var save = new Button { Text = "Save as file…", Width = 120 };
            var status = new Label { AutoSize = true, Padding = new Padding(0, 8, 12, 0), ForeColor = Color.FromArgb(30, 110, 60) };
            buttons.Controls.Add(close); buttons.Controls.Add(copy); buttons.Controls.Add(save); buttons.Controls.Add(status);
            copy.Click += (s, e) => { try { Clipboard.SetText(box.Text); status.Text = "Copied."; } catch (Exception ex) { status.Text = ex.Message; } };
            save.Click += (s, e) =>
            {
                using (var d = new SaveFileDialog { Filter = "Markdown|*.md|Text|*.txt", FileName = "AI-GUIDE.md" })
                    if (d.ShowDialog(this) == DialogResult.OK) { System.IO.File.WriteAllText(d.FileName, AiGuide.Text); status.Text = "Saved."; }
            };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0) }; body.Controls.Add(box);
            Controls.Add(body); Controls.Add(buttons); Controls.Add(intro);
            CancelButton = close;
        }
    }

    public class SongDialog : Form
    {
        public SongDialog(Song s)
        {
            Text = "Song settings"; Size = new Size(380, 250); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; Font = new Font("Segoe UI", 9.5f);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
            var title = new TextBox { Text = s.Title, Width = 200 };
            var bpm = new NumericUpDown { Minimum = 20, Maximum = 300, DecimalPlaces = 1, Value = (decimal)Math.Max(20, Math.Min(300, s.BaseBpm)) };
            var num = new NumericUpDown { Minimum = 1, Maximum = 16, Value = s.Num };
            var den = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }; den.Items.AddRange(new object[] { "2", "4", "8", "16" }); den.SelectedItem = s.Den.ToString();
            if (den.SelectedIndex < 0) den.SelectedIndex = 1;
            grid.Controls.Add(new Label { Text = "Title", AutoSize = true }); grid.Controls.Add(title);
            grid.Controls.Add(new Label { Text = "Tempo (quarter notes per minute)", AutoSize = true }); grid.Controls.Add(bpm);
            grid.Controls.Add(new Label { Text = "Beats per bar", AutoSize = true }); grid.Controls.Add(num);
            grid.Controls.Add(new Label { Text = "Beat unit", AutoSize = true }); grid.Controls.Add(den);
            var note = new Label { AutoSize = true, ForeColor = Color.Gray, Text = s.Tempos.Count > 1 ? "This song has " + s.Tempos.Count + " tempo changes; only the first is edited here." : "" };
            grid.Controls.Add(new Label()); grid.Controls.Add(note);
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
            Controls.Add(grid); Controls.Add(ok); AcceptButton = ok;
            FormClosing += (s2, e) =>
            {
                if (DialogResult != DialogResult.OK) return;
                s.Title = title.Text; s.BaseBpm = (double)bpm.Value; s.Num = (int)num.Value; s.Den = int.Parse((string)den.SelectedItem);
            };
        }
    }

    // plays a set of notes on a track's instrument, for dialog previews
    public static class Preview
    {
        static Player p = new Player();
        public static void Play(Song song, Track t, List<NoteEv> notes)
        {
            if (notes == null || notes.Count == 0) return;
            var s = song.Clone();
            var tr = t.Clone(); tr.Mute = false; tr.Solo = false;
            int start = notes.Min(n => n.Start);
            tr.Notes = notes.Select(n => { var c = n.Clone(); return c; }).ToList();
            s.Tracks = new List<Track> { tr };
            try { p.Play(s, start); } catch (Exception ex) { MessageBox.Show(ex.Message, "Playback"); }
        }
        public static void Stop() { p.Stop(); }
    }
}
