// Piano-roll editor: draw, select, move and resize notes of the current track; other tracks show as ghosts.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Abend
{
    public class PianoRoll : Control
    {
        public const int KEYW = 58, RULERH = 26;
        public Song Song;
        public int TrackIndex;
        public int RowH = 12;
        public double PxPerQuarter = 48;
        public int Snap = Song.PPQ / 4;
        public int NewLen = Song.PPQ;
        public int NewVel = 90;
        public int CursorTick;
        public int PlayTick = -1;
        public HashSet<NoteEv> Selection = new HashSet<NoteEv>();
        public event Action BeforeEdit;
        public event Action Edited;
        public event Action SelectionChanged;
        public event Action CursorMoved;

        HScrollBar hs = new HScrollBar();
        VScrollBar vs = new VScrollBar();
        List<NoteEv> clipboard = new List<NoteEv>();

        static readonly Color Accent = Color.FromArgb(38, 80, 138);
        static readonly Color Paper = Color.FromArgb(248, 250, 248);
        static readonly Color BlackRow = Color.FromArgb(236, 240, 238);
        static readonly Color Grid = Color.FromArgb(221, 226, 228);
        static readonly Color BarLine = Color.FromArgb(160, 170, 176);
        static readonly Color Ghost = Color.FromArgb(70, 120, 130, 140);

        public PianoRoll()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = true;
            hs.Dock = DockStyle.Bottom; vs.Dock = DockStyle.Right;
            Controls.Add(hs); Controls.Add(vs);
            hs.Scroll += (s, e) => Invalidate(); vs.Scroll += (s, e) => Invalidate();
            hs.ValueChanged += (s, e) => Invalidate(); vs.ValueChanged += (s, e) => Invalidate();
            Font = new Font("Segoe UI", 8f);
        }

        Track Cur { get { return Song != null && TrackIndex >= 0 && TrackIndex < Song.Tracks.Count ? Song.Tracks[TrackIndex] : null; } }
        double Ppt { get { return PxPerQuarter / Song.PPQ; } }
        int GridW { get { return ClientSize.Width - KEYW - vs.Width; } }
        int GridH { get { return ClientSize.Height - RULERH - hs.Height; } }

        public void UpdateScroll()
        {
            if (Song == null) return;
            int bars = Math.Max(64, Song.EndTick() / Song.BarTicks + 16);
            int contentW = (int)(bars * Song.BarTicks * Ppt);
            hs.Minimum = 0; hs.Maximum = Math.Max(0, contentW); hs.LargeChange = Math.Max(1, GridW); hs.SmallChange = 40;
            vs.Minimum = 0; vs.Maximum = 128 * RowH; vs.LargeChange = Math.Max(1, GridH); vs.SmallChange = RowH * 3;
            if (hs.Value > Math.Max(0, hs.Maximum - hs.LargeChange + 1)) hs.Value = Math.Max(0, hs.Maximum - hs.LargeChange + 1);
            if (vs.Value > Math.Max(0, vs.Maximum - vs.LargeChange + 1)) vs.Value = Math.Max(0, vs.Maximum - vs.LargeChange + 1);
            Invalidate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScroll(); }

        public void ScrollToPitch(int pitch) { int y = (127 - pitch) * RowH - GridH / 2; vs.Value = Math.Max(0, Math.Min(Math.Max(0, vs.Maximum - vs.LargeChange + 1), y)); }
        public void FollowPlayhead()
        {
            if (PlayTick < 0) return;
            int x = (int)(PlayTick * Ppt);
            if (x < hs.Value || x > hs.Value + GridW - 40) hs.Value = Math.Max(0, Math.Min(Math.Max(0, hs.Maximum - hs.LargeChange + 1), x - 40));
        }
        public void Zoom(double f)
        {
            int centreTick = (int)((hs.Value + GridW / 2) / Ppt);
            PxPerQuarter = Math.Max(6, Math.Min(400, PxPerQuarter * f));
            UpdateScroll();
            hs.Value = Math.Max(0, Math.Min(Math.Max(0, hs.Maximum - hs.LargeChange + 1), (int)(centreTick * Ppt) - GridW / 2));
        }

        int X(int tick) { return KEYW + (int)Math.Round(tick * Ppt) - hs.Value; }
        int Y(int pitch) { return RULERH + (127 - pitch) * RowH - vs.Value; }
        int TickAt(int x) { return (int)((x - KEYW + hs.Value) / Ppt); }
        int PitchAt(int y) { return 127 - (y - RULERH + vs.Value) / RowH; }
        int SnapTick(int t) { return Snap <= 1 ? t : (int)Math.Round((double)t / Snap) * Snap; }
        int SnapFloor(int t) { return Snap <= 1 ? t : (t / Snap) * Snap; }
        static bool IsBlack(int p) { int k = p % 12; return k == 1 || k == 3 || k == 6 || k == 8 || k == 10; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Paper);
            if (Song == null) return;
            int w = ClientSize.Width - vs.Width, h = ClientSize.Height - hs.Height;
            g.SetClip(new Rectangle(KEYW, RULERH, w - KEYW, h - RULERH));
            // rows
            int pTop = PitchAt(RULERH), pBot = PitchAt(h);
            using (var br = new SolidBrush(BlackRow))
            using (var cPen = new Pen(Grid))
                for (int p = Math.Min(127, pTop + 1); p >= Math.Max(0, pBot - 1); p--)
                {
                    int y = Y(p);
                    if (IsBlack(p)) g.FillRectangle(br, KEYW, y, w, RowH);
                    if (p % 12 == 0) g.DrawLine(cPen, KEYW, y + RowH - 1, w, y + RowH - 1);
                }
            // vertical grid
            int t0 = TickAt(KEYW), t1 = TickAt(w);
            int beat = Song.PPQ * 4 / Song.Den;
            using (var gp = new Pen(Grid)) using (var bp = new Pen(BarLine)) using (var sp = new Pen(Color.FromArgb(234, 238, 238)))
            {
                if (Snap * Ppt >= 6 && Snap < beat)
                    for (int t = SnapFloor(Math.Max(0, t0)); t <= t1; t += Snap) g.DrawLine(sp, X(t), RULERH, X(t), h);
                if (beat * Ppt >= 5)
                    for (int t = (Math.Max(0, t0) / beat) * beat; t <= t1; t += beat) g.DrawLine(gp, X(t), RULERH, X(t), h);
                for (int t = (Math.Max(0, t0) / Song.BarTicks) * Song.BarTicks; t <= t1; t += Song.BarTicks) g.DrawLine(bp, X(t), RULERH, X(t), h);
            }
            // ghost notes of other tracks
            using (var gb = new SolidBrush(Ghost))
                for (int ti = 0; ti < Song.Tracks.Count; ti++)
                {
                    if (ti == TrackIndex) continue;
                    foreach (var n in Song.Tracks[ti].Notes)
                    {
                        if (n.Start > t1 || n.Start + n.Dur < t0) continue;
                        g.FillRectangle(gb, X(n.Start), Y(n.Pitch) + 3, Math.Max(2, X(n.Start + n.Dur) - X(n.Start)), RowH - 6);
                    }
                }
            // current track
            var cur = Cur;
            if (cur != null)
                foreach (var n in cur.Notes)
                {
                    if (n.Start > t1 || n.Start + n.Dur < t0) continue;
                    var r = new Rectangle(X(n.Start), Y(n.Pitch) + 1, Math.Max(3, X(n.Start + n.Dur) - X(n.Start) - 1), RowH - 2);
                    bool sel = Selection.Contains(n);
                    int a = 110 + n.Vel;
                    using (var b = new SolidBrush(sel ? Color.FromArgb(Math.Min(255, a), 214, 120, 30) : Color.FromArgb(Math.Min(255, a), Accent)))
                        g.FillRectangle(b, r);
                    using (var p = new Pen(sel ? Color.FromArgb(120, 60, 10) : Color.FromArgb(20, 45, 80)))
                        g.DrawRectangle(p, r);
                }
            // rubber band
            if (mode == Mode.Band)
            {
                var r = Rectangle.FromLTRB(Math.Min(bandA.X, bandB.X), Math.Min(bandA.Y, bandB.Y), Math.Max(bandA.X, bandB.X), Math.Max(bandA.Y, bandB.Y));
                using (var b = new SolidBrush(Color.FromArgb(40, Accent))) g.FillRectangle(b, r);
                using (var p = new Pen(Accent)) g.DrawRectangle(p, r);
            }
            // cursor + playhead
            using (var cp = new Pen(Color.FromArgb(150, Accent), 1) { DashStyle = DashStyle.Dash }) g.DrawLine(cp, X(CursorTick), RULERH, X(CursorTick), h);
            if (PlayTick >= 0) using (var pp = new Pen(Color.FromArgb(200, 40, 40), 2)) g.DrawLine(pp, X(PlayTick), RULERH, X(PlayTick), h);
            g.ResetClip();

            // ruler
            g.SetClip(new Rectangle(KEYW, 0, w - KEYW, RULERH));
            using (var rb = new SolidBrush(Color.FromArgb(232, 236, 236))) g.FillRectangle(rb, KEYW, 0, w, RULERH);
            using (var tb = new SolidBrush(Color.FromArgb(40, 50, 60))) using (var bp = new Pen(BarLine))
            {
                int every = Math.Max(1, (int)Math.Ceiling(40 / (Song.BarTicks * Ppt)));
                for (int t = (Math.Max(0, t0) / Song.BarTicks) * Song.BarTicks; t <= t1; t += Song.BarTicks)
                {
                    int bar = t / Song.BarTicks + 1;
                    g.DrawLine(bp, X(t), RULERH - 8, X(t), RULERH);
                    if ((bar - 1) % every == 0) g.DrawString(bar.ToString(), Font, tb, X(t) + 3, 5);
                }
            }
            using (var cb = new SolidBrush(Accent))
            {
                int cx = X(CursorTick);
                g.FillPolygon(cb, new[] { new Point(cx - 6, RULERH - 9), new Point(cx + 6, RULERH - 9), new Point(cx, RULERH - 1) });
            }
            if (PlayTick >= 0) using (var pb = new SolidBrush(Color.FromArgb(200, 40, 40))) g.FillRectangle(pb, X(PlayTick) - 1, 0, 3, RULERH);
            g.ResetClip();

            // keyboard
            g.SetClip(new Rectangle(0, RULERH, KEYW, h - RULERH));
            for (int p = Math.Min(127, pTop + 1); p >= Math.Max(0, pBot - 1); p--)
            {
                int y = Y(p);
                bool bl = IsBlack(p);
                using (var kb = new SolidBrush(bl ? Color.FromArgb(52, 58, 66) : Color.White)) g.FillRectangle(kb, 0, y, bl ? KEYW - 18 : KEYW, RowH);
                using (var kp = new Pen(Color.FromArgb(200, 205, 210))) g.DrawLine(kp, 0, y + RowH - 1, KEYW, y + RowH - 1);
                if (p % 12 == 0) using (var tb = new SolidBrush(Color.FromArgb(70, 80, 90))) g.DrawString(GM.NoteName(p), Font, tb, KEYW - 30, y - 1);
            }
            g.ResetClip();
            using (var lp = new Pen(BarLine)) g.DrawLine(lp, KEYW - 1, 0, KEYW - 1, h);
            using (var corner = new SolidBrush(Color.FromArgb(232, 236, 236))) g.FillRectangle(corner, 0, 0, KEYW - 1, RULERH);
        }

        // ---------- mouse ----------
        enum Mode { None, Move, Resize, Band, Create }
        Mode mode = Mode.None;
        Point down, bandA, bandB;
        Dictionary<NoteEv, NoteEv> orig = new Dictionary<NoteEv, NoteEv>();
        NoteEv anchor;
        bool editedThisDrag;

        NoteEv HitNote(Point pt, out bool edge)
        {
            edge = false;
            var cur = Cur; if (cur == null) return null;
            int tick = TickAt(pt.X), pitch = PitchAt(pt.Y);
            for (int i = cur.Notes.Count - 1; i >= 0; i--)
            {
                var n = cur.Notes[i];
                if (n.Pitch == pitch && tick >= n.Start && tick < n.Start + n.Dur)
                {
                    edge = X(n.Start + n.Dur) - pt.X <= 6 && X(n.Start + n.Dur) - X(n.Start) > 10;
                    return n;
                }
            }
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (Song == null) return;
            int gw = ClientSize.Width - vs.Width, gh = ClientSize.Height - hs.Height;
            if (e.X >= gw || e.Y >= gh) return;
            if (e.Y < RULERH && e.X >= KEYW)
            {
                CursorTick = Math.Max(0, SnapTick(TickAt(e.X)));
                if (CursorMoved != null) CursorMoved();
                Invalidate(); return;
            }
            if (e.X < KEYW) { Player.Preview(Cur, PitchAt(e.Y), NewVel); return; }
            var cur = Cur; if (cur == null) return;
            bool edge; var hit = HitNote(e.Location, out edge);
            down = e.Location; editedThisDrag = false;
            if (e.Button == MouseButtons.Right)
            {
                if (hit != null) { Edit(); cur.Notes.Remove(hit); Selection.Remove(hit); Done(); }
                else { Selection.Clear(); SelChanged(); }
                Invalidate(); return;
            }
            if (hit != null)
            {
                if ((ModifierKeys & Keys.Shift) != 0) { if (!Selection.Remove(hit)) Selection.Add(hit); }
                else if (!Selection.Contains(hit)) { Selection.Clear(); Selection.Add(hit); }
                SelChanged();
                anchor = hit;
                mode = edge ? Mode.Resize : Mode.Move;
                orig = Selection.ToDictionary(n => n, n => n.Clone());
                Player.Preview(cur, hit.Pitch, hit.Vel);
            }
            else if ((ModifierKeys & Keys.Control) != 0 || (ModifierKeys & Keys.Shift) != 0)
            {
                mode = Mode.Band; bandA = bandB = e.Location;
            }
            else
            {
                Edit();
                var n = new NoteEv { Start = Math.Max(0, SnapFloor(TickAt(e.X))), Pitch = Math.Max(0, Math.Min(127, PitchAt(e.Y))), Dur = NewLen, Vel = NewVel };
                cur.Notes.Add(n);
                Selection.Clear(); Selection.Add(n); SelChanged();
                anchor = n; mode = Mode.Create; orig = new Dictionary<NoteEv, NoteEv> { { n, n.Clone() } };
                editedThisDrag = true;
                Player.Preview(cur, n.Pitch, n.Vel);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Song == null) return;
            if (mode == Mode.None)
            {
                bool edge = false; var hit = e.X >= KEYW && e.Y >= RULERH ? HitNote(e.Location, out edge) : null;
                Cursor = hit != null ? (edge ? Cursors.SizeWE : Cursors.SizeAll) : (e.X >= KEYW && e.Y >= RULERH ? Cursors.Cross : Cursors.Default);
                return;
            }
            if (mode == Mode.Band) { bandB = e.Location; Invalidate(); return; }
            int dt = (int)((e.X - down.X) / Ppt);
            int dp = -(int)Math.Round((double)(e.Y - down.Y) / RowH);
            if (!editedThisDrag && (Math.Abs(e.X - down.X) > 2 || Math.Abs(e.Y - down.Y) > 2)) { Edit(); editedThisDrag = true; }
            if (!editedThisDrag) return;
            if (mode == Mode.Move)
            {
                var o = orig[anchor];
                int newStart = Math.Max(0, SnapTick(o.Start + dt));
                int shift = newStart - o.Start;
                int minStart = orig.Values.Min(n => n.Start);
                if (minStart + shift < 0) shift = -minStart;
                int lastP = anchor.Pitch;
                foreach (var kv in orig)
                {
                    kv.Key.Start = kv.Value.Start + shift;
                    kv.Key.Pitch = Math.Max(0, Math.Min(127, kv.Value.Pitch + dp));
                }
                if (anchor.Pitch != lastP) Player.Preview(Cur, anchor.Pitch, anchor.Vel);
            }
            else
            {
                var o = orig[anchor];
                int endT = SnapTick(TickAt(e.X));
                if (mode == Mode.Create) endT = Math.Max(SnapTick(TickAt(e.X)), o.Start + Math.Max(Snap, 30));
                int newDur = Math.Max(Math.Max(Snap, 30), endT - o.Start);
                int dd = newDur - o.Dur;
                foreach (var kv in orig) kv.Key.Dur = Math.Max(Math.Max(Snap, 30), kv.Value.Dur + dd);
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (mode == Mode.Band)
            {
                var r = Rectangle.FromLTRB(Math.Min(bandA.X, bandB.X), Math.Min(bandA.Y, bandB.Y), Math.Max(bandA.X, bandB.X), Math.Max(bandA.Y, bandB.Y));
                int ta = TickAt(r.Left), tb = TickAt(r.Right), pa = PitchAt(r.Bottom), pb = PitchAt(r.Top);
                if ((ModifierKeys & Keys.Shift) == 0) Selection.Clear();
                foreach (var n in Cur.Notes) if (n.Start < tb && n.Start + n.Dur > ta && n.Pitch >= pa && n.Pitch <= pb) Selection.Add(n);
                SelChanged();
            }
            if (mode == Mode.Create && anchor != null) NewLen = anchor.Dur;
            if (editedThisDrag) { Cur.Notes.Sort((a, b) => a.Start.CompareTo(b.Start)); Done(); }
            mode = Mode.None; orig.Clear(); anchor = null;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) != 0) { Zoom(e.Delta > 0 ? 1.25 : 0.8); return; }
            if ((ModifierKeys & Keys.Shift) != 0) hs.Value = Math.Max(0, Math.Min(Math.Max(0, hs.Maximum - hs.LargeChange + 1), hs.Value - e.Delta));
            else vs.Value = Math.Max(0, Math.Min(Math.Max(0, vs.Maximum - vs.LargeChange + 1), vs.Value - e.Delta / 2));
        }

        // ---------- commands ----------
        void Edit() { if (BeforeEdit != null) BeforeEdit(); }
        void Done() { UpdateScroll(); if (Edited != null) Edited(); }
        void SelChanged() { if (SelectionChanged != null) SelectionChanged(); }

        public void ClearSelection() { Selection.Clear(); SelChanged(); Invalidate(); }
        public void SelectAll() { if (Cur == null) return; Selection = new HashSet<NoteEv>(Cur.Notes); SelChanged(); Invalidate(); }
        public void DeleteSelected()
        {
            if (Cur == null || Selection.Count == 0) return;
            Edit(); Cur.Notes.RemoveAll(n => Selection.Contains(n)); Selection.Clear(); SelChanged(); Done(); Invalidate();
        }
        public void Transpose(int semis)
        {
            if (Cur == null || Selection.Count == 0) return;
            Edit(); foreach (var n in Selection) n.Pitch = Math.Max(0, Math.Min(127, n.Pitch + semis)); Done(); Invalidate();
        }
        public void Nudge(int ticks)
        {
            if (Cur == null || Selection.Count == 0) return;
            if (Selection.Min(n => n.Start) + ticks < 0) return;
            Edit(); foreach (var n in Selection) n.Start += ticks; Done(); Invalidate();
        }
        public void SetVelocity(int v)
        {
            if (Cur == null || Selection.Count == 0) return;
            Edit(); foreach (var n in Selection) n.Vel = v; Done(); Invalidate();
        }
        public void Quantize()
        {
            if (Cur == null || Selection.Count == 0) return;
            Edit(); foreach (var n in Selection) { n.Start = Math.Max(0, SnapTick(n.Start)); n.Dur = Math.Max(Snap, SnapTick(n.Dur)); } Done(); Invalidate();
        }
        public void Copy() { if (Selection.Count == 0) return; int m = Selection.Min(n => n.Start); clipboard = Selection.Select(n => { var c = n.Clone(); c.Start -= m; return c; }).ToList(); }
        public void Cut() { Copy(); DeleteSelected(); }
        public void Paste()
        {
            if (Cur == null || clipboard.Count == 0) return;
            Edit();
            Selection.Clear();
            foreach (var c in clipboard) { var n = c.Clone(); n.Start += CursorTick; Cur.Notes.Add(n); Selection.Add(n); }
            Cur.Notes.Sort((a, b) => a.Start.CompareTo(b.Start));
            CursorTick += clipboard.Max(n => n.Start + n.Dur);
            SelChanged(); Done(); Invalidate();
        }
        public void InsertNotes(IEnumerable<NoteEv> notes)
        {
            if (Cur == null) return;
            Edit(); Selection.Clear();
            foreach (var n in notes) { Cur.Notes.Add(n); Selection.Add(n); }
            Cur.Notes.Sort((a, b) => a.Start.CompareTo(b.Start));
            SelChanged(); Done(); Invalidate();
            if (Selection.Count > 0) ScrollToPitch((int)Selection.Average(n => n.Pitch));
        }

        protected override bool IsInputKey(Keys k)
        {
            var kc = k & Keys.KeyCode;
            return kc == Keys.Up || kc == Keys.Down || kc == Keys.Left || kc == Keys.Right || base.IsInputKey(k);
        }
    }
}
