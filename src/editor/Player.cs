// Playback through the Windows built-in MIDI synthesizer (winmm), on its own timing thread.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Abend
{
    public static class MidiOut
    {
        [DllImport("winmm.dll")] static extern int midiOutOpen(out IntPtr h, int dev, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int midiOutShortMsg(IntPtr h, int msg);
        [DllImport("winmm.dll")] static extern int midiOutClose(IntPtr h);
        [DllImport("winmm.dll")] static extern int midiOutReset(IntPtr h);
        [DllImport("winmm.dll")] public static extern int timeBeginPeriod(int p);
        [DllImport("winmm.dll")] static extern int midiOutGetNumDevs();

        static IntPtr h = IntPtr.Zero;
        static readonly object gate = new object();
        public static string Error;

        public static bool Open()
        {
            lock (gate)
            {
                if (h != IntPtr.Zero) return true;
                if (midiOutGetNumDevs() == 0) { Error = "No MIDI output device was found on this PC."; return false; }
                int r = midiOutOpen(out h, -1, IntPtr.Zero, IntPtr.Zero, 0);
                if (r != 0) { h = IntPtr.Zero; Error = "The MIDI synthesizer could not be opened (error " + r + "). Another program may be using it."; return false; }
                return true;
            }
        }
        public static void Send(int status, int d1, int d2)
        {
            lock (gate) { if (h != IntPtr.Zero) midiOutShortMsg(h, status | (d1 << 8) | (d2 << 16)); }
        }
        public static void AllOff()
        {
            for (int c = 0; c < 16; c++) { Send(0xB0 | c, 123, 0); Send(0xB0 | c, 120, 0); }
        }
        public static void Close()
        {
            lock (gate) { if (h != IntPtr.Zero) { midiOutReset(h); midiOutClose(h); h = IntPtr.Zero; } }
        }
    }

    public class Player
    {
        class Ev { public double Ms; public int Msg; public int Ord; }
        Thread th;
        volatile bool stopFlag;
        Stopwatch sw = new Stopwatch();
        double startMs;
        Song song;
        public bool Playing { get { return th != null && th.IsAlive; } }
        public event Action Finished;

        public int CurrentTick
        {
            get { return song == null ? 0 : song.MsToTick(startMs + sw.Elapsed.TotalMilliseconds); }
        }

        public void Play(Song s, int fromTick)
        {
            Stop();
            if (!MidiOut.Open()) throw new Exception(MidiOut.Error);
            song = s.Clone();
            startMs = song.TickToMs(fromTick);
            bool anySolo = song.Tracks.Any(t => t.Solo);
            var evs = new List<Ev>();
            var chans = new HashSet<int>();
            foreach (var tr in song.Tracks)
            {
                if (tr.Mute || (anySolo && !tr.Solo)) continue;
                int ch = tr.Channel;
                if (chans.Add(ch))
                {
                    if (!tr.IsDrums) evs.Add(new Ev { Ms = startMs, Ord = 0, Msg = (0xC0 | ch) | (tr.Program << 8) });
                    evs.Add(new Ev { Ms = startMs, Ord = 0, Msg = (0xB0 | ch) | (7 << 8) | (tr.Volume << 16) });
                    evs.Add(new Ev { Ms = startMs, Ord = 0, Msg = (0xB0 | ch) | (10 << 8) | (tr.Pan << 16) });
                    evs.Add(new Ev { Ms = startMs, Ord = 0, Msg = (0xB0 | ch) | (11 << 8) | (127 << 16) });
                    evs.Add(new Ev { Ms = startMs, Ord = 0, Msg = (0xB0 | ch) | (91 << 8) | (50 << 16) });
                }
                foreach (var n in tr.Notes)
                {
                    if (n.Start + n.Dur <= fromTick) continue;
                    double on = song.TickToMs(Math.Max(n.Start, fromTick)), off = song.TickToMs(n.Start + n.Dur);
                    if (n.Start < fromTick) continue; // do not restart notes already sounding
                    evs.Add(new Ev { Ms = on, Ord = 2, Msg = (0x90 | ch) | (n.Pitch << 8) | (Math.Max(1, Math.Min(127, n.Vel)) << 16) });
                    evs.Add(new Ev { Ms = Math.Max(on + 5, off - 2), Ord = 1, Msg = (0x80 | ch) | (n.Pitch << 8) });
                }
            }
            var list = evs.OrderBy(e => e.Ms).ThenBy(e => e.Ord).ToList();
            double endMs = list.Count > 0 ? list[list.Count - 1].Ms + 600 : startMs;
            stopFlag = false;
            MidiOut.timeBeginPeriod(1);
            sw.Restart();
            th = new Thread(() =>
            {
                int i = 0;
                while (!stopFlag)
                {
                    double now = startMs + sw.Elapsed.TotalMilliseconds;
                    while (i < list.Count && list[i].Ms <= now)
                    {
                        int m = list[i].Msg;
                        MidiOut.Send(m & 0xFF, (m >> 8) & 0xFF, (m >> 16) & 0xFF);
                        i++;
                    }
                    if (i >= list.Count && now > endMs) break;
                    Thread.Sleep(1);
                }
                MidiOut.AllOff();
                if (!stopFlag) { var f = Finished; if (f != null) f(); }
            });
            th.IsBackground = true;
            th.Priority = ThreadPriority.Highest;
            th.Start();
        }

        public void Stop()
        {
            if (th != null) { stopFlag = true; th.Join(500); th = null; }
            sw.Stop();
        }

        // short audition of a pitch on a track's instrument
        public static void Preview(Track tr, int pitch, int vel)
        {
            if (tr == null || !MidiOut.Open()) return;
            int ch = tr.Channel;
            if (!tr.IsDrums) MidiOut.Send(0xC0 | ch, tr.Program, 0);
            MidiOut.Send(0xB0 | ch, 7, tr.Volume);
            MidiOut.Send(0x90 | ch, pitch, vel);
            var t = new Timer(_ => MidiOut.Send(0x80 | ch, pitch, 0), null, 350, Timeout.Infinite);
            GC.KeepAlive(t);
            lock (timers) { timers.Add(t); if (timers.Count > 32) { timers[0].Dispose(); timers.RemoveAt(0); } }
        }
        static readonly List<Timer> timers = new List<Timer>();
    }
}
