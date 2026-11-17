using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Health;
using AjyalPcCare.Core.Memory;
using AjyalPcCare.Core.Monitoring;
using AjyalPcCare.UI.Controls;
using AjyalPcCare.UI.Services;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Pages;

public sealed class DashboardPage : PageBase
{
    public override string PageTitle => "لوحة المراقبة";
    public override string PageDescription => "قراءات لحظية حقيقية من النظام — تتحدث كل ثانية";

    private readonly MetricCard _cpu = new("المعالج") { Accent = UiTheme.ChartCpu };
    private readonly MetricCard _mem = new("الذاكرة المستخدمة") { Accent = UiTheme.ChartMemory };
    private readonly MetricCard _disk = new("نشاط الديسك") { Accent = UiTheme.ChartDisk };
    private readonly MetricCard _net = new("الشبكة") { Accent = UiTheme.ChartNet };

    private readonly LineChart _cpuChart = new() { Title = "المعالج %", Height = 230 };
    private readonly LineChart _memChart = new() { Title = "الذاكرة %", Height = 230 };
    private readonly LineChart _diskChart = new() { Title = "نشاط الديسك %", Height = 230 };

    private Label _mUsed = null!, _mAvail = null!, _mStandby = null!, _mModified = null!, _mFree = null!, _mCommit = null!, _mProc = null!;
    private readonly Label _score = Ui.Note("—", UiTheme.Muted, UiTheme.Huge);
    private readonly Label _grade = Ui.Note("اضغط \"احسب المؤشر\"", UiTheme.Text, UiTheme.Subtitle);
    private readonly Label _findings = Ui.Note("");
    private readonly Button _calcBtn;
    private readonly Button _cleanMemBtn;
    private readonly Label _cleanResult = Ui.Note("", UiTheme.Success, UiTheme.Bold);

    private readonly DataGridView _grid = Ui.Grid();
    private readonly RadioButton _byMem = new() { Text = "ترتيب حسب الذاكرة", Checked = true, AutoSize = true, Margin = new Padding(8, 4, 8, 4) };
    private readonly RadioButton _byCpu = new() { Text = "ترتيب حسب المعالج", AutoSize = true, Margin = new Padding(8, 4, 8, 4) };
    private readonly Label _leaks = Ui.Note("", UiTheme.Warning, UiTheme.Bold);

    private bool _active;
    private bool _healthCalculated;

    public DashboardPage()
    {
        // كروت الأرقام: 4 جنب بعض (بتنزل صفين تلقائيًا لو الشاشة ضيقة)
        var cards = Columns(_cpu, _mem, _disk, _net);

        // الرسوم
        _cpuChart.Series.Add(new ChartSeries { Name = "CPU", Color = UiTheme.ChartCpu });
        _memChart.Series.Add(new ChartSeries { Name = "RAM", Color = UiTheme.ChartMemory });
        _diskChart.Series.Add(new ChartSeries { Name = "Disk", Color = UiTheme.ChartDisk });
        var charts = Columns(_cpuChart, _memChart, _diskChart);
        charts.MinColumnWidth = UiTheme.Px(260);

        var memCard = new FlowCard("تفاصيل الذاكرة");
        _mUsed = memCard.AddField("المستخدم فعليًا");
        _mAvail = memCard.AddField("المتاح لأي برنامج");
        _mStandby = memCard.AddField("Standby (كاش)");
        _mModified = memCard.AddField("Modified");
        _mFree = memCard.AddField("فارغة فعليًا");
        _mCommit = memCard.AddField("Commit (الضغط الحقيقي)");
        _mProc = memCard.AddField("البروسيسات / Threads");
        _cleanMemBtn = Ui.Button("🧹 نظّف الذاكرة", true, async (_, _) => await CleanMemoryAsync());
        _cleanMemBtn.Enabled = AppEnvironment.IsAdministrator;
        memCard.AddButtons(_cleanMemBtn, Ui.Button("خيارات أكتر ←", false, (_, _) => (FindForm() as MainForm)?.Navigate("memory")));
        memCard.Add(_cleanResult);
        _cleanResult.Visible = false;
        memCard.Add(Ui.Note("Standby = كاش مفيد بيتنازل عنه ويندوز فورًا لأي برنامج محتاجه — مش ذاكرة مهدرة. المؤشر الحقيقي للضغط هو Commit.", UiTheme.Muted, UiTheme.Small));

        // مؤشر الصحة
        var healthCard = new FlowCard("مؤشر صحة الجهاز");
        _calcBtn = Ui.Button("احسب المؤشر", true, async (_, _) => await CalculateHealthAsync());
        healthCard.Add(_score);
        healthCard.Add(_grade);
        healthCard.Add(_findings);
        healthCard.AddButtons(_calcBtn, Ui.Button("تفاصيل الصحة ←", false, (_, _) => (FindForm() as MainForm)?.Navigate("health")));

        var info = Columns(memCard, healthCard);
        info.MinColumnWidth = UiTheme.Px(360);

        // البروسيسات
        var procCard = new Card("أعلى 10 بروسيسات") { Height = 420 };
        _grid.Columns.AddRange(
            Ui.TextColumn("Name", "الاسم", 120),
            Ui.TextColumn("Pid", "PID", 45),
            Ui.TextColumn("Cpu", "المعالج %", 55),
            Ui.TextColumn("Private", "الذاكرة الخاصة", 70),
            Ui.TextColumn("WS", "Working Set", 70),
            Ui.TextColumn("Publisher", "الناشر", 120),
            Ui.TextColumn("Path", "المسار", 220));
        var sortBar = Ui.Toolbar();
        sortBar.Controls.Add(_byMem);
        sortBar.Controls.Add(_byCpu);
        _byMem.CheckedChanged += (_, _) => RefreshProcesses();
        procCard.AddContent(sortBar);
        procCard.AddContent(_grid);
        _grid.BringToFront();

        _leaks.Visible = false;
        Stack(cards, charts, info, _leaks, procCard);
        AppServices.Instance.Updated += OnUpdate;
        Disposed += (_, _) => AppServices.Instance.Updated -= OnUpdate;
    }

    public override void OnActivated()
    {
        _active = true;
        if (AppServices.Instance.Last is { } last) Render(last);
        if (!_healthCalculated) _ = CalculateHealthAsync();
    }

    public override void OnDeactivated() => _active = false;

    private void OnUpdate(MonitorUpdate u)
    {
        if (_active) OnUi(() => Render(u));
    }

    private void Render(MonitorUpdate u)
    {
        var s = u.Snapshot;
        var history = AppServices.Instance.Monitor.History;

        _cpu.SetValue(Format.Percent(s.CpuPercent), $"{Environment.ProcessorCount} أنوية منطقية", s.CpuPercent);
        if (s.Memory is { } m)
        {
            _mem.SetValue(Format.Percent(s.MemoryPercent), $"{Format.Bytes(s.UsedPhysical)} من {Format.Bytes(m.TotalPhysical)}", s.MemoryPercent);
            _mUsed.Text = $"{Format.Bytes(s.UsedPhysical)} من {Format.Bytes(m.TotalPhysical)}";
            _mAvail.Text = Format.Bytes(m.AvailablePhysical);
            if (s.Lists is { } l)
            {
                _mStandby.Text = Format.Bytes(l.Standby);
                _mModified.Text = Format.Bytes(l.Modified);
                _mFree.Text = Format.Bytes(l.Free + l.Zeroed);
            }
            else
            {
                _mStandby.Text = _mModified.Text = _mFree.Text = "يحتاج صلاحيات مسؤول";
            }
            _mCommit.Text = $"{Format.Bytes(m.CommitTotal)} من {Format.Bytes(m.CommitLimit)} ({s.CommitPercent:0}%)";
            _mCommit.ForeColor = s.CommitPercent > 90 ? UiTheme.Danger : s.CommitPercent > 80 ? UiTheme.Warning : UiTheme.Text;
            _mProc.Text = $"{m.ProcessCount:N0} / {m.ThreadCount:N0}";
        }

        if (s.DiskActivePercent is double d)
            _disk.SetValue(Format.Percent(d), $"قراءة {Format.Rate(s.DiskReadBps ?? 0)} • كتابة {Format.Rate(s.DiskWriteBps ?? 0)}", d);
        else
            _disk.SetValue("—", "عدادات الديسك غير متاحة", 0);

        _net.SetValue(Format.Rate(s.NetReceiveBps), $"إرسال {Format.Rate(s.NetSendBps)}", 0);

        _cpuChart.Series[0].Values = history.Select(h => h.CpuPercent).ToArray();
        _cpuChart.ValueText = Format.Percent(s.CpuPercent);
        _memChart.Series[0].Values = history.Select(h => h.MemoryPercent).ToArray();
        _memChart.ValueText = Format.Percent(s.MemoryPercent);
        _diskChart.Series[0].Values = history.Select(h => h.DiskActivePercent ?? 0).ToArray();
        _diskChart.ValueText = s.DiskActivePercent is double dd ? Format.Percent(dd) : "—";
        _cpuChart.Invalidate();
        _memChart.Invalidate();
        _diskChart.Invalidate();

        if (u.Processes is not null) RenderProcesses(u.Processes);

        _leaks.Visible = u.LeakSuspects.Count > 0;
        _leaks.Text = u.LeakSuspects.Count == 0
            ? ""
            : "⚠ اشتباه تسريب ذاكرة: " + string.Join("، ", u.LeakSuspects.Take(3).Select(x => $"{x.Name} (+{Format.Bytes(x.Growth)} خلال {Format.Duration(x.Window)})")) + " — شوف صفحة الذاكرة";
    }

    private IReadOnlyList<ProcessInfo> _procs = [];

    private void RenderProcesses(IReadOnlyList<ProcessInfo> procs)
    {
        _procs = procs;
        RefreshProcesses();
    }

    private void RefreshProcesses()
    {
        var top = _byMem.Checked ? ProcessMonitor.TopByMemory(_procs) : ProcessMonitor.TopByCpu(_procs);
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var p in top)
            _grid.Rows.Add(p.Name, p.Pid, p.CpuPercent.ToString("0.0"), Format.Bytes(p.PrivateBytes), Format.Bytes(p.WorkingSet), p.Publisher ?? "—", p.Path ?? "(محمي)");
        _grid.ResumeLayout();
    }

    private async Task CleanMemoryAsync()
    {
        await RunGuardedAsync(async ct =>
        {
            var options = new OptimizeOptions { TrimIdleProcesses = SettingsStore.Current.Memory.OptimizeTrimIdle };
            var r = await MemoryOptimizer.OptimizeAsync(options, AppServices.Instance.Last?.Processes, ct);
            _cleanResult.Visible = true;
            _cleanResult.ForeColor = r.Errors.Count == 0 ? UiTheme.Success : UiTheme.Warning;
            _cleanResult.Text = $"✓ الفاضية زادت {Format.Bytes(Math.Max(0, r.FreeGained))}" +
                                (r.InUseReduced > 0 ? $" — المستخدم قلّ {Format.Bytes(r.InUseReduced)}" : "") +
                                (r.Errors.Count > 0 ? " — " + string.Join("، ", r.Errors) : "");
        }, busy => _cleanMemBtn.Enabled = !busy && AppEnvironment.IsAdministrator);
    }

    private async Task CalculateHealthAsync()
    {
        await RunGuardedAsync(async ct =>
        {
            _grade.Text = "جاري الحساب...";
            var report = await HealthService.AnalyzeAsync(SettingsStore.Current.Health, null, ct);
            _healthCalculated = true;
            _score.Text = report.Score.ToString();
            _score.ForeColor = UiTheme.ScoreColor(report.Score);
            _grade.Text = $"{report.Grade} — من 100";
            _findings.Text = report.Findings.Count == 0
                ? "مفيش مشاكل مكتشفة"
                : string.Join("\n", report.Findings.Take(4).Select(f => $"• {f.Title} (-{f.Penalty})"));
            HealthPage.LastReport = report;
        }, busy => _calcBtn.Enabled = !busy);
    }
}
