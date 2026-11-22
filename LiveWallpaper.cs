using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace Hoster;

// ---------------- live wallpaper: a looping, muted video that sits behind your desktop icons ----------------
static class Live {
    static LiveWindow win; static string shown = "";

    public static void Start() {
        Apply();
        Store.Changed += Apply;
        Full.Changed += Pause; Full.CoveredChanged += Pause;   // nothing is drawn or decoded while a window covers the desktop
    }
    static bool Idle => Full.On || Full.Covered;
    static void Pause() => win?.SetPlaying(!Idle);

    static void Apply() {
        var c = Store.C;
        bool want = c.LiveWallpaperOn && !string.IsNullOrEmpty(c.LiveWallpaper) && File.Exists(c.LiveWallpaper);
        if (!want) { Close(); return; }
        if (win != null && shown == c.LiveWallpaper) return;
        Close();
        shown = c.LiveWallpaper;
        var w = new LiveWindow(c.LiveWallpaper); win = w;
        w.Closed += (_, _) => { if (win == w) { win = null; shown = ""; } };
        if (!w.OpenBehindIcons()) {
            win = null; shown = "";
            c.LiveWallpaperOn = false; Store.Save(false);
            MessageBox.Show("Couldn't attach the video to the desktop on this version of Windows, so the live wallpaper was turned off.", "Hoster");
            return;
        }
        w.SetPlaying(!Idle);
    }
    static void Close() { var w = win; win = null; shown = ""; if (w != null) { try { w.Close(); } catch { } } }

    // returns true if a video was chosen
    public static bool Pick() {
        var d = new OpenFileDialog { Filter = "Video|*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv|All files|*.*" };
        if (d.ShowDialog() != true) return false;
        Store.C.LiveWallpaper = d.FileName; Store.C.LiveWallpaperOn = true; Store.Save();
        return true;
    }
}

class LiveWindow : Window {
    readonly MediaElement me = new();
    bool failed;

    public LiveWindow(string path) {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false;
        Background = Brushes.Black; WindowStartupLocation = WindowStartupLocation.Manual; Left = -32000; Top = -32000; Width = 640; Height = 360;
        me.LoadedBehavior = MediaState.Manual; me.UnloadedBehavior = MediaState.Manual; me.Stretch = Stretch.UniformToFill;
        me.IsMuted = true; me.Volume = 0; me.IsHitTestVisible = false;
        me.MediaEnded += (_, _) => { me.Position = TimeSpan.Zero; me.Play(); };   // loop
        me.MediaFailed += (_, e) => {
            if (failed) return; failed = true;
            MessageBox.Show("This video couldn't be played: " + e.ErrorException?.Message + "\n\nMP4 (H.264) works best.", "Hoster");
            Store.C.LiveWallpaperOn = false; Store.Save();
        };
        me.Source = new Uri(path);
        Content = me;
    }

    public void SetPlaying(bool on) { try { if (on) me.Play(); else me.Pause(); } catch { } }

    public bool OpenBehindIcons() {
        Show();
        var h = new WindowInteropHelper(this).Handle;
        var b = WF.Screen.PrimaryScreen.Bounds; var v = WF.SystemInformation.VirtualScreen;
        return LiveNative.Attach(h, b.Left - v.Left, b.Top - v.Top, b.Width, b.Height);
    }
}

static class LiveNative {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr res);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetLong(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetLong(IntPtr h, int i, IntPtr v);

    // Re-parents the window into the layer Explorer draws the wallpaper on, which is underneath the desktop icons.
    public static bool Attach(IntPtr h, int x, int y, int w, int hh) {
        try {
            if (h == IntPtr.Zero) return false;
            var progman = FindWindow("Progman", null); if (progman == IntPtr.Zero) return false;
            SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0, 1000, out _);   // ask Explorer to create the wallpaper layer
            IntPtr worker = IntPtr.Zero;
            EnumWindows((top, _) => {
                if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) worker = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                return true;
            }, IntPtr.Zero);
            if (worker == IntPtr.Zero) worker = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);   // newer Windows 11 builds keep it inside Progman
            if (worker == IntPtr.Zero) return false;
            long st = GetLong(h, -16).ToInt64();
            st = (st & ~0x80000000L) | 0x40000000L | 0x10000000L;                                      // popup -> child, visible
            SetLong(h, -16, new IntPtr(st));
            SetParent(h, worker);
            return SetWindowPos(h, IntPtr.Zero, x, y, w, hh, 0x54);                                    // NOZORDER | NOACTIVATE | SHOWWINDOW
        } catch { return false; }
    }
}
