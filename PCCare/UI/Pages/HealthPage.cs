using System.Diagnostics;
using System.Text;
using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Health;
using AjyalPcCare.Core.Reports;
using AjyalPcCare.UI.Controls;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Pages;

public sealed class HealthPage : PageBase
{
    public override string PageTitle => "صحة الجهاز";
    public override string PageDescription => "المؤشر محسوب من قواعد معلنة — كل خصم ليه سبب مكتوب";

    public static HealthReport? LastReport { get; set; }

    private readonly Label _score = Ui.Note("—", UiTheme.Muted, UiTheme.Huge);
    private readonly Label _grade = Ui.Label("", UiTheme.Subtitle);
    private readonly DataGridView _findings = Ui.Grid();
    private readonly Label _os, _cpu, _ram, _uptime, _update, _pagefile, _startup;
    private readonly DataGridView _disks = Ui.Grid();
    private readonly DataGridView _volumes = Ui.Grid();
    private readonly ComboBox _plans = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly Label _planAdvice = Ui.Label("", UiTheme.Base, UiTheme.Muted);
    private readonly ProgressBar _progress = Ui.Progress();
    private readonly Label _status = Ui.Label("", UiTheme.Base, UiTheme.Muted);
    private readonly Button _analyzeBtn, _optimizeBtn, _applyPlanBtn, _restartBtn, _exportBtn;

    public HealthPage()
    {
        _analyzeBtn = Ui.Button("تحليل الآن", true, async (_, _) => await AnalyzeAsync());
        _exportBtn = Ui.Button("تصدير تقرير HTML", false, async (_, _) => await ExportAsync());
        var scoreCard = new FlowCard("مؤشر الصحة");
        scoreCard.Add(_score);
        scoreCard.Add(_grade);
        scoreCard.AddButtons(_analyzeBtn, _exportBtn);
        scoreCard.Add(_progress, stretch: true);
        scoreCard.Add(_status);

        var sysCard = new FlowCard("معلومات النظام");
        _os = sysCard.AddField("نظام التشغيل");
        _cpu = sysCard.AddField("المعالج");
        _ram = sysCard.AddField("الذاكرة");
        _uptime = sysCard.AddField("مدة التشغيل منذ آخر Restart");
        _update = sysCard.AddField("آخر تحديث ويندوز");
        _pagefile = sysCard.AddField("ملف الصفحات (Page File)");
        _startup = sysCard.AddField("برامج Startup المفعّلة");
        _restartBtn = Ui.Button("إعادة تشغيل الجهاز", false, (_, _) => Restart());
        sysCard.AddButtons(_restartBtn);

        var top = Columns(scoreCard, sysCard);
        top.MinColumnWidth = UiTheme.Px(360);

        var findingsCard = new Card("الملاحظات والقواعد") { Height = 280 };
        _findings.Columns.AddRange(
            Ui.TextColumn("Rule", "القاعدة", 35),
            Ui.TextColumn("Title", "الملاحظة", 120),
            Ui.TextColumn("Detail", "التفاصيل", 140, wrap: true),
            Ui.TextColumn("Penalty", "الخصم", 35),
            Ui.TextColumn("Rec", "التوصية", 220, wrap: true));
        findingsCard.AddContent(_findings);

        var disksCard = new Card("الديسكات (SMART / Storage Reliability)") { Height = 210 };
        _disks.Columns.AddRange(
            Ui.TextColumn("Name", "الاسم", 140),
            Ui.TextColumn("Type", "النوع", 60),
            Ui.TextColumn("Health", "الحالة", 80),
            Ui.TextColumn("Size", "الحجم", 60),
            Ui.TextColumn("Temp", "الحرارة", 45),
            Ui.TextColumn("Wear", "الاستهلاك", 50),
            Ui.TextColumn("Hours", "ساعات التشغيل", 60),
            Ui.TextColumn("Letters", "الدرايفات", 50));
        disksCard.AddContent(_disks);

        var volCard = new Card("المساحات") { Height = 210 };
        _volumes.Columns.AddRange(
            Ui.TextColumn("Letter", "الدرايف", 40),
            Ui.TextColumn("Label", "الاسم", 90),
            Ui.TextColumn("Fs", "نظام الملفات", 50),
            Ui.TextColumn("Total", "الكلي", 60),
            Ui.TextColumn("Free", "الفاضي", 60),
            Ui.TextColumn("Pct", "النسبة", 40));
        volCard.AddContent(_volumes);

        _optimizeBtn = Ui.Button("تحسين الدرايفات (TRIM / Defrag)", false, async (_, _) => await OptimizeAsync());
        _applyPlanBtn = Ui.Button("تطبيق", false, async (_, _) => await ApplyPlanAsync());
        var tools = new FlowCard("أدوات");
        tools.Add(Ui.Label("خطة الطاقة:", UiTheme.Bold));
        tools.AddButtons(_plans, _applyPlanBtn);
        tools.Add(_planAdvice);
        tools.Add(Ui.Label("تحسين الدرايفات: SSD ← TRIM فقط (Defrag ممنوع على SSD)، HDD ← Defrag، غير محدد ← ويندوز يختار.", UiTheme.Bold));
        tools.AddButtons(_optimizeBtn);

        if (!AppEnvironment.IsAdministrator) _optimizeBtn.Enabled = _applyPlanBtn.Enabled = false;

        Stack(top, findingsCard, disksCard, volCard, tools);
    }

    public override void OnActivated()
    {
        if (LastReport is not null) Render(LastReport);
        else _ = AnalyzeAsync();
    }

    private void SetBusy(bool busy)
    {
        _progress.Visible = busy;
        _analyzeBtn.Enabled = _exportBtn.Enabled = !busy;
        _optimizeBtn.Enabled = _applyPlanBtn.Enabled = !busy && AppEnvironment.IsAdministrator;
    }

    private Progress<ModuleProgress> MakeProgress() => new(p => { _progress.Value = Math.Clamp(p.Percent, 0, 100); _status.Text = p.Message; });

    private async Task AnalyzeAsync()
    {
        await RunGuardedAsync(async ct =>
        {
            var report = await HealthService.AnalyzeAsync(SettingsStore.Current.Health, MakeProgress(), ct);
            LastReport = report;
            Render(report);
            _status.Text = $"آخر تحليل: {report.GeneratedAt:HH:mm:ss}";
        }, SetBusy);
    }

    private void Render(HealthReport r)
    {
        _score.Text = r.Score.ToString();
        _score.ForeColor = UiTheme.ScoreColor(r.Score);
        _grade.Text = $"{r.Grade} — من 100";

        _os.Text = $"{r.System.OsName} {r.System.OsVersion} (Build {r.System.Build})";
        _cpu.Text = $"{r.System.CpuName} — {r.System.LogicalCores} أنوية منطقية";
        _ram.Text = Format.Bytes(r.System.TotalRam);
        _uptime.Text = Format.Duration(r.System.Uptime);
        _uptime.ForeColor = r.System.Uptime.TotalDays > SettingsStore.Current.Health.UptimeWarningDays ? UiTheme.Warning : UiTheme.Text;
        _update.Text = r.Extras?.LastUpdateInstalled is DateTime d ? $"{d:yyyy-MM-dd} ({(int)(DateTime.Now - d).TotalDays} يوم) — {r.Extras.LastUpdateTitle}" : "غير معروف";
        _pagefile.Text = r.Extras is { } ex
            ? (ex.PageFiles.Count == 0 ? "غير موجود" : string.Join(" | ", ex.PageFiles.Select(p => $"{p.Name}: {p.AllocatedBaseSize:N0} م.ب (مستخدم {p.CurrentUsage:N0}، أقصى {p.PeakUsage:N0})")))
              + (ex.AutomaticManagedPagefile == true ? " — يديره ويندوز تلقائيًا (الأفضل)" : ex.AutomaticManagedPagefile == false ? " — حجم يدوي" : "")
            : "—";
        _startup.Text = r.StartupEnabledCount is int sc ? $"{sc}{(r.StartupBrokenCount is > 0 ? $" (مكسور: {r.StartupBrokenCount})" : "")}" : "—";

        _findings.Rows.Clear();
        if (r.Findings.Count == 0) _findings.Rows.Add("—", "مفيش مشاكل مكتشفة", "", "0", "");
        foreach (var f in r.Findings)
        {
            int i = _findings.Rows.Add(f.Rule, f.Title, f.Detail, "-" + f.Penalty, f.Recommendation);
            _findings.Rows[i].Cells["Title"].Style.ForeColor = UiTheme.SeverityColor(f.Severity);
        }

        _disks.Rows.Clear();
        foreach (var d2 in r.Disks)
        {
            int i = _disks.Rows.Add((d2.FriendlyName ?? "") + (d2.IsSystemDisk ? " (النظام)" : ""), d2.MediaDisplay, d2.HealthDisplay, Format.Bytes(d2.Size),
                d2.Temperature is int t && t > 0 ? t + "°" : "—", d2.Wear is int w ? w + "%" : "—", d2.PowerOnHours?.ToString("N0") ?? "—", d2.DriveLetters ?? "");
            _disks.Rows[i].Cells["Health"].Style.ForeColor = d2.IsHealthy ? UiTheme.Success : UiTheme.Danger;
        }
        if (r.Disks.Count == 0) _disks.Rows.Add("تعذر قراءة الديسكات (Storage module غير متاح؟)", "", "", "", "", "", "", "");

        _volumes.Rows.Clear();
        foreach (var v in r.Volumes)
        {
            int i = _volumes.Rows.Add(v.Letter, v.Label, v.FileSystem, Format.Bytes(v.Total), Format.Bytes(v.Free), v.FreePercent.ToString("0.#") + "%");
            var hs = SettingsStore.Current.Health;
            _volumes.Rows[i].Cells["Pct"].Style.ForeColor = v.FreePercent < hs.CriticalDiskPercent ? UiTheme.Danger : v.FreePercent < hs.LowDiskPercent ? UiTheme.Warning : UiTheme.Success;
        }

        _plans.Items.Clear();
        foreach (var p in r.PowerPlans)
        {
            _plans.Items.Add(new PlanItem(p));
            if (p.Active) _plans.SelectedIndex = _plans.Items.Count - 1;
        }
        var active = r.PowerPlans.FirstOrDefault(p => p.Active);
        _planAdvice.Text = active is null ? "" : PowerPlanService.Advice(active.Id);
    }

    private sealed record PlanItem(PowerPlan Plan)
    {
        public override string ToString() => Plan.Name + (Plan.Active ? " (الحالية)" : "");
    }

    private async Task ApplyPlanAsync()
    {
        if (!RequireAdmin() || _plans.SelectedItem is not PlanItem item || item.Plan.Active) return;
        await RunGuardedAsync(async ct =>
        {
            bool ok = await PowerPlanService.SetActiveAsync(item.Plan.Id, ct);
            if (!ok) { Dialogs.Error(FindForm(), "تعذر تغيير خطة الطاقة"); return; }
            if (LastReport is not null) LastReport.PowerPlans = await PowerPlanService.ListAsync(ct);
            if (LastReport is not null) Render(LastReport);
            _status.Text = "تم تغيير خطة الطاقة";
        }, SetBusy);
    }

    private async Task OptimizeAsync()
    {
        if (!RequireAdmin()) return;
        await RunGuardedAsync(async ct =>
        {
            var m = new DriveOptimizeModule();
            var preview = await ModuleRunner.SafeAnalyzeAsync(m, MakeProgress(), ct);
            var items = preview.Items.Where(i => i.Available).ToList();
            if (items.Count == 0) { Dialogs.Info(FindForm(), "مفيش درايفات متاحة للتحسين."); return; }

            var sb = new StringBuilder("هيتم تنفيذ:\n\n");
            foreach (var i in items) sb.Append($"• {i.Title}: {i.Description}\n");
            if (items.Any(i => i.Warning is not null)) sb.Append("\n⚠ Defrag على HDD ممكن ياخد وقت طويل.");
            sb.Append("\n\nتكمل؟");
            if (!Dialogs.Confirm(FindForm(), sb.ToString(), "تأكيد")) return;

            var r = await ModuleRunner.SafeExecuteAsync(m, preview, MakeProgress(), ct);
            Dialogs.Info(FindForm(), string.Join("\n", r.Messages.Concat(r.Errors.Select(e => "✖ " + e))));
        }, SetBusy);
    }

    private async Task ExportAsync()
    {
        await RunGuardedAsync(async ct =>
        {
            var h = LastReport ?? await HealthService.AnalyzeAsync(SettingsStore.Current.Health, MakeProgress(), ct);
            LastReport = h;
            var report = new MachineReport();
            report.ApplyHealth(h);
            var files = await ReportWriter.WriteAsync(report, null, ct);
            Ui.OpenPath(files.HtmlPath);
        }, SetBusy);
    }

    private void Restart()
    {
        if (!Dialogs.Confirm(FindForm(), "إعادة تشغيل الجهاز الآن؟ احفظ شغلك الأول.", "تأكيد", true)) return;
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"AJYAL PC Care\"") { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex)
        {
            Dialogs.Error(FindForm(), ex.Message);
        }
    }
}
