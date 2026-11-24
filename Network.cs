using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WNet = Windows.Networking.Connectivity;
using WRad = Windows.Devices.Radios;
using WWifi = Windows.Devices.WiFi;
using WEnum = Windows.Devices.Enumeration;
using WBt = Windows.Devices.Bluetooth;

namespace Hoster;

// ---------------- Wi-Fi + Bluetooth (Windows runtime APIs) ----------------
class WifiNet { public string Ssid; public int Bars; public bool Open, Saved, Current, Action, NoNet; public WWifi.WiFiAvailableNetwork Raw; }   // Action = Windows' "action needed" (sign-in page)
class BtDev { public string Name; public bool Connected; }

static class Net {
    static WWifi.WiFiAdapter adapter;

    // ----- "action needed": connected to Wi-Fi that wants a sign-in (captive portal) -----
    public static bool Alert; public static event Action AlertChanged;
    public const string SignInUrl = "http://www.msftconnecttest.com/redirect";   // Windows' own connectivity check: bounces to the network's sign-in page
    public static void SignIn() => Open(SignInUrl);
    public static void Start() {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) }; t.Tick += async (_, _) => await Check(); t.Start();
        try { WNet.NetworkInformation.NetworkStatusChanged += _ => Application.Current?.Dispatcher.BeginInvoke(new Action(async () => await Check())); } catch { }
        _ = Check();
    }
    static async Task Check() {
        bool a = false;
        if (Store.C.Network) a = await Task.Run(() => {
            try { foreach (var p in WNet.NetworkInformation.GetConnectionProfiles()) if (p.IsWlanConnectionProfile && p.GetNetworkConnectivityLevel() == WNet.NetworkConnectivityLevel.ConstrainedInternetAccess) return true; } catch { }
            return false;
        });
        if (a != Alert) { Alert = a; AlertChanged?.Invoke(); }
    }

    public static async Task<WRad.Radio> Radio(WRad.RadioKind kind) {
        try {
            if (await WRad.Radio.RequestAccessAsync() != WRad.RadioAccessStatus.Allowed) return null;
            var all = await WRad.Radio.GetRadiosAsync();
            return all.FirstOrDefault(r => r.Kind == kind);
        } catch { return null; }
    }
    public static async Task SetRadio(WRad.Radio r, bool on) {
        try { await r.SetStateAsync(on ? WRad.RadioState.On : WRad.RadioState.Off); } catch { }
    }

    static async Task<WWifi.WiFiAdapter> Adapter() {
        if (adapter != null) return adapter;
        try {
            if (await WWifi.WiFiAdapter.RequestAccessAsync() != WWifi.WiFiAccessStatus.Allowed) return null;
            var all = await WWifi.WiFiAdapter.FindAllAdaptersAsync();
            adapter = all.FirstOrDefault();
        } catch { }
        return adapter;
    }

    // names of networks Windows already has a saved profile (and password) for
    static HashSet<string> SavedProfiles() {
        var set = new HashSet<string>();
        try {
            var psi = new ProcessStartInfo("netsh", "wlan show profiles") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using var p = Process.Start(psi);
            string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000);
            foreach (var line in o.Split('\n')) {
                int i = line.IndexOf(':'); if (i < 0) continue;
                var v = line.Substring(i + 1).Trim(); if (v.Length > 0) set.Add(v);
            }
        } catch { }
        return set;
    }

    // null = no access / no adapter
    public static async Task<List<WifiNet>> Scan(bool rescan) {
        var ad = await Adapter(); if (ad == null) return null;
        var res = new List<WifiNet>();
        try {
            if (rescan) { try { await ad.ScanAsync(); } catch { } }
            string cur = null; var lvl = WNet.NetworkConnectivityLevel.InternetAccess;
            try { var p = await ad.NetworkAdapter.GetConnectedProfileAsync(); cur = p?.ProfileName; if (p != null) lvl = p.GetNetworkConnectivityLevel(); } catch { }
            var saved = await Task.Run(SavedProfiles);
            foreach (var n in ad.NetworkReport.AvailableNetworks) {
                if (string.IsNullOrEmpty(n.Ssid)) continue;
                int bars = n.SignalBars;
                var ex = res.FirstOrDefault(x => x.Ssid == n.Ssid);
                if (ex != null) { if (bars > ex.Bars) { ex.Bars = bars; ex.Raw = n; } continue; }
                res.Add(new WifiNet {
                    Ssid = n.Ssid, Bars = bars, Raw = n, Current = n.Ssid == cur, Saved = saved.Contains(n.Ssid),
                    Action = n.Ssid == cur && lvl == WNet.NetworkConnectivityLevel.ConstrainedInternetAccess, NoNet = n.Ssid == cur && lvl == WNet.NetworkConnectivityLevel.LocalAccess,
                    Open = n.SecuritySettings.NetworkAuthenticationType == Windows.Networking.Connectivity.NetworkAuthenticationType.Open80211 });
            }
        } catch { }
        return res.OrderByDescending(x => x.Current).ThenByDescending(x => x.Bars).ToList();
    }

    public static async Task<bool> Connect(WifiNet n, string pw) {
        var ad = await Adapter(); if (ad == null) return false;
        try {
            if (pw == null && n.Saved) {   // Windows already knows the password
                await Task.Run(() => {
                    using var p = Process.Start(new ProcessStartInfo("netsh", "wlan connect name=\"" + n.Ssid + "\"") { UseShellExecute = false, CreateNoWindow = true });
                    p?.WaitForExit(5000);
                });
                await Task.Delay(1500);
                return true;
            }
            var kind = WWifi.WiFiReconnectionKind.Automatic;
            var r = pw == null
                ? await ad.ConnectAsync(n.Raw, kind)
                : await ad.ConnectAsync(n.Raw, kind, new Windows.Security.Credentials.PasswordCredential { Password = pw });
            return r.ConnectionStatus == WWifi.WiFiConnectionStatus.Success;
        } catch { return false; }
    }
    public static void Disconnect() { try { adapter?.Disconnect(); } catch { } }

    public static async Task<List<BtDev>> BtDevices() {
        var res = new List<BtDev>();
        try {
            var infos = await WEnum.DeviceInformation.FindAllAsync(WBt.BluetoothDevice.GetDeviceSelectorFromPairingState(true));
            foreach (var di in infos) {
                bool c = false;
                try { using var bd = await WBt.BluetoothDevice.FromIdAsync(di.Id); c = bd != null && bd.ConnectionStatus == WBt.BluetoothConnectionStatus.Connected; } catch { }
                if (!string.IsNullOrEmpty(di.Name)) res.Add(new BtDev { Name = di.Name, Connected = c });
            }
        } catch { }
        return res;
    }

    public static void Open(string uri) { try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { } }
}

// ---------------- flyout from the top bar ----------------
class NetFlyout : Flyout {
    readonly StackPanel root = new(), wifiBox = new(), btBox = new();
    bool dead;

    public NetFlyout() : base(320) {
        Closed += (_, _) => dead = true;
        root.Margin = new Thickness(16);
        root.Children.Add(UI.Txt("Loading…", Th.Dim, 12));
        card.Child = root;
        Load();
    }

    TextBlock Note(string s) { var t = UI.Txt(s, Th.Dim, 12); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 8, 0, 4); return t; }
    UIElement Link(string s, Action a) => UI.Click(UI.Txt(s, Th.Dim, 12), a, null, 8, 4);

    UIElement Switch(bool on, Action<bool> set) {
        var knob = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Brushes.White,
            Margin = new Thickness(on ? 19 : 1, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Width = 38, Height = 20, CornerRadius = new CornerRadius(10), Background = on ? Th.Accent : Th.Tile, Cursor = Cursors.Hand, Child = knob };
        b.MouseLeftButtonUp += (_, _) => set(!on);
        return b;
    }

    UIElement Head(string title, string glyph, WRad.Radio r) {
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        var g = UI.Glyph(glyph, Th.Fg, 15); g.Margin = new Thickness(0, 0, 10, 0);
        left.Children.Add(g); left.Children.Add(UI.Txt(title, Th.Fg, 14, FontWeights.SemiBold));
        var dp = new DockPanel();
        UIElement right = r == null ? UI.Txt("Not available", Th.Dim, 11)
            : Switch(r.State == WRad.RadioState.On, async v => { await Net.SetRadio(r, v); await Task.Delay(400); if (!dead) Load(); });
        ((FrameworkElement)right).VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
        dp.Children.Add(right); dp.Children.Add(left);
        return dp;
    }

    async void Load() {
        try {
            var wr = await Net.Radio(WRad.RadioKind.WiFi);
            var br = await Net.Radio(WRad.RadioKind.Bluetooth);
            if (dead) return;
            root.Children.Clear(); wifiBox.Children.Clear(); btBox.Children.Clear();
            root.Children.Add(Head("Wi-Fi", "\uE701", wr)); root.Children.Add(wifiBox);
            root.Children.Add(new Border { Height = 1, Background = Th.Line, Margin = new Thickness(0, 12, 0, 12) });
            root.Children.Add(Head("Bluetooth", "\uE702", br)); root.Children.Add(btBox);
            FillWifi(wr == null || wr.State == WRad.RadioState.On);
            FillBt(br == null || br.State == WRad.RadioState.On);
        } catch { }
    }

    // ----- Wi-Fi -----
    async void FillWifi(bool on) {
        try {
            if (!on) { wifiBox.Children.Add(Note("Wi-Fi is off.")); return; }
            wifiBox.Children.Add(Note("Looking for networks…"));
            var list = await Net.Scan(false);                 // instant: whatever Windows already knows
            if (dead) return;
            if (list == null) { NoAccess(); return; }
            if (list.Count > 0) ShowNets(list);
            list = await Net.Scan(true);                      // then a fresh scan
            if (dead) return;
            ShowNets(list ?? new List<WifiNet>());
        } catch { }
    }
    void NoAccess() {
        wifiBox.Children.Clear();
        wifiBox.Children.Add(Note("Couldn't read Wi-Fi. Turn on Location access for desktop apps in Windows privacy settings."));
        wifiBox.Children.Add(Link("Open privacy settings", () => Net.Open("ms-settings:privacy-location")));
    }
    void ShowNets(List<WifiNet> list) {
        wifiBox.Children.Clear();
        if (list.Count == 0) wifiBox.Children.Add(Note("No networks found."));
        foreach (var n in list.Take(8)) wifiBox.Children.Add(NetRow(n));
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Children = {
            Link("Rescan", () => { wifiBox.Children.Clear(); FillWifi(true); }), Link("Wi-Fi settings", () => Net.Open("ms-settings:network-wifi")) } };
        if (list.Any(x => x.Current && x.Action)) links.Children.Add(Link("Disconnect", async () => { Net.Disconnect(); await Task.Delay(800); if (!dead) Load(); }));
        wifiBox.Children.Add(links);
    }
    UIElement NetRow(WifiNet n) {
        var g = UI.Glyph("\uE701", n.Current ? Th.Accent : Th.Fg, 14); g.Opacity = 0.35 + 0.16 * n.Bars; g.Margin = new Thickness(0, 0, 10, 0);
        var name = UI.Txt(n.Ssid, n.Current ? Th.Accent : Th.Fg, 13, n.Current ? FontWeights.SemiBold : FontWeights.Normal); name.TextTrimming = TextTrimming.CharacterEllipsis;
        var sub = UI.Txt(n.Action ? "Action needed, click to sign in" : n.Current ? (n.NoNet ? "Connected, no internet. Click to disconnect" : "Connected, click to disconnect") : n.Saved ? "Saved" : "", n.Action ? BattUI.Warn : Th.Dim, 10.5);
        if (n.Action) { name.Foreground = BattUI.Warn; g.Foreground = BattUI.Warn; }
        var mid = new StackPanel { Children = { name } }; if (sub.Text.Length > 0) mid.Children.Add(sub);
        var dp = new DockPanel();
        if (!n.Open) { var lk = UI.Glyph("\uE72E", Th.Dim, 11); DockPanel.SetDock(lk, System.Windows.Controls.Dock.Right); dp.Children.Add(lk); }
        if (n.Action) { var w = UI.Glyph("\uE7BA", BattUI.Warn, 13); DockPanel.SetDock(w, System.Windows.Controls.Dock.Right); dp.Children.Add(w); }
        DockPanel.SetDock(g, System.Windows.Controls.Dock.Left); dp.Children.Add(g); dp.Children.Add(mid);
        return UI.Click(dp, () => Pick(n), null, 8, 6);
    }
    async void Pick(WifiNet n) {
        try {
            if (n.Action) { Net.SignIn(); Close(); return; }      // "action needed": open the network's sign-in page
            if (n.Current) { Net.Disconnect(); await Task.Delay(800); if (!dead) Load(); return; }
            string pw = null;
            if (!n.Saved && !n.Open) { pw = PassDialog.Ask(n.Ssid); if (pw == null) return; }
            if (!dead) { wifiBox.Children.Clear(); wifiBox.Children.Add(Note("Connecting to " + n.Ssid + "…")); }
            bool ok = await Net.Connect(n, pw);
            if (dead) return;
            if (!ok) { wifiBox.Children.Clear(); wifiBox.Children.Add(Note("Couldn't connect to " + n.Ssid + (pw != null ? " (wrong password?)" : "") + ".")); await Task.Delay(2200); }
            if (!dead) Load();
        } catch { }
    }

    // ----- Bluetooth -----
    async void FillBt(bool on) {
        try {
            if (!on) { btBox.Children.Add(Note("Bluetooth is off.")); return; }
            btBox.Children.Add(Note("Checking paired devices…"));
            var list = await Net.BtDevices();
            if (dead) return;
            btBox.Children.Clear();
            if (list.Count == 0) btBox.Children.Add(Note("No paired devices."));
            foreach (var d in list.OrderByDescending(x => x.Connected).ThenBy(x => x.Name).Take(8)) {
                var name = UI.Txt(d.Name, d.Connected ? Th.Accent : Th.Fg, 13, d.Connected ? FontWeights.SemiBold : FontWeights.Normal); name.TextTrimming = TextTrimming.CharacterEllipsis;
                var g = UI.Glyph("\uE702", d.Connected ? Th.Accent : Th.Dim, 13); g.Margin = new Thickness(0, 0, 10, 0);
                var st = UI.Txt(d.Connected ? "Connected" : "Paired", Th.Dim, 10.5);
                var dp = new DockPanel();
                DockPanel.SetDock(g, System.Windows.Controls.Dock.Left); dp.Children.Add(g);
                dp.Children.Add(new StackPanel { Children = { name, st } });
                btBox.Children.Add(UI.Click(dp, () => Net.Open("ms-settings:bluetooth"), null, 8, 6));
            }
            btBox.Children.Add(Link("Pair a new device / manage", () => Net.Open("ms-settings:bluetooth")));
        } catch { }
    }
}

// ---------------- password prompt (a normal, focusable window: the bar's own windows can't take keyboard input) ----------------
class PassDialog : Window {
    string result; readonly PasswordBox pb = new();
    public static string Ask(string ssid) { var d = new PassDialog(ssid); d.ShowDialog(); return d.result; }
    PassDialog(string ssid) {
        Title = "Wi-Fi"; Width = 340; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStyle = WindowStyle.ToolWindow;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true; ShowInTaskbar = false; Background = Th.Bg;
        pb.Margin = new Thickness(0, 10, 0, 14); pb.Padding = new Thickness(6); pb.FontSize = 14;
        pb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Done(true); else if (e.Key == Key.Escape) Done(false); };
        var ok = UI.Click(UI.Txt("Connect", Brushes.White), () => Done(true), Th.Accent, 14, 6);
        var no = UI.Click(UI.Txt("Cancel", Th.Fg), () => Done(false), Th.Tile, 14, 6); no.Margin = new Thickness(0, 0, 8, 0);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, ok } };
        Content = new StackPanel { Margin = new Thickness(18), Children = { UI.Txt("Password for " + ssid, Th.Fg, 14, FontWeights.SemiBold), pb, btns } };
        Loaded += (_, _) => { Activate(); pb.Focus(); };
    }
    void Done(bool ok) { result = ok && pb.Password.Length > 0 ? pb.Password : null; Close(); }
}
