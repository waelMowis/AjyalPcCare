using System.ComponentModel;
using System.Diagnostics;
using AjyalPcCare.Core.Common;
using AjyalPcCare.UI.Controls;
using AjyalPcCare.UI.Pages;
using AjyalPcCare.UI.Services;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI;

public sealed partial class MainForm : Form
{
    private readonly Panel _host = new() { Dock = DockStyle.Fill, BackColor = UiTheme.Background };
    private readonly Panel _nav = new() { Dock = DockStyle.Left, Width = UiTheme.Px(230), BackColor = UiTheme.Brand, Padding = new Padding(0, 0, 0, 10) };
    private readonly Dictionary<string, (string Text, Func<PageBase> Factory)> _routes;
    private readonly Dictionary<string, PageBase> _pages = new();
    private readonly Dictionary<string, Button> _navButtons = new();
    private PageBase? _current;
    private readonly ToolStripStatusLabel _statusMem = new();
    private readonly ToolStripStatusLabel _statusCpu = new();

    public MainForm()
    {
        Text = $"AJYAL PC Care — صيانة الأجهزة {(AppEnvironment.IsAdministrator ? "" : "(وضع القراءة فقط)")}";
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true; // مع Dock.Left القائمة بتظهر يمين
        Font = UiTheme.Base;
        BackColor = UiTheme.Background;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(UiTheme.Px(1100), UiTheme.Px(700));
        Size = new Size(UiTheme.Px(1360), UiTheme.Px(860));
        // المقاسات الثابتة بتتحول بـ UiTheme.Px — من غير AutoScale عشان ما يحصلش تحجيم مزدوج
        AutoScaleMode = AutoScaleMode.None;
        try { Icon = Icon.ExtractAssociatedIcon(AppEnvironment.ExecutablePath); } catch { /* ignore */ }

        _routes = new()
        {
            ["dashboard"] = ("لوحة المراقبة", () => new DashboardPage()),
            ["memory"] = ("الذاكرة", () => new MemoryPage()),
            ["cleanup"] = ("تنظيف الملفات", () => new CleanupPage()),
            ["startup"] = ("بدء التشغيل", () => new StartupPage()),
            ["uninstall"] = ("إلغاء التثبيت", () => new UninstallPage()),
            ["health"] = ("صحة الجهاز", () => new HealthPage()),
            ["lab"] = ("وضع المعامل", () => new LabPage()),
            ["history"] = ("السجل والتراجع", () => new HistoryPage())
        };

        BuildNav();

        var status = new StatusStrip { BackColor = UiTheme.Card, SizingGrip = false, RightToLeft = RightToLeft.Yes };
        status.Items.Add(new ToolStripStatusLabel($"الجهاز: {Environment.MachineName}"));
        status.Items.Add(new ToolStripStatusLabel($"المستخدم: {Environment.UserName}"));
        status.Items.Add(new ToolStripStatusLabel(AppEnvironment.IsAdministrator ? "مسؤول ✓" : "بدون صلاحيات مسؤول") { ForeColor = AppEnvironment.IsAdministrator ? UiTheme.Success : UiTheme.Warning });
        status.Items.Add(_statusCpu);
        status.Items.Add(_statusMem);
        status.Items.Add(_statusUpdate);
        status.Items.Add(new ToolStripStatusLabel($"الخط: {UiTheme.FontName}") { Spring = true, TextAlign = ContentAlignment.MiddleLeft });

        // ترتيب الإضافة مهم للـ Dock: آخر واحد بيترتب الأول
        Controls.Add(_host);
        if (!AppEnvironment.IsAdministrator) Controls.Add(BuildAdminBanner());
        Controls.Add(_nav);
        Controls.Add(status);

        AppServices.Instance.Updated += OnMonitor;
        Shown += (_, _) =>
        {
            Navigate("dashboard");
            if (!SmokeTest.Active) AfterStartupUpdateCheck();
        };
    }

    private void BuildNav()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = UiTheme.Px(96), BackColor = UiTheme.BrandDark };
        var title = new Label
        {
            Text = "AJYAL PC Care",
            Dock = DockStyle.Top,
            Height = UiTheme.Px(50),
            Font = UiTheme.Font(UiTheme.BaseSize + 4, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.BottomCenter
        };
        var sub = new Label
        {
            Text = "مدارس أجيال اليرموك الأهلية",
            Dock = DockStyle.Top,
            Height = UiTheme.Px(30),
            Font = UiTheme.Small,
            ForeColor = Color.FromArgb(200, 230, 222),
            TextAlign = ContentAlignment.TopCenter
        };
        header.Controls.Add(sub);
        header.Controls.Add(title);

        var list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(UiTheme.Px(10), UiTheme.Px(14), UiTheme.Px(10), 0),
            BackColor = UiTheme.Brand
        };

        foreach (var (key, route) in _routes)
        {
            var b = new Button
            {
                Text = route.Text,
                Width = _nav.Width - UiTheme.Px(20),
                Height = Measure.LineHeight(UiTheme.NavActive) + UiTheme.Px(20),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = UiTheme.Brand,
                Font = UiTheme.Nav,
                TextAlign = ContentAlignment.MiddleLeft, // بداية السطر = يمين في RTL
                Padding = new Padding(UiTheme.Px(14), 0, UiTheme.Px(10), 0),
                Margin = new Padding(0, UiTheme.Px(2), 0, UiTheme.Px(2)),
                Cursor = Cursors.Hand,
                Tag = key
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = UiTheme.BrandDark;
            b.Click += (_, _) => Navigate(key);
            _navButtons[key] = b;
            list.Controls.Add(b);
        }

        var version = new Label
        {
            Text = $"الإصدار {AppEnvironment.Version}",
            Dock = DockStyle.Bottom,
            Height = Measure.LineHeight(UiTheme.Small) + UiTheme.Px(14),
            ForeColor = Color.FromArgb(180, 215, 205),
            Font = UiTheme.Small,
            TextAlign = ContentAlignment.MiddleCenter
        };

        BuildUpdateNavButtons();

        // ترتيب الـ Dock: آخر واحد بيتحط الأول (العنوان فوق، الإصدار تحت خالص، فوقه زرار التحقق، فوقه "فيه تحديث")
        _nav.Controls.Add(list);
        _nav.Controls.Add(_updateAvailableButton);
        _nav.Controls.Add(_checkUpdatesButton);
        _nav.Controls.Add(version);
        _nav.Controls.Add(header);
    }

    private Control BuildAdminBanner()
    {
        var p = new Panel { Dock = DockStyle.Top, Height = Measure.LineHeight(UiTheme.Base) + UiTheme.Px(26), BackColor = UiTheme.WarningLight, Padding = new Padding(UiTheme.Px(16), UiTheme.Px(6), UiTheme.Px(16), UiTheme.Px(6)) };
        var btn = Ui.Button("إعادة التشغيل كمسؤول", true, (_, _) => RestartAsAdmin());
        btn.Dock = DockStyle.Left;
        var lbl = new Label
        {
            Text = "وضع القراءة فقط — المراقبة والفحص متاحين، والعمليات اللي بتغيّر في الجهاز مقفولة.",
            Dock = DockStyle.Fill,
            ForeColor = UiTheme.Warning,
            Font = UiTheme.Base,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        p.Controls.Add(lbl);
        p.Controls.Add(btn);
        return p;
    }

    private void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppEnvironment.ExecutablePath) { UseShellExecute = true, Verb = "runas" });
            Application.Exit();
        }
        catch (Win32Exception)
        {
            // المستخدم رفض نافذة UAC — نفضل في وضع القراءة
        }
    }

    public void Navigate(string key)
    {
        if (!_routes.TryGetValue(key, out var route)) return;
        if (_current is not null && _pages.TryGetValue(key, out var existing) && existing == _current) return;

        _current?.OnDeactivated();
        if (!_pages.TryGetValue(key, out var page))
        {
            try
            {
                page = route.Factory();
            }
            catch (Exception ex)
            {
                AppLogger.Error("UI", $"تعذر فتح صفحة {route.Text}", ex);
                Dialogs.Error(this, $"تعذر فتح الصفحة: {ex.Message}");
                return;
            }
            _pages[key] = page;
            _host.Controls.Add(page);
        }

        page.BringToFront();
        page.Visible = true;
        foreach (var p in _pages.Values.Where(p => p != page)) p.Visible = false;
        _current = page;

        foreach (var (k, b) in _navButtons)
        {
            bool active = k == key;
            b.BackColor = active ? UiTheme.Card : UiTheme.Brand;
            b.ForeColor = active ? UiTheme.Brand : Color.White;
            b.Font = active ? UiTheme.NavActive : UiTheme.Nav;
        }

        try
        {
            page.OnActivated();
        }
        catch (Exception ex)
        {
            AppLogger.Error("UI", "OnActivated", ex);
        }
    }

    /// <summary>فحص الفتح: يفتح كل صفحة ويتأكد إنها اتبنت من غير أخطاء.</summary>
    internal void RunSmokeChecks()
    {
        foreach (var key in _routes.Keys)
        {
            Navigate(key);
            if (!_pages.ContainsKey(key)) throw new InvalidOperationException($"الصفحة {key} ماتفتحتش");
            for (int i = 0; i < 10; i++)
            {
                Application.DoEvents();
                Thread.Sleep(40);
            }
            Console.WriteLine($"SMOKE page OK: {key}");
        }
        Navigate("dashboard");
        Application.DoEvents();
    }

    private void OnMonitor(MonitorUpdate u)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                _statusCpu.Text = $"المعالج: {Format.Percent(u.Snapshot.CpuPercent)}";
                _statusMem.Text = $"الذاكرة: {Format.Percent(u.Snapshot.MemoryPercent)}";
            });
        }
        catch (InvalidOperationException) { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!SmokeTest.Active && !_closingForUpdate && _pages.Values.Any(p => p.IsBusy) &&
            !Dialogs.Confirm(this, "فيه عملية شغالة دلوقتي. الخروج هيلغيها — متأكد؟", "تأكيد الخروج", true))
        {
            e.Cancel = true;
            return;
        }
        AppServices.Instance.Updated -= OnMonitor;
        base.OnFormClosing(e);
    }
}
