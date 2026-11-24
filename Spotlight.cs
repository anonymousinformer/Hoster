using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Hoster;

// ---------------- Spotlight: one search box for apps, open windows, files, sums, commands and the web ----------------
static class Spotlight {
    public static readonly string[] Keys = { "Alt+Space", "Alt+S", "Ctrl+Space", "Alt+Q" };
    public static bool Ok = true;                       // false if another app already owns the chosen shortcut
    const int Id = 0x4801;
    static HwndSource src; static bool reg; static string current;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    public static void Start() {
        try {
            src = new HwndSource(new HwndSourceParameters("HosterHotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });   // message-only window
            src.AddHook(Hook); Apply(); Store.Changed += Apply;
        } catch { }
    }
    public static void Stop() { try { if (reg) UnregisterHotKey(src.Handle, Id); reg = false; src?.Dispose(); } catch { } }

    static void Apply() {
        try {
            var c = Store.C; string want = c.SearchOn ? c.SearchKey : "";
            if (want == current) return; current = want;
            if (reg) { UnregisterHotKey(src.Handle, Id); reg = false; }
            Ok = true; if (want == "") return;
            uint mod = 1, vk = 0x20;                    // Alt+Space
            if (want == "Alt+S") vk = 0x53; else if (want == "Alt+Q") vk = 0x51; else if (want == "Ctrl+Space") mod = 2;
            reg = RegisterHotKey(src.Handle, Id, mod | 0x4000, vk); Ok = reg;     // 0x4000 = don't repeat while held
        } catch { }
    }
    static IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) {
        if (msg == 0x0312 && w.ToInt32() == Id) { handled = true; SpotlightWindow.Toggle(); }
        return IntPtr.Zero;
    }
}

class Hit { public string Title, Sub, Glyph; public ImageSource Icon; public Action Run; }

// a tiny expression evaluator: + - * / % ^ and brackets
class Calc {
    string s; int i;
    public static double? Eval(string t) {
        try {
            var c = new Calc { s = t.Replace(" ", "").Replace(',', '.') }; double v = c.Expr();
            return c.i != c.s.Length || double.IsNaN(v) || double.IsInfinity(v) ? null : v;
        } catch { return null; }
    }
    double Expr() { double v = Term(); while (i < s.Length && (s[i] == '+' || s[i] == '-')) { char o = s[i++]; double r = Term(); v = o == '+' ? v + r : v - r; } return v; }
    double Term() { double v = Pow(); while (i < s.Length && (s[i] == '*' || s[i] == '/' || s[i] == '%')) { char o = s[i++]; double r = Pow(); v = o == '*' ? v * r : o == '/' ? v / r : v % r; } return v; }
    double Pow() { double b = Unary(); if (i < s.Length && s[i] == '^') { i++; return Math.Pow(b, Pow()); } return b; }
    double Unary() { if (i < s.Length && s[i] == '-') { i++; return -Unary(); } if (i < s.Length && s[i] == '+') { i++; return Unary(); } return Atom(); }
    double Atom() {
        if (i < s.Length && s[i] == '(') { i++; double v = Expr(); if (i >= s.Length || s[i] != ')') throw new FormatException(); i++; return v; }
        int st = i; while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
        if (st == i) throw new FormatException();
        return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
    }
}

class SpotlightWindow : Window {
    static SpotlightWindow inst;
    public static void Toggle() { if (inst != null) { inst.CloseMe(); return; } inst = new SpotlightWindow(); inst.Open(); }

    readonly TextBox box = new(); readonly TextBlock hint; readonly StackPanel list = new(); readonly ScrollViewer sv = new(); readonly Border sep;
    readonly List<(uint pid, IntPtr h, string title, string path)> wins;
    List<Hit> calc = new(), apps = new(), wns = new(), cmds = new(), files = new(); Hit web;
    readonly List<Hit> hits = new(); readonly List<Border> rows = new(); readonly List<TextBlock> enters = new();
    int sel, ver; bool ready, closing; CancellationTokenSource cts;

    static int Score(string name, string q) {
        string n = name.ToLowerInvariant(), k = q.ToLowerInvariant();
        if (n == k) return 100; if (n.StartsWith(k)) return 80; if (n.Contains(" " + k)) return 60; if (n.Contains(k)) return 40;
        int j = 0; foreach (char ch in n) if (j < k.Length && ch == k[j]) j++;      // letters in order, e.g. "ffx" -> Firefox
        return j == k.Length && k.Length >= 2 ? 15 : 0;
    }

    SpotlightWindow() {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; Topmost = true;
        SizeToContent = SizeToContent.Height; Width = 680;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2; Top = SystemParameters.PrimaryScreenHeight * 0.2;
        try { wins = N.Running(); } catch { wins = new(); }
        if (StartMenu.Apps.Count == 0) _ = StartMenu.Load();

        var mag = UI.Glyph("\uE721", Th.Dim, 18); mag.Margin = new Thickness(20, 0, 0, 0); mag.HorizontalAlignment = HorizontalAlignment.Left;
        box.Background = Brushes.Transparent; box.BorderThickness = new Thickness(0); box.Foreground = Th.Fg; box.CaretBrush = Th.Fg; box.SelectionBrush = Th.Accent;
        box.FontSize = 20; box.VerticalContentAlignment = VerticalAlignment.Center; box.Margin = new Thickness(54, 0, 18, 0); box.Height = 64;
        hint = UI.Txt("Search apps, files, windows or type a sum", Th.Dim, 20); hint.Margin = new Thickness(56, 0, 0, 0); hint.IsHitTestVisible = false;
        var bar = new Grid { Height = 64 }; bar.Children.Add(mag); bar.Children.Add(hint); bar.Children.Add(box);
        sep = new Border { Height = 1, Background = Th.Line, Visibility = Visibility.Collapsed };
        sv.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden; sv.MaxHeight = 470; sv.Content = list; sv.Margin = new Thickness(8, 6, 8, 8);

        Content = new Border { Background = Th.Bg, BorderBrush = Th.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Math.Max(16, Store.C.Radius + 4)), Margin = new Thickness(14),
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 5, Opacity = 0.5, Color = Colors.Black }, Child = new StackPanel { Children = { bar, sep, sv } } };

        box.TextChanged += (_, _) => { hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Query(); };
        PreviewKeyDown += (_, e) => {
            if (e.Key == Key.Escape) { CloseMe(); e.Handled = true; }
            else if (e.Key == Key.Down) { Move(1); e.Handled = true; }
            else if (e.Key == Key.Up) { Move(-1); e.Handled = true; }
            else if (e.Key == Key.Enter) { RunSel(); e.Handled = true; }
        };
        Activated += (_, _) => ready = true;
        Deactivated += (_, _) => { if (ready) CloseMe(); };
        Closed += (_, _) => { cts?.Cancel(); if (inst == this) inst = null; };
    }

    void Open() {
        Opacity = 0; Show(); Activate(); box.Focus();
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        var top = Top; BeginAnimation(TopProperty, new DoubleAnimation(top - 10, top, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
    void CloseMe() { if (closing) return; closing = true; try { Close(); } catch { } }

    // ----- searching -----
    void Query() {
        string q = box.Text.Trim(); ver++; cts?.Cancel();
        calc = new(); apps = new(); wns = new(); cmds = new(); files = new(); web = null;
        if (q.Length > 0) {
            // sums
            if (q.Any(ch => "+-*/%^".Contains(ch)) && q.All(ch => char.IsDigit(ch) || " +-*/%^().,".Contains(ch)) && Calc.Eval(q) is double v) {
                string r = v.ToString("0.##########", CultureInfo.InvariantCulture);
                calc.Add(new Hit { Title = "= " + r, Sub = q + "   ·   Enter copies the answer", Glyph = "\uE8EF", Run = () => { try { Clipboard.SetText(r); } catch { } } });
            }
            // apps
            apps = StartMenu.Apps.Select(a => (a, s: Score(a.Name, q))).Where(x => x.s > 0).OrderByDescending(x => x.s).ThenBy(x => x.a.Name.Length).Take(5)
                .Select(x => { var id = x.a.Id; return new Hit { Title = x.a.Name, Sub = "Application", Glyph = "\uE71D", Icon = StartMenu.AppIcon(id),
                    Run = () => { try { Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + id) { UseShellExecute = true }); } catch { } } }; }).ToList();
            // open windows
            wns = wins.Select(w => (w, s: Math.Max(Score(w.title ?? "", q), Score(Path.GetFileNameWithoutExtension(w.path ?? ""), q)))).Where(x => x.s >= 40).OrderByDescending(x => x.s).Take(3)
                .Select(x => { var h = x.w.h; return new Hit { Title = x.w.title, Sub = "Switch to window  ·  " + Path.GetFileNameWithoutExtension(x.w.path ?? ""), Glyph = "\uE737", Icon = x.w.path != null ? N.Icon(x.w.path) : null, Run = () => N.Focus(h) }; }).ToList();
            // commands
            cmds = Commands().Select(c => (c, s: Score(c.Title, q))).Where(x => x.s >= 40).OrderByDescending(x => x.s).Take(3).Select(x => x.c).ToList();
            // web
            bool url = !q.Contains(' ') && (q.StartsWith("http://") || q.StartsWith("https://") || (q.Contains('.') && q.Length > 3 && !q.EndsWith(".")));
            string target = url ? (q.StartsWith("http") ? q : "https://" + q) : "https://www.google.com/search?q=" + Uri.EscapeDataString(q);
            web = new Hit { Title = url ? "Open " + q : "Search the web for “" + q + "”", Sub = url ? "In your browser" : "Google", Glyph = "\uE774",
                Run = () => { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { } } };
            if (q.Length >= 2) _ = LoadFiles(ver, q);
        }
        sel = 0; Render();
    }

    static List<Hit> Commands() {
        void Sh(string f, string a) { try { Process.Start(new ProcessStartInfo(f, a) { UseShellExecute = false, CreateNoWindow = true }); } catch { } }
        return new List<Hit> {
            new Hit { Title = "Control panel", Sub = "Hoster settings", Glyph = "\uE713", Run = ControlPanel.Open },
            new Hit { Title = "Lock screen", Sub = "Show the Hoster lock screen", Glyph = "\uE72E", Run = () => LockScreen.Open() },
            new Hit { Title = "Edit widgets", Sub = "Drag your desktop widgets around", Glyph = "\uE7C4", Run = () => WidgetHost.Edit(true) },
            new Hit { Title = "Lock PC", Sub = "Windows lock screen", Glyph = "\uE72E", Run = () => Sh("rundll32.exe", "user32.dll,LockWorkStation") },
            new Hit { Title = "Sign out", Sub = "Log off this account", Glyph = "\uE7E8", Run = () => Sh("shutdown.exe", "/l") },
            new Hit { Title = "Sleep", Sub = "Put the PC to sleep", Glyph = "\uE708", Run = () => Sh("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") },
            new Hit { Title = "Restart", Sub = "Restart the PC", Glyph = "\uE777", Run = () => Sh("shutdown.exe", "/r /t 0") },
            new Hit { Title = "Shut down", Sub = "Turn the PC off", Glyph = "\uE7E8", Run = () => Sh("shutdown.exe", "/s /t 0") },
            new Hit { Title = "Quit Hoster", Sub = "Close Hoster and bring the taskbar back", Glyph = "\uE711", Run = Program.Quit },
        };
    }

    // files in Desktop / Documents / Downloads (a few folders deep), found in the background so typing never stalls
    async Task LoadFiles(int v, string q) {
        await Task.Delay(180); if (v != ver || closing) return;
        var my = new CancellationTokenSource(); cts = my; var ct = my.Token;
        var found = await Task.Run(() => {
            var res = new List<Hit>(); var sw = Stopwatch.StartNew();
            string pat = "*" + q.Replace("*", "").Replace("?", "") + "*";
            var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") };
            var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 3, AttributesToSkip = FileAttributes.System };
            foreach (var r in roots) {
                if (!Directory.Exists(r)) continue;
                try {
                    foreach (var f in Directory.EnumerateFileSystemEntries(r, pat, opt)) {
                        if (ct.IsCancellationRequested || sw.ElapsedMilliseconds > 900) return res;
                        string path = f; bool dir = Directory.Exists(path);
                        res.Add(new Hit { Title = Path.GetFileName(path), Sub = Path.GetDirectoryName(path), Glyph = dir ? "\uE8B7" : "\uE8A5",
                            Run = () => { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } } });
                        if (res.Count >= 6) return res;
                    }
                } catch { }
            }
            return res;
        });
        if (v != ver || closing || ct.IsCancellationRequested) return;
        foreach (var h in found) { try { string p = h.Sub == null ? null : Path.Combine(h.Sub, h.Title); if (p != null && File.Exists(p)) h.Icon = N.Icon(p); } catch { } }
        files = found; Render();
    }

    // ----- drawing -----
    void Group(string name) { var t = UI.Txt(name, Th.Dim, 11, FontWeights.SemiBold); t.Margin = new Thickness(12, 8, 0, 4); list.Children.Add(t); }

    void Render() {
        list.Children.Clear(); hits.Clear(); rows.Clear(); enters.Clear();
        void Add(string title, List<Hit> g) { if (g.Count == 0) return; if (title != null) Group(title); foreach (var h in g) AddRow(h); }
        Add(null, calc); Add("Applications", apps); Add("Windows", wns); Add("Commands", cmds); Add("Files", files);
        if (web != null) { if (hits.Count > 0) Group("Web"); AddRow(web); }
        sep.Visibility = hits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (sel >= hits.Count) sel = Math.Max(0, hits.Count - 1);
        Paint();
    }

    void AddRow(Hit h) {
        int idx = hits.Count; hits.Add(h);
        FrameworkElement icon = h.Icon != null ? new Image { Source = h.Icon, Width = 26, Height = 26 }
            : new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = Th.Tile, Child = UI.Glyph(h.Glyph, Th.Fg, 14) };
        icon.Margin = new Thickness(0, 0, 14, 0);
        var t = UI.Txt(h.Title, Th.Fg, 14); t.TextTrimming = TextTrimming.CharacterEllipsis;
        var s = UI.Txt(h.Sub ?? "", Th.Dim, 11); s.TextTrimming = TextTrimming.CharacterEllipsis;
        var en = UI.Txt("↵", Th.Dim, 13); en.Margin = new Thickness(10, 0, 4, 0);
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { t, s } }; Grid.SetColumn(txt, 1); Grid.SetColumn(en, 2);
        g.Children.Add(icon); g.Children.Add(txt); g.Children.Add(en);
        var b = new Border { Child = g, CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 7, 10, 7), Cursor = Cursors.Hand, Background = Brushes.Transparent };
        b.MouseEnter += (_, _) => { sel = idx; Paint(); };
        b.MouseLeftButtonUp += (_, _) => { sel = idx; RunSel(); };
        rows.Add(b); enters.Add(en); list.Children.Add(b);
    }

    void Paint() {
        for (int i = 0; i < rows.Count; i++) { rows[i].Background = i == sel ? Th.Tile : Brushes.Transparent; enters[i].Visibility = i == sel ? Visibility.Visible : Visibility.Hidden; }
        if (sel >= 0 && sel < rows.Count) rows[sel].BringIntoView();
    }
    void Move(int d) { if (hits.Count == 0) return; sel = (sel + d + hits.Count) % hits.Count; Paint(); }
    void RunSel() { if (sel < 0 || sel >= hits.Count) return; var run = hits[sel].Run; CloseMe(); try { run?.Invoke(); } catch { } }
}
