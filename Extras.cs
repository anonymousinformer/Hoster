using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace Hoster;

// ---------------- lock screen: big clock over your wallpaper, with the player and a calendar ----------------
// This is Hoster's own full-screen screen. It does not replace the real Windows lock screen (Windows doesn't allow that),
// and it has no password, so use Win+L when you need to actually lock the PC.
class LockScreen : Window {
    static LockScreen inst;
    public static void Open() { if (inst != null) { inst.Activate(); return; } inst = new LockScreen(); inst.Show(); inst.Activate(); }

    static readonly Brush Cream = new SolidColorBrush(Color.FromRgb(0xE8, 0xE0, 0xC8));
    static readonly Brush CreamDim = new SolidColorBrush(Color.FromArgb(180, 0xE8, 0xE0, 0xC8));
    readonly TextBlock clock = new(), date = new();
    readonly Border mediaHost = new();
    readonly DispatcherTimer t = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Action pc;
    bool closing;

    LockScreen() {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual; Opacity = 0;
        var b = WF.Screen.PrimaryScreen.Bounds; double sc = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Left = b.Left / sc; Top = b.Top / sc; Width = b.Width / sc; Height = b.Height / sc;

        var root = new Grid();
        var wp = Wallpaper();
        if (wp != null) root.Children.Add(new Image { Source = wp, Stretch = Stretch.UniformToFill });
        root.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromArgb(110, 0, 0, 0), Color.FromArgb(190, 0, 0, 0), 90) });

        // big clock + date
        clock.FontFamily = new FontFamily("Segoe UI Light, Segoe UI"); clock.FontSize = 170; clock.Foreground = Cream; clock.HorizontalAlignment = HorizontalAlignment.Center;
        date.FontSize = 24; date.Foreground = CreamDim; date.HorizontalAlignment = HorizontalAlignment.Center; date.Margin = new Thickness(0, -14, 0, 0);
        root.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 120), Children = { clock, date } });

        // calendar card, top left
        var cal = new StackPanel { Width = 252 };
        var ttl = UI.Txt(DateTime.Today.ToString("MMMM yyyy"), Th.Fg, 13, FontWeights.SemiBold); ttl.Margin = new Thickness(0, 0, 0, 6);
        cal.Children.Add(ttl); cal.Children.Add(CalGrid.Build(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)));
        root.Children.Add(new Border { Background = Th.Glass, BorderBrush = Th.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(40), Child = cal });

        // player card + user, bottom centre
        mediaHost.Background = Th.Glass; mediaHost.BorderBrush = Th.Line; mediaHost.BorderThickness = new Thickness(1); mediaHost.CornerRadius = new CornerRadius(18);
        mediaHost.Padding = new Thickness(12); mediaHost.HorizontalAlignment = HorizontalAlignment.Center; mediaHost.Margin = new Thickness(0, 0, 0, 22);
        mediaHost.MouseLeftButtonDown += (_, e) => e.Handled = true;   // clicking the player must not unlock

        string name = Environment.UserName;
        var av = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(29), Background = Th.Accent, HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = name.Substring(0, 1).ToUpper(), FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var nm = UI.Txt(name, Cream, 15, FontWeights.SemiBold); nm.HorizontalAlignment = HorizontalAlignment.Center; nm.Margin = new Thickness(0, 8, 0, 2);
        var hint = UI.Txt("Click anywhere or press a key to continue", CreamDim, 11.5); hint.HorizontalAlignment = HorizontalAlignment.Center;
        root.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 36), Children = { mediaHost, av, nm, hint } });
        Content = root;

        Tick(); RenderMedia();
        t.Tick += (_, _) => Tick(); t.Start();
        pc = () => Dispatcher.BeginInvoke(new Action(RenderMedia)); Player.Changed += pc;

        MouseLeftButtonDown += (_, _) => Unlock();
        KeyDown += (_, _) => Unlock();
        Loaded += (_, _) => { Activate(); Focus(); BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))); };
        Closed += (_, _) => { t.Stop(); Player.Changed -= pc; if (inst == this) inst = null; };
    }

    void Tick() {
        var n = DateTime.Now;
        clock.Text = n.ToString(Store.C.Clock24 ? "HH:mm" : "h:mm");
        date.Text = n.ToString("dddd, d MMMM");
    }

    void RenderMedia() {
        if (!Player.Has) { mediaHost.Visibility = Visibility.Collapsed; mediaHost.Child = null; return; }
        mediaHost.Visibility = Visibility.Visible;
        var art = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(10), Background = Player.Art != null ? new ImageBrush(Player.Art) { Stretch = Stretch.UniformToFill } : Th.Tile };
        var title = UI.Txt(Player.Title, Th.Fg, 14, FontWeights.SemiBold); title.TextTrimming = TextTrimming.CharacterEllipsis; title.MaxWidth = 200;
        var who = UI.Txt(Player.Artist, Th.Dim, 12); who.TextTrimming = TextTrimming.CharacterEllipsis; who.MaxWidth = 200;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 14, 0), Children = { title, who } };
        var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = {
            UI.Round("\uE892", Player.Prev, 34, false), UI.Round(Player.Playing ? "\uE769" : "\uE768", Player.PlayPause, 40, true), UI.Round("\uE893", Player.Next, 34, false) } };
        mediaHost.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { art, text, ctl } };
    }

    void Unlock() {
        if (closing) return; closing = true;
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)); a.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, a);
    }

    static ImageSource Wallpaper() {
        try {
            string p = null;
            using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop")) p = k?.GetValue("WallPaper") as string;
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            if (!File.Exists(p)) return null;
            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 1920; bi.UriSource = new Uri(p); bi.EndInit(); bi.Freeze();
            return bi;
        } catch { return null; }
    }
}

// ---------------- bottom visualiser: a soft audio "mountain range" along the bottom of the desktop ----------------
static class BottomViz {
    static VizBar bar; static bool capturing;

    public static void Start() {
        Apply();
        Store.Changed += Apply; Player.Changed += Apply; Full.Changed += Apply; Full.CoveredChanged += Apply;
    }
    static void Apply() {
        bool want = Store.C.BottomViz, idle = Full.On || Full.Covered;
        if (want && bar == null) { bar = new VizBar(); bar.Show(); }
        else if (!want && bar != null) { bar.Close(); bar = null; }
        bool run = want && Player.Playing && !idle;       // only listens to audio while music is playing and the desktop is visible
        if (run && !capturing) { Viz.Start(); capturing = true; }
        else if (!run && capturing) { Viz.Stop(); capturing = false; }
        if (bar != null) { bar.Active = run; bar.Visibility = idle ? Visibility.Hidden : Visibility.Visible; bar.Reposition(); }
    }
}

class VizBar : Shell {
    readonly WaveView wave = new();
    public bool Active { set => wave.Active = value; }
    public VizBar() {
        Topmost = false; Height = 130; Content = wave;
        Loaded += async (_, _) => { MakeClickThrough(); Sink(); await Task.Delay(1500); Place(); };   // the dock reserves its space a moment after start-up
        IsVisibleChanged += (_, _) => { if (IsVisible) Sink(); };
        var tk = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) }; tk.Tick += (_, _) => { if (IsVisible) Sink(); }; tk.Start();
        Closed += (_, _) => tk.Stop();
    }
    void Sink() { if (IsLoaded) N.ToDesktop(Handle); }
    public void Reposition() { if (IsLoaded) Place(); }
    protected override void Place() {
        var s = WF.Screen.PrimaryScreen; double sc = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Height = 130; Width = s.Bounds.Width / sc; Left = s.Bounds.Left / sc; Top = s.WorkingArea.Bottom / sc - Height;
    }
    void MakeClickThrough() {   // clicks go to the desktop underneath
        var h = Handle; long ex = VizNative.Get(h, -20).ToInt64();
        VizNative.Set(h, -20, new IntPtr(ex | 0x20 | 0x80));
    }
}

class WaveView : FrameworkElement {
    const int Pts = 72;
    readonly double[] v = new double[Pts], tgt = new double[Pts], y = new double[Pts];   // reused every frame: no per-frame arrays
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(33) };   // ~30 fps
    public bool Active;
    bool drawn;
    Brush fill; Pen line; Color fillCol;      // built once, rebuilt only when the accent changes
    public WaveView() {
        IsHitTestVisible = false;
        tick.Tick += (_, _) => Frame();
        Loaded += (_, _) => tick.Start(); Unloaded += (_, _) => tick.Stop();
    }
    void Frame() {
        bool live = Active && Environment.TickCount64 - Viz.Last < 300;
        bool any = false;
        for (int i = 0; i < Pts; i++) {
            double f = i * (Viz.Bands - 1) / (double)(Pts - 1); int a = (int)f, b = Math.Min(Viz.Bands - 1, a + 1);
            tgt[i] = live ? Viz.Level[a] + (Viz.Level[b] - Viz.Level[a]) * (f - a) : 0;
        }
        for (int i = 0; i < Pts; i++) {   // blur neighbours so the shape is smooth, then ease toward it
            double s = (tgt[Math.Max(0, i - 1)] + tgt[i] * 2 + tgt[Math.Min(Pts - 1, i + 1)]) / 4;
            v[i] = s > v[i] ? v[i] + (s - v[i]) * 0.55 : Math.Max(s, v[i] - 0.045);
            if (v[i] > 0.004) any = true;
        }
        if (!any && !drawn) return;      // nothing to show: no redraw at all
        drawn = any; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc) {
        double w = ActualWidth, h = ActualHeight; if (w <= 0 || h <= 0) return;
        var col = (Th.Accent as SolidColorBrush)?.Color ?? Colors.White;
        if (fill == null || col != fillCol) {
            fillCol = col;
            var gb = new LinearGradientBrush(Color.FromArgb(150, col.R, col.G, col.B), Color.FromArgb(0, col.R, col.G, col.B), 90); gb.Freeze(); fill = gb;
        }
        if (line == null) {
            var lb = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)); lb.Freeze();
            line = new Pen(lb, 1.4); line.Freeze();
        }
        for (int i = 0; i < Pts; i++) y[i] = h - Math.Pow(v[i], 0.85) * h * 0.92;   // pow once per point, not twice
        var g = new StreamGeometry();
        using (var c = g.Open()) {
            c.BeginFigure(new Point(0, h), true, true);
            for (int i = 0; i < Pts; i++) c.LineTo(new Point(w * i / (Pts - 1), y[i]), true, true);
            c.LineTo(new Point(w, h), true, true);
        }
        g.Freeze(); dc.DrawGeometry(fill, null, g);
        var top = new StreamGeometry();
        using (var c = top.Open()) {
            c.BeginFigure(new Point(0, y[0]), false, false);
            for (int i = 1; i < Pts; i++) c.LineTo(new Point(w * i / (Pts - 1), y[i]), true, true);
        }
        top.Freeze(); dc.DrawGeometry(null, line, top);
    }
}

static class VizNative {
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr Get(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr Set(IntPtr h, int i, IntPtr v);
}
