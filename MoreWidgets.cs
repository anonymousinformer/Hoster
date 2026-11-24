using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Shp = System.Windows.Shapes;
using WF = System.Windows.Forms;

namespace Hoster;

// ---------------- battery ----------------
static class Batt {
    public static bool Has, Charging, Plugged; public static int Percent; public static TimeSpan Left;   // Left = zero when Windows doesn't know
    public static event Action Changed;
    static string sig = "";

    public static void Start() {
        Poll();
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) }; t.Tick += (_, _) => Poll(); t.Start();
        SystemEvents.PowerModeChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(new Action(Poll));   // plug / unplug shows up straight away
    }
    public static void Poll() {
        try {
            var ps = WF.SystemInformation.PowerStatus; float f = ps.BatteryLifePercent;
            bool has = (ps.BatteryChargeStatus & WF.BatteryChargeStatus.NoSystemBattery) == 0 && f >= 0 && f <= 1;
            int pct = has ? (int)Math.Round(f * 100) : 0;
            bool plugged = ps.PowerLineStatus == WF.PowerLineStatus.Online, charging = has && (ps.BatteryChargeStatus & WF.BatteryChargeStatus.Charging) != 0;
            int secs = ps.BatteryLifeRemaining;
            var left = has && !plugged && secs > 0 ? TimeSpan.FromSeconds(secs) : TimeSpan.Zero;
            string s = $"{has}|{pct}|{plugged}|{charging}|{(int)(left.TotalMinutes / 5)}";
            Has = has; Percent = pct; Plugged = plugged; Charging = charging; Left = left;
            if (s == sig) return; sig = s; Changed?.Invoke();
        } catch { }
    }
}

static class BattUI {
    public static readonly Brush Warn = Frozen("#FFB020"), Low = Frozen("#FF6B6B");
    static Brush Frozen(string h) { var b = (Brush)new BrushConverter().ConvertFrom(h); b.Freeze(); return b; }
    public static bool IsLow => Batt.Has && Batt.Percent <= 15 && !Batt.Plugged;
    public static string Status() {
        if (!Batt.Has) return "No battery";
        if (Batt.Charging) return "Charging";
        if (Batt.Plugged) return "Plugged in";
        if (Batt.Left > TimeSpan.Zero) return (int)Batt.Left.TotalHours + "h " + Batt.Left.Minutes + "m left";
        return "On battery";
    }
    // a little battery: outline, fill by percent, and a bolt while charging
    public static FrameworkElement Icon(double w) {
        double h = w * 0.5; Brush ink = IsLow ? Low : Th.Fg, fillB = IsLow ? Low : Batt.Plugged ? Th.Accent : Th.Fg;
        var fill = new Border { Width = Math.Max(0, (w - 5.4) * Batt.Percent / 100.0), Background = fillB, CornerRadius = new CornerRadius(h * 0.14), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1.5) };
        var body = new Grid { Children = { new Border { Width = w, Height = h, BorderBrush = ink, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(h * 0.26), Child = fill } } };
        if (Batt.Charging) body.Children.Add(new Shp.Path { Data = Geometry.Parse("M5,0 L0,6.5 L3.6,6.5 L2.6,12 L8,4.8 L4.4,4.8 Z"), Fill = Brushes.White, Stroke = Th.Bg, StrokeThickness = 0.5,
            Stretch = Stretch.Uniform, Height = h * 0.75, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var nub = new Border { Width = 2, Height = h * 0.42, Background = ink, CornerRadius = new CornerRadius(0, 1, 1, 0), VerticalAlignment = VerticalAlignment.Center };
        return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { body, nub } };
    }
}

class BatteryWidget : Widget {
    readonly Shp.Ellipse[] ring = new Shp.Ellipse[60]; TextBlock big, sub; Border iconHost; readonly Action h;
    public BatteryWidget(WCfg c) : base(c) { h = () => Dispatcher.BeginInvoke(new Action(Fill)); Batt.Changed += h; Closed += (_, _) => Batt.Changed -= h; }
    protected override UIElement Body() {
        var rc = new Canvas { Width = 180, Height = 180 };
        for (int i = 0; i < 60; i++) { double a = i * Math.PI / 30; ring[i] = Dots.Add(rc, 90 + 84 * Math.Sin(a), 90 - 84 * Math.Cos(a), 3.4, Th.Line); }
        iconHost = new Border { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        big = UI.Txt("", Th.Fg, 38, FontWeights.Light); big.FontFamily = new FontFamily("Segoe UI Light, Segoe UI"); big.HorizontalAlignment = HorizontalAlignment.Center;
        sub = UI.Txt("", Th.Dim, 12); sub.HorizontalAlignment = HorizontalAlignment.Center;
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { iconHost, big, sub } };
        var dial = new Grid { Width = 180, Height = 180, HorizontalAlignment = HorizontalAlignment.Center, Children = { rc, mid } };
        var p = new StackPanel { Width = 200, Children = { dial } };
        Fill(); return p;
    }
    void Fill() {
        if (big == null) return;
        int lit = Batt.Has ? (int)Math.Round(Batt.Percent * 0.6) : 0; Brush on = BattUI.IsLow ? BattUI.Low : Th.Accent;
        for (int i = 0; i < 60; i++) ring[i].Fill = i < lit ? on : Th.Line;
        big.Text = Batt.Has ? Batt.Percent + "%" : "—"; big.Foreground = BattUI.IsLow ? BattUI.Low : Th.Fg;
        sub.Text = BattUI.Status(); iconHost.Child = Batt.Has ? BattUI.Icon(34) : null;
    }
}

// ---------------- matrix display: a dot-matrix panel you can configure ----------------
// Classic 5x7 font, ASCII 0x20..0x5A (upper case only). One byte per column, bit 0 = top row.
static class MxFont {
    static readonly byte[] F = {
        0x00,0x00,0x00,0x00,0x00, 0x00,0x00,0x5F,0x00,0x00, 0x00,0x07,0x00,0x07,0x00, 0x14,0x7F,0x14,0x7F,0x14, 0x24,0x2A,0x7F,0x2A,0x12, 0x23,0x13,0x08,0x64,0x62, 0x36,0x49,0x55,0x22,0x50, 0x00,0x05,0x03,0x00,0x00,
        0x00,0x1C,0x22,0x41,0x00, 0x00,0x41,0x22,0x1C,0x00, 0x08,0x2A,0x1C,0x2A,0x08, 0x08,0x08,0x3E,0x08,0x08, 0x00,0x50,0x30,0x00,0x00, 0x08,0x08,0x08,0x08,0x08, 0x00,0x60,0x60,0x00,0x00, 0x20,0x10,0x08,0x04,0x02,
        0x3E,0x51,0x49,0x45,0x3E, 0x00,0x42,0x7F,0x40,0x00, 0x42,0x61,0x51,0x49,0x46, 0x21,0x41,0x45,0x4B,0x31, 0x18,0x14,0x12,0x7F,0x10, 0x27,0x45,0x45,0x45,0x39, 0x3C,0x4A,0x49,0x49,0x30, 0x01,0x71,0x09,0x05,0x03,
        0x36,0x49,0x49,0x49,0x36, 0x06,0x49,0x49,0x29,0x1E, 0x00,0x36,0x36,0x00,0x00, 0x00,0x56,0x36,0x00,0x00, 0x00,0x08,0x14,0x22,0x41, 0x14,0x14,0x14,0x14,0x14, 0x41,0x22,0x14,0x08,0x00, 0x02,0x01,0x51,0x09,0x06,
        0x32,0x49,0x79,0x41,0x3E, 0x7E,0x11,0x11,0x11,0x7E, 0x7F,0x49,0x49,0x49,0x36, 0x3E,0x41,0x41,0x41,0x22, 0x7F,0x41,0x41,0x22,0x1C, 0x7F,0x49,0x49,0x49,0x41, 0x7F,0x09,0x09,0x01,0x01, 0x3E,0x41,0x41,0x51,0x32,
        0x7F,0x08,0x08,0x08,0x7F, 0x00,0x41,0x7F,0x41,0x00, 0x20,0x40,0x41,0x3F,0x01, 0x7F,0x08,0x14,0x22,0x41, 0x7F,0x40,0x40,0x40,0x40, 0x7F,0x02,0x04,0x02,0x7F, 0x7F,0x04,0x08,0x10,0x7F, 0x3E,0x41,0x41,0x41,0x3E,
        0x7F,0x09,0x09,0x09,0x06, 0x3E,0x41,0x51,0x21,0x5E, 0x7F,0x09,0x19,0x29,0x46, 0x46,0x49,0x49,0x49,0x31, 0x01,0x01,0x7F,0x01,0x01, 0x3F,0x40,0x40,0x40,0x3F, 0x1F,0x20,0x40,0x20,0x1F, 0x7F,0x20,0x18,0x20,0x7F,
        0x63,0x14,0x08,0x14,0x63, 0x03,0x04,0x78,0x04,0x03, 0x61,0x51,0x49,0x45,0x43 };
    const int Glyphs = 59;   // 0x20 .. 0x5A

    // columns of the text: each int holds 7 bits (bit 0 = top row); one blank column between letters
    public static List<int> Render(string s) {
        var l = new List<int>();
        foreach (char ch in s.ToUpperInvariant()) {
            int idx = ch - 0x20; if (idx < 0 || idx >= Glyphs) idx = '?' - 0x20;
            for (int i = 0; i < 5; i++) l.Add(F[idx * 5 + i]);
            l.Add(0);
        }
        if (l.Count > 0) l.RemoveAt(l.Count - 1);
        return l;
    }
}

class MatrixView : FrameworkElement {
    public readonly int Cols, Rows; readonly double cell, dot; readonly bool square, ghost; readonly Brush lit, off; readonly bool[,] g;
    public MatrixView(int cols, int rows, int dot, bool square, bool ghost, Brush lit) {
        Cols = cols; Rows = rows; this.dot = dot; cell = dot * 1.45; this.square = square; this.ghost = ghost; this.lit = lit; off = Th.Line; g = new bool[cols, rows];
    }
    protected override Size MeasureOverride(Size a) => new Size(Cols * cell, Rows * cell);
    public void Set(bool[,] n) {
        bool ch = false;
        for (int x = 0; x < Cols; x++) for (int y = 0; y < Rows; y++) if (g[x, y] != n[x, y]) { g[x, y] = n[x, y]; ch = true; }
        if (ch) InvalidateVisual();      // nothing changed = nothing redrawn
    }
    protected override void OnRender(DrawingContext dc) {
        double r = dot / 2;
        for (int x = 0; x < Cols; x++) for (int y = 0; y < Rows; y++) {
            bool on = g[x, y]; if (!on && !ghost) continue;
            var b = on ? lit : off; double cx = x * cell + cell / 2, cy = y * cell + cell / 2;
            if (square) dc.DrawRoundedRectangle(b, null, new Rect(cx - r, cy - r, dot, dot), dot * 0.18, dot * 0.18); else dc.DrawEllipse(b, null, new Point(cx, cy), r, r);
        }
    }
}

class MatrixWidget : Widget {
    MatrixView mv; bool[,] buf; string mode = "Text", lastStr = ""; List<int> txt = new(); int off, cols, rows; bool scroll, viz;
    readonly float[] disp = new float[64]; readonly DispatcherTimer t = new();
    public MatrixWidget(WCfg c) : base(c) {
        t.Tick += (_, _) => Step();
        IsVisibleChanged += (_, _) => SetViz();
        Closed += (_, _) => { t.Stop(); mode = ""; SetViz(); };
    }
    // audio capture only runs while the panel is on screen and showing the audio mode
    void SetViz() { bool want = mode == "Audio" && IsVisible; if (want == viz) return; viz = want; if (want) Viz.Start(); else Viz.Stop(); }

    protected override UIElement Body() {
        var c = Store.C;
        cols = Math.Max(8, Math.Min(64, c.MxCols)); rows = Math.Max(7, Math.Min(20, c.MxRows)); mode = c.MxMode ?? "Text"; scroll = c.MxScroll; off = 0; lastStr = "";
        Array.Clear(disp, 0, disp.Length);
        Brush lit = Th.Accent;
        if (!string.IsNullOrEmpty(c.MxColor) && c.MxColor != "accent") { try { var b = (Brush)new BrushConverter().ConvertFrom(c.MxColor); b.Freeze(); lit = b; } catch { } }
        mv = new MatrixView(cols, rows, Math.Max(3, Math.Min(12, c.MxDot)), c.MxSquare, c.MxGhost, lit); buf = new bool[cols, rows];
        bool marquee = mode == "Text" && scroll;
        t.Interval = TimeSpan.FromMilliseconds(mode == "Audio" ? 40 : marquee ? 190 - Math.Max(1, Math.Min(10, c.MxSpeed)) * 15 : 1000);
        t.Start(); SetViz(); Step();
        return mv;
    }

    string Content() {
        var c = Store.C; var now = DateTime.Now;
        switch (mode) {
            case "Clock": { string s = now.ToString(c.Clock24 ? "HH:mm" : "h:mm"); return now.Second % 2 == 0 ? s : s.Replace(':', ' '); }   // blinking colon
            case "Date": return now.ToString("ddd d MMM");
            case "Battery": return Batt.Has ? Batt.Percent + "%" + (Batt.Charging ? " +" : "") : "NO BATTERY";
            case "CPU": return "CPU " + Stats.Cpu + "%";
            default: return string.IsNullOrEmpty(c.MxText) ? " " : c.MxText;
        }
    }

    void Step() {
        if (mv == null || Visibility != Visibility.Visible || Full.On || Full.Covered) return;
        Array.Clear(buf, 0, buf.Length);
        if (mode == "Audio") Bars(); else Text();
        mv.Set(buf);
    }
    void Text() {
        string s = Content().ToUpperInvariant();
        if (s != lastStr) { lastStr = s; txt = MxFont.Render(s); off = 0; }
        int w = txt.Count, oy = (rows - 7) / 2, gap = Math.Max(6, cols / 3);
        bool marquee = mode == "Text" && scroll && w > cols;
        for (int x = 0; x < cols; x++) {
            int src = marquee ? (x + off) % (w + gap) : x - Math.Max(0, (cols - w) / 2);
            int bits = src >= 0 && src < w ? txt[src] : 0; if (bits == 0) continue;
            for (int r = 0; r < 7; r++) if (((bits >> r) & 1) != 0) buf[x, r + oy] = true;
        }
        if (marquee) off = (off + 1) % (w + gap);
    }
    void Bars() {
        bool live = Environment.TickCount64 - Viz.Last < 300;
        for (int x = 0; x < cols; x++) {
            double f = x * (Viz.Bands - 1) / (double)Math.Max(1, cols - 1); int a = (int)f, b = Math.Min(Viz.Bands - 1, a + 1);
            float tgt = live ? (float)(Viz.Level[a] + (Viz.Level[b] - Viz.Level[a]) * (f - a)) : 0;
            disp[x] = tgt > disp[x] ? disp[x] + (tgt - disp[x]) * 0.6f : Math.Max(0, disp[x] - 0.05f);   // quick rise, slow fall
            int h = (int)Math.Round(disp[x] * rows); if (h == 0 && disp[x] > 0.03f) h = 1;
            for (int r = 0; r < h; r++) buf[x, rows - 1 - r] = true;
        }
    }
}
