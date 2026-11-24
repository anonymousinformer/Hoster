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
    public bool BatteryBar { get; set; } = true;                         // battery % in the top bar (hidden on PCs without a battery)
    public string MxMode { get; set; } = "Text";                         // matrix widget: Text, Clock, Date, Battery, CPU, Audio
    public string MxText { get; set; } = "HOSTER";
    public int MxCols { get; set; } = 30;
    public int MxRows { get; set; } = 9;
    public int MxDot { get; set; } = 6;
    public int MxSpeed { get; set; } = 6;
    public string MxColor { get; set; } = "accent";                      // "accent" or a hex colour
    public bool MxScroll { get; set; } = true;
    public bool MxGhost { get; set; } = true;
    public bool MxSquare { get; set; }
    public bool Visualizer { get; set; } = true;
    public bool HoverList { get; set; } = true;                          // hover a dock icon to list its windows / browser tabs
    public bool SearchOn { get; set; } = true;                          // Spotlight search
    public string SearchKey { get; set; } = "Alt+Space";                // its shortcut
    public bool OwnStart { get; set; } = true;                           // Hoster's own Start menu (Win key + dock button)
    public bool SyncWindows { get; set; } = true;                       // make the Windows Start menu follow the Hoster theme
    public Dictionary<string, string> WinOrig { get; set; }            // Windows colour settings as they were before Hoster changed them
    public bool HideInFullscreen { get; set; } = true;
    public bool BottomViz { get; set; } = true;                          // audio "mountains" along the bottom of the desktop while music plays
    public bool LiveWallpaperOn { get; set; }                            // looping video behind the desktop icons
    public string LiveWallpaper { get; set; } = "";                      // path of that video
    public bool ThemeWall { get; set; } = true;                           // picking a theme / accent also sets a wallpaper drawn in its colours
    public string PowerMode { get; set; } = "off";                        // off | saver | perf
    public string SaverPlan { get; set; } = "";                           // power plan for Energy saver mode ("" = pick a battery-saving one)
    public string PerfPlan { get; set; } = "";                            // power plan for Performance mode ("" = pick Ultimate / High performance)
    public bool FanControl { get; set; } = true;                          // also set the ASUS fan mode
    public int FanPerf { get; set; }                                      // ASUS mode used in Performance mode: 0 = Performance, 1 = Turbo
    public string PlanOrig { get; set; } = "";                            // what to go back to when both modes are off
    public int FanOrig { get; set; } = -1;
    public string EsGuid { get; set; } = "";                              // the plan whose Energy saver "turn on at" level a mode changed...
    public int EsVal { get; set; } = -1;                                  // ...and what it was before
    public bool Anim { get; set; } = true;                                // menus fade / slide, the dock eases (off = lighter on slow PCs)
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
class Pal {
    public string Id, Name, Sub, Bg, Glass, Fg, Dim, Line, Tile, Accent, W1, W2;   // W1/W2 = the little "wallpaper" gradient in the previews
    public bool Light, Tinted;                                                       // Tinted = also colours the Windows Start menu
}
static class Th {
    public static Brush Bg, Fg, Dim, Line, Glass, Tile, Accent;
    static Brush B(string h) { var b = (Brush)new BrushConverter().ConvertFrom(h); b.Freeze(); return b; }

    public static readonly Pal[] All = {
        new Pal { Id = "dark",      Name = "Classic Dark", Sub = "Neutral, red",       Bg = "#0E0E10", Glass = "#D9101012", Fg = "#F1F1F1", Dim = "#8D8D93", Line = "#1FFFFFFF", Tile = "#10FFFFFF", Accent = "#E5303A", W1 = "#26262E", W2 = "#0A0A0C" },
        new Pal { Id = "black",     Name = "Onyx",         Sub = "True black, orange", Bg = "#000000", Glass = "#E6000000", Fg = "#F1F1F1", Dim = "#8D8D93", Line = "#1FFFFFFF", Tile = "#10FFFFFF", Accent = "#FF7A1A", W1 = "#16161C", W2 = "#000000" },
        new Pal { Id = "navy",      Name = "Deep Sea",     Sub = "Navy, blue",         Bg = "#0B1322", Glass = "#E00B1322", Fg = "#F1F1F1", Dim = "#8D8D93", Line = "#1FFFFFFF", Tile = "#147AA2FF", Accent = "#4F8CFF", W1 = "#1C3A6B", W2 = "#060B16", Tinted = true },
        new Pal { Id = "aubergine", Name = "Nightshade",   Sub = "Plum, violet",       Bg = "#2A0F2A", Glass = "#E02A0F2A", Fg = "#F1F1F1", Dim = "#B79BB7", Line = "#1FFFFFFF", Tile = "#1AFF9BF0", Accent = "#B66BFF", W1 = "#55235A", W2 = "#1A081A", Tinted = true },
        new Pal { Id = "forest",    Name = "Pinewood",     Sub = "Forest, green",      Bg = "#0D1A14", Glass = "#E00D1A14", Fg = "#EAF1EC", Dim = "#8FA397", Line = "#1FFFFFFF", Tile = "#143DDC84", Accent = "#2FAE6B", W1 = "#1F4A35", W2 = "#07110C", Tinted = true },
        new Pal { Id = "ember",     Name = "Ember",        Sub = "Warm brown, amber",  Bg = "#1A0F0A", Glass = "#E01A0F0A", Fg = "#F3ECE6", Dim = "#A89485", Line = "#1FFFFFFF", Tile = "#14FF9A4D", Accent = "#E8742A", W1 = "#5A2A12", W2 = "#120703", Tinted = true },
        new Pal { Id = "ocean",     Name = "Lagoon",       Sub = "Teal, aqua",         Bg = "#08191C", Glass = "#E008191C", Fg = "#E8F2F2", Dim = "#86A3A6", Line = "#1FFFFFFF", Tile = "#1428D4C8", Accent = "#19B5A5", W1 = "#11505A", W2 = "#041014", Tinted = true },
        new Pal { Id = "rose",      Name = "Rosewood",     Sub = "Berry, rose",        Bg = "#220E14", Glass = "#E0220E14", Fg = "#F5ECEE", Dim = "#B796A0", Line = "#1FFFFFFF", Tile = "#14FF6B8A", Accent = "#E04A6E", W1 = "#5A1F34", W2 = "#150509", Tinted = true },
        new Pal { Id = "light",     Name = "Daylight",     Sub = "Light, red",         Bg = "#F3F2EF", Glass = "#E6F3F2EF", Fg = "#16161A", Dim = "#6B6B72", Line = "#20000000", Tile = "#10000000", Accent = "#E5303A", W1 = "#D4E1F5", W2 = "#F4EEE6", Light = true },
        new Pal { Id = "paper",     Name = "Parchment",    Sub = "Cream, copper",      Bg = "#F4EFE3", Glass = "#E6F4EFE3", Fg = "#2A2418", Dim = "#7A705C", Line = "#22000000", Tile = "#12000000", Accent = "#B5651D", W1 = "#E6D7B2", W2 = "#F8F2E2", Light = true },
        new Pal { Id = "graphite", Name = "Graphite", Sub = "Cool grey, blue", Bg = "#14161A", Glass = "#E014161A", Fg = "#ECEFF4", Dim = "#8B93A1", Line = "#1FFFFFFF", Tile = "#148FB4FF", Accent = "#5B8DEF", W1 = "#2B313C", W2 = "#0C0D10", Tinted = true },
        new Pal { Id = "nord", Name = "Nord", Sub = "Arctic, ice blue", Bg = "#2E3440", Glass = "#E02E3440", Fg = "#ECEFF4", Dim = "#9AA5B8", Line = "#1FFFFFFF", Tile = "#1488C0D0", Accent = "#4C9AB3", W1 = "#4C566A", W2 = "#1F232B", Tinted = true },
        new Pal { Id = "dracula", Name = "Dracula", Sub = "Violet, pink", Bg = "#21222C", Glass = "#E021222C", Fg = "#F8F8F2", Dim = "#8F93B2", Line = "#1FFFFFFF", Tile = "#14BD93F9", Accent = "#9D6BF0", W1 = "#44475A", W2 = "#16171E", Tinted = true },
        new Pal { Id = "tokyo", Name = "Tokyo Night", Sub = "Indigo, sky", Bg = "#16161E", Glass = "#E016161E", Fg = "#C0CAF5", Dim = "#787C99", Line = "#1FFFFFFF", Tile = "#147AA2F7", Accent = "#5B86F0", W1 = "#2A2F4A", W2 = "#0D0D14", Tinted = true },
        new Pal { Id = "gruvbox", Name = "Gruvbox", Sub = "Earthy, amber", Bg = "#1D2021", Glass = "#E01D2021", Fg = "#EBDBB2", Dim = "#A89984", Line = "#1FFFFFFF", Tile = "#14FABD2F", Accent = "#C98512", W1 = "#504945", W2 = "#141617", Tinted = true },
        new Pal { Id = "solar", Name = "Solar Night", Sub = "Deep teal, gold", Bg = "#002B36", Glass = "#E0002B36", Fg = "#EEE8D5", Dim = "#839496", Line = "#1FFFFFFF", Tile = "#14B58900", Accent = "#B58900", W1 = "#0B4A58", W2 = "#001E26", Tinted = true },
        new Pal { Id = "mocha", Name = "Mocha", Sub = "Warm mauve", Bg = "#1E1E2E", Glass = "#E01E1E2E", Fg = "#CDD6F4", Dim = "#9399B2", Line = "#1FFFFFFF", Tile = "#14F5C2E7", Accent = "#B07CE8", W1 = "#3A3A55", W2 = "#11111B", Tinted = true },
        new Pal { Id = "sunset", Name = "Sunset", Sub = "Dusk, coral", Bg = "#1C1220", Glass = "#E01C1220", Fg = "#F6EDF0", Dim = "#B49AAE", Line = "#1FFFFFFF", Tile = "#14FF7B54", Accent = "#F2603E", W1 = "#6A2A4A", W2 = "#120914", Tinted = true },
        new Pal { Id = "mint", Name = "Mint Night", Sub = "Charcoal, mint", Bg = "#101614", Glass = "#E0101614", Fg = "#E8F3EE", Dim = "#86A096", Line = "#1FFFFFFF", Tile = "#1428E0A8", Accent = "#1FB98A", W1 = "#1E4A40", W2 = "#070B0A", Tinted = true },
        new Pal { Id = "crimson", Name = "Crimson", Sub = "Black, deep red", Bg = "#120A0B", Glass = "#E0120A0B", Fg = "#F5ECEC", Dim = "#A58F91", Line = "#1FFFFFFF", Tile = "#14FF4D5A", Accent = "#D62839", W1 = "#4A1118", W2 = "#0A0405", Tinted = true },
        new Pal { Id = "slate", Name = "Slate", Sub = "Blue-grey, cyan", Bg = "#1A2028", Glass = "#E01A2028", Fg = "#E6EDF3", Dim = "#8795A6", Line = "#1FFFFFFF", Tile = "#1438BDF8", Accent = "#0EA5E9", W1 = "#33475C", W2 = "#0F141A", Tinted = true },
        new Pal { Id = "midnight", Name = "Midnight", Sub = "Indigo, violet", Bg = "#0B0B1E", Glass = "#E00B0B1E", Fg = "#EDEBFF", Dim = "#8F8CB5", Line = "#1FFFFFFF", Tile = "#148B7BFF", Accent = "#6D5DFC", W1 = "#272A66", W2 = "#05050F", Tinted = true },
        new Pal { Id = "terminal", Name = "Terminal", Sub = "Black, green", Bg = "#07090A", Glass = "#E007090A", Fg = "#E6F5EA", Dim = "#78968A", Line = "#1FFFFFFF", Tile = "#1439FF14", Accent = "#16A34A", W1 = "#0F3A1E", W2 = "#030604", Tinted = true },
        new Pal { Id = "sky", Name = "Sky", Sub = "Pale blue, azure", Bg = "#EEF3FA", Glass = "#E6EEF3FA", Fg = "#14181F", Dim = "#5F6B7A", Line = "#20000000", Tile = "#10000000", Accent = "#2F6FED", W1 = "#CFE0FA", W2 = "#F3F8FF", Light = true },
        new Pal { Id = "spearmint", Name = "Spearmint", Sub = "Pale green, emerald", Bg = "#EDF6F1", Glass = "#E6EDF6F1", Fg = "#13201A", Dim = "#5B6E64", Line = "#20000000", Tile = "#10000000", Accent = "#12945F", W1 = "#CDEBDD", W2 = "#F2FAF6", Light = true },
        new Pal { Id = "blush", Name = "Blush", Sub = "Soft pink, rose", Bg = "#FAF0F2", Glass = "#E6FAF0F2", Fg = "#26161B", Dim = "#7A5F67", Line = "#20000000", Tile = "#10000000", Accent = "#D6406A", W1 = "#F4CFD8", W2 = "#FDF6F8", Light = true },
        new Pal { Id = "dune", Name = "Dune", Sub = "Sand, terracotta", Bg = "#F5EEE6", Glass = "#E6F5EEE6", Fg = "#2A1E16", Dim = "#7D6A5A", Line = "#20000000", Tile = "#10000000", Accent = "#C25B2E", W1 = "#EBD5BD", W2 = "#FAF4EC", Light = true },
        new Pal { Id = "lavender", Name = "Lavender", Sub = "Lilac, purple", Bg = "#F2EFFA", Glass = "#E6F2EFFA", Fg = "#1D1630", Dim = "#6A6286", Line = "#20000000", Tile = "#10000000", Accent = "#7C4DDB", W1 = "#DCD2F5", W2 = "#F8F6FE", Light = true },
        new Pal { Id = "frost", Name = "Frost", Sub = "Icy, teal", Bg = "#EEF2F4", Glass = "#E6EEF2F4", Fg = "#1B2A30", Dim = "#61757C", Line = "#20000000", Tile = "#10000000", Accent = "#1C8FA6", W1 = "#CFE2E8", W2 = "#F5F9FA", Light = true },
        new Pal { Id = "cloud", Name = "Cloud", Sub = "Neutral grey, blue", Bg = "#EDEDEF", Glass = "#E6EDEDEF", Fg = "#18181B", Dim = "#6B6B73", Line = "#20000000", Tile = "#10000000", Accent = "#3B6FD8", W1 = "#D6D6DC", W2 = "#F6F6F8", Light = true },
    };
    public static Pal Get(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
    public static bool IsLight(string id) => Get(id).Light;
    public static Color? Tint(string id) { var p = Get(id); if (!p.Tinted) return null; return (Color)ColorConverter.ConvertFromString(p.Bg); }

    public static void Apply() {
        var c = Store.C; var p = Get(c.Theme);
        Bg = B(p.Bg); Glass = B(p.Glass); Fg = B(p.Fg); Dim = B(p.Dim); Line = B(p.Line); Tile = B(p.Tile);
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
        bool light = Th.IsLight(theme);
        Color? tint = Th.Tint(theme);
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
    public static bool Fx => Store.C.Anim;                                           // animations on?
    public static readonly CubicEase Ease = MakeEase();
    static CubicEase MakeEase() { var e = new CubicEase { EasingMode = EasingMode.EaseOut }; e.Freeze(); return e; }
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
    static IntPtr DesktopHost() {
        IntPtr host = IntPtr.Zero;
        EnumWindows((w, _) => { if (FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) { host = w; return false; } return true; }, IntPtr.Zero);
        return host != IntPtr.Zero ? host : FindWindow("Progman", null);
    }
    public static void ToDesktop(IntPtr h) {
        if (h == IntPtr.Zero) return;
        var host = DesktopHost();
        if (host != IntPtr.Zero) SetWindowPos(h, host, 0, 0, 0, 0, 0x13);   // NOMOVE | NOSIZE | NOACTIVATE
    }
    // Show a hidden window and put it under the apps in one step, so it can't flash on top of them first
    public static void ShowAtDesktop(IntPtr h) {
        if (h == IntPtr.Zero) return;
        var host = DesktopHost();
        if (host != IntPtr.Zero) SetWindowPos(h, host, 0, 0, 0, 0, 0x13 | 0x40);   // + SWP_SHOWWINDOW
        else ShowWindow(h, 4);
    }
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] static extern IntPtr SendMsgTimeout(IntPtr h, uint msg, UIntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr res);

    // Where a wallpaper-layer window can live: inside the desktop itself, under the icons and above the wallpaper.
    // A window in there is part of the desktop, so it can never be drawn over an app, a widget, the dock or a menu.
    public static bool WallpaperLayer(out IntPtr parent, out IntPtr after) {
        parent = after = IntPtr.Zero;
        var progman = FindWindow("Progman", null); if (progman == IntPtr.Zero) return false;
        try { SendMsgTimeout(progman, 0x052C, new UIntPtr(0xD), (IntPtr)1, 0, 1000, out _); } catch { }   // asks the shell for its separate wallpaper window
        IntPtr icons = IntPtr.Zero;
        EnumWindows((w, _) => { if (FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) { icons = w; return false; } return true; }, IntPtr.Zero);
        if (icons != IntPtr.Zero && icons != progman) {                       // classic layout: icons in one WorkerW, the wallpaper in the WorkerW behind it
            parent = FindWindowEx(IntPtr.Zero, icons, "WorkerW", null);
            return parent != IntPtr.Zero;
        }
        after = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);   // newer Windows 11: the icons are a child of Progman, go just under them
        parent = progman;
        return after != IntPtr.Zero;
    }
    // x, y, w, hh = where the window should cover, in screen pixels
    public static bool Embed(IntPtr h, int x, int y, int w, int hh) {
        if (!WallpaperLayer(out var parent, out var after)) return false;
        if (!GetWindowRect(parent, out var r)) return false;
        int st = GetWindowLong(h, -16);
        SetWindowLong(h, -16, (st & ~unchecked((int)0x80000000)) | 0x40000000);   // pop-up window -> child window
        SetParent(h, parent);
        return SetWindowPos(h, after, x - r.L, y - r.T, w, hh, 0x10 | 0x20);   // NOACTIVATE | FRAMECHANGED
    }
    public static void ClickThrough(IntPtr h) => SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x20);   // WS_EX_TRANSPARENT: clicks go to what is behind
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

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource> icons = new();
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
    static int offOn, offCov;
    public static void Poll() {
        bool mine = N.ForegroundIsMine();   // Hoster's own Start menu / flyouts opened over a full-screen or maximised window: nothing has changed behind them
        bool v = Store.C.HideInFullscreen && (mine ? On : N.Fullscreen());
        // Leaving full screen has to hold for ~0.8s. A new-tab popup, tooltip or the Start menu flashing in front used to flip this and pop the dock, bar and widgets over the app.
        if (On && !v) { if (++offOn < 2) v = true; } else offOn = 0;
        if (v != On) { On = v; Changed?.Invoke(); }
        if (mine) return;
        bool c = Store.C.HideInFullscreen && N.Covered();
        if (Covered && !c) { if (++offCov < 2) c = true; } else offCov = 0;
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
    // Brings Spotify to the front if it's open, otherwise starts it: the installed app first, then the spotify: link, the Store version, and finally the web player.
    public static void OpenSpotify() {
        try {
            var w = N.Running().FirstOrDefault(x => string.Equals(Path.GetFileName(x.path), "Spotify.exe", StringComparison.OrdinalIgnoreCase));
            if (w.h != IntPtr.Zero) { N.Focus(w.h); return; }
        } catch { }
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe");
        string[] tries = { File.Exists(exe) ? exe : null, "spotify:", @"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "https://open.spotify.com" };
        foreach (var t in tries) {
            if (t == null) continue;
            try { Process.Start(t.StartsWith("shell:") ? new ProcessStartInfo("explorer.exe", t) { UseShellExecute = true } : new ProcessStartInfo(t) { UseShellExecute = true }); return; } catch { }
        }
    }
}

class BarWindow : Shell {
    TextBlock clk, dat, st, ttl; Border art; TextBlock pp; FrameworkElement mediaBox, statBox, netBox, batBox;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) }, poll = new() { Interval = TimeSpan.FromMilliseconds(50) };
    bool shown, animating; int animId, dwell, away; Flyout fly; string flyKind;

    public BarWindow() {
        SizeToContent = SizeToContent.WidthAndHeight; Width = double.NaN; Height = double.NaN; Top = -100;
        Build();
        Store.Changed += () => { CloseFly(); Build(); Place(); };
        Player.Changed += UpdateMedia;
        Batt.Changed += () => { Build(); Place(); }; Net.AlertChanged += () => { Build(); Place(); };
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
        Flyout f; if (kind == "cal") f = new CalendarFlyout(); else if (kind == "net") f = new NetFlyout(); else if (kind == "pwr") f = new PowerFlyout(); else f = new MediaFlyout();
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

        var wifiG = UI.Glyph("\uE701", Net.Alert ? BattUI.Warn : Th.Fg, 12); var btG = UI.Glyph("\uE702", Th.Fg, 12); btG.Margin = new Thickness(8, 0, 0, 0);
        UIElement wifiIcon = wifiG;
        if (Net.Alert) wifiIcon = new Grid { Children = { wifiG, new Shp.Ellipse { Width = 6, Height = 6, Fill = BattUI.Warn, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -3, -4, 0) } } };
        Border netBtn = null;
        netBtn = UI.Click(new StackPanel { Orientation = Orientation.Horizontal, Children = { wifiIcon, btG } }, () => Toggle("net", netBtn), null, 7, 3);
        if (Net.Alert) netBtn.ToolTip = "Wi-Fi: action needed (sign in)";

        var batTx = UI.Txt(Batt.Percent + "%", BattUI.IsLow ? BattUI.Low : Th.Fg, 11.5); batTx.FontFamily = mono; batTx.Margin = new Thickness(6, 0, 0, 0);
        Border batBtn = null;
        batBtn = UI.Click(new StackPanel { Orientation = Orientation.Horizontal, Children = { BattUI.Icon(20), batTx } }, () => Toggle("pwr", batBtn), null, 7, 3);
        batBtn.ToolTip = BattUI.Status();
        batBox = new StackPanel { Orientation = Orientation.Horizontal, Children = { Div(), batBtn } };
        netBox = new StackPanel { Orientation = Orientation.Horizontal, Children = { Div(), netBtn } };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { logo, mediaBox, Div(), clkBtn, statBox, batBox, netBox } };
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
        batBox.Visibility = c.BatteryBar && Batt.Has ? Visibility.Visible : Visibility.Collapsed;
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
        Left = left; Top = top; if (!UI.Fx) { Show(); return; }
        Opacity = 0; Show();
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
        Top = Math.Max(4, iconTop - ActualHeight + 14);
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
        var ic = StartMenu.IconFor(exe); if (ic != null) hd.Children.Add(new Image { Source = ic, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) });
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
        Top = Math.Max(4, iconTop - ActualHeight + 14);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(80)));
    }
}

class DockWindow : Shell {
    List<(uint pid, IntPtr h, string title, string path)> live = new();
    StackPanel row;
    readonly DispatcherTimer t = new() { Interval = TimeSpan.FromSeconds(3) };
    readonly DispatcherTimer hov = new() { Interval = TimeSpan.FromMilliseconds(40) };
    AppPopup pop; string popKey, hoverKey; DateTime hoverSince, leftAt = DateTime.UtcNow; bool showing;
    string liveSig; readonly Dictionary<StackPanel, double> goal = new(); bool easing;
    public DockWindow() {
        Build();
        if (UI.Fx) { Opacity = 0; Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(380))); }
        hov.Tick += (_, _) => { if (!Full.On) HoverTick(); }; hov.Start();
        Store.Changed += () => { Build(); Place(); };
        async Task Poll() {
            var n = await Task.Run(() => N.Running());
            var sig = string.Join("|", n.Select(x => x.path + "@" + x.h));
            live = n;
            if (sig == liveSig && row != null) return;          // nothing opened or closed: skip rebuilding the whole dock
            liveSig = sig; Build();
        }
        Full.Changed += async () => { Visibility = Full.On ? Visibility.Hidden : Visibility.Visible; if (Full.On) HidePop(); else await Poll(); };
        t.Tick += async (_, _) => { if (!Full.On) await Poll(); }; t.Start();
        Loaded += async (_, _) => await Poll();
        MouseMove += (_, e) => Mag(e.GetPosition(row));
        MouseLeave += (_, _) => Mag(new Point(-9999, 0));
    }
    protected override void Place() => N.AppBar(Handle, 3, Px(Store.C.IconSize + 44), Px(Math.Max(70, 64 + Store.C.IconSize * Store.C.MagnifyAmount / 100.0)));   // extra headroom so big magnification isn't cut off
    void Mag(Point m) {
        foreach (var e in row.Children.OfType<StackPanel>()) {
            if (e.RenderTransform is not ScaleTransform) continue;
            double cx = e.TranslatePoint(new Point(e.ActualWidth / 2, 0), row).X;
            goal[e] = Store.C.Magnify ? 1 + Store.C.MagnifyAmount / 100.0 * Math.Max(0, 1 - Math.Abs(m.X - cx) / 110) : 1;
        }
        if (!UI.Fx) { foreach (var kv in goal) { var s0 = (ScaleTransform)kv.Key.RenderTransform; s0.ScaleX = s0.ScaleY = kv.Value; } return; }
        if (!easing) { easing = true; CompositionTarget.Rendering += Ease; }      // only runs while icons are still moving
    }
    void Ease(object s, EventArgs a) {
        bool moving = false;
        foreach (var kv in goal) {
            var st = (ScaleTransform)kv.Key.RenderTransform; double d = kv.Value - st.ScaleX;
            if (Math.Abs(d) < 0.004) { if (d != 0) st.ScaleX = st.ScaleY = kv.Value; continue; }
            st.ScaleX = st.ScaleY = st.ScaleX + d * 0.3; moving = true;
        }
        if (!moving) { easing = false; CompositionTarget.Rendering -= Ease; }
    }
    FrameworkElement Face(string name, string target, int sz) {
        var ic = StartMenu.IconFor(target);
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
        var c = Store.C; int sz = c.IconSize; goal.Clear();
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

// ---------------- what the Start menu finds besides apps: Settings pages, classic Control Panel items and admin tools ----------------
record Sx(string Cat, string Title, string File, string Args, string Keys, bool Admin = false);

static class SettingsIndex {
    static Sx S(string title, string uri, string keys) => new("Settings", title, uri, "", keys);
    static Sx C(string title, string args, string keys) => new("Control Panel", title, "control.exe", args, keys, true);
    static Sx T(string title, string file, string keys, string args = "") => new("Tool", title, file, args, keys, true);
    static Sx M(string title, string msc, string keys) => new("Tool", title, "mmc.exe", msc, keys, true);

    public static readonly Sx[] All = {
        // ----- Settings app -----
        S("Display", "ms-settings:display", "screen resolution scale brightness monitor refresh rate hdr orientation multiple displays"),
        S("Sound", "ms-settings:sound", "volume audio output input speakers microphone devices"),
        S("Notifications", "ms-settings:notifications", "alerts action center banners"),
        S("Focus / Do not disturb", "ms-settings:quiethours", "focus assist quiet hours dnd"),
        S("Power & battery", "ms-settings:powersleep", "power plan sleep screen turn off battery energy mode lid"),
        S("Battery saver", "ms-settings:batterysaver", "battery usage low battery"),
        S("Storage", "ms-settings:storagesense", "disk space cleanup storage sense temporary files drives"),
        S("Multitasking", "ms-settings:multitasking", "snap windows virtual desktops alt tab"),
        S("Clipboard", "ms-settings:clipboard", "clipboard history paste sync"),
        S("About this PC", "ms-settings:about", "system info device name specs version windows edition rename pc"),
        S("Remote Desktop settings", "ms-settings:remotedesktop", "rdp remote access"),
        S("Night light", "ms-settings:nightlight", "blue light warm colour"),
        S("Projecting to this PC", "ms-settings:project", "cast wireless display"),
        S("Nearby sharing", "ms-settings:crossdevice", "share devices"),
        S("Bluetooth & devices", "ms-settings:bluetooth", "pair headphones add device"),
        S("Printers & scanners", "ms-settings:printers", "print add printer"),
        S("Mouse", "ms-settings:mousetouchpad", "pointer speed cursor buttons scroll"),
        S("Touchpad", "ms-settings:devices-touchpad", "gestures tap scroll"),
        S("Typing", "ms-settings:typing", "autocorrect spelling text suggestions"),
        S("AutoPlay", "ms-settings:autoplay", "removable drives memory cards"),
        S("USB", "ms-settings:usb", "usb notifications charging"),
        S("Network & internet", "ms-settings:network", "status connection internet"),
        S("Wi-Fi", "ms-settings:network-wifi", "wireless networks known networks"),
        S("Ethernet", "ms-settings:network-ethernet", "wired lan adapter ip dns"),
        S("VPN", "ms-settings:network-vpn", "virtual private network"),
        S("Airplane mode", "ms-settings:network-airplanemode", "flight wireless radios"),
        S("Proxy", "ms-settings:network-proxy", "proxy server"),
        S("Mobile hotspot", "ms-settings:network-mobilehotspot", "share internet tethering"),
        S("Data usage", "ms-settings:datausage", "metered bandwidth"),
        S("Background", "ms-settings:personalization-background", "wallpaper desktop picture slideshow"),
        S("Colours", "ms-settings:personalization-colors", "accent colour dark mode light mode theme transparency"),
        S("Themes", "ms-settings:themes", "desktop icons mouse cursor sounds contrast"),
        S("Lock screen", "ms-settings:lockscreen", "sign in screen picture"),
        S("Start settings", "ms-settings:personalization-start", "start menu layout recent folders"),
        S("Taskbar settings", "ms-settings:taskbar", "taskbar behaviours icons corner system tray"),
        S("Fonts", "ms-settings:fonts", "typeface install font"),
        S("Installed apps", "ms-settings:appsfeatures", "uninstall remove programs apps and features"),
        S("Default apps", "ms-settings:defaultapps", "file types browser email default program"),
        S("Optional features", "ms-settings:optionalfeatures", "windows features add feature"),
        S("Startup apps", "ms-settings:startupapps", "run at login boot programs"),
        S("Your account", "ms-settings:yourinfo", "profile picture name microsoft account"),
        S("Sign-in options", "ms-settings:signinoptions", "pin password fingerprint windows hello face security key dynamic lock"),
        S("Email & accounts", "ms-settings:emailandaccounts", "add account"),
        S("Other users", "ms-settings:otherusers", "family add user kiosk"),
        S("Access work or school", "ms-settings:workplace", "organisation connect"),
        S("Date & time", "ms-settings:dateandtime", "clock time zone set time automatically"),
        S("Language & region", "ms-settings:regionlanguage", "display language keyboard layout add language"),
        S("Region", "ms-settings:regionformatting", "country format date format currency"),
        S("Speech", "ms-settings:speech", "voice microphone language"),
        S("Game Bar", "ms-settings:gaming-gamebar", "xbox overlay controller"),
        S("Game Mode", "ms-settings:gaming-gamemode", "gaming performance"),
        S("Captures", "ms-settings:gaming-gamedvr", "screenshots recording clips"),
        S("Accessibility", "ms-settings:easeofaccess", "ease of access"),
        S("Text size", "ms-settings:easeofaccess-display", "bigger text scale"),
        S("Magnifier", "ms-settings:easeofaccess-magnifier", "zoom"),
        S("Narrator", "ms-settings:easeofaccess-narrator", "screen reader"),
        S("Contrast themes", "ms-settings:easeofaccess-highcontrast", "high contrast"),
        S("Keyboard accessibility", "ms-settings:easeofaccess-keyboard", "sticky keys filter keys on-screen keyboard"),
        S("Mouse pointer & touch", "ms-settings:easeofaccess-mousepointer", "cursor size colour"),
        S("Closed captions", "ms-settings:easeofaccess-closedcaptioning", "subtitles"),
        S("Privacy & security", "ms-settings:privacy", "permissions"),
        S("Camera privacy", "ms-settings:privacy-webcam", "webcam access"),
        S("Microphone privacy", "ms-settings:privacy-microphone", "mic access"),
        S("Location privacy", "ms-settings:privacy-location", "gps"),
        S("Windows Update", "ms-settings:windowsupdate", "updates check for updates pause install patches"),
        S("Windows Security", "windowsdefender:", "antivirus defender virus protection firewall malware"),
        S("Backup", "ms-settings:backup", "file history onedrive restore"),
        S("Recovery", "ms-settings:recovery", "reset this pc go back advanced startup reinstall"),
        S("Activation", "ms-settings:activation", "product key licence"),
        S("Troubleshoot", "ms-settings:troubleshoot", "fix problems diagnose"),
        S("For developers", "ms-settings:developers", "developer mode"),
        S("Find my device", "ms-settings:findmydevice", "locate lost"),
        S("Delivery optimisation", "ms-settings:delivery-optimization", "update bandwidth"),
        S("Windows Insider Program", "ms-settings:windowsinsider", "preview builds"),

        // ----- classic Control Panel -----
        C("Control Panel", "", "classic all items"),
        C("Power Options", "powercfg.cpl", "power plan power plans high performance balanced power saver sleep hibernate lid close battery"),
        C("Choose what the power buttons do", "/name Microsoft.PowerOptions /page pageGlobalSettings", "power button lid fast startup hibernate shutdown"),
        C("Advanced power settings", "powercfg.cpl,,3", "processor minimum maximum usb selective suspend power plan advanced"),
        C("Programs and Features", "appwiz.cpl", "uninstall a program change program turn windows features on or off"),
        C("System", "/name Microsoft.System", "computer name windows edition processor ram"),
        C("Network and Sharing Center", "/name Microsoft.NetworkAndSharingCenter", "network adapter sharing wifi"),
        C("Network Connections", "ncpa.cpl", "adapters change adapter settings ethernet wifi ip dns"),
        C("Sound (classic)", "mmsys.cpl", "playback recording devices speakers microphone sounds communications"),
        C("Mouse (classic)", "main.cpl", "pointer buttons wheel double click speed"),
        C("Keyboard (classic)", "keyboard", "repeat delay cursor blink"),
        C("Date and Time (classic)", "timedate.cpl", "clock time zone additional clocks internet time"),
        C("Region (classic)", "intl.cpl", "formats location administrative language for non-unicode"),
        C("Internet Options", "inetcpl.cpl", "browser proxy security internet explorer certificates"),
        C("Windows Defender Firewall", "firewall.cpl", "firewall allow an app through firewall"),
        C("User Accounts", "/name Microsoft.UserAccounts", "change account type account name"),
        C("Credential Manager", "/name Microsoft.CredentialManager", "saved passwords web credentials windows credentials vault"),
        C("Devices and Printers", "/name Microsoft.DevicesAndPrinters", "hardware peripherals"),
        C("Fonts (classic)", "/name Microsoft.Fonts", "install fonts font settings"),
        C("File Explorer Options", "folders", "folder options show hidden files extensions"),
        C("Administrative Tools", "admintools", "windows tools management"),
        C("Security and Maintenance", "/name Microsoft.ActionCenter", "action center reliability"),
        C("Troubleshooting (classic)", "/name Microsoft.Troubleshooting", "fix problems"),
        C("Backup and Restore (Windows 7)", "/name Microsoft.BackupAndRestore", "system image backup"),
        C("Recovery (classic)", "/name Microsoft.Recovery", "create a recovery drive restore system"),
        C("BitLocker Drive Encryption", "/name Microsoft.BitLockerDriveEncryption", "encrypt drive"),
        C("Storage Spaces", "/name Microsoft.StorageSpaces", "pool drives"),
        C("Ease of Access Center", "/name Microsoft.EaseOfAccessCenter", "accessibility"),
        C("Indexing Options", "/name Microsoft.IndexingOptions", "search index"),
        C("AutoPlay (classic)", "/name Microsoft.AutoPlay", "media defaults"),
        C("Phone and Modem", "telephon.cpl", "dial-up area code"),
        C("Game Controllers", "joy.cpl", "gamepad joystick calibrate"),

        // ----- tools (most of these can run as administrator) -----
        T("Advanced system settings", "SystemPropertiesAdvanced.exe", "performance startup recovery environment variables system properties"),
        T("Environment Variables", "rundll32.exe", "path variables user system", "sysdm.cpl,EditEnvironmentVariables"),
        T("Computer name", "SystemPropertiesComputerName.exe", "rename pc workgroup domain"),
        T("Performance Options", "SystemPropertiesPerformance.exe", "visual effects virtual memory page file"),
        T("System Protection", "SystemPropertiesProtection.exe", "restore point create restore"),
        T("Remote settings", "SystemPropertiesRemote.exe", "remote assistance remote desktop allow"),
        T("Color Management", "colorcpl.exe", "icc profile display calibration"),
        T("Task Manager", "taskmgr.exe", "processes performance startup kill end task cpu memory"),
        T("Command Prompt", "cmd.exe", "cmd terminal console dos"),
        T("Windows PowerShell", "powershell.exe", "ps terminal scripts"),
        T("Registry Editor", "regedit.exe", "registry keys hkey"),
        T("Resource Monitor", "resmon.exe", "cpu disk network memory usage"),
        T("System Information", "msinfo32.exe", "msinfo hardware resources components"),
        T("System Configuration", "msconfig.exe", "msconfig boot services startup safe mode"),
        T("Disk Cleanup", "cleanmgr.exe", "free space temporary files"),
        T("Defragment and Optimize Drives", "dfrgui.exe", "defrag trim ssd"),
        T("Windows Memory Diagnostic", "mdsched.exe", "ram test"),
        T("DirectX Diagnostic Tool", "dxdiag.exe", "graphics gpu sound directx"),
        T("Remote Desktop Connection", "mstsc.exe", "rdp connect"),
        T("Character Map", "charmap.exe", "symbols special characters"),
        T("Component Services", "dcomcnfg.exe", "com dcom"),
        T("ODBC Data Sources", "odbcad32.exe", "database drivers"),
        M("Device Manager", "devmgmt.msc", "drivers hardware update driver disable device"),
        M("Disk Management", "diskmgmt.msc", "partitions format drive letters volumes"),
        M("Services", "services.msc", "start stop service background"),
        M("Event Viewer", "eventvwr.msc", "logs errors crashes"),
        M("Computer Management", "compmgmt.msc", "manage"),
        M("Task Scheduler", "taskschd.msc", "scheduled tasks automate"),
        M("Performance Monitor", "perfmon.msc", "counters"),
        M("Local Group Policy Editor", "gpedit.msc", "policy pro enterprise"),
        M("Local Users and Groups", "lusrmgr.msc", "accounts administrators"),
        M("Certificate Manager", "certmgr.msc", "certificates"),
        M("Windows Firewall with Advanced Security", "wf.msc", "inbound outbound rules"),
        M("Shared Folders", "fsmgmt.msc", "shares sessions open files"),
    };

    // every typed word has to appear in the title, its keywords or its group, so "power plan", "uninstall" or "control panel" all work
    public static List<(Sx e, int s)> Find(string q, int max) {
        string ql = q.ToLowerInvariant().Trim();
        var toks = ql.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        var res = new List<(Sx e, int s)>();
        if (toks.Length == 0) return res;
        foreach (var e in All) {
            string t = e.Title.ToLowerInvariant(), hay = t + " " + e.Keys + " " + e.Cat.ToLowerInvariant();
            if (!toks.All(k => hay.Contains(k))) continue;
            int s = t.StartsWith(ql) ? 0 : t.Contains(ql) ? 1 : toks.All(k => t.Contains(k)) ? 2 : 3;
            res.Add((e, s));
        }
        return res.OrderBy(x => x.s).ThenBy(x => x.e.Title.Length).Take(max).ToList();
    }
}

class StartMenu : Window {
    static StartMenu inst;
    public static List<(string Name, string Id)> Apps = new();
    public static event Action AppsChanged;
    static bool loading;
    static readonly Dictionary<string, ImageSource> icons = new();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
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

    public static ImageSource AppIcon(string id) {
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

    // icon for a dock pin: a file path, or a Store app ("shell:AppsFolder\<id>")
    const string AF = "shell:AppsFolder\\";
    public static ImageSource IconFor(string target) => target != null && target.StartsWith(AF, StringComparison.OrdinalIgnoreCase) ? AppIcon(target.Substring(AF.Length)) : N.Icon(target);

    // desktop apps: the real .exe, so the dock can show the running dot and focus the window. Store apps keep the AppsFolder form.
    static string ResolveExe(string id) {
        try {
            if (id.IndexOf('!') >= 0) return null;
            if (File.Exists(id)) return id;
            if (id.StartsWith("{")) {   // "{known-folder-guid}\\notepad.exe"
                int e = id.IndexOf('}');
                if (e > 0 && Guid.TryParse(id.Substring(0, e + 1), out var g) && SHGetKnownFolderPath(g, 0, IntPtr.Zero, out var p) == 0) {
                    var dir = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p);
                    var full = Path.Combine(dir, id.Substring(e + 1).TrimStart('\\')); if (File.Exists(full)) return full;
                }
            }
        } catch { }
        return null;
    }
    static string PinTarget(string id) => ResolveExe(id) ?? AF + id;

    // right-click menu on a result in the Start menu
    void RowMenu(string name, string target, Action open, Action admin = null, Action locate = null, bool canPin = true) {
        var pinned = Store.C.Pinned.FirstOrDefault(p => string.Equals(p.Target, target, StringComparison.OrdinalIgnoreCase));
        var o = WF.Cursor.Position; var d = VisualTreeHelper.GetDpi(this);
        var items = new List<(string glyph, string text, bool danger, bool on, Action act)> { ("\uE8A7", "Open", false, true, open) };
        if (admin != null) items.Add(("\uE7EF", "Run as administrator", false, true, admin));
        if (locate != null) items.Add(("\uE838", "Open file location", false, true, locate));
        if (canPin) {
            items.Add((null, null, false, true, null));
            if (pinned == null) items.Add(("\uE718", "Pin to taskbar", false, true, () => { Store.C.Pinned.Add(new Pin { Name = name, Target = target }); Store.Save(); }));
            else items.Add(("\uE77A", "Unpin from taskbar", false, true, () => { Store.C.Pinned.Remove(pinned); Store.Save(); }));
        }
        new DockMenu(name, target, items).OpenAbove(o.X / d.DpiScaleX, o.Y / d.DpiScaleY + 8);
    }

    // run something elevated (Windows shows its permission prompt; cancelling it just does nothing)
    static void RunAdmin(string file, string args) { try { Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true, Verb = "runas" }); } catch { } }
    static void Locate(string path) { try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }); } catch { } }
    static void Launch(Sx e, bool admin) { try { var psi = new ProcessStartInfo(e.File, e.Args ?? "") { UseShellExecute = true }; if (admin) psi.Verb = "runas"; Process.Start(psi); } catch { } }
    static readonly string CtlIcon = Path.Combine(Environment.SystemDirectory, "control.exe");

    readonly TextBox box = new(); readonly TextBlock hint; readonly StackPanel list = new(); readonly ScrollViewer sv = new(); readonly Grid foot = new();
    bool ready, powerOpen; Action first, firstAdmin;

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
        hint = UI.Txt("Search apps, settings and tools", Th.Dim, 14); hint.Margin = new Thickness(42, 0, 0, 0); hint.IsHitTestVisible = false;
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
            else if (e.Key == Key.Enter && first != null) { bool both = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift); if (both && firstAdmin != null) firstAdmin(); else first(); e.Handled = true; }   // Ctrl+Shift+Enter = as administrator
        };
        Activated += (_, _) => ready = true;
        Deactivated += (_, _) => { if (ready) CloseMe(); };
        Action again = () => Render(), gone = () => CloseMe();
        AppsChanged += again; Store.Changed += gone;
        Closed += (_, _) => { AppsChanged -= again; Store.Changed -= gone; if (inst == this) inst = null; };
        Foot(); Render();
        if (Apps.Count == 0) _ = Load();
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
        var row = UI.Click(new StackPanel { Orientation = Orientation.Horizontal, Children = { img, tx } }, () => LaunchApp(id), null, 10, 6);
        row.MouseRightButtonUp += (_, e) => {
            bool uwp = id.IndexOf('!') >= 0; string exe = ResolveExe(id);                  // Store apps can't be run elevated
            RowMenu(name, PinTarget(id), () => LaunchApp(id), uwp ? null : () => { CloseMe(); RunAdmin(exe ?? AF + id, ""); }, exe != null ? () => { CloseMe(); Locate(exe); } : null);
            e.Handled = true;
        };
        return row;
    }

    // a result that isn't an app: Settings page, Control Panel item, tool, a path to run, or a web search
    UIElement ItemRow(string glyph, string title, string tag, Action open, Action menu) {
        var gl = UI.Glyph(glyph, Th.Dim, 15); gl.Width = 24; gl.HorizontalAlignment = HorizontalAlignment.Left;
        var tx = UI.Txt(title, Th.Fg, 13); tx.Margin = new Thickness(14, 0, 0, 0); tx.TextTrimming = TextTrimming.CharacterEllipsis;
        var tg = UI.Txt(tag, Th.Dim, 11); tg.Margin = new Thickness(12, 0, 0, 0); tg.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(tg, 1);
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { gl, tx } }); g.Children.Add(tg);
        var row = UI.Click(g, open, null, 10, 6);
        if (menu != null) row.MouseRightButtonUp += (_, e) => { menu(); e.Handled = true; };
        return row;
    }

    UIElement SetRow(Sx e) {
        string sys = Path.Combine(Environment.SystemDirectory, e.File), exe = e.Cat == "Tool" && e.File.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(sys) && e.File != "rundll32.exe" ? sys : null;
        string target = e.Cat == "Settings" ? e.File : (File.Exists(sys) ? sys : CtlIcon);
        Action open = () => { CloseMe(); Launch(e, false); };
        return ItemRow(e.Cat == "Tool" ? "\uE756" : "\uE713", e.Title, e.Cat, open,
            () => RowMenu(e.Title, target, open, e.Admin ? () => { CloseMe(); Launch(e, true); } : null, exe != null ? () => { CloseMe(); Locate(exe); } : null, e.Cat == "Settings"));
    }

    // typed paths, programs, links and shell: addresses can be run straight from the box, like Windows' Run
    static bool Runnable(string q) {
        var x = Environment.ExpandEnvironmentVariables(q); var oic = StringComparison.OrdinalIgnoreCase;
        try { if (File.Exists(x) || Directory.Exists(x)) return true; } catch { }
        return x.Contains("://") || x.StartsWith("shell:", oic) || (x.StartsWith("ms-", oic) && x.Contains(':')) || x.StartsWith("\\\\")
            || (x.Length > 2 && x[1] == ':' && (x[2] == '\\' || x[2] == '/'))
            || x.EndsWith(".exe", oic) || x.EndsWith(".msc", oic) || x.EndsWith(".cpl", oic) || x.EndsWith(".lnk", oic);
    }

    UIElement PinTile(Pin p) {
        var ic = IconFor(p.Target);
        FrameworkElement face = ic != null ? new Image { Source = ic, Width = 36, Height = 36 }
            : new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Background = Th.Tile, Child = UI.Txt(p.Name.Substring(0, 1).ToUpper(), Th.Fg, 14, FontWeights.SemiBold) };
        if (face is Border fb) ((FrameworkElement)fb.Child).HorizontalAlignment = HorizontalAlignment.Center;
        var nm = UI.Txt(p.Name, Th.Fg, 11); nm.TextTrimming = TextTrimming.CharacterEllipsis; nm.MaxWidth = 84; nm.HorizontalAlignment = HorizontalAlignment.Center; nm.Margin = new Thickness(0, 6, 0, 0);
        var tile = UI.Click(new StackPanel { Children = { face, nm } }, () => LaunchPath(p.Target), null, 4, 10);
        tile.MouseRightButtonUp += (_, e) => { RowMenu(p.Name, p.Target, () => LaunchPath(p.Target)); e.Handled = true; };
        return tile;
    }

    void Render() {
        list.Children.Clear(); first = null; firstAdmin = null; string q = box.Text.Trim();
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
            var apps = Apps.Where(a => a.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(a => a.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).Take(12).ToList();
            var sets = q.Length >= 2 ? SettingsIndex.Find(q, 8) : new List<(Sx e, int s)>();
            string runPath = Runnable(q) ? Environment.ExpandEnvironmentVariables(q) : null;
            int appBest = apps.Count == 0 ? 9 : apps[0].Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1, setBest = sets.Count == 0 ? 9 : sets[0].s;
            bool head = true;
            void Sect(string title) { list.Children.Add(Head(head ? "Best match" : title)); head = false; }

            if (runPath != null) {
                list.Children.Add(Head("Run")); head = false;
                Action go = () => LaunchPath(runPath), adm = () => { CloseMe(); RunAdmin(runPath, ""); };
                list.Children.Add(ItemRow("\uE768", "Open “" + q + "”", "Run", go, () => RowMenu(q, CtlIcon, go, adm, null, false)));
                first = go; firstAdmin = adm;
            }
            void ShowApps() {
                if (apps.Count == 0) return; Sect("Apps");
                if (first == null) { var f = apps[0]; first = () => LaunchApp(f.Id); if (f.Id.IndexOf('!') < 0) firstAdmin = () => { CloseMe(); RunAdmin(ResolveExe(f.Id) ?? AF + f.Id, ""); }; }
                foreach (var a in apps) list.Children.Add(AppRow(a.Name, a.Id));
            }
            void ShowSets() {
                if (sets.Count == 0) return; Sect("Settings and tools");
                if (first == null) { var f = sets[0].e; first = () => { CloseMe(); Launch(f, false); }; if (f.Admin) firstAdmin = () => { CloseMe(); Launch(f, true); }; }
                foreach (var s in sets) list.Children.Add(SetRow(s.e));
            }
            if (appBest <= setBest) { ShowApps(); ShowSets(); } else { ShowSets(); ShowApps(); }

            if (apps.Count == 0 && sets.Count == 0 && runPath == null) { var l = UI.Txt("No apps or settings match “" + q + "”", Th.Dim, 12); l.Margin = new Thickness(10, 14, 0, 0); list.Children.Add(l); }
            string web = "https://www.bing.com/search?q=" + Uri.EscapeDataString(q);
            list.Children.Add(Head("Web"));
            list.Children.Add(ItemRow("\uE774", "Search the web for “" + q + "”", "Web", () => LaunchPath(web), null));
            if (first == null) first = () => LaunchPath(web);
            if (firstAdmin != null) { var n = UI.Txt("Ctrl + Shift + Enter runs the first result as administrator", Th.Dim, 11); n.Margin = new Thickness(10, 10, 0, 6); list.Children.Add(n); }
        }
        sv.ScrollToTop();
    }
}

// ---------------- control panel widgets: switch + slider ----------------
class Sw : Border {
    bool on;
    readonly Shp.Ellipse knob = new() { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), RenderTransform = new TranslateTransform() };
    public Sw(bool on, Action<bool> set) {
        this.on = on; Width = 40; Height = 22; CornerRadius = new CornerRadius(11); Cursor = Cursors.Hand; Child = knob; Paint(false);
        MouseLeftButtonUp += (_, _) => { this.on = !this.on; Paint(true); set(this.on); };
    }
    void Paint(bool animate) {
        Background = on ? Th.Accent : Th.Line; knob.Fill = on ? Brushes.White : Th.Dim;
        var t = (TranslateTransform)knob.RenderTransform; double x = on ? 18 : 0;
        if (animate && UI.Fx) t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(150)) { EasingFunction = UI.Ease });
        else { t.BeginAnimation(TranslateTransform.XProperty, null); t.X = x; }
    }
}

class FlatSlider : Grid {
    readonly int min, max; int val; readonly Action<int> set;
    readonly Border fill = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    readonly Shp.Ellipse thumb = new() { Width = 14, Height = 14, Fill = Brushes.White, Stroke = Th.Line, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };   // no blur shadow: cheaper on integrated graphics
    public FlatSlider(int min, int max, int v, Action<int> set) {
        this.min = min; this.max = max; this.set = set; val = Math.Max(min, Math.Min(max, v));
        Width = 170; Height = 24; Cursor = Cursors.Hand; Background = Brushes.Transparent;
        fill.Background = Th.Accent;
        Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Th.Line, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(fill); Children.Add(thumb); Paint();
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); Move(e.GetPosition(this).X); };
        MouseMove += (_, e) => { if (IsMouseCaptured) Move(e.GetPosition(this).X); };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
    }
    void Move(double x) {
        double f = Math.Max(0, Math.Min(1, (x - 7) / (Width - 14)));
        int nv = (int)Math.Round(min + f * (max - min));
        if (nv != val) { val = nv; Paint(); set(nv); }
    }
    void Paint() { double w = (Width - 14) * (val - min) / Math.Max(1, max - min); thumb.Margin = new Thickness(w, 0, 0, 0); fill.Width = w + 7; }
}

// ---------------- control panel ----------------
class ControlPanel : Window {
    static ControlPanel inst;
    StackPanel nav, body; ScrollViewer sv; string page = "Appearance", lastShown;
    static readonly (string Name, string Glyph)[] Pages = {
        ("Appearance", "\uE771"), ("Dock", "\uE718"), ("Top bar", "\uE700"), ("Widgets", "\uE7C4"), ("Desktop", "\uE7F4"), ("Power", "\uE945") };

    public static void Open() {
        if (inst == null) {
            var w = inst = new ControlPanel();
            w.Closed += (_, _) => { inst = null; Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Mem.Trim)); };   // give the theme previews' memory back
            if (UI.Fx) { w.Opacity = 0; w.Loaded += (_, _) => w.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))); }
        }
        inst.Show(); inst.Activate();
    }

    ControlPanel() {
        Title = "Hoster"; Width = 980; Height = 680; MinWidth = 860; MinHeight = 540; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); Background = Th.Bg;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome {
            CaptionHeight = 48, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        string seen = Store.C.Theme + "|" + Store.C.Accent;
        Action h = () => {      // only a new theme / accent repaints the whole panel (so sliders keep working while you drag them)
            var k = Store.C.Theme + "|" + Store.C.Accent; if (k == seen) return; seen = k;
            Background = Th.Bg; double off = sv?.VerticalOffset ?? 0; Build();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => sv?.ScrollToVerticalOffset(off)));
        };
        Store.Changed += h; Closed += (_, _) => Store.Changed -= h;
        Build();
    }

    static Brush Br(string hex) { var b = (Brush)new BrushConverter().ConvertFrom(hex); b.Freeze(); return b; }

    // ----- frame: sidebar + page -----
    void Build() {
        nav = new StackPanel { Margin = new Thickness(12, 4, 12, 0) };
        body = new StackPanel { Margin = new Thickness(32, 30, 32, 36) };
        sv = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };

        var logo = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = Th.Accent, Child = UI.Txt("H", Brushes.White, 18, FontWeights.Bold) };
        ((FrameworkElement)logo.Child).HorizontalAlignment = HorizontalAlignment.Center;
        var names = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { UI.Txt("Hoster", Th.Fg, 15, FontWeights.SemiBold), UI.Txt("Control panel", Th.Dim, 11) } };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 7, 12, 16), Children = { logo, names } };

        var side = new Grid(); side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); side.RowDefinitions.Add(new RowDefinition()); side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(nav, 1);
        var foot = UI.Txt("Changes save automatically", Th.Dim, 11); foot.Margin = new Thickness(22, 0, 12, 18); Grid.SetRow(foot, 2);
        side.Children.Add(brand); side.Children.Add(nav); side.Children.Add(foot);
        var sidebar = new Border { Background = Th.Tile, BorderBrush = Th.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = side };

        var ctl = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        ctl.Children.Add(WinBtn("\uE921", () => WindowState = WindowState.Minimized, false)); ctl.Children.Add(WinBtn("\uE8BB", Close, true));
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(ctl, true);

        var root = new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(sv, 1); Grid.SetColumn(ctl, 1);
        root.Children.Add(sidebar); root.Children.Add(sv); root.Children.Add(ctl);
        Content = new Border { BorderBrush = Th.Line, BorderThickness = new Thickness(1), Child = root };
        Go(page);
    }

    UIElement WinBtn(string glyph, Action a, bool close) {
        var b = new Border { Width = 46, Height = 34, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = UI.Glyph(glyph, Th.Dim, 10) };
        b.MouseEnter += (_, _) => b.Background = close ? Br("#E81123") : Th.Tile; b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, _) => a();
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        return b;
    }

    void BuildNav() {
        nav.Children.Clear();
        foreach (var p in Pages) {
            bool sel = p.Name == page; string n = p.Name;
            var chip = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = sel ? new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)) : Th.Tile, Child = UI.Glyph(p.Glyph, sel ? Brushes.White : Th.Dim, 14) };
            var label = UI.Txt(p.Name, sel ? Brushes.White : Th.Fg, 13.5, sel ? FontWeights.SemiBold : FontWeights.Normal); label.Margin = new Thickness(12, 0, 0, 0);
            var b = new Border { Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { chip, label } }, CornerRadius = new CornerRadius(11),
                Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, Background = sel ? Th.Accent : Brushes.Transparent };
            if (!sel) { b.MouseEnter += (_, _) => b.Background = Th.Tile; b.MouseLeave += (_, _) => b.Background = Brushes.Transparent; }
            b.MouseLeftButtonUp += (_, _) => Go(n);
            nav.Children.Add(b);
        }
    }

    // ----- building blocks -----
    UIElement PageHead(string title, string sub) {
        var sp = new StackPanel();
        sp.Children.Add(UI.Txt(title, Th.Fg, 28, FontWeights.SemiBold));
        var s = UI.Txt(sub, Th.Dim, 13); s.Margin = new Thickness(0, 4, 0, 0); sp.Children.Add(s);
        return sp;
    }
    UIElement Group(string title, params UIElement[] rows) {
        rows = rows.Where(r => r != null).ToArray();      // (a row can be left out, e.g. the magnify amount when magnify is off)
        var inner = new StackPanel();
        for (int i = 0; i < rows.Length; i++) { if (i > 0) inner.Children.Add(new Border { Height = 1, Background = Th.Line }); inner.Children.Add(rows[i]); }
        var outer = new StackPanel { Margin = new Thickness(0, 24, 0, 0) };
        if (title != null) { var t = UI.Txt(title, Th.Dim, 12, FontWeights.SemiBold); t.Margin = new Thickness(6, 0, 0, 8); outer.Children.Add(t); }
        outer.Children.Add(new Border { Background = Th.Tile, CornerRadius = new CornerRadius(16), Padding = new Thickness(20, 2, 20, 2), Child = inner });
        return outer;
    }
    UIElement Row(string label, UIElement ctl, string sub = null) {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; left.Children.Add(UI.Txt(label, Th.Fg, 13.5));
        if (sub != null) { var s = UI.Txt(sub, Th.Dim, 11.5); s.TextWrapping = TextWrapping.Wrap; s.Margin = new Thickness(0, 2, 0, 0); left.Children.Add(s); }
        ((FrameworkElement)ctl).VerticalAlignment = VerticalAlignment.Center; ((FrameworkElement)ctl).Margin = new Thickness(16, 0, 0, 0);
        var dp = new DockPanel { Margin = new Thickness(0, 13, 0, 13) }; DockPanel.SetDock(ctl, System.Windows.Controls.Dock.Right); dp.Children.Add(ctl); dp.Children.Add(left);
        return dp;
    }
    UIElement Tog(string label, bool v, Action<bool> set, string sub = null) => Row(label, new Sw(v, x => { set(x); Store.Save(); }), sub);
    UIElement Sld(string label, int min, int max, int v, Action<int> set) {
        var val = UI.Txt(v.ToString(), Th.Dim, 12); val.Width = 34; val.TextAlignment = TextAlignment.Right; val.Margin = new Thickness(10, 0, 0, 0);
        var s = new FlatSlider(min, max, v, x => { val.Text = x.ToString(); set(x); Store.Save(); });
        return Row(label, new StackPanel { Orientation = Orientation.Horizontal, Children = { s, val } });
    }
    Border Btn(string text, Action a, bool accent = false) => UI.Click(UI.Txt(text, accent ? Brushes.White : Th.Fg, 12.5), a, accent ? Th.Accent : Th.Tile, 16, 7);

    // ----- theme previews: a tiny desktop (top bar, window, widgets, dock) drawn in that theme's colours -----
    static Border Blk(double w, double h, Brush f, double r = 2, double op = 1) => new Border { Width = w, Height = h, Background = f, CornerRadius = new CornerRadius(r), Opacity = op };

    static UIElement Preview(Pal p, double w, double h, bool big, string accent, double rad) {
        var cv = new Canvas { Width = w, Height = h, Clip = new RectangleGeometry(new Rect(0, 0, w, h), rad, rad) };
        cv.Background = new LinearGradientBrush(((SolidColorBrush)Br(p.W1)).Color, ((SolidColorBrush)Br(p.W2)).Color, 55);
        Brush acc = Br(accent), card = Br(p.Bg), line = Br(p.Line), fg = Br(p.Fg), dim = Br(p.Dim);
        void Put(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); cv.Children.Add(e); }
        double u = h / 100.0, wr = Math.Max(3, u * 5), lh = Math.Max(2, u * 2.6);

        // top bar pill
        double bw = w * 0.38, bh = Math.Max(6, u * 8);
        var bar = new Border { Width = bw, Height = bh, Background = Br(p.Glass), BorderBrush = line, BorderThickness = new Thickness(1, 0, 1, 1), CornerRadius = new CornerRadius(0, 0, bh / 2, bh / 2) };
        var br = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        br.Children.Add(new Shp.Ellipse { Width = bh * 0.34, Height = bh * 0.34, Fill = acc, Margin = new Thickness(0, 0, bh * 0.3, 0) });
        if (big) br.Children.Add(UI.Txt("Mon 23 Nov   9:41", fg, bh * 0.48, FontWeights.SemiBold)); else br.Children.Add(Blk(bw * 0.4, Math.Max(2, bh * 0.22), fg, 1, 0.8));
        bar.Child = br; Put(bar, (w - bw) / 2, 0);

        // window
        double ww = w * 0.46, wh = u * 44, pad = ww * 0.09;
        var win = new Border { Width = ww, Height = wh, Background = card, BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(wr) };
        var wc = new Canvas();
        void Ln(double x, double y, double lw, Brush f, double op) { var b = Blk(lw, lh, f, lh / 2, op); Canvas.SetLeft(b, x); Canvas.SetTop(b, y); wc.Children.Add(b); }
        Ln(pad, wh * 0.14, ww * 0.42, fg, 0.9); Ln(pad, wh * 0.14 + lh * 2.2, ww * 0.7, dim, 0.7); Ln(pad, wh * 0.14 + lh * 4.4, ww * 0.55, dim, 0.7);
        var chip = Blk(ww * 0.28, Math.Max(4, u * 7), acc, Math.Max(2, u * 3.5)); Canvas.SetLeft(chip, pad); Canvas.SetTop(chip, wh * 0.64); wc.Children.Add(chip);
        win.Child = wc; Put(win, w * 0.08, u * 24);

        // clock widget
        double cw = w * 0.28, ch = u * 26;
        var clk = new Border { Width = cw, Height = ch, Background = card, BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(wr) };
        if (big) { var t = UI.Txt("9:41", fg, ch * 0.4, FontWeights.SemiBold); t.HorizontalAlignment = HorizontalAlignment.Center; clk.Child = t; }
        else {
            var cs = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var l2 = Blk(cw * 0.3, lh * 0.8, dim, 2, 0.7); l2.Margin = new Thickness(0, lh * 0.8, 0, 0);
            cs.Children.Add(Blk(cw * 0.5, lh * 1.4, fg, 2, 0.9)); cs.Children.Add(l2); clk.Child = cs;
        }
        Put(clk, w * 0.64, u * 22);

        // player widget
        double ph = u * 16;
        var pl = new Border { Width = cw, Height = ph, Background = card, BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(wr) };
        var pr = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(cw * 0.1, 0, 0, 0) };
        pr.Children.Add(new Shp.Ellipse { Width = ph * 0.45, Height = ph * 0.45, Fill = acc, Margin = new Thickness(0, 0, cw * 0.08, 0) }); pr.Children.Add(Blk(cw * 0.4, lh, fg, lh / 2, 0.8));
        pl.Child = pr; Put(pl, w * 0.64, u * 22 + ch + u * 4);

        // dock
        double dh = Math.Max(10, u * 15), dw = w * 0.56;
        var dock = new Border { Width = dw, Height = dh, Background = Br(p.Glass), BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(dh / 2) };
        var dr = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 6; i++) { var ic = Blk(dh * 0.5, dh * 0.5, i == 0 ? acc : dim, dh * 0.15, i == 0 ? 1 : 0.55); ic.Margin = new Thickness(dh * 0.11, 0, dh * 0.11, 0); dr.Children.Add(ic); }
        dock.Child = dr; Put(dock, (w - dw) / 2, h - dh - u * 5);
        return cv;
    }

    UIElement ThemeCard(Pal p) {
        bool sel = p.Id == Store.C.Theme;
        var frame = new Border { CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(2), BorderBrush = sel ? Th.Accent : Brushes.Transparent, Padding = new Thickness(2),
            Child = Preview(p, 104, 66, false, p.Accent, 8), HorizontalAlignment = HorizontalAlignment.Left };
        var g = new Grid(); g.Children.Add(frame);
        if (sel) g.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Th.Accent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 7, 7, 0), Child = UI.Glyph("\uE73E", Brushes.White, 9) });
        var nm = UI.Txt(p.Name, Th.Fg, 12.5, FontWeights.SemiBold); nm.Margin = new Thickness(4, 6, 0, 0);
        var sb = UI.Txt(p.Sub, Th.Dim, 10.5); sb.Margin = new Thickness(4, 1, 0, 0); sb.TextTrimming = TextTrimming.CharacterEllipsis; sb.MaxWidth = 108;
        var c = UI.Click(new StackPanel { Children = { g, nm, sb } }, () => { var cfg = Store.C; cfg.Theme = p.Id; cfg.Accent = p.Accent; Store.Save(); Wall.Sync(); }, null, 4, 4);
        c.Margin = new Thickness(0, 0, 4, 4); return c;
    }

    // ----- matrix display settings -----
    UIElement MatrixGroup(string p) {
        var c = Store.C;
        var modes = new WrapPanel { MaxWidth = 400 };
        foreach (var m in new[] { "Text", "Clock", "Date", "Battery", "CPU", "Audio" }) {
            var mm = m; var b = Btn(mm, () => { c.MxMode = mm; Store.Save(); Go(p); }, mm == c.MxMode); b.Margin = new Thickness(6, 0, 0, 6); modes.Children.Add(b);
        }
        var tb = new TextBox { Text = c.MxText, Width = 220, MaxLength = 80, FontSize = 13, Background = Th.Tile, Foreground = Th.Fg, CaretBrush = Th.Fg, BorderThickness = new Thickness(0), Padding = new Thickness(8, 6, 8, 6) };
        tb.TextChanged += (_, _) => { c.MxText = tb.Text; Store.Save(); };
        var sw = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var hex in new[] { "accent", "#FFFFFF", "#3DDC84", "#FFB000", "#4F8CFF", "#FF3DB8" }) {
            var hh = hex;
            var e = new Shp.Ellipse { Width = 22, Height = 22, Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, Fill = hh == "accent" ? Th.Accent : Br(hh), Stroke = c.MxColor == hh ? Th.Fg : Brushes.Transparent, StrokeThickness = 2 };
            e.MouseLeftButtonUp += (_, _) => { c.MxColor = hh; Store.Save(); Go(p); }; sw.Children.Add(e);
        }
        bool text = c.MxMode == "Text";
        return Group("Matrix display",
            Row("Shows", modes, "Text and Date and Clock use the dot font; Audio draws live bars from whatever is playing"),
            text ? Row("Text", tb, "Upper case only. Letters, numbers and basic punctuation") : null,
            text ? Tog("Scroll long text", c.MxScroll, v => c.MxScroll = v) : null,
            text && c.MxScroll ? Sld("Scroll speed", 1, 10, c.MxSpeed, v => c.MxSpeed = v) : null,
            Sld("Width (dots)", 8, 64, c.MxCols, v => c.MxCols = v),
            Sld("Height (dots)", 7, 20, c.MxRows, v => c.MxRows = v),
            Sld("Dot size", 3, 12, c.MxDot, v => c.MxDot = v),
            Row("Colour", sw),
            Tog("Show unlit dots", c.MxGhost, v => c.MxGhost = v),
            Tog("Square dots", c.MxSquare, v => c.MxSquare = v));
    }

    // ----- pages -----
    void Go(string p) {
        bool newPage = lastShown != p; lastShown = p; page = p; var c = Store.C; BuildNav(); body.Children.Clear();
        switch (p) {
            case "Appearance": {
                var cur = Th.Get(c.Theme);
                body.Children.Add(PageHead("Appearance", "Pick a theme. It restyles the dock, top bar, widgets, menus and this panel."));

                // big live preview of the current theme + accent colours
                var hero = new Grid(); hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); hero.ColumnDefinitions.Add(new ColumnDefinition());
                var pv = new Border { CornerRadius = new CornerRadius(14), Child = Preview(cur, 300, 188, true, c.Accent, 14),
                    Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black } };
                var info = new StackPanel { Margin = new Thickness(26, 6, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(UI.Txt(cur.Name, Th.Fg, 22, FontWeights.SemiBold));
                var sub = UI.Txt(cur.Sub + (cur.Light ? " · light" : " · dark"), Th.Dim, 12.5); sub.Margin = new Thickness(0, 2, 0, 18); info.Children.Add(sub);
                var at = UI.Txt("Accent colour", Th.Dim, 12, FontWeights.SemiBold); at.Margin = new Thickness(0, 0, 0, 9); info.Children.Add(at);
                var sw = new WrapPanel();
                foreach (var a in new[] { cur.Accent, "#E5303A", "#FF7A1A", "#F5C518", "#3DDC84", "#4F8CFF", "#B66BFF" }.Distinct(StringComparer.OrdinalIgnoreCase)) {
                    var aa = a; bool on = string.Equals(a, c.Accent, StringComparison.OrdinalIgnoreCase);
                    var e = new Shp.Ellipse { Width = 26, Height = 26, Fill = Br(a), Stroke = on ? Th.Fg : Brushes.Transparent, StrokeThickness = 2, Cursor = Cursors.Hand };
                    var ring = new Border { Padding = new Thickness(3), Margin = new Thickness(0, 0, 6, 6), Child = e };
                    ring.MouseLeftButtonUp += (_, _) => { c.Accent = aa; Store.Save(); Wall.Sync(); };
                    sw.Children.Add(ring);
                }
                info.Children.Add(sw);
                var reset = Btn("Use the theme's own accent", () => { c.Accent = cur.Accent; Store.Save(); Wall.Sync(); }); reset.HorizontalAlignment = HorizontalAlignment.Left; reset.Margin = new Thickness(0, 12, 0, 0);
                if (!string.Equals(c.Accent, cur.Accent, StringComparison.OrdinalIgnoreCase)) info.Children.Add(reset);
                Grid.SetColumn(info, 1); hero.Children.Add(pv); hero.Children.Add(info);
                body.Children.Add(new Border { Margin = new Thickness(0, 24, 0, 0), Background = Th.Tile, CornerRadius = new CornerRadius(18), Padding = new Thickness(18), Child = hero });

                // the theme gallery
                foreach (var lightSet in new[] { false, true }) {
                    var gal = new WrapPanel(); foreach (var t in Th.All.Where(x => x.Light == lightSet)) gal.Children.Add(ThemeCard(t));
                    var gh = UI.Txt(lightSet ? "Light themes" : "Dark themes", Th.Dim, 12, FontWeights.SemiBold); gh.Margin = new Thickness(6, 26, 0, 8); body.Children.Add(gh);
                    body.Children.Add(new Border { Background = Th.Tile, CornerRadius = new CornerRadius(18), Padding = new Thickness(14, 14, 10, 10), Child = gal });
                }

                body.Children.Add(Group("Windows", Tog("Match the Windows Start menu to the theme", c.SyncWindows, v => c.SyncWindows = v, "Light or dark follows the theme, and the coloured themes tint it. Turning this off puts your old Windows colours back.")));
                body.Children.Add(Group("Performance", Tog("Animations", c.Anim, v => c.Anim = v, "Menus fade and slide, switches glide and the dock eases when you hover. Turn off to save a little CPU and GPU on a slow PC")));
                break; }
            case "Dock":
                body.Children.Add(PageHead("Dock", "How the dock looks and behaves."));
                body.Children.Add(Group("Icons",
                    Sld("Icon size", 32, 72, c.IconSize, v => c.IconSize = v),
                    Sld("Corner roundness", 4, 30, c.Radius, v => c.Radius = v),
                    Tog("Magnify on hover", c.Magnify, v => { c.Magnify = v; Dispatcher.BeginInvoke(new Action(() => Go(p))); }),   // redraw so the amount slider shows / hides
                    c.Magnify ? Sld("Magnify amount (%)", 10, 100, c.MagnifyAmount, v => c.MagnifyAmount = v) : null,
                    Tog("Window and tab list on hover", c.HoverList, v => c.HoverList = v, "Hover a running app to see its windows; browsers also list their tabs")));
                var pins = c.Pinned.ToList().Select(pp => Row(pp.Name, Btn("Remove", () => { c.Pinned.Remove(pp); Store.Save(); Go(p); }), pp.Target)).ToArray();
                body.Children.Add(Group("Pinned apps (or right-click an icon in the dock to unpin)", pins.Length > 0 ? pins : new[] { Row("Nothing pinned yet", new Border()) }));
                var add = Btn("Pin an app…", () => { Store.PinApp(); Go(p); }, true); add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 14, 0, 0);
                body.Children.Add(add); break;
            case "Top bar":
                body.Children.Add(PageHead("Top bar", "The pill that slides in when you push the cursor to the top of the screen."));
                body.Children.Add(Group("Modules",
                    Tog("Now playing (Spotify / media)", c.Media, v => c.Media = v, "Click it in the bar for the player; the clock opens the calendar"),
                    Tog("Audio visualiser in the player", c.Visualizer, v => c.Visualizer = v, "Reacts to whatever your PC is playing"),
                    Tog("Wi-Fi and Bluetooth menu", c.Network, v => c.Network = v, "Wi-Fi / Bluetooth icons at the right of the bar"),
                    Tog("CPU and memory", c.Stats, v => c.Stats = v),
                    Tog("Battery", c.BatteryBar, v => c.BatteryBar = v, "Battery percentage in the bar (only shows on PCs with a battery)")));
                body.Children.Add(Group("Clock",
                    Tog("Date", c.Date, v => c.Date = v),
                    Tog("24-hour clock", c.Clock24, v => c.Clock24 = v),
                    Tog("Show seconds", c.Seconds, v => c.Seconds = v)));
                body.Children.Add(Group("Behaviour",
                    Tog("Hide dock and bar in full screen", c.HideInFullscreen, v => c.HideInFullscreen = v, "Games, fullscreen video, F11 browser, slideshows. Desktop widgets also pause behind maximised windows")));
                break;
            case "Widgets":
                body.Children.Add(PageHead("Widgets", "Small cards that sit on your desktop, under your apps."));
                body.Children.Add(Group(null, Row("Layout", Btn("Edit layout", () => WidgetHost.Edit(true), true), "Drag the widgets around, then click done")));
                foreach (var wc in c.Widgets.ToList()) {
                    var wcc = wc;
                    body.Children.Add(Group(WidgetHost.Label(wc.Kind), Tog("Show on desktop", wcc.On, v => wcc.On = v), Sld("Size (%)", 60, 200, wcc.Size, v => wcc.Size = v)));
                    if (wc.Kind == "Matrix") body.Children.Add(MatrixGroup(p));
                }
                body.Children.Add(Group(null, Row("Reset", Btn("Reset positions and sizes", WidgetHost.Reset))));
                break;
            case "Power": {
                body.Children.Add(PageHead("Power", "One switch for battery life, one for speed. Each sets Windows' Energy saver, a power plan and the ASUS fan mode together. The same switches are on the bar's battery button."));
                body.Children.Add(Group("Modes",
                    Tog("Energy saver mode", c.PowerMode == "saver", v => { Pwr.Set(v ? "saver" : "off"); Dispatcher.BeginInvoke(new Action(() => Go(p))); }, "Energy saver on, your battery-saving power plan, ASUS fan on Whisper"),
                    Tog("Performance mode", c.PowerMode == "perf", v => { Pwr.Set(v ? "perf" : "off"); Dispatcher.BeginInvoke(new Action(() => Go(p))); }, "Energy saver off, your ultimate performance power plan, ASUS fan on Performance"),
                    Row("Energy saver right now", UI.Txt(Pwr.EnergySaver(), Th.Dim, 12.5), "Windows only runs Energy saver on battery, so unplugged is where mode 1 shows")));

                var plans = Pwr.Plans();
                UIElement Picker(string cur, Action<string> pick) {
                    var wp = new WrapPanel { MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Right };
                    var auto = Btn("Auto", () => { pick(""); Store.Save(); Go(p); }, string.IsNullOrEmpty(cur)); auto.Margin = new Thickness(0, 0, 6, 6); wp.Children.Add(auto);
                    foreach (var pl in plans) { var g = pl.guid; var b = Btn(pl.name, () => { pick(g); Store.Save(); Go(p); }, string.Equals(cur, g, StringComparison.OrdinalIgnoreCase)); b.Margin = new Thickness(0, 0, 6, 6); wp.Children.Add(b); }
                    return wp;
                }
                body.Children.Add(Group("Power plans",
                    Row("Energy saver mode uses", Picker(c.SaverPlan, v => c.SaverPlan = v), "Auto picks a plan called Power saver / battery saver"),
                    Row("Performance mode uses", Picker(c.PerfPlan, v => c.PerfPlan = v), "Auto picks your Ultimate Performance plan, or High performance")));

                var fanBtns = new StackPanel { Orientation = Orientation.Horizontal };
                var fp = Btn("Performance", () => { c.FanPerf = 0; Store.Save(); Go(p); }, c.FanPerf == 0); var ft = Btn("Turbo", () => { c.FanPerf = 1; Store.Save(); Go(p); }, c.FanPerf == 1); ft.Margin = new Thickness(6, 0, 0, 0);
                fanBtns.Children.Add(fp); fanBtns.Children.Add(ft);
                body.Children.Add(Group("ASUS fan mode",
                    Tog("Change the fan mode too", c.FanControl, v => c.FanControl = v, Asus.Note),
                    Row("Fans in Performance mode", fanBtns, "Performance is the normal fast mode, Turbo spins the fans harder"),
                    Row("Fans in Energy saver mode", UI.Txt("Whisper", Th.Dim, 12.5), "Quiet and cool, lowest power")));
                break;
            }
            case "Desktop":
                body.Children.Add(PageHead("Desktop", "The taskbar, Start menu, search and wallpaper."));
                var keys = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var k in Spotlight.Keys) { var kk = k; var kb = Btn(kk, () => { c.SearchKey = kk; Store.Save(); Go(p); }, kk == c.SearchKey); kb.Margin = new Thickness(6, 0, 0, 0); keys.Children.Add(kb); }
                body.Children.Add(Group("Search",
                    Tog("Spotlight search", c.SearchOn, v => { c.SearchOn = v; Dispatcher.BeginInvoke(new Action(() => Go(p))); }, "One box for apps, open windows, files, sums, system commands and the web"),
                    c.SearchOn ? Row("Shortcut", keys, Spotlight.Ok ? "Press it anywhere to open the search" : "Another app is already using that shortcut. Pick a different one.") : null));
                body.Children.Add(Group("Taskbar and Start",
                    Tog("Hide the Windows taskbar", c.HideTaskbar, v => { c.HideTaskbar = v; N.Taskbar(!v); }, "Replaced by this dock and top bar"),
                    Tog("Hoster Start menu", c.OwnStart, v => c.OwnStart = v, "Opens from the Win key or the first dock button, in your theme"),
                    Tog("Start with Windows", Program.Autostart, v => Program.Autostart = v, "Launches Hoster when you sign in")));
                body.Children.Add(Group("Wallpaper",
                    Tog("Match wallpaper to theme", c.ThemeWall, v => c.ThemeWall = v, "Picking a theme or accent sets a wallpaper drawn in the same colours. Turn off to keep your own picture"),
                    Row("Theme wallpaper", Btn("Apply now", () => Wall.Sync(true)), "Sets the wallpaper for the current theme and accent"),
                    Row("Wallpaper", Btn("Choose image…", () => { var d = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp" }; if (d.ShowDialog() == true) N.Wallpaper(d.FileName); })),
                    Tog("Live wallpaper", c.LiveWallpaperOn, v => c.LiveWallpaperOn = v, "A looping, muted video behind your icons. Pauses while a window covers the desktop"),
                    Row("Live wallpaper video", Btn("Choose video…", () => { if (Live.Pick()) Go(p); }), string.IsNullOrEmpty(c.LiveWallpaper) ? "No video chosen (MP4 works best)" : c.LiveWallpaper),
                    Tog("Bottom visualiser", c.BottomViz, v => c.BottomViz = v, "Soft audio waves along the bottom of the desktop while music plays")));
                body.Children.Add(Group("Maintenance",
                    Row("Taskbar stuck hidden?", Btn("Show Windows taskbar", Program.ShowTaskbar)),
                    Row("Stop Hoster", Btn("Quit and restore taskbar", Program.Quit, true))));
                break;
        }
        if (!newPage || !UI.Fx) return;                         // switching pages fades and slides in; a toggle that redraws the same page doesn't flash
        var tr = new TranslateTransform(0, 12); body.RenderTransform = tr;
        body.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = UI.Ease });
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
        Loaded += (_, _) => { Place(); ToBottom(); WidgetHost.Restack(); };
    }
    public string Kind => cfg.Kind;
    public bool AutoPlaced => cfg.X < 0 || cfg.Y < 0;
    public void Replace() { UpdateLayout(); Place(); }
    protected abstract UIElement Body();
    public virtual void Tick() { }
    public void Init() { Build(); Show(); }
    public void Rebuild() { Build(); if (cfg.X < 0) { UpdateLayout(); Place(); WidgetHost.Restack(); } }
    public void ToBottom() { if (!Topmost && IsLoaded) N.ToDesktop(Handle); }
    public void Reveal() {            // un-hide under the apps (it used to be shown first and sunk a moment later, which could flash on top)
        if (!IsLoaded) return;
        if (!IsVisible && !Topmost) N.ShowAtDesktop(Handle);
        Visibility = Visibility.Visible; ToBottom();
    }

    protected override void Place() {
        if (dragging) return;
        var s = WF.Screen.PrimaryScreen.Bounds; double sc = VisualTreeHelper.GetDpi(this).DpiScaleX; double sw = s.Width / sc, sh = s.Height / sc;
        double w = ActualWidth > 0 ? ActualWidth : 300, x = cfg.X, y = cfg.Y;
        if (x < 0 || y < 0) { var a = WidgetHost.Auto(cfg.Kind, sw, sh, w, ActualHeight > 0 ? ActualHeight : 300); x = a.Item1; y = a.Item2; }
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
    public static readonly string[] Kinds = { "Clock", "Media", "System", "Calendar", "Battery", "Matrix" };
    public static string Label(string k) => k switch { "Media" => "Music player", "System" => "System monitor", "Matrix" => "Matrix display", _ => k };
    static Widget Make(WCfg c) {
        switch (c.Kind) {
            case "Clock": return new ClockWidget(c);
            case "Media": return new MediaWidget(c);
            case "System": return new SystemWidget(c);
            case "Calendar": return new CalendarWidget(c);
            case "Battery": return new BatteryWidget(c);
            case "Matrix": return new MatrixWidget(c);
            default: return null;
        }
    }
    // Default spots until you drag a widget somewhere: clock + system stacked on the left, player + calendar stacked on the right.
    // Each one sits under the one above it using that widget's real height, so a taller player (song playing) never covers the calendar.
    static double Below(string k, double fallback) {
        if (open.TryGetValue(k, out var w) && w.AutoPlaced && w.IsLoaded && !double.IsNaN(w.Top) && w.ActualHeight > 0) return w.Top + w.ActualHeight;
        return fallback;
    }
    public static (double, double) Auto(string kind, double sw, double sh, double w, double h) {
        switch (kind) {
            case "Clock": return (28, 28);
            case "System": return (28, Below("Clock", 28));
            case "Battery": return (28, Below("System", Below("Clock", 28)));
            case "Matrix": return (28, Below("Battery", Below("System", Below("Clock", 28))));
            case "Media": return (sw - w - 28, 28);
            default: {
                double y = Below("Media", 28);
                // not enough room under the player: sit beside it instead
                if (y > 28 && y + h > sh - 12 && open.TryGetValue("Media", out var m)) return (sw - w - 28 - m.ActualWidth, 28);
                return (sw - w - 28, y);
            }
        }
    }
    public static void Sink() { foreach (var w in open.Values.ToList()) w.ToBottom(); }   // widgets back above anything else that sits on the desktop
    public static void Restack() { foreach (var k in Kinds) if (open.TryGetValue(k, out var w) && w.AutoPlaced) w.Replace(); }
    public static void Start() {
        Sync(); Store.Changed += OnChanged;
        // hidden windows aren't drawn, so the widgets cost nothing behind a full-screen app
        Action hide = () => {
            bool idle = Full.On || Full.Covered;
            foreach (var w in open.Values.ToList()) { if (idle) w.Visibility = Visibility.Hidden; else w.Reveal(); }
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
        foreach (var k in Kinds) if (!Store.C.Widgets.Any(x => x.Kind == k)) Store.C.Widgets.Add(new WCfg { Kind = k, On = !(k == "Battery" && !Batt.Has) });   // no battery widget on PCs without one
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

// ---------------- a wallpaper drawn from each theme's colours, so the desktop matches the dock, bar and widgets ----------------
// Drawn once per theme + accent at your screen's shape, saved as a PNG in %AppData%\Hoster\wallpapers and reused after that.
static class Wall {
    static string last, lastFile;
    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hoster", "wallpapers");
    static Color Col(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    static int Seed(string s) { int h = 17; foreach (var ch in s) h = unchecked(h * 31 + ch); return h; }   // stable, so a theme always gets the same picture

    public static void Sync(bool force = false) {
        var c = Store.C; if (!force && !c.ThemeWall) return;
        var p = Th.Get(c.Theme); string key = p.Id + "|" + c.Accent;
        if (!force && key == last) return;
        try { var f = Make(p, c.Accent); var old = lastFile; last = key; lastFile = f; if (UI.Fx) WallSlide.Run(f, old); else N.Wallpaper(f); } catch { }
    }

    static string Make(Pal p, string accent) {
        Directory.CreateDirectory(Dir);
        string file = Path.Combine(Dir, p.Id + "-" + accent.TrimStart('#') + ".png");
        if (File.Exists(file)) return file;
        var b = WF.Screen.PrimaryScreen.Bounds;
        int W = Math.Min(2560, b.Width), H = (int)Math.Round(W * (double)b.Height / b.Width);
        var rnd = new Random(Seed(p.Id));
        Color c1 = Col(p.W1), c2 = Col(p.W2), ac; try { ac = Col(accent); } catch { ac = Col(p.Accent); }
        bool light = p.Light;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) {
            var full = new Rect(0, 0, W, H);
            dc.DrawRectangle(new LinearGradientBrush(c1, c2, 55), null, full);
            void Glow(double cx, double cy, double r, Color col, byte a0) {
                var pt = new Point(cx, cy);
                dc.DrawRectangle(new RadialGradientBrush(A(col, a0), A(col, 0)) { MappingMode = BrushMappingMode.Absolute, Center = pt, GradientOrigin = pt, RadiusX = r, RadiusY = r }, null, full);
            }
            Glow(W * (0.62 + rnd.NextDouble() * 0.3), H * (0.05 + rnd.NextDouble() * 0.3), W * 0.55, ac, light ? (byte)0x70 : (byte)0x80);
            Glow(W * (0.05 + rnd.NextDouble() * 0.25), H * (0.65 + rnd.NextDouble() * 0.3), W * 0.45, ac, 0x40);
            Glow(W * 0.5, H * 0.5, W * 0.7, light ? Colors.White : c1, 0x30);
            for (int i = 0; i < 3; i++) {                                   // soft layered waves along the bottom
                double y = H * (0.66 + i * 0.11 + rnd.NextDouble() * 0.04), amp = H * (0.07 + rnd.NextDouble() * 0.05), ph = rnd.NextDouble();
                var g = new StreamGeometry();
                using (var sc = g.Open()) {
                    sc.BeginFigure(new Point(0, y), true, true);
                    sc.BezierTo(new Point(W * 0.3, y - amp * (1 + ph)), new Point(W * 0.6, y + amp * (2 - ph)), new Point(W, y - amp * 0.4), true, false);
                    sc.LineTo(new Point(W, H), true, false); sc.LineTo(new Point(0, H), true, false);
                }
                g.Freeze();
                dc.DrawGeometry(new LinearGradientBrush(A(ac, (byte)(0x2E - i * 8)), A(ac, 0), 90), null, g);
            }
            if (!light) {                                                   // darken the corners so icons and widgets read well
                var mid = new Point(W / 2.0, H / 2.0);
                var vg = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = mid, GradientOrigin = mid, RadiusX = W * 0.75, RadiusY = H * 0.9 };
                vg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55)); vg.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 1));
                dc.DrawRectangle(vg, null, full);
            }
        }
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32); rtb.Render(dv);
        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(file)) enc.Save(fs);
        try { foreach (var old in Directory.GetFiles(Dir, p.Id + "-*.png")) if (old != file) File.Delete(old); } catch { }   // keep one picture per theme
        return file;
    }
}

// ---------------- wallpaper change animation: the new picture whooshes in from the top-right to the bottom-left ----------------
// The slide lives INSIDE the desktop's own wallpaper layer (under the icons, above the wallpaper), like a live wallpaper does, so it is part of
// the desktop and can't be drawn over any app, widget, dock or menu. It shows the old picture, slides the new one over it as a motion-blurred
// streak that sharpens as it lands, then sets the real wallpaper and goes away. If the desktop layer can't be found it just changes the wallpaper.
class WallSlide : Window {
    static WallSlide cur;
    static readonly QuinticEase Whoosh = MakeWhoosh();
    static QuinticEase MakeWhoosh() { var e = new QuinticEase { EasingMode = EasingMode.EaseOut }; e.Freeze(); return e; }
    [DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "SystemParametersInfo")] static extern int SpiGet(int a, int b, StringBuilder sb, int ini);

    readonly string file; readonly DispatcherTimer end = new() { Interval = TimeSpan.FromMilliseconds(900) };
    bool done;

    static void Instant(string file) => Task.Run(() => N.Wallpaper(file));

    public static void Run(string file, string old) {
        cur?.Abort(); cur = null;
        if (!N.WallpaperLayer(out _, out _)) { Instant(file); return; }
        WallSlide w = null;
        try { w = new WallSlide(file, old); cur = w; w.Show(); }
        catch { cur = null; try { w?.Close(); } catch { } Instant(file); }
    }
    void Abort() { end.Stop(); done = true; try { Close(); } catch { } }

    // the picture that is on the desktop right now
    static BitmapImage Current(string old, int px) {
        var tries = new List<string>();
        if (!string.IsNullOrEmpty(old)) tries.Add(old);
        var sb = new StringBuilder(520); if (SpiGet(0x73, sb.Capacity, sb, 0) != 0 && sb.Length > 0) tries.Add(sb.ToString());
        tries.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper"));
        foreach (var t in tries) { var i = Load(t, px); if (i != null) return i; }
        return null;
    }
    static BitmapImage Load(string path, int px) {
        try {
            if (!File.Exists(path)) return null;
            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.DecodePixelWidth = px; bi.EndInit(); bi.Freeze(); return bi;
        } catch { return null; }
    }

    // the picture blurred along the direction it travels, made by averaging shifted copies (drawn once, a few ms)
    static BitmapSource Streak(BitmapSource src) {
        int w = src.PixelWidth, h = src.PixelHeight, n = 9; double dx = w * 0.012;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int i = 0; i < n; i++) {
                double o = (i - (n - 1) / 2.0) * dx;
                dc.PushOpacity(1.0 / (i + 1));                                  // 1, 1/2, 1/3 ... stacks into an even average
                dc.DrawImage(src, new Rect(o, -o * 0.35, w, h));
                dc.Pop();
            }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32); rtb.Render(dv); rtb.Freeze(); return rtb;
    }

    WallSlide(string file, string old) {
        this.file = file;
        var b = WF.Screen.PrimaryScreen.Bounds; int px = Math.Min(2560, b.Width);
        var cur0 = Current(old, px); if (cur0 == null) throw new InvalidOperationException("no current wallpaper");   // can't show the old one: skip the slide
        var nw = Load(file, px); if (nw == null) throw new InvalidOperationException("no new wallpaper");
        var small = Load(file, 1280);

        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = false;
        Background = Brushes.Black; WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -20000; Top = -20000; Width = SystemParameters.PrimaryScreenWidth; Height = SystemParameters.PrimaryScreenHeight;   // parked off screen until it is inside the desktop
        SourceInitialized += (_, _) => {
            var h = new WindowInteropHelper(this).Handle; N.NoActivate(h);
            if (!N.Embed(h, b.Left, b.Top, b.Width, b.Height)) { Width = 1; Height = 1; failed = true; }
        };

        var oldImg = new Image { Source = cur0, Stretch = Stretch.UniformToFill };
        var tr = new TranslateTransform(20000, 0);
        var sharp = new Image { Source = nw, Stretch = Stretch.UniformToFill, CacheMode = new BitmapCache() };
        var blur = new Image { Source = Streak(small ?? nw), Stretch = Stretch.UniformToFill, Opacity = 0, CacheMode = new BitmapCache() };
        var layer = new Grid { ClipToBounds = true, IsHitTestVisible = false, RenderTransform = tr, Children = { sharp, blur } };
        Content = new Grid { ClipToBounds = true, Children = { oldImg, layer } };

        end.Tick += (_, _) => { end.Stop(); if (ReferenceEquals(cur, this)) cur = null; Close(); };
        Closed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Mem.Trim));   // give the pictures' memory back
        Loaded += (_, _) => {
            if (failed) { Instant(file); Close(); return; }
            double W = ActualWidth, H = ActualHeight; var d = TimeSpan.FromMilliseconds(750);
            tr.X = W; tr.Y = -H * 0.35;
            tr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(W, 0, d) { EasingFunction = Whoosh });
            blur.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, d) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });   // sharpens as it lands
            var ya = new DoubleAnimation(-H * 0.35, 0, d) { EasingFunction = Whoosh };
            ya.Completed += (_, _) => {
                if (done) return;
                Instant(file);                                                    // the real wallpaper changes underneath
                end.Start();                                                      // the slide stays a moment so Windows' own fade isn't seen
            };
            tr.BeginAnimation(TranslateTransform.YProperty, ya);
        };
    }
    bool failed;
}

// ---------------- Performance mode / Energy saver mode: Windows Energy saver + a power plan + the ASUS fan mode ----------------
static class Pwr {
    const string SubEs = "de830923-a562-41af-a086-e3a2c6bad2da", EsThresh = "e69653ca-cf7f-4f05-aa73-cb833fa90ad4";   // Energy saver: "turn on automatically at"
    static readonly Guid Efficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a"), BestPerf = new("ded574b5-45a0-4f42-8737-46345c09c238");
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveOverlayScheme(Guid g);
    static bool overlayUsed;

    public static string Mode => Store.C.PowerMode;

    static string Pc(string args) {
        try {
            var psi = new ProcessStartInfo("powercfg.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using var pr = Process.Start(psi); var o = pr.StandardOutput.ReadToEnd(); pr.WaitForExit(5000); return o;
        } catch { return ""; }
    }
    public static List<(string guid, string name, bool active)> Plans() {
        var res = new List<(string guid, string name, bool active)>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(Pc("/list"), @"([0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\s+\((.*?)\)(\s*\*)?"))
            res.Add((m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value, m.Groups[3].Success));
        return res;
    }
    static string Active() => Plans().FirstOrDefault(x => x.active).guid ?? "";
    static int Threshold(string plan) {                     // Energy saver turns on at this % battery (the last number printed is the on-battery value)
        var ms = System.Text.RegularExpressions.Regex.Matches(Pc("/query " + plan + " " + SubEs + " " + EsThresh), @"0x[0-9a-fA-F]{8}");
        return ms.Count > 0 ? Convert.ToInt32(ms[ms.Count - 1].Value, 16) : -1;
    }
    static void SetThreshold(string plan, int v) => Pc("/setdcvalueindex " + plan + " " + SubEs + " " + EsThresh + " " + v);
    static void SetPlan(string guid) => Pc("/setactive " + guid);

    static string Pick(string chosen, params string[] words) {
        var all = Plans(); chosen = (chosen ?? "").ToLowerInvariant();
        if (chosen.Length > 0 && all.Any(x => x.guid == chosen)) return chosen;
        foreach (var w in words) { var hit = all.FirstOrDefault(x => x.name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0); if (hit.guid != null) return hit.guid; }
        return null;
    }

    // "Energy saver: on / off" as Windows reports it
    public static string EnergySaver() {
        try {
            return Windows.System.Power.PowerManager.EnergySaverStatus switch {
                Windows.System.Power.EnergySaverStatus.On => "on", Windows.System.Power.EnergySaverStatus.Off => "off",
                Windows.System.Power.EnergySaverStatus.Disabled => "off (disabled in Windows)", _ => "unknown" };
        } catch { return "unknown"; }
    }

    public static void Set(string mode) {
        var c = Store.C; if (mode == c.PowerMode) return;
        bool fromOff = c.PowerMode == "off";
        c.PowerMode = mode; Store.Save();
        Task.Run(() => Apply(mode, fromOff));
    }

    static void Apply(string mode, bool fromOff) {
        var c = Store.C;
        try {
            if (fromOff) { c.PlanOrig = Active(); c.FanOrig = Asus.Get(); }                  // remember what to go back to
            if (!string.IsNullOrEmpty(c.EsGuid)) { if (c.EsVal >= 0) SetThreshold(c.EsGuid, c.EsVal); c.EsGuid = ""; c.EsVal = -1; }   // undo the last mode's Energy saver level
            int fan;
            if (mode == "off") {
                if (!string.IsNullOrEmpty(c.PlanOrig)) SetPlan(c.PlanOrig);
                if (overlayUsed) { try { PowerSetActiveOverlayScheme(Guid.Empty); } catch { } overlayUsed = false; }
                fan = c.FanOrig >= 0 && c.FanOrig <= 2 ? c.FanOrig : 0;
            } else {
                bool saver = mode == "saver"; int level = saver ? 100 : 0;                  // 100 = Energy saver always on (on battery), 0 = never
                string plan = saver ? Pick(c.SaverPlan, "saver", "saving", "eco", "battery") : Pick(c.PerfPlan, "ultimate", "performance");
                if (plan == null) {                                                          // modern PCs hide the old plans: use Windows' own power mode instead
                    try { PowerSetActiveOverlayScheme(saver ? Efficiency : BestPerf); overlayUsed = true; } catch { }
                    plan = Active();
                } else if (overlayUsed) { try { PowerSetActiveOverlayScheme(Guid.Empty); } catch { } overlayUsed = false; }
                if (!string.IsNullOrEmpty(plan)) {
                    int cur = Threshold(plan); if (cur >= 0) { c.EsGuid = plan; c.EsVal = cur; }
                    SetThreshold(plan, level); SetPlan(plan);                                // setting it on the plan then re-applying the plan is what makes Windows act on it
                }
                fan = saver ? 2 : c.FanPerf;                                                 // ASUS: 2 = Whisper (silent), 0 = Performance, 1 = Turbo
            }
            if (c.FanControl) Asus.Set(fan);
        } catch { }
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => Store.Save()));                  // keep what we remembered
    }
}

// ASUS laptops: the Armoury Crate fan / thermal mode, through ASUS' own ATKACPI driver (installed with "ASUS System Control Interface")
static class Asus {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(IntPtr h, uint code, byte[] inBuf, uint inLen, byte[] outBuf, uint outLen, ref uint ret, IntPtr ov);
    const uint DEVS = 0x53564544, DSTS = 0x53545344, THERMAL = 0x00120075;      // "DEVS" set, "DSTS" get, the thermal-policy device
    static readonly IntPtr Bad = new(-1); static IntPtr H = Bad; static int err; static readonly object gate = new();

    static bool Open() {
        lock (gate) {
            if (H != Bad) return true;
            var h = CreateFile(@"\\.\ATKACPI", 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
            if (h == Bad) { err = Marshal.GetLastWin32Error(); return false; }
            H = h; return true;
        }
    }
    static byte[] Call(uint method, byte[] args) {
        var inb = new byte[8 + args.Length]; BitConverter.GetBytes(method).CopyTo(inb, 0); BitConverter.GetBytes((uint)args.Length).CopyTo(inb, 4); args.CopyTo(inb, 8);
        var outb = new byte[16]; uint ret = 0;
        lock (gate) DeviceIoControl(H, 0x0022240C, inb, (uint)inb.Length, outb, (uint)outb.Length, ref ret, IntPtr.Zero);
        return outb;
    }
    public static int Get() { try { return Open() ? BitConverter.ToInt32(Call(DSTS, BitConverter.GetBytes(THERMAL)), 0) - 65536 : -1; } catch { return -1; } }
    public static bool Set(int mode) {
        try {
            if (!Open()) return false;
            var a = new byte[8]; BitConverter.GetBytes(THERMAL).CopyTo(a, 0); BitConverter.GetBytes((uint)mode).CopyTo(a, 4);
            return BitConverter.ToInt32(Call(DEVS, a), 0) == 1;
        } catch { return false; }
    }
    public static string Note { get { Open(); return H != Bad ? "ASUS fan control found" : err == 5 ? "Found, but Windows blocked it: run Hoster as administrator" : "Not found: needs an ASUS laptop with the ASUS System Control Interface driver"; } }
}

// ---------------- the little menu on the bar's battery button ----------------
class PowerFlyout : Flyout {
    public PowerFlyout() : base(270) { Build(); }
    void Build() {
        var col = new StackPanel { Margin = new Thickness(16, 14, 16, 12) };
        col.Children.Add(UI.Txt("Power", Th.Fg, 14, FontWeights.SemiBold));
        var st = UI.Txt(BattUI.Status(), Th.Dim, 11.5); st.Margin = new Thickness(0, 2, 0, 0); st.TextWrapping = TextWrapping.Wrap; col.Children.Add(st);
        var es = UI.Txt("Energy saver is " + Pwr.EnergySaver(), Th.Dim, 11.5); es.Margin = new Thickness(0, 0, 0, 10); col.Children.Add(es);
        col.Children.Add(Line("Energy saver mode", "Battery plan, Energy saver on, whisper fans", Pwr.Mode == "saver", v => Pwr.Set(v ? "saver" : "off")));
        col.Children.Add(Line("Performance mode", "Fast plan, Energy saver off, performance fans", Pwr.Mode == "perf", v => Pwr.Set(v ? "perf" : "off")));
        var more = UI.Click(UI.Txt("Battery settings", Th.Dim, 12), () => Net.Open("ms-settings:batterysaver"), null, 10, 6); more.Margin = new Thickness(-10, 8, 0, 0); more.HorizontalAlignment = HorizontalAlignment.Left;
        col.Children.Add(more);
        card.Child = col;
    }
    UIElement Line(string title, string sub, bool on, Action<bool> set) {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) }; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sb = UI.Txt(sub, Th.Dim, 11); sb.TextWrapping = TextWrapping.Wrap; sb.MaxWidth = 185;
        var tx = new StackPanel { Children = { UI.Txt(title, Th.Fg, 13), sb } };
        var sw = new Sw(on, v => { set(v); Dispatcher.BeginInvoke(new Action(Build)); }); sw.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(sw, 1);
        g.Children.Add(tx); g.Children.Add(sw); return g;
    }
}

// ---------------- keeps Hoster's memory small (matters on integrated graphics, where RAM is shared) ----------------
static class Mem {
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr h);
    public static void Start() {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };                 // once start-up (icons, Start menu list) has settled
        first.Tick += (_, _) => { first.Stop(); Trim(); }; first.Start();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) }; t.Tick += (_, _) => Trim(); t.Start();
    }
    public static void Trim() {
        try { GC.Collect(2, GCCollectionMode.Forced, true, true); using var me = Process.GetCurrentProcess(); EmptyWorkingSet(me.Handle); } catch { }
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
        try { Spotlight.Stop(); } catch { }
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
            // if the folder was moved or renamed, point "Start with Windows" at the exe's current location
            else if (rk?.GetValue("Hoster") is string cur && cur != "\"" + Environment.ProcessPath + "\"") rk.SetValue("Hoster", "\"" + Environment.ProcessPath + "\"");
        } catch { }
        var warm = Store.C.Pinned.Select(x => x.Target).Where(x => !string.IsNullOrEmpty(x)).ToList();
        Task.Run(() => Parallel.ForEach(warm, t => N.Icon(t)));   // pull the dock icons on other cores while the rest of start-up runs
        Th.Apply();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => {
            try { var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hoster"); Directory.CreateDirectory(d); File.AppendAllText(Path.Combine(d, "crash.log"), DateTime.Now + "\r\n" + e.Exception + "\r\n\r\n"); } catch { }
            Cleanup();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
        SystemEvents.SessionEnding += (_, _) => Cleanup();

        if (Store.C.HideTaskbar) {
            N.Taskbar(false);   // wait for Windows to release the taskbar's work area, but no longer than before
            var sw = Stopwatch.StartNew(); var scr = WF.Screen.PrimaryScreen;
            while (sw.ElapsedMilliseconds < 400 && WF.Screen.PrimaryScreen.WorkingArea.Height < scr.Bounds.Height) Thread.Sleep(10);
        }
        Stats.Start(); Batt.Start(); Net.Start(); Mem.Start();
        WinKey.Start(); Spotlight.Start();
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () => { await Task.Delay(3000); await StartMenu.Load(); }));   // PowerShell is heavy, so it waits until the dock is up
        new BarWindow().Show(); new DockWindow().Show();
        WidgetHost.Start();
        Live.Start();
        BottomViz.Start();
        app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Player.Init));
        var fsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) }; fsTimer.Tick += (_, _) => Full.Poll(); fsTimer.Start();
        Store.Changed += Full.Poll;
        var keepHidden = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };   // Explorer sometimes re-shows its taskbar
        keepHidden.Tick += (_, _) => { if (Store.C.HideTaskbar) N.HideTaskbarWindows(true); }; keepHidden.Start();

        tray = new WF.NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath), Text = "Hoster", Visible = true, ContextMenuStrip = new WF.ContextMenuStrip() };
        tray.ContextMenuStrip.Items.Add("Control panel", null, (_, _) => ControlPanel.Open());
        tray.ContextMenuStrip.Items.Add("Search", null, (_, _) => SpotlightWindow.Toggle());
        tray.ContextMenuStrip.Items.Add("Edit widgets", null, (_, _) => WidgetHost.Edit(true));
        var vizItem = new WF.ToolStripMenuItem("Bottom visualiser");   // quick on/off without opening the control panel
        vizItem.Click += (_, _) => { Store.C.BottomViz = !Store.C.BottomViz; Store.Save(); };
        tray.ContextMenuStrip.Opening += (_, _) => vizItem.Checked = Store.C.BottomViz;
        tray.ContextMenuStrip.Items.Add(vizItem);
        tray.ContextMenuStrip.Items.Add("Show Windows taskbar", null, (_, _) => ShowTaskbar());
        tray.ContextMenuStrip.Items.Add("Quit and restore taskbar", null, (_, _) => Quit());
        tray.DoubleClick += (_, _) => ControlPanel.Open();
        app.Run();
    }
}
