using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Shp = System.Windows.Shapes;
using WF = System.Windows.Forms;
using Smtc = Windows.Media.Control;
using UIA = System.Windows.Automation;

namespace Hoster;

// ---------------- settings ----------------
class Pin { public string Name { get; set; } public string Target { get; set; } }
class WCfg { public string Kind { get; set; } public double X { get; set; } = -1; public double Y { get; set; } = -1; public int Size { get; set; } = 100; public bool On { get; set; } = true; }
class Cfg {
    static string W(string s) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), s);
    public string Theme { get; set; } = "dark";
    public string Accent { get; set; } = "#E5303A";
    public int IconSize { get; set; } = 48;
    public int Radius { get; set; } = 18;
    public bool Magnify { get; set; } = true;
    public int MagnifyAmount { get; set; } = 60;                         // how much the hovered icon grows, in %
    public bool HideTaskbar { get; set; } = true;
    public bool Clock24 { get; set; } = true;
    public bool Seconds { get; set; }
    public bool Stats { get; set; } = true;
    public bool Date { get; set; } = true;
    public bool Media { get; set; } = true;
    public bool Network { get; set; } = true;                            // Wi-Fi / Bluetooth menu in the top bar
    public bool Visualizer { get; set; } = true;
    public bool HoverList { get; set; } = true;                          // hover a dock icon to list its windows / browser tabs
    public bool OwnStart { get; set; } = true;                           // Hoster's own Start menu (Win key + dock button)
    public bool SyncWindows { get; set; } = true;                       // make the Windows Start menu follow the Hoster theme
    public Dictionary<string, string> WinOrig { get; set; }            // Windows colour settings as they were before Hoster changed them
    public bool HideInFullscreen { get; set; } = true;
    public bool LiveWallpaperOn { get; set; }                            // looping video behind the desktop icons
    public string LiveWallpaper { get; set; } = "";                      // path of that video
    public List<WCfg> Widgets { get; set; } = new() { new WCfg { Kind = "Clock" }, new WCfg { Kind = "Media" }, new WCfg { Kind = "System" }, new WCfg { Kind = "Calendar" } };
    public uint? TaskbarState { get; set; }   // original Windows taskbar state, kept so a crash can be recovered from
    public List<Pin> Pinned { get; set; } = new() {
        new Pin { Name = "Files", Target = W("explorer.exe") },
        new Pin { Name = "Notepad", Target = W(@"System32\notepad.exe") },
        new Pin { Name = "Calculator", Target = W(@"System32\calc.exe") },
        new Pin { Name = "Settings", Target = "ms-settings:" } };
}
static class Store {
    static string F => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hoster", "settings.json");
    public static Cfg C = Load();
    public static event Action Changed;
    static Cfg Load() {
        try {
            if (!File.Exists(F)) {   // first run after the DotDesk -> Hoster rename: carry the old settings over
                var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DotDesk", "settings.json");
                if (File.Exists(old)) { Directory.CreateDirectory(Path.GetDirectoryName(F)); File.Copy(old, F); }
            }
            return JsonSerializer.Deserialize<Cfg>(File.ReadAllText(F)) ?? new Cfg();
        } catch { return new Cfg(); }
    }
    public static void Save(bool notify = true) {
        try { Directory.CreateDirectory(Path.GetDirectoryName(F)); File.WriteAllText(F, JsonSerializer.Serialize(C)); } catch { }
        if (notify) { Th.Apply(); Changed?.Invoke(); }
    }
    public static void PinApp() {
        var d = new OpenFileDialog { Filter = "Apps|*.exe;*.lnk;*.bat|All files|*.*" };
        if (d.ShowDialog() == true) { C.Pinned.Add(new Pin { Name = Path.GetFileNameWithoutExtension(d.FileName), Target = d.FileName }); Save(); }
    }
}

// ---------------- theme + small UI helpers ----------------
static class Th {
    public static Brush Bg, Fg, Dim, Line, Glass, Tile, Accent;
    static Brush B(string h) { var b = (Brush)new BrushConverter().ConvertFrom(h); b.Freeze(); return b; }
    public static void Apply() {
        var c = Store.C;
        var (bg, glass, fg, dim, line, tile) = c.Theme switch {
            "black" => ("#000000", "#E6000000", "#F1F1F1", "#8D8D93", "#1FFFFFFF", "#10FFFFFF"),
            "navy" => ("#0B1322", "#E00B1322", "#F1F1F1", "#8D8D93", "#1FFFFFFF", "#147AA2FF"),
            "aubergine" => ("#2A0F2A", "#E02A0F2A", "#F1F1F1", "#B79BB7", "#1FFFFFFF", "#1AFF9BF0"),
            "light" => ("#F3F2EF", "#E6F3F2EF", "#16161A", "#6B6B72", "#20000000", "#10000000"),
            _ => ("#0E0E10", "#D9101012", "#F1F1F1", "#8D8D93", "#1FFFFFFF", "#10FFFFFF") };
        Bg = B(bg); Glass = B(glass); Fg = B(fg); Dim = B(dim); Line = B(line); Tile = B(tile);
        try { Accent = B(c.Accent); } catch { Accent = B("#E5303A"); }
        WinTheme.Sync();
    }
}
// ---------------- make the Windows Start menu follow the theme (light/dark + tint) ----------------
static class WinTheme {
    const string P = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string A = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    static readonly (string Path, string Name)[] Keys = {
        (P, "SystemUsesLightTheme"), (P, "AppsUseLightTheme"), (P, "ColorPrevalence"),
        (A, "AccentColorMenu"), (A, "StartColorMenu"), (A, "AccentPalette") };
    static string sig;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, UIntPtr w, string l, uint flags, uint timeout, out UIntPtr res);

    public static void Sync() {
        try {
            var c = Store.C;
            string want = c.SyncWindows ? c.Theme : "-";
            if (want == sig) return; sig = want;
            if (c.SyncWindows) {
                if (c.WinOrig == null) { c.WinOrig = new(); foreach (var k in Keys) c.WinOrig[k.Path + "|" + k.Name] = Read(k.Path, k.Name); Store.Save(false); }
                Apply(c.Theme);
            } else if (c.WinOrig != null) {
                foreach (var k in Keys) { c.WinOrig.TryGetValue(k.Path + "|" + k.Name, out var v); Write(k.Path, k.Name, v); }
                c.WinOrig = null; Store.Save(false); Broadcast();
            }
        } catch { }
    }

    static void Apply(string theme) {
        bool light = theme == "light";
        Color? tint = theme == "navy" ? (Color?)Color.FromRgb(0x0B, 0x13, 0x22) : theme == "aubergine" ? (Color?)Color.FromRgb(0x2A, 0x0F, 0x2A) : null;
        Set(P, "SystemUsesLightTheme", light ? 1 : 0);
        Set(P, "AppsUseLightTheme", light ? 1 : 0);
        if (tint != null) {
            var t = Mix(tint.Value, 0.12);
            int abgr = unchecked((int)(0xFF000000u | (uint)(t.B << 16) | (uint)(t.G << 8) | t.R));
            Set(P, "ColorPrevalence", 1);                  // "show accent colour on Start" (needs dark mode)
            Set(A, "AccentColorMenu", abgr); Set(A, "StartColorMenu", abgr);
            var pal = new byte[32]; double[] f = { 0.6, 0.4, 0.2, 0, -0.2, -0.4, -0.6, 0 };
            for (int i = 0; i < 8; i++) { var m = Mix(t, f[i]); pal[i * 4] = m.R; pal[i * 4 + 1] = m.G; pal[i * 4 + 2] = m.B; }
            using var k = Registry.CurrentUser.CreateSubKey(A); k.SetValue("AccentPalette", pal, RegistryValueKind.Binary);
        } else Set(P, "ColorPrevalence", 0);
        Broadcast();
    }

    static Color Mix(Color c, double t) {
        byte M(byte x) => (byte)Math.Max(0, Math.Min(255, t >= 0 ? x + (255 - x) * t : x * (1 + t)));
        return Color.FromRgb(M(c.R), M(c.G), M(c.B));
    }
    static void Set(string path, string name, int v) { using var k = Registry.CurrentUser.CreateSubKey(path); k.SetValue(name, v, RegistryValueKind.DWord); }
    static string Read(string path, string name) {
        using var k = Registry.CurrentUser.OpenSubKey(path); var v = k?.GetValue(name);
        return v is int i ? "i:" + i : v is byte[] b ? "b:" + Convert.ToBase64String(b) : null;
    }
    static void Write(string path, string name, string s) {
        if (s == null) { using var k = Registry.CurrentUser.OpenSubKey(path, true); k?.DeleteValue(name, false); }
        else if (s.StartsWith("i:")) Set(path, name, int.Parse(s.Substring(2)));
        else if (s.StartsWith("b:")) { using var k = Registry.CurrentUser.CreateSubKey(path); k.SetValue(name, Convert.FromBase64String(s.Substring(2)), RegistryValueKind.Binary); }
    }
    static void Broadcast() => Task.Run(() => { try { SendMessageTimeout((IntPtr)0xFFFF, 0x001A, UIntPtr.Zero, "ImmersiveColorSet", 2, 300, out _); } catch { } });
}

static class UI {
    public static TextBlock Txt(string s, Brush fg, double size = 13, FontWeight? w = null) => new TextBlock {
        Text = s, Foreground = fg, FontSize = size, FontWeight = w ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
    public static string Time(TimeSpan x) => x.TotalHours >= 1 ? $"{(int)x.TotalHours}:{x.Minutes:00}:{x.Seconds:00}" : $"{x.Minutes}:{x.Seconds:00}";
    public static Border Round(string glyph, Action act, double size, bool main) {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 6, 0),
            Background = main ? Th.Accent : Brushes.Transparent, Child = Glyph(glyph, main ? Brushes.White : Th.Fg, main ? 16 : 14) };
        b.MouseEnter += (_, _) => { if (!main) b.Background = Th.Tile; }; b.MouseLeave += (_, _) => { if (!main) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonUp += (_, _) => act();
        return b;
    }
    public static TextBlock Glyph(string g, Brush fg, double size) => new TextBlock {
        Text = g, Foreground = fg, FontSize = size, FontFamily = new FontFamily("Segoe MDL2 Assets"),
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    public static Border Click(UIElement content, Action act, Brush rest = null, double pad = 8, double padV = 4) {
        rest ??= Brushes.Transparent;
        var b = new Border { Child = content, Background = rest, CornerRadius = new CornerRadius(8), Padding = new Thickness(pad, padV, pad, padV), Cursor = Cursors.Hand };
        b.MouseEnter += (_, _) => b.Background = Th.Tile;
        b.MouseLeave += (_, _) => b.Background = rest;
        b.MouseLeftButtonUp += (_, _) => act();
        return b;
    }
}

// ---------------- Win32 ----------------
static class N {
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct ABD { public int cb; public IntPtr hWnd; public uint msg, edge; public RECT rc; public IntPtr lp; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cb; public RECT rc, work; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] struct MEM { public uint len, load; public ulong a, b, c, d, e, f, g; }
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint m, ref ABD d);
    [DllImport("user32.dll")] static extern uint RegisterWindowMessage(string s);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(int a, int b, string s, int f);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int sz);
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEM m);

    // Make a window a non-activating tool window (no focus stealing, hidden from Alt+Tab)
    public static void NoActivate(IntPtr h) => SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x80);

    // Register as a real Windows appbar so maximised windows leave room. edge: 1 = top, 3 = bottom.
    // 'extra' lets the window extend past the reserved area (used for dock magnification headroom).
    static readonly HashSet<IntPtr> registered = new();
    public static void AppBar(IntPtr h, uint edge, int size, int extra) {
        var s = WF.Screen.PrimaryScreen.Bounds;
        var d = new ABD { cb = Marshal.SizeOf<ABD>(), hWnd = h, edge = edge, msg = RegisterWindowMessage("HosterBar" + edge) };
        if (registered.Add(h)) SHAppBarMessage(0, ref d);              // ABM_NEW
        d.rc = new RECT { L = s.Left, T = s.Top, R = s.Right, B = s.Bottom };
        SHAppBarMessage(2, ref d);                                      // ABM_QUERYPOS
        if (edge == 1) d.rc.B = d.rc.T + size; else d.rc.T = d.rc.B - size;
        SHAppBarMessage(3, ref d);                                      // ABM_SETPOS
        int top = edge == 1 ? d.rc.T : d.rc.T - extra;
        SetWindowPos(h, (IntPtr)(-1), d.rc.L, top, d.rc.R - d.rc.L, size + extra, 0x10);
    }
    public static void RemoveBars() {
        foreach (var h in registered) { var d = new ABD { cb = Marshal.SizeOf<ABD>(), hWnd = h }; SHAppBarMessage(1, ref d); }
        registered.Clear();
    }

    // Windows taskbar: remember its state, switch it to auto-hide (frees its work area) and hide the window.
    public static void HideTaskbarWindows(bool hide) {
        foreach (var c in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" }) { var w = FindWindow(c, null); if (w != IntPtr.Zero) ShowWindow(w, hide ? 0 : 5); }
    }
    public static void Taskbar(bool show) {
        var d = new ABD { cb = Marshal.SizeOf<ABD>(), hWnd = FindWindow("Shell_TrayWnd", null) };
        if (Store.C.TaskbarState == null && !show) { Store.C.TaskbarState = SHAppBarMessage(4, ref d).ToUInt32(); Store.Save(false); }   // ABM_GETSTATE
        uint st = Store.C.TaskbarState ?? 2u; d.lp = (IntPtr)(long)(show ? st : (st | 1u));                                          // 1 = auto-hide
        SHAppBarMessage(10, ref d);                                                                                                         // ABM_SETSTATE
        HideTaskbarWindows(!show);
        if (show) { Store.C.TaskbarState = null; Store.Save(false); }
    }

    // Is the cursor (screen pixels) inside this window's rectangle, grown by 'pad' pixels?
    public static bool Hit(IntPtr h, int x, int y, int pad) {
        if (h == IntPtr.Zero || !IsWindowVisible(h) || !GetWindowRect(h, out var r)) return false;
        return x >= r.L - pad && x < r.R + pad && y >= r.T - pad && y < r.B + pad;
    }

    // Is a full-screen app (game, video, F11 browser, slideshow) in front on the primary monitor?
    static readonly HashSet<string> ignoreClasses = new() { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "MultitaskingViewFrame", "ForegroundStaging", "XamlExplorerHostIslandWindow", "TaskSwitcherWnd" };
    public static bool Fullscreen() {
        var h = GetForegroundWindow(); if (h == IntPtr.Zero) return false;
        GetWindowThreadProcessId(h, out var pid); if (pid == (uint)Environment.ProcessId) return false;
        var sb = new StringBuilder(64); GetClassName(h, sb, 64); if (ignoreClasses.Contains(sb.ToString())) return false;
        int style = GetWindowLong(h, -16);
        if ((style & 0x00C00000) == 0x00C00000 && (style & 0x01000000) != 0) return false;   // an ordinary maximised window (has a title bar)
        if (!GetWindowRect(h, out var r)) return false;
        var mi = new MONITORINFO { cb = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(h, 2), ref mi) || (mi.flags & 1) == 0) return false;   // primary monitor only
        return r.L <= mi.rc.L && r.T <= mi.rc.T && r.R >= mi.rc.R && r.B >= mi.rc.B;
    }

    // Is an ordinary maximised window (title bar and all) in front on the primary monitor? It hides the desktop, so the widgets can pause.
    public static bool ForegroundIsMine() {
        var h = GetForegroundWindow(); if (h == IntPtr.Zero) return false;
        GetWindowThreadProcessId(h, out var pid); return pid == (uint)Environment.ProcessId;
    }
    public static bool Covered() {
        var h = GetForegroundWindow(); if (h == IntPtr.Zero) return false;
        GetWindowThreadProcessId(h, out var pid); if (pid == (uint)Environment.ProcessId) return false;
        var sb = new StringBuilder(64); GetClassName(h, sb, 64); if (ignoreClasses.Contains(sb.ToString())) return false;
        if ((GetWindowLong(h, -16) & 0x01000000) == 0 || IsIconic(h)) return false;
        var mi = new MONITORINFO { cb = Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(MonitorFromWindow(h, 2), ref mi) && (mi.flags & 1) != 0;
    }

    public static void Wallpaper(string path) => SystemParametersInfo(20, 0, path, 3);
    public static void Focus(IntPtr h) { if (IsIconic(h)) ShowWindow(h, 9); SetForegroundWindow(h); }

    static long pi, pk, pu;
    public static int Cpu() {
        GetSystemTimes(out var i, out var k, out var u);
        long di = i - pi, tot = (k - pk) + (u - pu); pi = i; pk = k; pu = u;
        return tot <= 0 ? 0 : (int)(100 * (tot - di) / tot);
    }
    public static (double used, double total) RamGb() {
        var m = new MEM { len = (uint)Marshal.SizeOf<MEM>() }; GlobalMemoryStatusEx(ref m);
        return ((m.a - m.b) / 1073741824.0, m.a / 1073741824.0);
    }
    // Put a window directly above the desktop (wallpaper + icons) and below every normal app window.
    public static void ToDesktop(IntPtr h) {
        if (h == IntPtr.Zero) return;
        IntPtr host = IntPtr.Zero;
        EnumWindows((w, _) => { if (FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) { host = w; return false; } return true; }, IntPtr.Zero);
        if (host == IntPtr.Zero) host = FindWindow("Progman", null);
        if (host != IntPtr.Zero) SetWindowPos(h, host, 0, 0, 0, 0, 0x13);   // NOMOVE | NOSIZE | NOACTIVATE
    }
    public static int Ram() { var m = new MEM { len = (uint)Marshal.SizeOf<MEM>() }; GlobalMemoryStatusEx(ref m); return (int)m.load; }

    // Visible top-level app windows, with the exe that owns each
    // Force-closes every process of the app at this exe path, child processes included (same as Task Manager's "End task")
    public static void EndTask(string path) {
        foreach (var p in Process.GetProcesses()) {
            try {
                if (p.Id == Environment.ProcessId) continue;
                if (string.Equals(p.MainModule.FileName, path, StringComparison.OrdinalIgnoreCase)) p.Kill(true);
            } catch { }   // protected / elevated processes can't be ended from here
            finally { p.Dispose(); }
        }
    }
    public static List<(uint pid, IntPtr h, string title, string path)> Running() {
        var me = (uint)Environment.ProcessId; var res = new List<(uint, IntPtr, string, string)>(); var wins = new List<(uint, IntPtr, string)>();
        EnumWindows((h, _) => {
            if (!IsWindowVisible(h) || GetWindow(h, 4) != IntPtr.Zero) return true;          // skip hidden and owned windows
            if ((GetWindowLong(h, -20) & 0x80) != 0) return true;                            // skip tool windows
            DwmGetWindowAttribute(h, 14, out int cloaked, 4); if (cloaked != 0) return true; // skip cloaked (virtual desktops, suspended UWP)
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            if (sb.Length == 0 || sb.ToString() == "Program Manager") return true;
            GetWindowThreadProcessId(h, out var pid); if (pid != me) wins.Add((pid, h, sb.ToString()));
            return true;
        }, IntPtr.Zero);
        foreach (var w in wins) {
            try { res.Add((w.Item1, w.Item2, w.Item3, Process.GetProcessById((int)w.Item1).MainModule.FileName)); } catch { }
        }
        return res;
    }

    static readonly Dictionary<string, ImageSource> icons = new();
    public static ImageSource Icon(string p) {
        if (icons.TryGetValue(p, out var s)) return s;
        ImageSource r = null;
        try {
            if (File.Exists(p)) {
                using var i = System.Drawing.Icon.ExtractAssociatedIcon(p);
                r = Imaging.CreateBitmapSourceFromHIcon(i.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); r.Freeze();
            }
        } catch { }
        icons[p] = r; return r;
    }
}

// ---------------- windows ----------------
class Shell : Window {
    protected Shell() {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize; Topmost = true; ShowActivated = false; Width = 800; Height = 40;
        SourceInitialized += (_, _) => { N.NoActivate(Handle); Place(); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplay;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplay;
    }
    void OnDisplay(object s, EventArgs e) => Dispatcher.Invoke(Place);
    protected IntPtr Handle => new WindowInteropHelper(this).Handle;
    protected int Px(double dip) => (int)Math.Round(dip * VisualTreeHelper.GetDpi(this).DpiScaleX);
    protected virtual void Place() { }
    public bool Over(int x, int y, int pad = 6) => N.Hit(Handle, x, y, pad);
}

// ---------------- full-screen detection (dock + bar get out of the way) ----------------
static class Full {
    public static bool On, Covered;          // On: full-screen app in front. Covered: a maximised window hides the desktop.
    public static event Action Changed, CoveredChanged;
    public static void Poll() {
        bool v = Store.C.HideInFullscreen && N.Fullscreen();
        if (v != On) { On = v; Changed?.Invoke(); }
        if (N.ForegroundIsMine()) return;   // Hoster's own Start menu / flyouts opened over the maximised window: it is still covering the desktop
        bool c = Store.C.HideInFullscreen && N.Covered();
        if (c != Covered) { Covered = c; CoveredChanged?.Invoke(); }
    }
}

// ---------------- media (Spotify or any player) via Windows' media session ----------------
static class Player {
    static Smtc.GlobalSystemMediaTransportControlsSessionManager mgr;
    static Smtc.GlobalSystemMediaTransportControlsSession cur;
    static readonly HashSet<Smtc.GlobalSystemMediaTransportControlsSession> hooked = new();
    public static event Action Changed;
    public static bool Has, Playing;
    public static string Title = "", Artist = "", App = "";
    public static ImageSource Art;
    static TimeSpan pos, end; static DateTimeOffset stamp; static string artKey = "", sig = "";
    static bool busy, again;
    public static TimeSpan End => end;
    public static TimeSpan Position {
        get {
            var p = pos; if (Playing) p += DateTimeOffset.Now - stamp;
            if (end > TimeSpan.Zero && p > end) p = end; if (p < TimeSpan.Zero) p = TimeSpan.Zero; return p;
        }
    }

    public static async void Init() {
        try {
            mgr = await Smtc.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            mgr.SessionsChanged += (_, _) => Kick();
            mgr.CurrentSessionChanged += (_, _) => Kick();
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; t.Tick += (_, _) => Kick(); t.Start();
            Kick();
        } catch { }
    }
    static void Kick() { try { Application.Current?.Dispatcher.BeginInvoke(new Action(Refresh)); } catch { } }

    static async void Refresh() {
        if (busy) { again = true; return; }
        busy = true;
        try { do { again = false; await Pull(); } while (again); } catch { } finally { busy = false; }
    }

    static Smtc.GlobalSystemMediaTransportControlsSession Pick() {
        var all = mgr.GetSessions().ToList(); if (all.Count == 0) return null;
        bool Pl(Smtc.GlobalSystemMediaTransportControlsSession x) { try { return x.GetPlaybackInfo().PlaybackStatus == Smtc.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; } catch { return false; } }
        bool Sp(Smtc.GlobalSystemMediaTransportControlsSession x) { try { return x.SourceAppUserModelId.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0; } catch { return false; } }
        return all.FirstOrDefault(x => Sp(x) && Pl(x)) ?? all.FirstOrDefault(Pl) ?? all.FirstOrDefault(Sp) ?? mgr.GetCurrentSession() ?? all[0];
    }

    static string Friendly(string id) {
        if (string.IsNullOrEmpty(id)) return "";
        if (id.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0) return "Spotify";
        int b = id.IndexOf('!'); if (b >= 0) id = id.Substring(b + 1);
        return id.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
    }

    static async Task Pull() {
        var s = Pick();
        cur = s;
        if (s != null && hooked.Add(s)) {
            s.MediaPropertiesChanged += (_, _) => Kick();
            s.PlaybackInfoChanged += (_, _) => Kick();
            s.TimelinePropertiesChanged += (_, _) => Kick();
        }
        if (s == null) { Commit(false, false, "", "", "", null); return; }
        bool playing = s.GetPlaybackInfo().PlaybackStatus == Smtc.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var mp = await s.TryGetMediaPropertiesAsync();
        string title = mp.Title ?? "", artist = mp.Artist ?? "";
        var tl = s.GetTimelineProperties();
        pos = tl.Position - tl.StartTime; end = tl.EndTime - tl.StartTime; stamp = tl.LastUpdatedTime;
        string key = title + "|" + artist;
        ImageSource art = Art;
        if (key != artKey || (art == null && mp.Thumbnail != null)) { artKey = key; art = await LoadArt(mp); }
        Commit(title.Length > 0, playing, title, artist, Friendly(s.SourceAppUserModelId), art);
    }

    static async Task<ImageSource> LoadArt(Smtc.GlobalSystemMediaTransportControlsSessionMediaProperties mp) {
        try {
            if (mp.Thumbnail == null) return null;
            using var rs = await mp.Thumbnail.OpenReadAsync();
            var ms = new MemoryStream(); await rs.AsStreamForRead().CopyToAsync(ms); ms.Position = 0;
            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.DecodePixelWidth = 200; bi.EndInit(); bi.Freeze();
            return bi;
        } catch { return null; }
    }

    static void Commit(bool has, bool playing, string title, string artist, string app, ImageSource art) {
        string n = $"{has}|{playing}|{title}|{artist}|{app}|{art != null}";
        Has = has; Playing = playing; Title = title; Artist = artist; App = app; Art = art;
        if (n == sig) return;
        sig = n; Changed?.Invoke();
    }

    static async void Do(Func<Task> act) {
        try { await act(); } catch { }
        await Task.Delay(350); Kick();
    }
    public static void PlayPause() => Do(async () => { if (cur != null) await cur.TryTogglePlayPauseAsync(); });
    public static void Next() => Do(async () => { if (cur != null) await cur.TrySkipNextAsync(); });
    public static void Prev() => Do(async () => { if (cur != null) await cur.TrySkipPreviousAsync(); });
    public static void OpenSpotify() { try { Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true }); } catch { } }
}

class BarWindow : Shell {
    TextBlock clk, dat, st, ttl; Border art; TextBlock pp; FrameworkElement mediaBox, statBox, netBox;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) }, poll = new() { Interval = TimeSpan.FromMilliseconds(50) };
    bool shown, animating; int animId, dwell, away; Flyout fly; string flyKind;

    public BarWindow() {
        SizeToContent = SizeToContent.WidthAndHeight; Width = double.NaN; Height = double.NaN; Top = -100;
        Build();
        Store.Changed += () => { CloseFly(); Build(); Place(); };
        Player.Changed += UpdateMedia;
        SizeChanged += (_, _) => Place();
        clock.Tick += (_, _) => { if (!Full.On) Tick(); }; clock.Start();
        poll.Tick += (_, _) => Poll();
        Loaded += (_, _) => { Visibility = Visibility.Hidden; Place(); poll.Start(); };
    }

    // The bar is a small pill that is NOT a reserved appbar: it slides in when the cursor hits the very top edge.
    protected override void Place() {
        var s = WF.Screen.PrimaryScreen.Bounds; double sc = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double w = ActualWidth > 0 ? ActualWidth : 400, h = ActualHeight > 0 ? ActualHeight : 30;
        Left = s.Left / sc + (s.Width / sc - w) / 2;
        if (!animating) Top = shown ? s.Top / sc : -h - 8;
    }

    void Slide(bool show) {
        if (show == shown) return;
        shown = show; if (show) Visibility = Visibility.Visible;
        double h = ActualHeight > 0 ? ActualHeight : 30, to = show ? 0 : -h - 8; int id = ++animId;
        var an = new DoubleAnimation(to, TimeSpan.FromMilliseconds(show ? 170 : 140)) { EasingFunction = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn } };
        animating = true;
        an.Completed += (_, _) => {
            if (id != animId) return;
            animating = false; BeginAnimation(TopProperty, null); Top = to;
            if (!shown) Visibility = Visibility.Hidden;
        };
        BeginAnimation(TopProperty, an);
    }

    void Poll() {
        var s = WF.Screen.PrimaryScreen.Bounds; var p = WF.Cursor.Position;
        if (Full.On) { dwell = away = 0; if (shown) { CloseFly(); Slide(false); } return; }
        bool atTop = p.Y <= s.Top && p.X >= s.Left && p.X < s.Right;
        if (atTop) { away = 0; if (!shown && ++dwell >= 3) Slide(true); return; }
        dwell = 0;
        if (!shown) return;
        bool inside = Over(p.X, p.Y) || (fly != null && fly.Over(p.X, p.Y));
        if (inside) away = 0;
        else if ((away += 50) >= 500) { CloseFly(); Slide(false); }
    }

    void CloseFly() { if (fly != null) { var f = fly; fly = null; f.Close(); } }
    void Toggle(string kind, FrameworkElement anchor) {
        if (fly != null && flyKind == kind) { CloseFly(); return; }
        CloseFly(); flyKind = kind;
        Flyout f; if (kind == "cal") f = new CalendarFlyout(); else if (kind == "net") f = new NetFlyout(); else f = new MediaFlyout();
        fly = f; f.Closed += (_, _) => { if (fly == f) fly = null; };
        var s = WF.Screen.PrimaryScreen.Bounds; double sc = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double cx = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0)).X / sc;
        double left = Math.Max(0, Math.Min(cx - f.Width / 2, s.Width / sc - f.Width));
        f.OpenAt(left, ActualHeight - 8);
    }

    static UIElement Div() => new Border { Width = 1, Height = 12, Background = Th.Line, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };

    void Build() {
        var mono = new FontFamily("Cascadia Mono, Consolas");
        dat = UI.Txt("", Th.Dim, 11.5); dat.Margin = new Thickness(0, 0, 8, 0);
        clk = UI.Txt("", Th.Fg, 12, FontWeights.SemiBold); st = UI.Txt("", Th.Dim, 11); clk.FontFamily = st.FontFamily = mono;
        st.Margin = new Thickness(6, 0, 6, 0);

        var logo = UI.Click(new Shp.Ellipse { Width = 8, Height = 8, Fill = Th.Accent }, ControlPanel.Open, null, 6, 2);

        art = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 7, 0), Background = Th.Tile };
        ttl = UI.Txt("", Th.Fg, 11.5); ttl.MaxWidth = 190; ttl.TextTrimming = TextTrimming.CharacterEllipsis;
        pp = UI.Glyph("\uE768", Th.Fg, 11);
        Border trackBtn = null;
        trackBtn = UI.Click(new StackPanel { Orientation = Orientation.Horizontal, Children = { art, ttl } }, () => Toggle("media", trackBtn), null, 6, 2);
        mediaBox = new StackPanel { Orientation = Orientation.Horizontal, Children = { Div(), trackBtn, UI.Click(pp, Player.PlayPause, null, 6, 3) } };

        Border clkBtn = null;
        clkBtn = UI.Click(new StackPanel { Orientation = Orientation.Horizontal, Children = { dat, clk } }, () => Toggle("cal", clkBtn), null, 7, 2);
        statBox = new StackPanel { Orientation = Orientation.Horizontal, Children = { Div(), st } };

        var wifiG = UI.Glyph("\uE701", Th.Fg, 12); var btG = UI.Glyph("\uE702", Th.Fg, 12); btG.Margin = new Thickness(8, 0, 0, 0);
        Border netBtn = null;
        netBtn = UI.Click(new StackPanel { Orientation = Orientation.Horizontal, Children = { wifiG, btG } }, () => Toggle("net", netBtn), null, 7, 3);
        netBox = new StackPanel { Orientation = Orientation.Horizontal, Children = { Div(), netBtn } };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { logo, mediaBox, Div(), clkBtn, statBox, netBox } };
        Content = new Border {
            Background = Th.Glass, BorderBrush = Th.Line, BorderThickness = new Thickness(1, 0, 1, 1), CornerRadius = new CornerRadius(0, 0, 14, 14),
            Padding = new Thickness(6, 2, 6, 3), Child = row };
        UpdateMedia(); Tick();
    }

    void UpdateMedia() {
        if (mediaBox == null) return;
        bool on = Store.C.Media && Player.Has;
        mediaBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) return;
        ttl.Text = Player.Artist.Length > 0 ? Player.Title + "  ·  " + Player.Artist : Player.Title;
        ttl.Foreground = Player.Playing ? Th.Fg : Th.Dim;
        art.Background = Player.Art != null ? (Brush)new ImageBrush(Player.Art) { Stretch = Stretch.UniformToFill } : Th.Tile;
        pp.Text = Player.Playing ? "\uE769" : "\uE768";
    }

    void Tick() {
        var c = Store.C; var now = DateTime.Now;
        string tf = c.Clock24 ? (c.Seconds ? "HH:mm:ss" : "HH:mm") : (c.Seconds ? "h:mm:ss tt" : "h:mm tt");
        dat.Visibility = c.Date ? Visibility.Visible : Visibility.Collapsed;
        dat.Text = now.ToString("ddd d MMM"); clk.Text = now.ToString(tf);
        statBox.Visibility = c.Stats ? Visibility.Visible : Visibility.Collapsed;
        netBox.Visibility = c.Network ? Visibility.Visible : Visibility.Collapsed;
        if (c.Stats) st.Text = $"CPU {Stats.Cpu.ToString().PadLeft(2)}%  RAM {Stats.Ram.ToString().PadLeft(2)}%";
    }
}

// ---------------- flyouts that drop down from the bar ----------------
abstract class Flyout : Shell {
    protected readonly Border card = new();
    protected Flyout(double w) {
        Width = w + 28; Height = double.NaN; SizeToContent = SizeToContent.Height;
        card.Background = Th.Bg; card.BorderBrush = Th.Line; card.BorderThickness = new Thickness(1); card.CornerRadius = new CornerRadius(16); card.Margin = new Thickness(14);
        card.Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 3, Opacity = 0.4, Color = Colors.Black };
        Content = card;
    }
    public void OpenAt(double left, double top) {
        Left = left; Top = top; Opacity = 0; Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        BeginAnimation(TopProperty, new DoubleAnimation(top - 8, top, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}

class CalendarFlyout : Flyout {
    DateTime view = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    readonly TextBlock big, sub, title; readonly Border host = new(); readonly DispatcherTimer t = new() { Interval = TimeSpan.FromSeconds(1) };
    public CalendarFlyout() : base(300) {
        var root = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        big = UI.Txt("", Th.Fg, 38, FontWeights.Light); big.FontFamily = new FontFamily("Segoe UI Light, Segoe UI");
        sub = UI.Txt("", Th.Dim, 12);
        root.Children.Add(big); root.Children.Add(sub);

        title = UI.Txt("", Th.Fg, 13, FontWeights.SemiBold); title.HorizontalAlignment = HorizontalAlignment.Center;
        var titleBtn = UI.Click(title, () => { view = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); Month(); }, null, 10, 3);
        titleBtn.HorizontalAlignment = HorizontalAlignment.Center;
        var prev = UI.Click(UI.Glyph("\uE76B", Th.Fg, 11), () => { view = view.AddMonths(-1); Month(); }, null, 9, 5);
        var next = UI.Click(UI.Glyph("\uE76C", Th.Fg, 11), () => { view = view.AddMonths(1); Month(); }, null, 9, 5);
        DockPanel.SetDock(prev, System.Windows.Controls.Dock.Left); DockPanel.SetDock(next, System.Windows.Controls.Dock.Right);
        var hdr = new DockPanel { Margin = new Thickness(0, 10, 0, 6) };
        hdr.Children.Add(prev); hdr.Children.Add(next); hdr.Children.Add(titleBtn);
        root.Children.Add(new Border { Height = 1, Background = Th.Line, Margin = new Thickness(0, 12, 0, 0) });
        root.Children.Add(hdr); root.Children.Add(host);
        card.Child = root;
        Month(); Tick();
        t.Tick += (_, _) => Tick(); t.Start(); Closed += (_, _) => t.Stop();
    }
    void Tick() {
        var c = Store.C; var n = DateTime.Now;
        big.Text = n.ToString(c.Clock24 ? "HH:mm:ss" : "h:mm:ss tt"); sub.Text = n.ToString("dddd, d MMMM yyyy");
    }
    static UIElement Cell(string s, Brush fg, Brush bg, double sz, bool bold) {
        var tx = UI.Txt(s, fg, sz, bold ? FontWeights.SemiBold : FontWeights.Normal); tx.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border { Height = 31, Child = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = bg, Child = tx, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
    }
    void Month() {
        title.Text = view.ToString("MMMM yyyy");
        var g = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7 };
        foreach (var d in new[] { "M", "T", "W", "T", "F", "S", "S" }) g.Children.Add(Cell(d, Th.Dim, null, 11, false));
        int off = ((int)view.DayOfWeek + 6) % 7, days = DateTime.DaysInMonth(view.Year, view.Month);
        for (int i = 0; i < off; i++) g.Children.Add(new Border { Height = 31 });
        for (int d = 1; d <= days; d++) {
            var date = new DateTime(view.Year, view.Month, d); bool today = date == DateTime.Today, we = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
            g.Children.Add(Cell(d.ToString(), today ? Brushes.White : we ? Th.Dim : Th.Fg, today ? Th.Accent : null, 12.5, today));
        }
        host.Child = g;
    }
}

// ---------------- audio visualiser: WASAPI loopback (what your speakers play) -> FFT -> bars ----------------
static class Viz {
    public const int Bands = 32;
    public static readonly float[] Level = new float[Bands];   // 0..1, written by the audio thread
    public static long Last;                                    // Environment.TickCount64 of the latest audio packet
    static NAudio.Wave.WasapiLoopbackCapture cap;
    static int users;
    const int N = 1024, M = 10;
    static readonly float[] buf = new float[N]; static int fill;
    static readonly float[] win = Enumerable.Range(0, N).Select(i => (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)))).ToArray();
    static readonly NAudio.Dsp.Complex[] fft = new NAudio.Dsp.Complex[N];
    static int rate = 48000, runs;

    static readonly object gate = new(); static bool starting, logged;
    static void Log(string m) {
        try { var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hoster"); Directory.CreateDirectory(d); File.AppendAllText(Path.Combine(d, "viz.log"), DateTime.Now.ToString("HH:mm:ss ") + m + "\r\n"); } catch { }
    }
    public static void Start() {
        lock (gate) { users++; if (cap != null || starting) return; starting = true; }
        // created and run on a background (MTA) thread so the audio COM objects never touch the UI thread
        new Thread(() => {
            NAudio.Wave.WasapiLoopbackCapture c = null;
            try {
                c = new NAudio.Wave.WasapiLoopbackCapture();
                rate = c.WaveFormat.SampleRate; fill = 0;
                Log($"capture start: {c.WaveFormat}");
                c.DataAvailable += OnData;
                c.StartRecording();
                lock (gate) { cap = c; starting = false; if (users <= 0) { var x = cap; cap = null; try { x.StopRecording(); x.Dispose(); } catch { } } }
            } catch (Exception ex) {
                Log("capture failed: " + ex);
                lock (gate) { starting = false; cap = null; }
                try { c?.Dispose(); } catch { }
            }
        }) { IsBackground = true, Name = "VizCapture" }.Start();
    }
    public static void Stop() {
        NAudio.Wave.WasapiLoopbackCapture c;
        lock (gate) { if (--users > 0) return; users = 0; c = cap; cap = null; }
        if (c == null) return;
        try { c.DataAvailable -= OnData; c.StopRecording(); c.Dispose(); } catch { }
    }

    static void OnData(object s, NAudio.Wave.WaveInEventArgs e) {
        try {
            var f = ((NAudio.CoreAudioApi.WasapiCapture)s).WaveFormat;
            int ch = Math.Max(1, f.Channels), bytes = f.BitsPerSample / 8, frame = ch * bytes;
            if (bytes != 4 && bytes != 2) return;
            Last = Environment.TickCount64;
            if (!logged) { logged = true; Log($"first audio packet: {e.BytesRecorded} bytes"); }
            for (int o = 0; o + frame <= e.BytesRecorded; o += frame) {
                float sum = 0;
                for (int c = 0; c < ch; c++)
                    sum += bytes == 4 ? BitConverter.ToSingle(e.Buffer, o + c * 4) : BitConverter.ToInt16(e.Buffer, o + c * 2) / 32768f;
                buf[fill++] = sum / ch;
                if (fill == N) { Analyse(); fill = 0; }
            }
        } catch (Exception ex) { Log("OnData: " + ex.Message); }
    }

    static void Analyse() {
        // NAudio already divides a forward FFT by N, so only the window gain (x4) is applied below
        for (int i = 0; i < N; i++) { fft[i].X = buf[i] * win[i]; fft[i].Y = 0; }
        NAudio.Dsp.FastFourierTransform.FFT(true, M, fft);
        double binHz = (double)rate / N, lo = 45, hi = Math.Min(15000, rate / 2.0 - 100);
        for (int b = 0; b < Bands; b++) {
            double f0 = lo * Math.Pow(hi / lo, (double)b / Bands), f1 = lo * Math.Pow(hi / lo, (double)(b + 1) / Bands);
            int i0 = Math.Max(1, (int)(f0 / binHz)), i1 = Math.Max(i0 + 1, (int)(f1 / binHz));
            double peak = 0;
            for (int i = i0; i < i1 && i < N / 2; i++) { double m = Math.Sqrt(fft[i].X * fft[i].X + fft[i].Y * fft[i].Y) * 4.0; if (m > peak) peak = m; }
            double db = 20 * Math.Log10(peak + 1e-9) + 14.0 * b / Bands;    // gentle treble tilt so highs show up
            Level[b] = (float)Math.Max(0, Math.Min(1, (db + 72) / 52));
        }
        if (++runs % 400 == 1) Log("levels: max " + Level.Max().ToString("0.00"));
    }
}

class VizView : FrameworkElement {
    readonly double[] v = new double[Viz.Bands];
    public VizView() {
        IsHitTestVisible = false; SnapsToDevicePixels = true;
        Loaded += (_, _) => { Viz.Start(); CompositionTarget.Rendering += Frame; };
        Unloaded += (_, _) => { CompositionTarget.Rendering -= Frame; Viz.Stop(); };
    }
    long lastDraw;   // limit the visualiser to ~30 fps
    void Frame(object s, EventArgs e) {
        long now = Environment.TickCount64;
        if (now - lastDraw < 30) return;
        lastDraw = now;
        bool live = Player.Playing && now - Viz.Last < 200;
        for (int i = 0; i < v.Length; i++) {
            double t = live ? Viz.Level[i] : 0;
            v[i] = t > v[i] ? v[i] + (t - v[i]) * 0.84 : Math.Max(t, v[i] - 0.07);   // steps doubled so the motion feels the same as at 60 fps
        }
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc) {
        int n = v.Length; double gap = 3, w = (ActualWidth - gap * (n - 1)) / n, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        for (int i = 0; i < n; i++) {
            double bh = Math.Max(3, v[i] * h);
            dc.DrawRoundedRectangle(Th.Accent, null, new Rect(i * (w + gap), h - bh, w, bh), Math.Min(2, w / 2), Math.Min(2, w / 2));
        }
    }
}

class MediaFlyout : Flyout {
    ColumnDefinition c0 = new(), c1 = new(); TextBlock cur, tot;
    readonly DispatcherTimer t = new() { Interval = TimeSpan.FromSeconds(1) }; readonly Action h;
    public MediaFlyout() : base(330) {
        h = Build; Player.Changed += h;
        Closed += (_, _) => { Player.Changed -= h; t.Stop(); };
        t.Tick += (_, _) => Prog(); t.Start(); Build();
    }
    static string Fmt(TimeSpan x) => x.TotalHours >= 1 ? $"{(int)x.TotalHours}:{x.Minutes:00}:{x.Seconds:00}" : $"{x.Minutes}:{x.Seconds:00}";
    void Prog() {
        if (!Player.Has || cur == null) return;
        var e = Player.End; var p = Player.Position;
        double f = e.TotalSeconds > 0 ? Math.Min(1, p.TotalSeconds / e.TotalSeconds) : 0;
        c0.Width = new GridLength(f, GridUnitType.Star); c1.Width = new GridLength(1 - f, GridUnitType.Star);
        cur.Text = Fmt(p); tot.Text = e.TotalSeconds > 0 ? Fmt(e) : "";
    }
    UIElement Round(string glyph, Action act, double size, bool main) {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 6, 0),
            Background = main ? Th.Accent : Brushes.Transparent, Child = UI.Glyph(glyph, main ? Brushes.White : Th.Fg, main ? 16 : 14) };
        b.MouseEnter += (_, _) => { if (!main) b.Background = Th.Tile; }; b.MouseLeave += (_, _) => { if (!main) b.Background = Brushes.Transparent; };
        b.MouseLeftButtonUp += (_, _) => act();
        return b;
    }
    void Build() {
        var root = new StackPanel { Margin = new Thickness(16) };
        cur = null;
        if (!Player.Has) {
            root.Children.Add(UI.Txt("Nothing playing", Th.Fg, 14, FontWeights.SemiBold));
            var d = UI.Txt("Play something in Spotify (or any media app) and it shows up here.", Th.Dim, 12); d.TextWrapping = TextWrapping.Wrap; d.Margin = new Thickness(0, 4, 0, 12);
            root.Children.Add(d);
            var b = UI.Click(UI.Txt("Open Spotify", Brushes.White), Player.OpenSpotify, Th.Accent, 14, 6); b.HorizontalAlignment = HorizontalAlignment.Left;
            root.Children.Add(b); card.Child = root; return;
        }
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); top.ColumnDefinitions.Add(new ColumnDefinition());
        var art = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(10), Background = Player.Art != null ? (Brush)new ImageBrush(Player.Art) { Stretch = Stretch.UniformToFill } : Th.Tile };
        var name = UI.Txt(Player.Title, Th.Fg, 14, FontWeights.SemiBold); name.TextTrimming = TextTrimming.CharacterEllipsis;
        var who = UI.Txt(Player.Artist, Th.Dim, 12); who.TextTrimming = TextTrimming.CharacterEllipsis;
        var app = UI.Txt(Player.App, Th.Dim, 10.5); app.Margin = new Thickness(0, 6, 0, 0);
        var info = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { name, who, app } };
        Grid.SetColumn(info, 1); top.Children.Add(art); top.Children.Add(info);
        root.Children.Add(top);
        if (Store.C.Visualizer) root.Children.Add(new VizView { Height = 44, Margin = new Thickness(0, 14, 0, 0) });

        c0 = new ColumnDefinition(); c1 = new ColumnDefinition();
        var bar = new Grid { Height = 4, Margin = new Thickness(0, 16, 0, 6) }; bar.ColumnDefinitions.Add(c0); bar.ColumnDefinitions.Add(c1);
        var track = new Border { CornerRadius = new CornerRadius(2), Background = Th.Tile }; Grid.SetColumnSpan(track, 2);
        var fill = new Border { CornerRadius = new CornerRadius(2), Background = Th.Accent };
        bar.Children.Add(track); bar.Children.Add(fill);
        root.Children.Add(bar);

        cur = UI.Txt("0:00", Th.Dim, 10.5); tot = UI.Txt("", Th.Dim, 10.5);
        var times = new DockPanel(); DockPanel.SetDock(tot, System.Windows.Controls.Dock.Right); times.Children.Add(tot); times.Children.Add(cur);
        root.Children.Add(times);

        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), Children = {
            Round("\uE892", Player.Prev, 36, false), Round(Player.Playing ? "\uE769" : "\uE768", Player.PlayPause, 44, true), Round("\uE893", Player.Next, 36, false) } });
        card.Child = root; Prog();
    }
}

// ---------------- window snapshots for the hover list ----------------
static class Snap {
    [StructLayout(LayoutKind.Sequential)] struct RC { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RC r, int size);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

    public static void Close(IntPtr h) => PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);

    // a picture of the window, scaled to fit maxW x maxH (null if it is minimised or can't be captured)
    public static BitmapSource Of(IntPtr h, int maxW, int maxH) {
        try {
            if (IsIconic(h) || !GetWindowRect(h, out var r)) return null;
            int w = r.R - r.L, hh = r.B - r.T; if (w <= 0 || hh <= 0) return null;
            using var full = new System.Drawing.Bitmap(w, hh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(full)) { var dc = g.GetHdc(); try { PrintWindow(h, dc, 2); } finally { g.ReleaseHdc(dc); } }
            var src = new System.Drawing.Rectangle(0, 0, w, hh);
            if (DwmGetWindowAttribute(h, 9, out var f, 16) == 0) {           // crop the invisible resize border / shadow
                int x = Math.Max(0, f.L - r.L), y = Math.Max(0, f.T - r.T), fw = Math.Min(w - x, f.R - f.L), fh = Math.Min(hh - y, f.B - f.T);
                if (fw > 0 && fh > 0) src = new System.Drawing.Rectangle(x, y, fw, fh);
            }
            double sc = Math.Min(1.5, Math.Min((double)maxW / src.Width, (double)maxH / src.Height));
            int tw = Math.Max(1, (int)(src.Width * sc)), th = Math.Max(1, (int)(src.Height * sc));
            using var small = new System.Drawing.Bitmap(tw, th, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (var g2 = System.Drawing.Graphics.FromImage(small)) {
                g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g2.DrawImage(full, new System.Drawing.Rectangle(0, 0, tw, th), src, System.Drawing.GraphicsUnit.Pixel);
            }
            var hb = small.GetHbitmap();
            try { var bs = Imaging.CreateBitmapSourceFromHBitmap(hb, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); bs.Freeze(); return bs; }
            finally { DeleteObject(hb); }
        } catch { return null; }
    }
}

// ---------------- hover over a dock icon: a preview of every window (and every browser tab) of that app ----------------
class AppPopup : Flyout {
    static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "brave", "firefox", "opera", "vivaldi", "librewolf", "zen", "waterfox" };
    readonly Action hide;

    public AppPopup(string title, string exe, List<(IntPtr h, string title)> wins, Action hide) : base(Math.Min(wins.Count, 5) * 240 + 16) {
        this.hide = hide;
        var head = UI.Txt(title, Th.Dim, 11, FontWeights.SemiBold); head.Margin = new Thickness(10, 2, 0, 6);
        var wrap = new WrapPanel();
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, MaxHeight = 620, Content = wrap };
        card.Child = new StackPanel { Margin = new Thickness(8), Children = { head, sv } };
        bool browser = Browsers.Contains(Path.GetFileNameWithoutExtension(exe)); var icon = N.Icon(exe);
        foreach (var w in wins) wrap.Children.Add(Card(w.h, w.title, icon, browser));
    }

    UIElement Card(IntPtr h, string title, ImageSource icon, bool browser) {
        // title row: small app icon, window title, close button
        var tr = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        tr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); tr.ColumnDefinitions.Add(new ColumnDefinition()); tr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (icon != null) tr.Children.Add(new Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0) });
        var tx = UI.Txt(title, Th.Fg, 12); tx.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(tx, 1); tr.Children.Add(tx);
        var x = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = UI.Glyph("\uE711", Th.Dim, 10), ToolTip = "Close window" };
        x.MouseEnter += (_, _) => x.Background = Th.Tile; x.MouseLeave += (_, _) => x.Background = Brushes.Transparent;
        x.MouseLeftButtonUp += (_, e) => { e.Handled = true; hide(); Snap.Close(h); };
        Grid.SetColumn(x, 2); tr.Children.Add(x);

        // snapshot of the window (fills in a moment after the popup opens)
        var thumb = new Border { Width = 216, Height = 124, CornerRadius = new CornerRadius(6), Background = Th.Tile, ClipToBounds = true };
        if (icon != null) thumb.Child = new Image { Source = icon, Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        LoadThumb(h, thumb);

        var body = new StackPanel { Width = 216, Children = { tr, thumb } };
        if (browser) {
            var host = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            body.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, MaxHeight = 220, Content = host });
            LoadTabs(h, host);
        }
        var c = UI.Click(body, () => { hide(); N.Focus(h); }, null, 8, 8); c.Margin = new Thickness(2); return c;
    }

    async void LoadThumb(IntPtr h, Border thumb) {
        try {
            var bs = await Task.Run(() => Snap.Of(h, 216, 124));
            if (bs != null && IsVisible) thumb.Child = new Image { Source = bs, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        } catch { }
    }

    UIElement TabRow(string text, bool selected, Action act) {
        var dot = new Shp.Ellipse { Width = 6, Height = 6, Fill = selected ? Th.Accent : Brushes.Transparent, Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var tx = UI.Txt(text, selected ? Th.Fg : Th.Dim, 12); tx.TextTrimming = TextTrimming.CharacterEllipsis; tx.MaxWidth = 180;
        var r = UI.Click(new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, tx } }, act, null, 6, 4);
        r.MouseLeftButtonUp += (_, e) => e.Handled = true;                // don't also trigger the window card underneath
        return r;
    }

    async void LoadTabs(IntPtr h, StackPanel host) {
        try {
            var tabs = await Task.Run(() => TabsOf(h));
            if (tabs.Count == 0) { await Task.Delay(800); tabs = await Task.Run(() => TabsOf(h)); }   // browsers build their accessibility tree on first contact
            if (!IsVisible) return;
            foreach (var t in tabs) { var pick = t.Pick; host.Children.Add(TabRow(t.Name, t.Sel, () => { hide(); N.Focus(h); Task.Run(pick); })); }
        } catch { }
    }

    static List<(string Name, bool Sel, Action Pick)> TabsOf(IntPtr hwnd) {
        var res = new List<(string, bool, Action)>();
        try {
            var root = UIA.AutomationElement.FromHandle(hwnd);
            var strip = root.FindFirst(UIA.TreeScope.Descendants, new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.Tab)) ?? root;
            var items = strip.FindAll(UIA.TreeScope.Descendants, new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.TabItem));
            foreach (UIA.AutomationElement t in items) {
                string n = t.Current.Name; if (string.IsNullOrWhiteSpace(n)) continue;
                bool sel = false; UIA.SelectionItemPattern sp = null;
                if (t.TryGetCurrentPattern(UIA.SelectionItemPattern.Pattern, out var o)) { sp = (UIA.SelectionItemPattern)o; try { sel = sp.Current.IsSelected; } catch { } }
                var s = sp; res.Add((n, sel, () => { try { s?.Select(); } catch { } }));
                if (res.Count >= 60) break;
            }
        } catch { }
        return res;
    }

    // cx = centre of the icon (DIPs), iconTop = top of the icon (DIPs); the popup sits just above it
    public void OpenAbove(double cx, double iconTop) {
        Opacity = 0; Left = Math.Max(4, Math.Min(SystemParameters.PrimaryScreenWidth - Width - 4, cx - Width / 2)); Top = iconTop - 300; Show(); UpdateLayout();
        Top = iconTop - ActualHeight + 14;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(60)));
    }
}

// ---------------- right-click menu for a dock icon (themed, replaces the plain Windows menu) ----------------
class DockMenu : Flyout {
    static DockMenu current;
    public static bool IsOpen => current != null;
    readonly DispatcherTimer t = new() { Interval = TimeSpan.FromMilliseconds(60) };
    DateTime away = DateTime.UtcNow.AddSeconds(1), opened = DateTime.UtcNow;
    static readonly Brush Danger = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));

    // items: text == null draws a divider line
    public DockMenu(string title, string exe, List<(string glyph, string text, bool danger, bool on, Action act)> items) : base(200) {
        current?.Dismiss(); current = this;
        var col = new StackPanel { Margin = new Thickness(6) };
        var hd = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 8) };
        var ic = N.Icon(exe); if (ic != null) hd.Children.Add(new Image { Source = ic, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) });
        var tt = UI.Txt(title, Th.Fg, 12.5, FontWeights.SemiBold); tt.TextTrimming = TextTrimming.CharacterEllipsis; tt.MaxWidth = 140; hd.Children.Add(tt);
        col.Children.Add(hd);
        col.Children.Add(new Border { Height = 1, Background = Th.Line, Margin = new Thickness(4, 0, 4, 4) });
        foreach (var it in items) {
            if (it.text == null) { col.Children.Add(new Border { Height = 1, Background = Th.Line, Margin = new Thickness(4, 4, 4, 4) }); continue; }
            var fg = it.danger ? Danger : Th.Fg;
            var g = UI.Glyph(it.glyph, fg, 13); g.Width = 22; g.HorizontalAlignment = HorizontalAlignment.Left;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { g, UI.Txt(it.text, fg, 13) } };
            var act = it.act;
            var b = UI.Click(row, () => { if (!it.on) return; Dismiss(); act(); }, null, 10, 7);
            if (!it.on) { b.Opacity = 0.4; b.Cursor = Cursors.Arrow; }
            col.Children.Add(b);
        }
        card.Child = col;
        Closed += (_, _) => { t.Stop(); if (current == this) current = null; };
        t.Tick += (_, _) => {
            var c = WF.Cursor.Position;
            if (Over(c.X, c.Y, 14)) { away = DateTime.UtcNow; return; }
            bool clickedOutside = WF.Control.MouseButtons != WF.MouseButtons.None && (DateTime.UtcNow - opened).TotalMilliseconds > 250;
            if (clickedOutside || (DateTime.UtcNow - away).TotalMilliseconds > 450) Dismiss();
        };
        t.Start();
    }
    void Dismiss() { try { Close(); } catch { } }

    // cx = centre of the icon (DIPs), iconTop = top of the icon (DIPs); the menu sits just above it
    public void OpenAbove(double cx, double iconTop) {
        Opacity = 0; Left = Math.Max(4, Math.Min(SystemParameters.PrimaryScreenWidth - Width - 4, cx - Width / 2)); Top = iconTop - 300; Show(); UpdateLayout();
        Top = iconTop - ActualHeight + 14;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(80)));
    }
}

class DockWindow : Shell {
    List<(uint pid, IntPtr h, string title, string path)> live = new();
    StackPanel row;
    readonly DispatcherTimer t = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly DispatcherTimer hov = new() { Interval = TimeSpan.FromMilliseconds(40) };
    AppPopup pop; string popKey, hoverKey; DateTime hoverSince, leftAt = DateTime.UtcNow; bool showing;
    public DockWindow() {
        Build();
        hov.Tick += (_, _) => { if (!Full.On) HoverTick(); }; hov.Start();
        Store.Changed += () => { Build(); Place(); };
        async Task Poll() { live = await Task.Run(() => N.Running()); Build(); }
        Full.Changed += async () => { Visibility = Full.On ? Visibility.Hidden : Visibility.Visible; if (Full.On) HidePop(); else await Poll(); };
        t.Tick += async (_, _) => { if (!Full.On) await Poll(); }; t.Start();
        Loaded += async (_, _) => await Poll();
        MouseMove += (_, e) => Mag(e.GetPosition(row));
        MouseLeave += (_, _) => Mag(new Point(-9999, 0));
    }
    protected override void Place() => N.AppBar(Handle, 3, Px(Store.C.IconSize + 44), Px(Math.Max(70, 64 + Store.C.IconSize * Store.C.MagnifyAmount / 100.0)));   // extra headroom so big magnification isn't cut off
    void Mag(Point m) {
        foreach (var e in row.Children.OfType<StackPanel>()) {
            if (e.RenderTransform is not ScaleTransform st) continue;
            double cx = e.TranslatePoint(new Point(e.ActualWidth / 2, 0), row).X;
            st.ScaleX = st.ScaleY = Store.C.Magnify ? 1 + Store.C.MagnifyAmount / 100.0 * Math.Max(0, 1 - Math.Abs(m.X - cx) / 110) : 1;
        }
    }
    FrameworkElement Face(string name, string target, int sz) {
        var ic = N.Icon(target);
        if (ic != null) return new Image { Source = ic, Width = sz, Height = sz };
        return new Border { Width = sz, Height = sz, CornerRadius = new CornerRadius(sz * 0.22), Background = Th.Tile, BorderBrush = Th.Line, BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = name.Substring(0, 1).ToUpper(), Foreground = Th.Fg, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
    }
    StackPanel Item(FrameworkElement face, string tip, bool running, Action click, string key = null) {
        var p = new StackPanel { Margin = new Thickness(3, 0, 3, 0), RenderTransformOrigin = new Point(0.5, 1), RenderTransform = new ScaleTransform(1, 1), ToolTip = (running && key != null && Store.C.HoverList) ? null : tip, Tag = key, Cursor = Cursors.Hand,
            Children = { face, new Shp.Ellipse { Width = 4, Height = 4, Margin = new Thickness(0, 3, 0, 0), Fill = Th.Accent, Opacity = running ? 1 : 0 } } };
        p.MouseLeftButtonUp += (_, _) => click();
        return p;
    }
    void ShowMenu(StackPanel he, string title, string exe, List<(string glyph, string text, bool danger, bool on, Action act)> items) {
        HidePop();
        var o = he.PointToScreen(new Point(0, 0)); var d = VisualTreeHelper.GetDpi(this);
        double cx = (o.X + he.ActualWidth * d.DpiScaleX / 2) / d.DpiScaleX, top = o.Y / d.DpiScaleY - Store.C.IconSize * 0.6 - 8;
        new DockMenu(title, exe, items).OpenAbove(cx, top);
    }
    void EndTask(string path) {
        HidePop();
        live = live.Where(x => !string.Equals(x.path, path, StringComparison.OrdinalIgnoreCase)).ToList(); Build();   // drop it from the dock straight away
        Task.Run(() => N.EndTask(path));
    }
    static void Launch(string t) { try { Process.Start(new ProcessStartInfo(t) { UseShellExecute = true }); } catch { } }
    void Build() {
        var c = Store.C; int sz = c.IconSize;
        row = new StackPanel { Orientation = Orientation.Horizontal };
        if (c.OwnStart) {
            var sg = new System.Windows.Controls.Primitives.UniformGrid { Rows = 2, Columns = 2, Width = sz * 0.44, Height = sz * 0.44, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            for (int q = 0; q < 4; q++) sg.Children.Add(new Border { Background = q == 0 ? Th.Accent : Th.Fg, CornerRadius = new CornerRadius(2), Margin = new Thickness(1.5) });
            row.Children.Add(Item(new Border { Width = sz, Height = sz, CornerRadius = new CornerRadius(sz * 0.22), Background = Th.Tile, BorderBrush = Th.Line, BorderThickness = new Thickness(1), Child = sg }, "Start", false, StartMenu.Toggle));
            row.Children.Add(Sep());
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pin in c.Pinned.ToList()) {
            var w = live.FirstOrDefault(x => string.Equals(x.path, pin.Target, StringComparison.OrdinalIgnoreCase)); seen.Add(pin.Target);
            var it = Item(Face(pin.Name, pin.Target, sz), pin.Name, w.h != IntPtr.Zero, () => { if (w.h != IntPtr.Zero) N.Focus(w.h); else Launch(pin.Target); }, pin.Target);
            bool isRunning = w.h != IntPtr.Zero;
            it.MouseRightButtonUp += (_, _) => ShowMenu(it, pin.Name, pin.Target, new() {
                ("\uE77A", "Unpin from dock", false, true, () => { c.Pinned.Remove(pin); Store.Save(); }),
                (null, null, false, true, null),
                ("\uE711", "End task", true, isRunning, () => EndTask(pin.Target)) });
            row.Children.Add(it);
        }
        var extra = live.Where(x => seen.Add(x.path)).ToList();
        if (extra.Count > 0) { row.Children.Add(Sep()); foreach (var x in extra) {
            var h = x.h; var path = x.path; var it = Item(Face(x.title, x.path, sz), x.title, true, () => N.Focus(h), x.path);
            it.MouseRightButtonUp += (_, _) => ShowMenu(it, AppTitle(path), path, new() { ("\uE711", "End task", true, true, () => EndTask(path)) });
            row.Children.Add(it);
        } }
        row.Children.Add(Sep());
        row.Children.Add(Item(Face("+", "", sz), "Pin an app", false, Store.PinApp));
        row.Children.Add(Item(new Border { Width = sz, Height = sz, CornerRadius = new CornerRadius(sz * 0.22), Background = Th.Tile, BorderBrush = Th.Line, BorderThickness = new Thickness(1),
            Child = new Shp.Ellipse { Width = 9, Height = 9, Fill = Th.Accent } }, "Control panel", false, ControlPanel.Open));
        Content = new Border { Background = Th.Glass, BorderBrush = Th.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(c.Radius), Padding = new Thickness(10, 8, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10), Child = row };
    }
    void HidePop() { var p = pop; pop = null; popKey = null; if (p != null) { try { p.Close(); } catch { } } }
    static string AppTitle(string path) {
        try { var d = FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrWhiteSpace(d)) return d; } catch { }
        return Path.GetFileNameWithoutExtension(path);
    }
    void HoverTick() {
        try {
            if (!Store.C.HoverList || Full.On || !IsVisible || row == null || DockMenu.IsOpen) { HidePop(); return; }
            var c = WF.Cursor.Position; string key = null; StackPanel he = null;
            var pt = row.PointFromScreen(new Point(c.X, c.Y));
            foreach (var e in row.Children.OfType<StackPanel>())
                if (e.Tag is string k && new Rect(e.TranslatePoint(new Point(0, 0), row), e.RenderSize).Contains(pt)) { key = k; he = e; break; }
            if (key != null) {
                if (key != hoverKey) { hoverKey = key; hoverSince = DateTime.UtcNow; RefreshLive(); }
                leftAt = DateTime.UtcNow;
                if (popKey != key && !showing) ShowPop(key, he);   // no dwell delay
            } else {
                hoverKey = null; if (pop == null) popKey = null;
                if (pop != null && pop.Over(c.X, c.Y, 10)) leftAt = DateTime.UtcNow;
                else if (pop != null && (DateTime.UtcNow - leftAt).TotalMilliseconds > 350) HidePop();
            }
        } catch { }
    }
    async void RefreshLive() { try { live = await Task.Run(N.Running); } catch { } }   // keeps window titles current for the next hover
    async void ShowPop(string key, StackPanel he) {
        showing = true;
        try {
            var o = he.PointToScreen(new Point(0, 0)); var d = VisualTreeHelper.GetDpi(this);
            double cx = (o.X + he.ActualWidth * d.DpiScaleX / 2) / d.DpiScaleX, top = o.Y / d.DpiScaleY - Store.C.IconSize * 0.6 - 8;
            var wins = live.Where(x => string.Equals(x.path, key, StringComparison.OrdinalIgnoreCase)).Select(x => (x.h, x.title)).ToList();
            HidePop(); popKey = key;                                   // remembered even when nothing is open, so it isn't re-queried every tick
            if (wins.Count == 0 || hoverKey != key) return;
            var p = new AppPopup(AppTitle(key), key, wins, HidePop); pop = p;
            p.OpenAbove(cx, top);
        } catch { } finally { showing = false; }
    }
    static UIElement Sep() => new Border { Width = 1, Background = Th.Line, Margin = new Thickness(6, 4, 6, 8) };
}

// ---------------- Hoster's own Start menu (Win key or the dock's first button) ----------------
[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItemImageFactory { [PreserveSig] int GetImage(SZ size, int flags, out IntPtr hbitmap); }
[StructLayout(LayoutKind.Sequential)] struct SZ { public int cx, cy; }

static class WinKey {
    delegate IntPtr Proc(int code, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct KB { public uint vk, scan, flags, time; public IntPtr extra; }
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, Proc p, IntPtr mod, uint tid);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    static readonly Proc proc = Hook; static IntPtr hh; static bool down, other;

    public static void Start() { if (hh == IntPtr.Zero) hh = SetWindowsHookEx(13, proc, GetModuleHandle(null), 0); }
    public static void Stop() { if (hh != IntPtr.Zero) { UnhookWindowsHookEx(hh); hh = IntPtr.Zero; } }

    static IntPtr Hook(int code, IntPtr w, IntPtr l) {
        try {
            if (code >= 0) {
                var k = Marshal.PtrToStructure<KB>(l);
                if ((k.flags & 0x10) == 0) {                       // ignore keys we injected ourselves
                    int m = (int)w; bool dn = m == 0x100 || m == 0x104, up = m == 0x101 || m == 0x105;
                    if (k.vk == 0x5B || k.vk == 0x5C) {            // left / right Windows key
                        if (dn) { if (!down) { down = true; other = false; } }
                        else if (up) {
                            bool tap = down && !other; down = false;
                            if (tap && Store.C.OwnStart && !Full.On) {
                                // swallow the real key-up and send a "masked" one so Windows doesn't open its own Start menu
                                keybd_event(0xE8, 0, 0, UIntPtr.Zero); keybd_event(0xE8, 0, 2, UIntPtr.Zero);
                                keybd_event((byte)k.vk, 0, 3, UIntPtr.Zero);
                                Application.Current?.Dispatcher.BeginInvoke(new Action(StartMenu.Toggle));
                                return (IntPtr)1;
                            }
                        }
                    } else if (dn && down) other = true;           // Win+R, Win+D ... keep working as normal
                }
            }
        } catch { }
        return CallNextHookEx(hh, code, w, l);
    }
}

class StartMenu : Window {
    static StartMenu inst;
    public static List<(string Name, string Id)> Apps = new();
    public static event Action AppsChanged;
    static bool loading;
    static readonly Dictionary<string, ImageSource> icons = new();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

    public static void Toggle() { if (inst != null) { inst.CloseMe(); return; } inst = new StartMenu(); inst.Open(); }

    // installed apps (desktop and Store apps) straight from Windows' own Start list
    public static async Task Load() {
        if (loading) return; loading = true;
        try {
            var list = await Task.Run(() => {
                var res = new List<(string, string)>();
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | ForEach-Object { $_.Name + '|' + $_.AppID }\"") {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 };
                using var p = Process.Start(psi);
                var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(15000);
                foreach (var line in o.Split('\n')) {
                    var s = line.Trim().TrimStart('\uFEFF'); int i = s.LastIndexOf('|');
                    if (i <= 0 || i == s.Length - 1) continue;
                    string n = s.Substring(0, i).Trim(), id = s.Substring(i + 1).Trim();
                    if (n.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    res.Add((n, id));
                }
                return res.GroupBy(x => x.Item1 + "|" + x.Item2).Select(g => g.First()).OrderBy(x => x.Item1, StringComparer.CurrentCultureIgnoreCase).ToList();
            });
            if (list.Count > 0) { Apps = list; AppsChanged?.Invoke(); }
        } catch { } finally { loading = false; }
    }

    static ImageSource AppIcon(string id) {
        if (icons.TryGetValue(id, out var c)) return c;
        ImageSource r = null;
        try {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName("shell:AppsFolder\\" + id, IntPtr.Zero, ref iid, out var f);
            if (f.GetImage(new SZ { cx = 48, cy = 48 }, 0, out var hbm) == 0 && hbm != IntPtr.Zero) {
                r = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); r.Freeze(); DeleteObject(hbm);
            }
        } catch { }
        icons[id] = r; return r;
    }

    readonly TextBox box = new(); readonly TextBlock hint; readonly StackPanel list = new(); readonly ScrollViewer sv = new(); readonly Grid foot = new();
    bool ready, powerOpen; Action first;

    StartMenu() {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; Topmost = true;
        var wa = SystemParameters.WorkArea;
        double bottom = Math.Min(wa.Bottom, SystemParameters.PrimaryScreenHeight - (Store.C.IconSize + 44));
        double w = 620, h = Math.Min(680, bottom - wa.Top - 12);
        Width = w + 28; Height = h + 28; Left = wa.Left + (wa.Width - Width) / 2; Top = bottom - Height - 2;

        var card = new Border { Background = Th.Bg, BorderBrush = Th.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Store.C.Radius), Margin = new Thickness(14),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black } };
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var sb = new Border { Background = Th.Tile, CornerRadius = new CornerRadius(10), Height = 40, Margin = new Thickness(18, 18, 18, 8) };
        var sg = new Grid();
        var mag = UI.Glyph("\uE721", Th.Dim, 14); mag.HorizontalAlignment = HorizontalAlignment.Left; mag.Margin = new Thickness(14, 0, 0, 0);
        box.Background = Brushes.Transparent; box.BorderThickness = new Thickness(0); box.Foreground = Th.Fg; box.CaretBrush = Th.Fg; box.SelectionBrush = Th.Accent;
        box.FontSize = 14; box.VerticalContentAlignment = VerticalAlignment.Center; box.Margin = new Thickness(40, 0, 12, 0);
        hint = UI.Txt("Search apps", Th.Dim, 14); hint.Margin = new Thickness(42, 0, 0, 0); hint.IsHitTestVisible = false;
        sg.Children.Add(mag); sg.Children.Add(hint); sg.Children.Add(box); sb.Child = sg;
        box.TextChanged += (_, _) => { hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Render(); };

        sv.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden; sv.Content = list; sv.Margin = new Thickness(10, 0, 10, 0);
        Grid.SetRow(sv, 1); Grid.SetRow(foot, 2);
        foot.Margin = new Thickness(0); foot.Background = Th.Tile;
        var fb = new Border { Child = foot, BorderBrush = Th.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(18, 10, 18, 10), CornerRadius = new CornerRadius(0, 0, Store.C.Radius, Store.C.Radius), Background = Th.Tile };
        Grid.SetRow(fb, 2);
        root.Children.Add(sb); root.Children.Add(sv); root.Children.Add(fb);
        card.Child = root; Content = card;

        PreviewKeyDown += (_, e) => {
            if (e.Key == Key.Escape) { CloseMe(); e.Handled = true; }
            else if (e.Key == Key.Enter && first != null) { first(); e.Handled = true; }
        };
        Activated += (_, _) => ready = true;
        Deactivated += (_, _) => { if (ready) CloseMe(); };
        Action again = () => Render(), gone = () => CloseMe();
        AppsChanged += again; Store.Changed += gone;
        Closed += (_, _) => { AppsChanged -= again; Store.Changed -= gone; if (inst == this) inst = null; };
        Foot(); Render();
    }

    bool closing;
    // closing twice (e.g. launching an app also deactivates the menu) throws, so only ever close once
    void CloseMe() { if (closing) return; closing = true; try { Close(); } catch { } }

    void Open() {
        Opacity = 0; Show(); Activate(); box.Focus();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(130)));
        var top = Top; BeginAnimation(TopProperty, new DoubleAnimation(top + 10, top, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _ = Load();
    }

    static void Run(string file, string args) { try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true }); } catch { } }
    void LaunchApp(string id) { CloseMe(); try { Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + id) { UseShellExecute = true }); } catch { } }
    void LaunchPath(string t) { CloseMe(); try { Process.Start(new ProcessStartInfo(t) { UseShellExecute = true }); } catch { } }

    void Foot() {
        foot.Children.Clear(); foot.ColumnDefinitions.Clear();
        foot.ColumnDefinitions.Add(new ColumnDefinition()); foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        UIElement left;
        if (!powerOpen) {
            var av = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Th.Accent, Child = UI.Txt(Environment.UserName.Substring(0, 1).ToUpper(), Brushes.White, 12, FontWeights.SemiBold) };
            ((FrameworkElement)av.Child).HorizontalAlignment = HorizontalAlignment.Center;
            var nm = UI.Txt(Environment.UserName, Th.Fg, 13); nm.Margin = new Thickness(10, 0, 0, 0);
            left = new StackPanel { Orientation = Orientation.Horizontal, Children = { av, nm } };
        } else {
            Border B(string t, Action a) { var b = UI.Click(UI.Txt(t, Th.Fg, 12), () => { CloseMe(); a(); }, Th.Tile, 10, 5); b.Margin = new Thickness(0, 0, 6, 0); return b; }
            left = new StackPanel { Orientation = Orientation.Horizontal, Children = {
                B("Lock", () => Run("rundll32.exe", "user32.dll,LockWorkStation")), B("Sign out", () => Run("shutdown.exe", "/l")),
                B("Sleep", () => Run("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0")), B("Restart", () => Run("shutdown.exe", "/r /t 0")), B("Shut down", () => Run("shutdown.exe", "/s /t 0")) } };
        }
        var pw = UI.Round("\uE7E8", () => { powerOpen = !powerOpen; Foot(); }, 34, false); Grid.SetColumn(pw, 1);
        foot.Children.Add(left); foot.Children.Add(pw);
    }

    TextBlock Head(string t) { var x = UI.Txt(t, Th.Dim, 11.5, FontWeights.SemiBold); x.Margin = new Thickness(10, 12, 0, 6); return x; }

    UIElement AppRow(string name, string id) {
        var img = new Image { Width = 24, Height = 24 };
        var tx = UI.Txt(name, Th.Fg, 13); tx.Margin = new Thickness(14, 0, 0, 0); tx.TextTrimming = TextTrimming.CharacterEllipsis;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => img.Source = AppIcon(id)));
        return UI.Click(new StackPanel { Orientation = Orientation.Horizontal, Children = { img, tx } }, () => LaunchApp(id), null, 10, 6);
    }

    UIElement PinTile(Pin p) {
        var ic = N.Icon(p.Target);
        FrameworkElement face = ic != null ? new Image { Source = ic, Width = 36, Height = 36 }
            : new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Background = Th.Tile, Child = UI.Txt(p.Name.Substring(0, 1).ToUpper(), Th.Fg, 14, FontWeights.SemiBold) };
        if (face is Border fb) ((FrameworkElement)fb.Child).HorizontalAlignment = HorizontalAlignment.Center;
        var nm = UI.Txt(p.Name, Th.Fg, 11); nm.TextTrimming = TextTrimming.CharacterEllipsis; nm.MaxWidth = 84; nm.HorizontalAlignment = HorizontalAlignment.Center; nm.Margin = new Thickness(0, 6, 0, 0);
        return UI.Click(new StackPanel { Children = { face, nm } }, () => LaunchPath(p.Target), null, 4, 10);
    }

    void Render() {
        list.Children.Clear(); first = null; string q = box.Text.Trim();
        if (q.Length == 0) {
            if (Store.C.Pinned.Count > 0) {
                list.Children.Add(Head("Pinned"));
                var g = new System.Windows.Controls.Primitives.UniformGrid { Columns = 6 };
                foreach (var p in Store.C.Pinned.ToList()) g.Children.Add(PinTile(p));
                list.Children.Add(g);
            }
            list.Children.Add(Head("All apps"));
            if (Apps.Count == 0) { var l = UI.Txt("Loading apps…", Th.Dim, 12); l.Margin = new Thickness(10, 4, 0, 0); list.Children.Add(l); }
            string last = "";
            foreach (var a in Apps) {
                char ch = char.ToUpper(a.Name[0]); string L = char.IsLetter(ch) ? ch.ToString() : "#";
                if (L != last) { last = L; var lh = UI.Txt(L, Th.Accent, 12, FontWeights.SemiBold); lh.Margin = new Thickness(14, 10, 0, 4); list.Children.Add(lh); }
                list.Children.Add(AppRow(a.Name, a.Id));
            }
        } else {
            var res = Apps.Where(a => a.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(a => a.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).Take(40).ToList();
            if (res.Count == 0) { var l = UI.Txt("No apps match “" + q + "”", Th.Dim, 12); l.Margin = new Thickness(10, 14, 0, 0); list.Children.Add(l); }
            else { var f = res[0]; first = () => LaunchApp(f.Id); }
            if (res.Count > 0) list.Children.Add(Head("Best match"));
            foreach (var a in res) list.Children.Add(AppRow(a.Name, a.Id));
        }
        sv.ScrollToTop();
    }
}

class ControlPanel : Window {
    static ControlPanel inst;
    readonly StackPanel nav = new(), body = new();
    public static void Open() {
        if (inst == null) { inst = new ControlPanel(); inst.Closed += (_, _) => inst = null; }
        inst.Show(); inst.Activate();
    }
    ControlPanel() {
        Title = "Hoster"; Width = 860; Height = 560; MinWidth = 700; MinHeight = 440; WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Th.Bg;
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        nav.Margin = new Thickness(10);
        var sv = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(24) }; Grid.SetColumn(sv, 1);
        g.Children.Add(nav); g.Children.Add(sv); Content = g;
        Action h = () => Background = Th.Bg; Store.Changed += h; Closed += (_, _) => Store.Changed -= h;
        Go("Appearance");
    }
    UIElement Head(string s) { var t = UI.Txt(s, Th.Dim, 12); t.Margin = new Thickness(0, 20, 0, 8); return t; }
    UIElement Row(string label, UIElement ctl, string sub = null) {
        var left = new StackPanel(); left.Children.Add(UI.Txt(label, Th.Fg));
        if (sub != null) left.Children.Add(UI.Txt(sub, Th.Dim, 11));
        var dp = new DockPanel(); DockPanel.SetDock(ctl, System.Windows.Controls.Dock.Right); dp.Children.Add(ctl); dp.Children.Add(left);
        return new Border { BorderBrush = Th.Line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 10, 0, 10), Child = dp };
    }
    UIElement Tog(string label, bool v, Action<bool> set, string sub = null) {
        var cb = new CheckBox { IsChecked = v, VerticalAlignment = VerticalAlignment.Center };
        cb.Checked += (_, _) => { set(true); Store.Save(); }; cb.Unchecked += (_, _) => { set(false); Store.Save(); };
        return Row(label, cb, sub);
    }
    UIElement Sld(string label, int min, int max, int v, Action<int> set) {
        var s = new Slider { Minimum = min, Maximum = max, Value = v, Width = 180 };
        s.ValueChanged += (_, e) => { set((int)e.NewValue); Store.Save(); };
        return Row(label, s);
    }
    Border Btn(string text, Action a, bool accent = false) => UI.Click(UI.Txt(text, accent ? Brushes.White : Th.Fg), a, accent ? Th.Accent : Th.Tile, 14);
    void Go(string page) {
        var c = Store.C; nav.Children.Clear(); body.Children.Clear();
        foreach (var k in new[] { "Appearance", "Dock", "Top bar", "Widgets", "Desktop" }) {
            var sel = k == page; var kk = k;
            nav.Children.Add(UI.Click(UI.Txt(k, sel ? Brushes.White : Th.Dim), () => Go(kk), sel ? Th.Accent : null, 12));
        }
        body.Children.Add(UI.Txt(page, Th.Fg, 20, FontWeights.SemiBold));
        switch (page) {
            case "Appearance":
                body.Children.Add(Head("Theme"));
                var wp = new WrapPanel();
                foreach (var t in new[] { "dark", "black", "navy", "aubergine", "light" }) {
                    var tt = t; var b = UI.Click(UI.Txt(t, Th.Fg), () => { c.Theme = tt; Store.Save(); Go(page); }, t == c.Theme ? Th.Accent : Th.Tile, 16);
                    b.Margin = new Thickness(0, 0, 8, 8); wp.Children.Add(b);
                }
                body.Children.Add(wp);
                body.Children.Add(Tog("Match the Windows Start menu to the theme", c.SyncWindows, v => c.SyncWindows = v, "Light/dark follows the theme; navy and aubergine also tint it. Turning this off puts your old Windows colours back"));
                body.Children.Add(Head("Accent colour"));
                var sw = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var a in new[] { "#E5303A", "#FF7A1A", "#F5C518", "#3DDC84", "#4F8CFF", "#B66BFF" }) {
                    var aa = a; var e = new Shp.Ellipse { Width = 24, Height = 24, Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand,
                        Fill = (Brush)new BrushConverter().ConvertFrom(a), Stroke = a == c.Accent ? Th.Fg : Brushes.Transparent, StrokeThickness = 2 };
                    e.MouseLeftButtonUp += (_, _) => { c.Accent = aa; Store.Save(); Go(page); }; sw.Children.Add(e);
                }
                body.Children.Add(sw); break;
            case "Dock":
                body.Children.Add(Sld("Icon size", 32, 72, c.IconSize, v => c.IconSize = v));
                body.Children.Add(Sld("Corner roundness", 4, 30, c.Radius, v => c.Radius = v));
                body.Children.Add(Tog("Magnify on hover", c.Magnify, v => { c.Magnify = v; Dispatcher.BeginInvoke(new Action(() => Go(page))); }));   // redraw so the amount slider shows / hides
                if (c.Magnify) body.Children.Add(Sld("Magnify amount (%)", 10, 100, c.MagnifyAmount, v => c.MagnifyAmount = v));
                body.Children.Add(Tog("Window and tab list on hover", c.HoverList, v => c.HoverList = v, "Hover a running app to see its windows; browsers also list their tabs"));
                body.Children.Add(Head("Pinned apps (right-click an icon in the dock to unpin)"));
                foreach (var p in c.Pinned.ToList()) { var pp = p; body.Children.Add(Row(p.Name, Btn("Remove", () => { c.Pinned.Remove(pp); Store.Save(); Go(page); }), p.Target)); }
                var add = Btn("Pin an app…", () => { Store.PinApp(); Go(page); }); add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 12, 0, 0);
                body.Children.Add(add); break;
            case "Top bar":
                body.Children.Add(Tog("Now playing (Spotify / media)", c.Media, v => c.Media = v, "Click it in the bar for the player; the clock opens the calendar"));
                body.Children.Add(Tog("Audio visualiser in the player", c.Visualizer, v => c.Visualizer = v, "Reacts to whatever your PC is playing"));
                body.Children.Add(Tog("Wi-Fi and Bluetooth menu", c.Network, v => c.Network = v, "Wi-Fi / Bluetooth icons at the right of the bar"));
                body.Children.Add(Tog("CPU and memory", c.Stats, v => c.Stats = v));
                body.Children.Add(Tog("Date", c.Date, v => c.Date = v));
                body.Children.Add(Tog("24-hour clock", c.Clock24, v => c.Clock24 = v));
                body.Children.Add(Tog("Show seconds", c.Seconds, v => c.Seconds = v));
                body.Children.Add(Tog("Hide dock and bar in full screen", c.HideInFullscreen, v => c.HideInFullscreen = v, "Games, fullscreen video, F11 browser, slideshows. Desktop widgets also pause behind maximised windows")); break;
            case "Widgets":
                var editBtn = Btn("Edit layout (drag widgets around)", () => WidgetHost.Edit(true), true); editBtn.HorizontalAlignment = HorizontalAlignment.Left; editBtn.Margin = new Thickness(0, 12, 0, 0);
                body.Children.Add(editBtn);
                foreach (var wc in c.Widgets.ToList()) {
                    var wcc = wc;
                    body.Children.Add(Head(WidgetHost.Label(wc.Kind)));
                    body.Children.Add(Tog("Show on desktop", wcc.On, v => wcc.On = v));
                    body.Children.Add(Sld("Size (%)", 60, 200, wcc.Size, v => wcc.Size = v));
                }
                var resetBtn = Btn("Reset positions and sizes", WidgetHost.Reset); resetBtn.HorizontalAlignment = HorizontalAlignment.Left; resetBtn.Margin = new Thickness(0, 20, 0, 0);
                body.Children.Add(resetBtn); break;
            case "Desktop":
                body.Children.Add(Tog("Hide the Windows taskbar", c.HideTaskbar, v => { c.HideTaskbar = v; N.Taskbar(!v); }, "Replaced by this dock and top bar"));
                body.Children.Add(Tog("Hoster Start menu", c.OwnStart, v => c.OwnStart = v, "Opens from the Win key or the first dock button, in your theme"));
                body.Children.Add(Tog("Start with Windows", Program.Autostart, v => Program.Autostart = v, "Launches Hoster when you sign in"));
                body.Children.Add(Row("Wallpaper", Btn("Choose image…", () => { var d = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" }; if (d.ShowDialog() == true) N.Wallpaper(d.FileName); })));
                body.Children.Add(Tog("Live wallpaper", c.LiveWallpaperOn, v => c.LiveWallpaperOn = v, "A looping, muted video behind your icons. Pauses while a window covers the desktop"));
                body.Children.Add(Row("Live wallpaper video", Btn("Choose video…", () => { if (Live.Pick()) Go(page); }), string.IsNullOrEmpty(c.LiveWallpaper) ? "No video chosen (MP4 works best)" : c.LiveWallpaper));
                body.Children.Add(Row("Taskbar stuck hidden?", Btn("Show Windows taskbar", Program.ShowTaskbar)));
                body.Children.Add(Row("Stop Hoster", Btn("Quit and restore taskbar", Program.Quit, true))); break;
        }
    }
}

// ---------------- shared sampling (so the bar and widgets agree) ----------------
static class Stats {
    public static int Cpu, Ram; public static double RamUsed, RamTotal;
    public static readonly List<int> CpuH = new(), RamH = new();
    public static void Start() {
        N.Cpu(); Ram = N.Ram(); var m = N.RamGb(); RamUsed = m.used; RamTotal = m.total;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; t.Tick += (_, _) => Sample(); t.Start();
    }
    static void Sample() {
        if (Full.On) return;   // nobody can see the numbers
        Cpu = N.Cpu(); Ram = N.Ram(); var m = N.RamGb(); RamUsed = m.used; RamTotal = m.total;
        Push(CpuH, Cpu); Push(RamH, Ram);
    }
    static void Push(List<int> l, int v) { l.Add(v); if (l.Count > 64) l.RemoveAt(0); }
}

// ---------------- dot helpers ----------------
static class Dots {
    public static Shp.Ellipse Add(Canvas c, double cx, double cy, double d, Brush f) {
        var e = new Shp.Ellipse { Width = d, Height = d, Fill = f };
        Canvas.SetLeft(e, cx - d / 2); Canvas.SetTop(e, cy - d / 2); c.Children.Add(e); return e;
    }
}

// Dot-matrix history graph (columns = time, lit dots = value)
class DotGraph {
    const int Cols = 32, Rows = 8; const double Cell = 7.6;
    public readonly Canvas Root; readonly Shp.Ellipse[,] d = new Shp.Ellipse[Cols, Rows];
    public DotGraph() {
        Root = new Canvas { Width = Cols * Cell, Height = Rows * Cell };
        for (int c = 0; c < Cols; c++) for (int r = 0; r < Rows; r++) d[c, r] = Dots.Add(Root, c * Cell + Cell / 2, r * Cell + Cell / 2, 4.6, Th.Line);
    }
    public void Update(List<int> h) {
        for (int c = 0; c < Cols; c++) {
            int idx = h.Count - Cols + c, v = idx >= 0 ? h[idx] : -1;
            int lit = v < 0 ? 0 : Math.Max(v > 0 ? 1 : 0, (int)Math.Round(v / 100.0 * Rows));
            for (int r = 0; r < Rows; r++) {
                bool on = (Rows - 1 - r) < lit; var e = d[c, r];
                e.Fill = on ? Th.Accent : Th.Line; e.Opacity = on ? 0.35 + 0.65 * c / (Cols - 1.0) : 1;
            }
        }
    }
}

static class CalGrid {
    static UIElement Cell(string s, Brush fg, Brush bg, double sz, bool bold) {
        var tx = UI.Txt(s, fg, sz, bold ? FontWeights.SemiBold : FontWeights.Normal); tx.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border { Height = 31, Child = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = bg, Child = tx, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
    }
    public static UIElement Build(DateTime view) {
        var g = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7 };
        foreach (var d in new[] { "M", "T", "W", "T", "F", "S", "S" }) g.Children.Add(Cell(d, Th.Dim, null, 11, false));
        int off = ((int)view.DayOfWeek + 6) % 7, days = DateTime.DaysInMonth(view.Year, view.Month);
        for (int i = 0; i < off; i++) g.Children.Add(new Border { Height = 31 });
        for (int d = 1; d <= days; d++) {
            var date = new DateTime(view.Year, view.Month, d); bool today = date == DateTime.Today, we = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
            g.Children.Add(Cell(d.ToString(), today ? Brushes.White : we ? Th.Dim : Th.Fg, today ? Th.Accent : null, 12.5, today));
        }
        return g;
    }
}

// ---------------- desktop widgets ----------------
// A widget is a glass card that lives on the desktop (below your apps). In edit mode it can be dragged, resized and hidden.
abstract class Widget : Shell {
    protected readonly WCfg cfg;
    bool dragging; int sx, sy; double startL, startT;
    protected Widget(WCfg c) {
        cfg = c; SizeToContent = SizeToContent.WidthAndHeight; Width = double.NaN; Height = double.NaN; Topmost = WidgetHost.Editing;
        Loaded += (_, _) => { Place(); ToBottom(); };
    }
    public string Kind => cfg.Kind;
    protected abstract UIElement Body();
    public virtual void Tick() { }
    public void Init() { Build(); Show(); }
    public void Rebuild() { Build(); if (cfg.X < 0) { UpdateLayout(); Place(); } }
    public void ToBottom() { if (!Topmost && IsLoaded) N.ToDesktop(Handle); }

    protected override void Place() {
        if (dragging) return;
        var s = WF.Screen.PrimaryScreen.Bounds; double sc = VisualTreeHelper.GetDpi(this).DpiScaleX; double sw = s.Width / sc, sh = s.Height / sc;
        double w = ActualWidth > 0 ? ActualWidth : 300, x = cfg.X, y = cfg.Y;
        if (x < 0 || y < 0) { var a = WidgetHost.Auto(cfg.Kind, sw, w); x = a.Item1; y = a.Item2; }
        Left = Math.Max(0, Math.Min(x, sw - 60)); Top = Math.Max(0, Math.Min(y, sh - 60));
    }

    void Build() {
        var inner = new Border {
            Background = Th.Glass, BorderBrush = Th.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(16),
            Child = Body(), Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.35, Color = Colors.Black } };
        var g = new Grid { Margin = new Thickness(16) }; g.Children.Add(inner);
        if (WidgetHost.Editing) g.Children.Add(Overlay());
        double s = Math.Max(0.4, cfg.Size / 100.0);
        var holder = new Border { Child = g, LayoutTransform = new ScaleTransform(s, s) };
        var cm = new ContextMenu();
        void Item(string t, Action a) { var mi = new MenuItem { Header = t }; mi.Click += (_, _) => a(); cm.Items.Add(mi); }
        Item("Edit layout", () => WidgetHost.Edit(true));
        Item("Bigger", () => { cfg.Size = Math.Min(220, cfg.Size + 10); Store.Save(); });
        Item("Smaller", () => { cfg.Size = Math.Max(50, cfg.Size - 10); Store.Save(); });
        Item("Hide this widget", () => { cfg.On = false; Store.Save(); });
        holder.ContextMenu = cm;
        Content = holder;
    }

    UIElement Overlay() {
        var col = (Th.Accent as SolidColorBrush)?.Color ?? Colors.Red;
        var ov = new Grid();
        var bg = new Border { CornerRadius = new CornerRadius(22), Background = new SolidColorBrush(Color.FromArgb(46, col.R, col.G, col.B)), BorderBrush = Th.Accent, BorderThickness = new Thickness(2), Cursor = Cursors.SizeAll };
        bg.MouseLeftButtonDown += (_, e) => { bg.CaptureMouse(); var p = WF.Cursor.Position; sx = p.X; sy = p.Y; startL = Left; startT = Top; dragging = true; e.Handled = true; };
        bg.MouseMove += (_, _) => {
            if (!dragging) return; var p = WF.Cursor.Position; double sc = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Left = startL + (p.X - sx) / sc; Top = startT + (p.Y - sy) / sc;
        };
        bg.MouseLeftButtonUp += (_, _) => {
            if (!dragging) return; dragging = false; bg.ReleaseMouseCapture();
            cfg.X = Left; cfg.Y = Top; Store.Save(false);
        };
        ov.Children.Add(bg);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0) };
        Border B(string glyph, Action a) { var b = UI.Click(UI.Glyph(glyph, Th.Fg, 11), a, Th.Bg, 8, 6); b.Margin = new Thickness(3, 0, 0, 0); return b; }
        bar.Children.Add(B("\uE738", () => { cfg.Size = Math.Max(50, cfg.Size - 10); Store.Save(); }));
        bar.Children.Add(B("\uE710", () => { cfg.Size = Math.Min(220, cfg.Size + 10); Store.Save(); }));
        bar.Children.Add(B("\uE711", () => { cfg.On = false; Store.Save(); }));
        ov.Children.Add(bar);
        return ov;
    }
}

class ClockWidget : Widget {
    Canvas hands; readonly Shp.Ellipse[] ring = new Shp.Ellipse[60]; readonly Brush[] baseB = new Brush[60]; TextBlock tm, dt;
    static readonly ScaleTransform Big = MkBig();
    static ScaleTransform MkBig() { var t = new ScaleTransform(1.9, 1.9); t.Freeze(); return t; }
    public ClockWidget(WCfg c) : base(c) { }
    protected override UIElement Body() {
        var rc = new Canvas { Width = 200, Height = 200 }; hands = new Canvas { Width = 200, Height = 200, IsHitTestVisible = false };
        for (int i = 0; i < 60; i++) {
            double a = i * Math.PI / 30; bool hr = i % 5 == 0; baseB[i] = hr ? Th.Fg : Th.Dim;
            var e = Dots.Add(rc, 100 + 92 * Math.Sin(a), 100 - 92 * Math.Cos(a), hr ? 6 : 3, baseB[i]); e.RenderTransformOrigin = new Point(0.5, 0.5); ring[i] = e;
        }
        var dial = new Grid { Width = 200, Height = 200, HorizontalAlignment = HorizontalAlignment.Center }; dial.Children.Add(rc); dial.Children.Add(hands);
        tm = UI.Txt("", Th.Fg, 34, FontWeights.Light); tm.FontFamily = new FontFamily("Segoe UI Light, Segoe UI"); tm.HorizontalAlignment = HorizontalAlignment.Center; tm.Margin = new Thickness(0, 8, 0, 0);
        dt = UI.Txt("", Th.Dim, 12.5); dt.HorizontalAlignment = HorizontalAlignment.Center;
        var p = new StackPanel { Width = 220, Children = { dial, tm, dt } };
        Tick(); return p;
    }
    public override void Tick() {
        if (hands == null) return;
        var n = DateTime.Now; int sec = n.Second;
        for (int i = 0; i < 60; i++) {
            int d = (sec - i + 60) % 60; var e = ring[i];
            if (d < 5) { e.Fill = Th.Accent; e.Opacity = 1 - d * 0.2; e.RenderTransform = d == 0 ? Big : Transform.Identity; }
            else { e.Fill = baseB[i]; e.Opacity = 1; e.RenderTransform = Transform.Identity; }
        }
        hands.Children.Clear();
        double ma = (n.Minute + n.Second / 60.0) * 6 * Math.PI / 180, ha = ((n.Hour % 12) + n.Minute / 60.0) * 30 * Math.PI / 180;
        for (double r = 16; r <= 80; r += 13) Dots.Add(hands, 100 + r * Math.Sin(ma), 100 - r * Math.Cos(ma), 5.5, Th.Fg);
        for (double r = 16; r <= 52; r += 12) Dots.Add(hands, 100 + r * Math.Sin(ha), 100 - r * Math.Cos(ha), 7.5, Th.Fg);
        Dots.Add(hands, 100, 100, 9, Th.Accent);
        tm.Text = n.ToString(Store.C.Clock24 ? "HH:mm" : "h:mm tt"); dt.Text = n.ToString("dddd, d MMMM");
    }
}

class CalendarWidget : Widget {
    DateTime view = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), day = DateTime.Today;
    Border host; TextBlock title, dow, big;
    public CalendarWidget(WCfg c) : base(c) { }
    protected override UIElement Body() {
        dow = UI.Txt("", Th.Dim, 11); big = UI.Txt("", Th.Fg, 26, FontWeights.Light); big.FontFamily = new FontFamily("Segoe UI Light, Segoe UI");
        title = UI.Txt("", Th.Fg, 13, FontWeights.SemiBold); title.HorizontalAlignment = HorizontalAlignment.Center;
        var titleBtn = UI.Click(title, () => { view = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); Render(); }, null, 10, 3); titleBtn.HorizontalAlignment = HorizontalAlignment.Center;
        var prev = UI.Click(UI.Glyph("\uE76B", Th.Fg, 11), () => { view = view.AddMonths(-1); Render(); }, null, 9, 5);
        var next = UI.Click(UI.Glyph("\uE76C", Th.Fg, 11), () => { view = view.AddMonths(1); Render(); }, null, 9, 5);
        DockPanel.SetDock(prev, System.Windows.Controls.Dock.Left); DockPanel.SetDock(next, System.Windows.Controls.Dock.Right);
        var hdr = new DockPanel { Margin = new Thickness(0, 12, 0, 4) }; hdr.Children.Add(prev); hdr.Children.Add(next); hdr.Children.Add(titleBtn);
        host = new Border();
        var p = new StackPanel { Width = 252, Children = { dow, big, hdr, host } };
        Render(); return p;
    }
    void Render() {
        var n = DateTime.Now; dow.Text = n.ToString("dddd").ToUpper(); big.Text = n.ToString("d MMMM");
        title.Text = view.ToString("MMMM yyyy"); host.Child = CalGrid.Build(view);
    }
    public override void Tick() {
        if (host == null || DateTime.Today == day) return;
        day = DateTime.Today; view = new DateTime(day.Year, day.Month, 1); Render();
    }
}

class SystemWidget : Widget {
    DotGraph cg, rg; TextBlock cpuT, ramT, upT;
    public SystemWidget(WCfg c) : base(c) { }
    protected override UIElement Body() {
        cg = new DotGraph(); rg = new DotGraph();
        cpuT = UI.Txt("", Th.Fg, 12, FontWeights.SemiBold); ramT = UI.Txt("", Th.Fg, 12, FontWeights.SemiBold); upT = UI.Txt("", Th.Dim, 11);
        UIElement Head(string label, TextBlock val) {
            var l = UI.Txt(label, Th.Dim, 11); DockPanel.SetDock(val, System.Windows.Controls.Dock.Right);
            return new DockPanel { Margin = new Thickness(0, 0, 0, 6), Children = { val, l } };
        }
        var p = new StackPanel { Width = 244 };
        p.Children.Add(Head("CPU", cpuT)); p.Children.Add(cg.Root);
        var h2 = Head("MEMORY", ramT); ((FrameworkElement)h2).Margin = new Thickness(0, 16, 0, 6); p.Children.Add(h2); p.Children.Add(rg.Root);
        upT.Margin = new Thickness(0, 14, 0, 0); p.Children.Add(upT);
        Tick(); return p;
    }
    public override void Tick() {
        if (cg == null) return;
        cpuT.Text = Stats.Cpu + "%"; ramT.Text = $"{Stats.RamUsed:0.0} / {Stats.RamTotal:0.0} GB";
        cg.Update(Stats.CpuH); rg.Update(Stats.RamH);
        var u = TimeSpan.FromMilliseconds(Environment.TickCount64);
        upT.Text = "Up " + (u.Days > 0 ? u.Days + "d " : "") + u.Hours + "h " + u.Minutes + "m";
    }
}

class MediaWidget : Widget {
    readonly Shp.Ellipse[] ring = new Shp.Ellipse[60]; TextBlock cur; readonly Action h;
    public MediaWidget(WCfg c) : base(c) { h = () => Rebuild(); Player.Changed += h; Closed += (_, _) => Player.Changed -= h; }
    protected override UIElement Body() {
        cur = null;
        var rc = new Canvas { Width = 210, Height = 210 };
        for (int i = 0; i < 60; i++) { double a = i * Math.PI / 30; ring[i] = Dots.Add(rc, 105 + 100 * Math.Sin(a), 105 - 100 * Math.Cos(a), 3.4, Th.Line); }
        var dial = new Grid { Width = 210, Height = 210, HorizontalAlignment = HorizontalAlignment.Center }; dial.Children.Add(rc);
        var art = new Border { Width = 168, Height = 168, CornerRadius = new CornerRadius(84), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (Player.Art != null) art.Background = new ImageBrush(Player.Art) { Stretch = Stretch.UniformToFill };
        else { art.Background = Th.Tile; art.Child = UI.Txt("♪", Th.Dim, 46); ((FrameworkElement)art.Child).HorizontalAlignment = HorizontalAlignment.Center; }
        dial.Children.Add(art);
        var p = new StackPanel { Width = 240 }; p.Children.Add(dial);
        if (!Player.Has) {
            var t = UI.Txt("Nothing playing", Th.Fg, 14, FontWeights.SemiBold); t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 12, 0, 8);
            var b = UI.Click(UI.Txt("Open Spotify", Brushes.White, 12), Player.OpenSpotify, Th.Accent, 14, 6); b.HorizontalAlignment = HorizontalAlignment.Center;
            p.Children.Add(t); p.Children.Add(b); return p;
        }
        var name = UI.Txt(Player.Title, Th.Fg, 15, FontWeights.SemiBold); name.TextTrimming = TextTrimming.CharacterEllipsis; name.HorizontalAlignment = HorizontalAlignment.Center; name.Margin = new Thickness(0, 12, 0, 0);
        var who = UI.Txt(Player.Artist, Th.Dim, 12); who.TextTrimming = TextTrimming.CharacterEllipsis; who.HorizontalAlignment = HorizontalAlignment.Center;
        cur = UI.Txt("", Th.Dim, 10.5); cur.HorizontalAlignment = HorizontalAlignment.Center; cur.Margin = new Thickness(0, 6, 0, 0);
        var ctl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0), Children = {
            UI.Round("\uE892", Player.Prev, 36, false), UI.Round(Player.Playing ? "\uE769" : "\uE768", Player.PlayPause, 46, true), UI.Round("\uE893", Player.Next, 36, false) } };
        if (Store.C.Visualizer) p.Children.Add(new VizView { Height = 34, Margin = new Thickness(0, 10, 0, 0) });
        p.Children.Add(name); p.Children.Add(who); p.Children.Add(cur); p.Children.Add(ctl);
        Tick(); return p;
    }
    public override void Tick() {
        if (!Player.Has || cur == null) return;
        var e = Player.End; var p = Player.Position;
        double f = e.TotalSeconds > 0 ? Math.Min(1, p.TotalSeconds / e.TotalSeconds) : 0; int on = (int)Math.Round(f * 60);
        for (int i = 0; i < 60; i++) ring[i].Fill = i < on ? Th.Accent : Th.Line;
        cur.Text = UI.Time(p) + (e.TotalSeconds > 0 ? "  /  " + UI.Time(e) : "");
    }
}

static class WidgetHost {
    public static bool Editing;
    static readonly Dictionary<string, Widget> open = new(); static EditBanner banner;
    public static readonly string[] Kinds = { "Clock", "Media", "System", "Calendar" };
    public static string Label(string k) => k == "Media" ? "Music player" : k == "System" ? "System monitor" : k;
    static Widget Make(WCfg c) {
        switch (c.Kind) {
            case "Clock": return new ClockWidget(c);
            case "Media": return new MediaWidget(c);
            case "System": return new SystemWidget(c);
            case "Calendar": return new CalendarWidget(c);
            default: return null;
        }
    }
    // Default spots until you drag a widget somewhere: clock + system on the left, player + calendar on the right.
    public static (double, double) Auto(string kind, double sw, double w) {
        switch (kind) {
            case "Clock": return (28, 28);
            case "System": return (28, 340);
            case "Media": return (sw - w - 28, 28);
            default: return (sw - w - 28, 420);
        }
    }
    public static void Start() {
        Sync(); Store.Changed += OnChanged;
        // hidden windows aren't drawn, so the widgets cost nothing behind a full-screen app
        Action hide = () => {
            bool idle = Full.On || Full.Covered;
            foreach (var w in open.Values.ToList()) {
                if (!idle) w.ToBottom();   // sink it under the apps first, so it never pops up on top of them
                w.Visibility = idle ? Visibility.Hidden : Visibility.Visible;
                if (!idle) w.ToBottom();
            }
        };
        Full.Changed += hide; Full.CoveredChanged += hide;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; int n = 0;
        t.Tick += (_, _) => {
            if (Full.On || Full.Covered) return;
            foreach (var w in open.Values.ToList()) w.Tick();
            if (!Editing && ++n % 3 == 0) foreach (var w in open.Values.ToList()) w.ToBottom();   // keep them under your apps
        };
        t.Start();
    }
    static void Sync() {
        foreach (var k in Kinds) if (!Store.C.Widgets.Any(x => x.Kind == k)) Store.C.Widgets.Add(new WCfg { Kind = k });
        foreach (var c in Store.C.Widgets.ToList()) {
            bool has = open.TryGetValue(c.Kind, out var w);
            if (c.On && !has) { var nw = Make(c); if (nw != null) { open[c.Kind] = nw; nw.Init(); } }
            else if (!c.On && has) { open.Remove(c.Kind); w.Close(); }
        }
    }
    static void OnChanged() { Sync(); foreach (var w in open.Values.ToList()) w.Rebuild(); }
    public static void Edit(bool on) {
        if (Editing == on) return; Editing = on;
        foreach (var w in open.Values.ToList()) { w.Topmost = on; w.Rebuild(); }
        if (on) { banner = new EditBanner(); banner.Show(); }
        else { banner?.Close(); banner = null; foreach (var w in open.Values.ToList()) w.ToBottom(); }
    }
    public static void Reset() { foreach (var c in Store.C.Widgets) { c.X = -1; c.Y = -1; c.Size = 100; } Store.Save(); }
}

class EditBanner : Shell {
    public EditBanner() {
        SizeToContent = SizeToContent.WidthAndHeight; Width = double.NaN; Height = double.NaN;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Shp.Ellipse { Width = 8, Height = 8, Fill = Th.Accent, Margin = new Thickness(2, 0, 10, 0) });
        row.Children.Add(UI.Txt("Editing widgets: drag to move, use the buttons on a widget to resize or hide", Th.Fg, 12.5));
        var reset = UI.Click(UI.Txt("Reset layout", Th.Fg, 12), WidgetHost.Reset, Th.Tile, 12, 5); reset.Margin = new Thickness(16, 0, 6, 0);
        var done = UI.Click(UI.Txt("Done", Brushes.White, 12, FontWeights.SemiBold), () => WidgetHost.Edit(false), Th.Accent, 14, 5);
        row.Children.Add(reset); row.Children.Add(done);
        Content = new Border {
            Margin = new Thickness(14), Padding = new Thickness(16, 8, 8, 8), CornerRadius = new CornerRadius(18), Background = Th.Bg, BorderBrush = Th.Line, BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 3, Opacity = 0.4, Color = Colors.Black }, Child = row };
        SizeChanged += (_, _) => Place();
    }
    protected override void Place() {
        var s = WF.Screen.PrimaryScreen.Bounds; double sc = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Left = s.Left / sc + (s.Width / sc - ActualWidth) / 2; Top = 6;
    }
}

// ---------------- entry point ----------------
static class Program {
    static WF.NotifyIcon tray; static bool cleaned;
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Autostart {
        get { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("Hoster") != null; }
        set { using var k = Registry.CurrentUser.OpenSubKey(RunKey, true); if (value) k.SetValue("Hoster", "\"" + Environment.ProcessPath + "\""); else k.DeleteValue("Hoster", false); }
    }
    public static void ShowTaskbar() { Store.C.HideTaskbar = false; Store.Save(false); N.Taskbar(true); }
    public static void Cleanup() {
        if (cleaned) return; cleaned = true;
        try { WinKey.Stop(); } catch { }
        try { N.RemoveBars(); N.Taskbar(true); } catch { }
        tray?.Dispose();
    }
    public static void Quit() { Cleanup(); Application.Current.Shutdown(); }

    [STAThread]
    static void Main(string[] args) {
        if (args.Contains("--restore")) { N.Taskbar(true); return; }
        using var mutex = new Mutex(true, "Hoster.SingleInstance", out var first);
        if (!first) return;
        try {   // an old "DotDesk" start-with-Windows entry becomes a "Hoster" one
            using var rk = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (rk?.GetValue("DotDesk") != null) { rk.DeleteValue("DotDesk", false); rk.SetValue("Hoster", "\"" + Environment.ProcessPath + "\""); }
        } catch { }
        Th.Apply();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => {
            try { var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hoster"); Directory.CreateDirectory(d); File.AppendAllText(Path.Combine(d, "crash.log"), DateTime.Now + "\r\n" + e.Exception + "\r\n\r\n"); } catch { }
            Cleanup();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
        SystemEvents.SessionEnding += (_, _) => Cleanup();

        if (Store.C.HideTaskbar) { N.Taskbar(false); Thread.Sleep(400); }   // let Windows release the taskbar's work area first
        Stats.Start();
        WinKey.Start(); _ = StartMenu.Load();
        new BarWindow().Show(); new DockWindow().Show();
        WidgetHost.Start();
        Live.Start();
        app.Dispatcher.BeginInvoke(new Action(Player.Init));
        var fsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) }; fsTimer.Tick += (_, _) => Full.Poll(); fsTimer.Start();
        Store.Changed += Full.Poll;
        var keepHidden = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };   // Explorer sometimes re-shows its taskbar
        keepHidden.Tick += (_, _) => { if (Store.C.HideTaskbar) N.HideTaskbarWindows(true); }; keepHidden.Start();

        tray = new WF.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath), Text = "Hoster", Visible = true, ContextMenuStrip = new WF.ContextMenuStrip() };
        tray.ContextMenuStrip.Items.Add("Control panel", null, (_, _) => ControlPanel.Open());
        tray.ContextMenuStrip.Items.Add("Edit widgets", null, (_, _) => WidgetHost.Edit(true));
        tray.ContextMenuStrip.Items.Add("Show Windows taskbar", null, (_, _) => ShowTaskbar());
        tray.ContextMenuStrip.Items.Add("Quit and restore taskbar", null, (_, _) => Quit());
        tray.DoubleClick += (_, _) => ControlPanel.Open();
        app.Run();
    }
}
