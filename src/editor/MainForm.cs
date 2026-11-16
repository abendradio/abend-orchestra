// Main window of Abend Orchestra.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Abend
{
    public static class App
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 1 && args[0] == "--selftest") { SelfTest(args[1]); return; }
            var f = new MainForm();
            if (args.Length > 0 && File.Exists(args[0])) f.OpenPath(args[0]);
            Application.Run(f);
        }

        // opens the window, exercises the editor without sound, logs the outcome, then closes
        static void SelfTest(string midi)
        {
            string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log");
            try
            {
                var f = new MainForm();
                f.Shown += (s, e) =>
                {
                    try { f.RunSelfTest(midi, log); }
                    catch (Exception ex) { File.AppendAllText(log, "FAIL " + ex + "\r\n"); }
                    f.BeginInvoke((Action)(() => { f.ForceClose(); }));
                };
                Application.Run(f);
            }
            catch (Exception ex) { File.AppendAllText(log, "FAIL " + ex + "\r\n"); }
        }
    }

    public class MainForm : Form
    {
        Song song = DefaultSong();
        string path;
        bool dirty;
        Stack<Song> undo = new Stack<Song>(), redo = new Stack<Song>();
        Player player = new Player();
        PianoRoll roll = new PianoRoll();
        ListBox tracks = new ListBox();
        TextBox tName = new TextBox();
        ComboBox tInst = new ComboBox(), snap = new ComboBox(), len = new ComboBox();
        TrackBar tVol = new TrackBar(), tPan = new TrackBar();
        CheckBox tMute = new CheckBox(), tSolo = new CheckBox();
        NumericUpDown vel = new NumericUpDown(), bpm = new NumericUpDown();
        ToolStripButton playBtn;
        ToolStripLabel posLabel = new ToolStripLabel();
        ToolStripStatusLabel status = new ToolStripStatusLabel();
        Timer ui = new Timer { Interval = 30 };
        bool loadingTrack;

        static readonly int[] SnapTicks = { 1920, 960, 480, 240, 120, 60, 320, 160, 1 };
        static readonly string[] SnapNames = { "Bar (4/4)", "1/2", "1/4", "1/8", "1/16", "1/32", "1/4 triplet", "1/8 triplet", "Off" };
        static readonly int[] LenTicks = { 1920, 1440, 960, 720, 480, 360, 240, 120, 160 };
        static readonly string[] LenNames = { "Whole", "Dotted half", "Half", "Dotted quarter", "Quarter", "Dotted eighth", "Eighth", "Sixteenth", "Eighth triplet" };

        static Song DefaultSong()
        {
            var s = new Song { Title = "Untitled" };
            int[] idx = { 10, 11, 12, 13, 14 };
            int ch = 0;
            foreach (var i in idx) { s.Tracks.Add(new Track { Name = GM.OrchestraNames[i], Program = GM.Orchestra[i][0], Channel = ch++ }); }
            return s;
        }

        public MainForm()
        {
            Text = "Abend Orchestra";
            Size = new Size(1280, 820);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            KeyPreview = true;
            Icon = SystemIcons.Application;

            // ----- menu -----
            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(Item("&New", Keys.Control | Keys.N, (s, e) => NewSong()));
            file.DropDownItems.Add(Item("New from &score script (paste from AI)…", Keys.Control | Keys.Shift | Keys.N, (s, e) => NewFromScript(null)));
            file.DropDownItems.Add(Item("&Open…", Keys.Control | Keys.O, (s, e) => OpenDialog()));
            file.DropDownItems.Add(Item("&Save", Keys.Control | Keys.S, (s, e) => Save(false)));
            file.DropDownItems.Add(Item("Save &as…", Keys.Control | Keys.Shift | Keys.S, (s, e) => Save(true)));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("&Import MIDI…", Keys.Control | Keys.I, (s, e) => ImportDialog()));
            file.DropDownItems.Add(Item("&Export MIDI…", Keys.Control | Keys.E, (s, e) => ExportDialog()));
            file.DropDownItems.Add(Item("Export au&dio (MP3, radio-ready)…", Keys.Control | Keys.Shift | Keys.E, (s, e) => ExportAudio()));
            var examples = new ToolStripMenuItem("Open e&xample");
            string exDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples");
            if (Directory.Exists(exDir))
                foreach (var f in Directory.GetFiles(exDir).Where(x => x.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".abend", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x))
                {
                    string fp = f;
                    examples.DropDownItems.Add(Item(Path.GetFileNameWithoutExtension(f), Keys.None, (s, e) => { if (ConfirmDiscard()) OpenPath(fp); }));
                }
            if (examples.DropDownItems.Count == 0) examples.Enabled = false;
            file.DropDownItems.Add(examples);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("E&xit", Keys.None, (s, e) => Close()));
            var edit = new ToolStripMenuItem("&Edit");
            edit.DropDownItems.Add(Item("&Undo", Keys.Control | Keys.Z, (s, e) => Undo()));
            edit.DropDownItems.Add(Item("&Redo", Keys.Control | Keys.Y, (s, e) => Redo()));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("Cu&t", Keys.Control | Keys.X, (s, e) => roll.Cut()));
            edit.DropDownItems.Add(Item("&Copy", Keys.Control | Keys.C, (s, e) => roll.Copy()));
            edit.DropDownItems.Add(Item("&Paste at cursor", Keys.Control | Keys.V, (s, e) => roll.Paste()));
            edit.DropDownItems.Add(Item("&Delete", Keys.Delete, (s, e) => roll.DeleteSelected()));
            edit.DropDownItems.Add(Item("Select &all", Keys.Control | Keys.A, (s, e) => roll.SelectAll()));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("Transpose up an octave", Keys.Shift | Keys.Up, (s, e) => roll.Transpose(12)));
            edit.DropDownItems.Add(Item("Transpose down an octave", Keys.Shift | Keys.Down, (s, e) => roll.Transpose(-12)));
            edit.DropDownItems.Add(Item("&Quantize to snap", Keys.Control | Keys.Q, (s, e) => roll.Quantize()));
            var insert = new ToolStripMenuItem("&Compose");
            insert.DropDownItems.Add(Item("Write &notation…", Keys.Control | Keys.T, (s, e) => WriteNotation()));
            insert.DropDownItems.Add(Item("&Harmony && accompaniment…", Keys.Control | Keys.H, (s, e) => WriteHarmony()));
            insert.DropDownItems.Add(new ToolStripSeparator());
            insert.DropDownItems.Add(Item("Add the full &orchestra", Keys.None, (s, e) => AddOrchestra()));
            insert.DropDownItems.Add(Item("Song &settings (title, tempo, metre)…", Keys.None, (s, e) => SongSettings()));
            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(Item("&Notation guide", Keys.F1, (s, e) => ShowText("Notation guide", Help.NotationGuide)));
            help.DropDownItems.Add(Item("Instructions for &AI (copy into any AI chat)…", Keys.None, (s, e) => { using (var d = new GuideDialog()) d.ShowDialog(this); }));
            help.DropDownItems.Add(Item("&Keyboard and mouse", Keys.None, (s, e) => ShowText("Keyboard and mouse", Help.Shortcuts)));
            help.DropDownItems.Add(Item("&About", Keys.None, (s, e) => MessageBox.Show(this, "Abend Orchestra 1.0\n\nA composing workbench built from the engine that wrote the symphony \"Per aspera\" by Anselm Abend (Claude).\n\nPlayback uses the synthesizer built into Windows. Export MIDI to hear your music with better orchestral sounds in a notation program or DAW.", "About")));
            menu.Items.AddRange(new ToolStripItem[] { file, edit, insert, help });

            // ----- toolbar -----
            var tool = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2), ImageScalingSize = new Size(20, 20) };
            tool.Items.Add(new ToolStripButton("⏮", null, (s, e) => { roll.CursorTick = 0; roll.Invalidate(); UpdatePos(); }) { ToolTipText = "Cursor to start (Home)", Font = new Font("Segoe UI Symbol", 11f) });
            playBtn = new ToolStripButton("▶  Play", null, (s, e) => TogglePlay()) { ToolTipText = "Play or stop from the cursor (Space)", Font = new Font("Segoe UI Symbol", 10f, FontStyle.Bold) };
            tool.Items.Add(playBtn);
            tool.Items.Add(new ToolStripSeparator());
            posLabel.Font = new Font("Consolas", 10f); posLabel.Text = "1.1   0:00";
            tool.Items.Add(posLabel);
            tool.Items.Add(new ToolStripSeparator());
            tool.Items.Add(new ToolStripLabel("Tempo"));
            bpm.Minimum = 20; bpm.Maximum = 300; bpm.DecimalPlaces = 0; bpm.Width = 60; bpm.Value = 100;
            bpm.ValueChanged += (s, e) => { if (loadingTrack) return; Checkpoint(); song.BaseBpm = (double)bpm.Value; MarkDirty(); };
            tool.Items.Add(new ToolStripControlHost(bpm));
            tool.Items.Add(new ToolStripSeparator());
            tool.Items.Add(new ToolStripLabel("Snap"));
            snap.DropDownStyle = ComboBoxStyle.DropDownList; snap.Items.AddRange(SnapNames); snap.SelectedIndex = 4; snap.Width = 100;
            snap.SelectedIndexChanged += (s, e) => { roll.Snap = snap.SelectedIndex == 0 ? song.BarTicks : SnapTicks[snap.SelectedIndex]; roll.Invalidate(); };
            tool.Items.Add(new ToolStripControlHost(snap));
            tool.Items.Add(new ToolStripLabel("  New note"));
            len.DropDownStyle = ComboBoxStyle.DropDownList; len.Items.AddRange(LenNames); len.SelectedIndex = 4; len.Width = 110;
            len.SelectedIndexChanged += (s, e) => roll.NewLen = LenTicks[len.SelectedIndex];
            tool.Items.Add(new ToolStripControlHost(len));
            tool.Items.Add(new ToolStripLabel("  Velocity"));
            vel.Minimum = 1; vel.Maximum = 127; vel.Value = 90; vel.Width = 55;
            vel.ValueChanged += (s, e) => { roll.NewVel = (int)vel.Value; if (roll.Selection.Count > 0 && vel.Focused) roll.SetVelocity((int)vel.Value); };
            tool.Items.Add(new ToolStripControlHost(vel));
            tool.Items.Add(new ToolStripSeparator());
            tool.Items.Add(new ToolStripButton("−", null, (s, e) => roll.Zoom(0.8)) { ToolTipText = "Zoom out (Ctrl+wheel)", Font = new Font("Segoe UI", 11f, FontStyle.Bold) });
            tool.Items.Add(new ToolStripButton("+", null, (s, e) => roll.Zoom(1.25)) { ToolTipText = "Zoom in (Ctrl+wheel)", Font = new Font("Segoe UI", 11f, FontStyle.Bold) });
            tool.Items.Add(new ToolStripSeparator());
            tool.Items.Add(new ToolStripButton("✎ Notation", null, (s, e) => WriteNotation()) { ToolTipText = "Write notes as text (Ctrl+T)" });
            tool.Items.Add(new ToolStripButton("♫ Harmony", null, (s, e) => WriteHarmony()) { ToolTipText = "Turn chord symbols into accompaniment (Ctrl+H)" });

            // ----- track panel -----
            var left = new Panel { Dock = DockStyle.Left, Width = 260, Padding = new Padding(8) };
            var lt = new Label { Text = "TRACKS", Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Color.FromArgb(70, 80, 90) };
            tracks.Dock = DockStyle.Fill; tracks.IntegralHeight = false; tracks.DrawMode = DrawMode.OwnerDrawFixed; tracks.ItemHeight = 36;
            tracks.DrawItem += DrawTrack;
            tracks.SelectedIndexChanged += (s, e) => SelectTrack(tracks.SelectedIndex);
            var tb = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34 };
            tb.Controls.Add(SmallBtn("Add", (s, e) => AddTrack()));
            tb.Controls.Add(SmallBtn("Remove", (s, e) => RemoveTrack()));
            tb.Controls.Add(SmallBtn("▲", (s, e) => MoveTrack(-1), 34));
            tb.Controls.Add(SmallBtn("▼", (s, e) => MoveTrack(1), 34));
            var props = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 200, ColumnCount = 2 };
            props.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70)); props.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tName.Dock = DockStyle.Fill;
            tName.TextChanged += (s, e) => { var t = CurTrack; if (t == null || loadingTrack) return; t.Name = tName.Text; tracks.Invalidate(); MarkDirty(); };
            tInst.DropDownStyle = ComboBoxStyle.DropDownList; tInst.Dock = DockStyle.Fill; tInst.DropDownWidth = 220;
            for (int i = 0; i < 128; i++) tInst.Items.Add((i + 1) + "  " + GM.Names[i]);
            tInst.Items.Add("Drums (channel 10)");
            tInst.SelectedIndexChanged += (s, e) =>
            {
                var t = CurTrack; if (t == null || loadingTrack) return;
                Checkpoint();
                if (tInst.SelectedIndex == 128) t.Channel = 9;
                else { if (t.IsDrums) t.Channel = song.NextFreeChannel(); t.Program = tInst.SelectedIndex; }
                tracks.Invalidate(); MarkDirty();
            };
            tVol.Minimum = 0; tVol.Maximum = 127; tVol.TickStyle = TickStyle.None; tVol.AutoSize = false; tVol.Height = 28; tVol.Dock = DockStyle.Fill;
            tVol.ValueChanged += (s, e) => { var t = CurTrack; if (t == null || loadingTrack) return; t.Volume = tVol.Value; MarkDirty(); };
            tPan.Minimum = 0; tPan.Maximum = 127; tPan.TickStyle = TickStyle.None; tPan.AutoSize = false; tPan.Height = 28; tPan.Dock = DockStyle.Fill;
            tPan.ValueChanged += (s, e) => { var t = CurTrack; if (t == null || loadingTrack) return; t.Pan = tPan.Value; MarkDirty(); };
            tMute.Text = "Mute"; tSolo.Text = "Solo";
            tMute.CheckedChanged += (s, e) => { var t = CurTrack; if (t == null || loadingTrack) return; t.Mute = tMute.Checked; tracks.Invalidate(); MarkDirty(); };
            tSolo.CheckedChanged += (s, e) => { var t = CurTrack; if (t == null || loadingTrack) return; t.Solo = tSolo.Checked; tracks.Invalidate(); MarkDirty(); };
            var ms = new FlowLayoutPanel { Dock = DockStyle.Fill }; ms.Controls.Add(tMute); ms.Controls.Add(tSolo);
            props.Controls.Add(new Label { Text = "Name", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }); props.Controls.Add(tName);
            props.Controls.Add(new Label { Text = "Sound", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }); props.Controls.Add(tInst);
            props.Controls.Add(new Label { Text = "Volume", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }); props.Controls.Add(tVol);
            props.Controls.Add(new Label { Text = "Pan", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }); props.Controls.Add(tPan);
            props.Controls.Add(new Label()); props.Controls.Add(ms);
            left.Controls.Add(tracks); left.Controls.Add(lt); left.Controls.Add(tb); left.Controls.Add(props);

            // ----- roll -----
            roll.Dock = DockStyle.Fill;
            roll.BeforeEdit += Checkpoint;
            roll.Edited += () => { MarkDirty(); tracks.Invalidate(); };
            roll.SelectionChanged += () => { status.Text = roll.Selection.Count == 0 ? "" : roll.Selection.Count + " note(s) selected"; if (roll.Selection.Count == 1) { loadingTrack = true; vel.Value = roll.Selection.First().Vel; loadingTrack = false; } };
            roll.CursorMoved += UpdatePos;

            var statusStrip = new StatusStrip(); statusStrip.Items.Add(status);
            Controls.Add(roll); Controls.Add(new Splitter { Dock = DockStyle.Left, Width = 4 }); Controls.Add(left);
            Controls.Add(tool); Controls.Add(menu); Controls.Add(statusStrip);
            MainMenuStrip = menu;

            ui.Tick += (s, e) =>
            {
                if (!player.Playing) { if (roll.PlayTick >= 0) { roll.PlayTick = -1; SetPlayButton(); roll.Invalidate(); } return; }
                roll.PlayTick = player.CurrentTick; roll.FollowPlayhead(); roll.Invalidate(); UpdatePos();
            };
            ui.Start();
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => { var fs = e.Data.GetData(DataFormats.FileDrop) as string[]; if (fs != null && fs.Length > 0 && ConfirmDiscard()) OpenPath(fs[0]); };
            player.Finished += () => BeginInvoke((Action)(() => { SetPlayButton(); }));
            FormClosing += (s, e) => { if (!ConfirmDiscard()) e.Cancel = true; else { player.Stop(); MidiOut.Close(); } };
            Shown += (s, e) => { LoadSong(song); roll.ScrollToPitch(64); status.Text = "Click the grid to add notes, or use Compose → Write notation. Space plays from the cursor."; };
        }

        ToolStripMenuItem Item(string text, Keys keys, EventHandler h)
        {
            var it = new ToolStripMenuItem(text, null, h);
            if (keys != Keys.None)
            {
                bool plain = keys == Keys.Delete || (keys & Keys.Modifiers) == Keys.Shift;
                if (plain) it.ShortcutKeyDisplayString = keys == Keys.Delete ? "Del" : "Shift+" + (keys & Keys.KeyCode);
                else it.ShortcutKeys = keys;
            }
            return it;
        }
        Button SmallBtn(string t, EventHandler h, int w = 70) { var b = new Button { Text = t, Width = w, Height = 26 }; b.Click += h; return b; }
        Track CurTrack { get { int i = tracks.SelectedIndex; return i >= 0 && i < song.Tracks.Count ? song.Tracks[i] : null; } }

        void DrawTrack(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= song.Tracks.Count) return;
            var t = song.Tracks[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? Color.FromArgb(38, 80, 138) : (e.Index % 2 == 0 ? Color.White : Color.FromArgb(246, 248, 248)))) e.Graphics.FillRectangle(bg, e.Bounds);
            var fg = sel ? Color.White : Color.FromArgb(25, 32, 43);
            var sub = sel ? Color.FromArgb(210, 225, 245) : Color.FromArgb(100, 110, 120);
            string flags = (t.Mute ? "  · muted" : "") + (t.Solo ? "  · solo" : "");
            using (var f1 = new Font("Segoe UI", 9.5f, FontStyle.Bold)) using (var b1 = new SolidBrush(fg)) e.Graphics.DrawString(t.Name + flags, f1, b1, e.Bounds.X + 6, e.Bounds.Y + 2);
            using (var b2 = new SolidBrush(sub)) e.Graphics.DrawString((t.IsDrums ? "Drums" : GM.Names[t.Program]) + " · " + t.Notes.Count + " notes", Font, b2, e.Bounds.X + 6, e.Bounds.Y + 19);
        }

        // ----- song state -----
        void LoadSong(Song s)
        {
            player.Stop();
            song = s;
            roll.Song = s; roll.Selection.Clear();
            loadingTrack = true;
            bpm.Value = (decimal)Math.Max(20, Math.Min(300, Math.Round(s.BaseBpm)));
            loadingTrack = false;
            RefreshTracks(Math.Min(Math.Max(0, roll.TrackIndex), s.Tracks.Count - 1));
            roll.UpdateScroll();
            UpdateTitle(); UpdatePos();
        }
        void RefreshTracks(int sel)
        {
            tracks.BeginUpdate(); tracks.Items.Clear();
            foreach (var t in song.Tracks) tracks.Items.Add(t.Name);
            tracks.EndUpdate();
            if (song.Tracks.Count > 0) tracks.SelectedIndex = Math.Max(0, Math.Min(sel, song.Tracks.Count - 1));
            else SelectTrack(-1);
        }
        void SelectTrack(int i)
        {
            roll.TrackIndex = i; roll.Selection.Clear();
            var t = CurTrack;
            loadingTrack = true;
            bool has = t != null;
            tName.Enabled = tInst.Enabled = tVol.Enabled = tPan.Enabled = tMute.Enabled = tSolo.Enabled = has;
            if (has)
            {
                tName.Text = t.Name; tInst.SelectedIndex = t.IsDrums ? 128 : t.Program; tVol.Value = t.Volume; tPan.Value = t.Pan; tMute.Checked = t.Mute; tSolo.Checked = t.Solo;
                if (t.Notes.Count > 0) roll.ScrollToPitch((int)t.Notes.Average(n => n.Pitch));
            }
            loadingTrack = false;
            roll.Invalidate();
        }
        void Checkpoint() { undo.Push(song.Clone()); redo.Clear(); if (undo.Count > 200) { var keep = undo.Take(150).Reverse().ToList(); undo = new Stack<Song>(keep); } }
        void Undo() { if (undo.Count == 0) return; redo.Push(song.Clone()); int ti = roll.TrackIndex; var s = undo.Pop(); roll.TrackIndex = ti; LoadSong(s); MarkDirty(); }
        void Redo() { if (redo.Count == 0) return; undo.Push(song.Clone()); int ti = roll.TrackIndex; var s = redo.Pop(); roll.TrackIndex = ti; LoadSong(s); MarkDirty(); }
        void MarkDirty() { dirty = true; UpdateTitle(); }
        void UpdateTitle() { Text = (dirty ? "• " : "") + song.Title + " — Abend Orchestra"; }
        void UpdatePos()
        {
            int t = player.Playing ? roll.PlayTick : roll.CursorTick;
            int bar = t / song.BarTicks + 1, beat = (t % song.BarTicks) / (Song.PPQ * 4 / song.Den) + 1;
            double sec = song.TickToMs(t) / 1000.0;
            posLabel.Text = bar + "." + beat + "   " + ((int)sec / 60) + ":" + ((int)sec % 60).ToString("00");
        }

        // ----- tracks -----
        void AddTrack()
        {
            Checkpoint();
            var t = new Track { Name = "Track " + (song.Tracks.Count + 1), Program = 0, Channel = song.NextFreeChannel() };
            song.Tracks.Add(t); RefreshTracks(song.Tracks.Count - 1); MarkDirty();
            if (song.Tracks.Count(x => !x.IsDrums) > 15) status.Text = "More than 15 instrument tracks: some now share a MIDI channel and will use the same sound.";
        }
        void RemoveTrack()
        {
            var t = CurTrack; if (t == null) return;
            if (t.Notes.Count > 0 && MessageBox.Show(this, "Remove \"" + t.Name + "\" and its " + t.Notes.Count + " notes?", "Remove track", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            Checkpoint(); int i = tracks.SelectedIndex; song.Tracks.RemoveAt(i); RefreshTracks(i); MarkDirty();
        }
        void MoveTrack(int d)
        {
            int i = tracks.SelectedIndex, j = i + d; if (i < 0 || j < 0 || j >= song.Tracks.Count) return;
            Checkpoint(); var t = song.Tracks[i]; song.Tracks.RemoveAt(i); song.Tracks.Insert(j, t); RefreshTracks(j); MarkDirty();
        }
        void AddOrchestra()
        {
            Checkpoint();
            for (int i = 0; i < GM.OrchestraNames.Length; i++)
            {
                if (song.Tracks.Any(t => t.Name == GM.OrchestraNames[i])) continue;
                song.Tracks.Add(new Track { Name = GM.OrchestraNames[i], Program = GM.Orchestra[i][0], Channel = song.NextFreeChannel() });
            }
            var order = GM.OrchestraNames.ToList();
            song.Tracks = song.Tracks.OrderBy(t => order.IndexOf(t.Name) < 0 ? 99 : order.IndexOf(t.Name)).ToList();
            RefreshTracks(0); MarkDirty();
            status.Text = "Added the orchestra of Per aspera: woodwinds, brass, timpani, harp and strings.";
        }

        // ----- compose tools -----
        int CursorBar { get { return roll.CursorTick / song.BarTicks + 1; } }
        void WriteNotation()
        {
            var t = CurTrack; if (t == null) { status.Text = "Add a track first."; return; }
            player.Stop();
            using (var d = new NotationDialog(song, t, CursorBar))
                if (d.ShowDialog(this) == DialogResult.OK) { roll.InsertNotes(d.Result); status.Text = d.Result.Count + " notes written into " + t.Name + "."; }
        }
        void WriteHarmony()
        {
            var t = CurTrack; if (t == null) { status.Text = "Add a track first."; return; }
            player.Stop();
            using (var d = new HarmonyDialog(song, t, CursorBar))
                if (d.ShowDialog(this) == DialogResult.OK) { roll.InsertNotes(d.Result); status.Text = d.Result.Count + " accompaniment notes written into " + t.Name + "."; }
        }
        void SongSettings()
        {
            using (var d = new SongDialog(song))
            {
                var before = song.Clone();
                if (d.ShowDialog(this) == DialogResult.OK) { undo.Push(before); redo.Clear(); LoadSong(song); MarkDirty(); }
            }
        }

        // ----- playback -----
        void TogglePlay()
        {
            if (player.Playing) { player.Stop(); roll.PlayTick = -1; SetPlayButton(); roll.Invalidate(); UpdatePos(); return; }
            try { player.Play(song, roll.CursorTick); SetPlayButton(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Playback"); }
        }
        void SetPlayButton() { playBtn.Text = player.Playing ? "■  Stop" : "▶  Play"; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool typing = ActiveControl is TextBox || ActiveControl is NumericUpDown || (ActiveControl is ComboBox) || (ActiveControl is UpDownBase);
            if (!typing)
            {
                switch (keyData)
                {
                    case Keys.Space: TogglePlay(); return true;
                    case Keys.Up: roll.Transpose(1); return true;
                    case Keys.Down: roll.Transpose(-1); return true;
                    case Keys.Shift | Keys.Up: roll.Transpose(12); return true;
                    case Keys.Shift | Keys.Down: roll.Transpose(-12); return true;
                    case Keys.Left: roll.Nudge(-Math.Max(30, roll.Snap)); return true;
                    case Keys.Right: roll.Nudge(Math.Max(30, roll.Snap)); return true;
                    case Keys.Delete: roll.DeleteSelected(); return true;
                    case Keys.Home: roll.CursorTick = 0; roll.Invalidate(); UpdatePos(); return true;
                    case Keys.Escape: roll.ClearSelection(); return true;
                }
            }
            else if (keyData == (Keys.Control | Keys.A) || keyData == (Keys.Control | Keys.C) || keyData == (Keys.Control | Keys.V) || keyData == (Keys.Control | Keys.X) || keyData == (Keys.Control | Keys.Z))
                return false; // let the text box handle its own clipboard and undo keys
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ----- files -----
        bool ConfirmDiscard()
        {
            if (!dirty) return true;
            var r = MessageBox.Show(this, "Save changes to \"" + song.Title + "\"?", "Abend Orchestra", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return Save(false);
            return true;
        }
        void NewSong() { if (!ConfirmDiscard()) return; path = null; undo.Clear(); redo.Clear(); roll.TrackIndex = 0; roll.CursorTick = 0; LoadSong(DefaultSong()); dirty = false; UpdateTitle(); }
        void OpenDialog()
        {
            if (!ConfirmDiscard()) return;
            using (var d = new OpenFileDialog { Filter = "Projects, score scripts and MIDI|*.abend;*.abscore;*.txt;*.mid;*.midi|Abend project|*.abend|Score script|*.abscore;*.txt|MIDI file|*.mid;*.midi" })
                if (d.ShowDialog(this) == DialogResult.OK) OpenPath(d.FileName);
        }
        void ImportDialog()
        {
            if (!ConfirmDiscard()) return;
            using (var d = new OpenFileDialog { Filter = "MIDI file|*.mid;*.midi" })
                if (d.ShowDialog(this) == DialogResult.OK) OpenPath(d.FileName);
        }
        void NewFromScript(string initial)
        {
            if (!ConfirmDiscard()) return;
            player.Stop();
            using (var d = new ScriptDialog(initial ?? "", "New from score script"))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null) return;
                path = null; undo.Clear(); redo.Clear(); roll.TrackIndex = 0; roll.CursorTick = 0;
                LoadSong(d.Result); dirty = true; UpdateTitle();
                status.Text = "Built " + d.Result.Title + " from the score script: " + d.Result.Tracks.Count + " tracks. Press Space to listen; save it as a project to keep it.";
            }
        }

        public void OpenPath(string p)
        {
            if (p.EndsWith(".abscore", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                string txt;
                try { txt = File.ReadAllText(p); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open"); return; }
                var r = ScoreScript.Parse(txt);
                if (r.Ok)
                {
                    path = null; undo.Clear(); redo.Clear(); roll.TrackIndex = 0; roll.CursorTick = 0;
                    LoadSong(r.Song); dirty = false; UpdateTitle();
                    status.Text = "Built " + Path.GetFileName(p) + ": " + r.Summary() + (r.Warnings.Count > 0 ? " · " + r.Warnings.Count + " warning(s): " + r.Warnings[0] : "");
                }
                else if (IsHandleCreated) BeginInvoke((Action)(() => NewFromScript(txt)));
                else Shown += (s, e) => NewFromScript(txt);
                return;
            }
            try
            {
                bool midi = p.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".midi", StringComparison.OrdinalIgnoreCase);
                var s = midi ? MidiFile.Read(p) : Song.Load(p);
                path = midi ? null : p;
                undo.Clear(); redo.Clear(); roll.TrackIndex = 0; roll.CursorTick = 0;
                LoadSong(s); dirty = false; UpdateTitle();
                status.Text = "Opened " + Path.GetFileName(p) + ": " + s.Tracks.Count + " tracks, " + s.Tracks.Sum(t => t.Notes.Count) + " notes.";
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not open " + Path.GetFileName(p) + ".\n\n" + ex.Message, "Open"); }
        }
        bool Save(bool asNew)
        {
            if (path == null || asNew)
            {
                using (var d = new SaveFileDialog { Filter = "Abend project|*.abend", FileName = song.Title + ".abend" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return false;
                    path = d.FileName;
                }
            }
            try { song.Save(path); dirty = false; UpdateTitle(); status.Text = "Saved " + Path.GetFileName(path) + "."; return true; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save"); return false; }
        }
        void ExportDialog()
        {
            using (var d = new SaveFileDialog { Filter = "MIDI file|*.mid", FileName = song.Title + ".mid" })
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    try { MidiFile.Write(song, d.FileName); status.Text = "Exported " + Path.GetFileName(d.FileName) + "."; }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export"); }
                }
        }
        bool forceClose;
        public void ForceClose() { forceClose = true; dirty = false; Close(); }

        public void RunSelfTest(string midi, string log)
        {
            Action<string> L = m => File.AppendAllText(log, m + "\r\n");
            File.WriteAllText(log, "");
            L("window " + ClientSize.Width + "x" + ClientSize.Height + ", tracks " + song.Tracks.Count);
            OpenPath(midi);
            L("opened: " + song.Title + ", " + tracks.Items.Count + " tracks listed");
            tracks.SelectedIndex = Math.Min(10, song.Tracks.Count - 1);
            roll.Zoom(1.25); roll.Refresh();
            var bmp = new Bitmap(roll.Width, roll.Height); roll.DrawToBitmap(bmp, new Rectangle(0, 0, roll.Width, roll.Height));
            bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest_roll.png"));
            var full = new Bitmap(Width, Height); DrawToBitmap(full, new Rectangle(0, 0, Width, Height));
            full.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest_window.png"));
            int before = CurTrack.Notes.Count;
            roll.InsertNotes(Engine.Notation(song, 1, @"\f " + string.Join(" ", Enumerable.Repeat("c5/" + song.Den, song.Num)) + " |", 0));
            roll.SelectAll(); roll.Transpose(2); roll.Copy(); roll.CursorTick = song.BarTicks * 2; roll.Paste();
            L("edit: notes " + before + " -> " + CurTrack.Notes.Count);
            Undo(); Undo(); Undo();
            L("undo x3: notes " + CurTrack.Notes.Count + (CurTrack.Notes.Count == before ? " (restored)" : " (MISMATCH)"));
            Redo(); L("redo: notes " + CurTrack.Notes.Count);
            AddOrchestra(); L("orchestra: " + song.Tracks.Count + " tracks");
            dirty = false; NewSong(); L("new song: " + song.Tracks.Count + " tracks: " + string.Join(", ", song.Tracks.Select(t => t.Name)));
            L("OK");
        }

        void ExportAudio()
        {
            var o = new RenderOptions();
            string fl, ff, sf;
            if (AudioRender.CheckSetup(o, out fl, out ff, out sf) != null)
            {
                if (MessageBox.Show(this, "Audio export needs FluidSynth, ffmpeg and a soundfont, which were not found next to the program.\n\nRun the setup script for the sounds first. Show me the folder that holds them?", "Export audio", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                using (var fb = new FolderBrowserDialog { Description = "Choose the folder with the tools and soundfonts folders inside" })
                {
                    if (fb.ShowDialog(this) != DialogResult.OK) return;
                    Settings.Set("radio", fb.SelectedPath);
                }
                var missing = AudioRender.CheckSetup(o, out fl, out ff, out sf);
                if (missing != null) { MessageBox.Show(this, missing, "Export audio"); return; }
            }
            string target;
            using (var d = new SaveFileDialog { Filter = "MP3 audio|*.mp3", FileName = song.Title + ".mp3" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                target = d.FileName;
            }
            double bass;
            if (!AskBass(out bass)) return;
            o.BassDb = bass;
            player.Stop();
            var snapshot = song.Clone();
            status.Text = "Rendering " + Path.GetFileName(target) + " with " + Path.GetFileName(sf) + "…";
            UseWaitCursor = true;
            var th = new System.Threading.Thread(() =>
            {
                string msg;
                try
                {
                    var r = AudioRender.RenderSong(snapshot, target, o, null);
                    msg = r.Ok ? "Exported " + r.Detail : "Export failed: " + r.Detail;
                }
                catch (Exception ex) { msg = "Export failed: " + ex.Message; }
                BeginInvoke((Action)(() => { UseWaitCursor = false; status.Text = msg; if (msg.StartsWith("Export failed")) MessageBox.Show(this, msg, "Export audio"); }));
            });
            th.IsBackground = true; th.Start();
        }

        // bass lift for audio export (remembered between exports); false = cancelled
        bool AskBass(out double db)
        {
            db = 0;
            double last;
            if (!double.TryParse(Settings.Get("bass") ?? "0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out last)) last = 0;
            var f = new Form { Text = "Export audio", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(360, 120) };
            f.Controls.Add(new Label { Text = "Bass lift for subwoofers (dB below about 100 Hz).\r\n0 leaves the sound as it is; 3 to 6 is a clear lift.", Location = new Point(12, 10), Size = new Size(340, 34) });
            var n = new NumericUpDown { Minimum = 0, Maximum = 12, DecimalPlaces = 1, Increment = 0.5m, Value = (decimal)Math.Max(0, Math.Min(12, last)), Location = new Point(12, 50), Width = 70 };
            f.Controls.Add(n);
            var okB = new Button { Text = "Export", DialogResult = DialogResult.OK, Location = new Point(184, 84), Width = 80 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(270, 84), Width = 80 };
            f.Controls.Add(okB); f.Controls.Add(cancel); f.AcceptButton = okB; f.CancelButton = cancel;
            using (f)
            {
                if (f.ShowDialog(this) != DialogResult.OK) return false;
                db = (double)n.Value;
            }
            Settings.Set("bass", db.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }

        void ShowText(string title, string text)
        {
            var f = new Form { Text = title, Size = new Size(620, 560), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false };
            f.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 10f), Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"), ScrollBars = ScrollBars.Vertical, BackColor = Color.White });
            f.Show(this);
        }
    }
}
