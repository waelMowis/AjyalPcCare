using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Memory;
using AjyalPcCare.Core.Monitoring;
using AjyalPcCare.UI.Controls;
using AjyalPcCare.UI.Services;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Pages;

public sealed class MemoryPage : PageBase
{
    public override string PageTitle => "إدارة الذاكرة";
    public override string PageDescription => "أدوات حقيقية بأثر مقاس — ومن غير أي تفريغ أعمى";

    private readonly Label _free, _standby, _modified, _commit, _available, _byPriority;
    private readonly Label _lastResult = Ui.Label("", UiTheme.Bold, UiTheme.Success);
    private readonly CheckBox _autoEnabled = new() { Text = "تفعيل التفريغ التلقائي المشروط", AutoSize = true };
    private readonly NumericUpDown _freeThreshold = new() { Minimum = 64, Maximum = 262144, Increment = 128, Width = 110 };
    private readonly NumericUpDown _standbyMin = new() { Minimum = 0, Maximum = 262144, Increment = 128, Width = 110 };
    private readonly NumericUpDown _cooldown = new() { Minimum = 30, Maximum = 86400, Increment = 30, Width = 110 };
    private readonly NumericUpDown _interval = new() { Minimum = 2, Maximum = 3600, Width = 110 };
    private readonly CheckBox _lowOnly = new() { Text = "تفريغ الأولوية المنخفضة فقط (أقل ضرر على الكاش المهم)", AutoSize = true };
    private readonly Label _autoStatus = Ui.Label("", UiTheme.Base, UiTheme.Muted);
    private readonly DataGridView _procGrid = Ui.Grid();
    private readonly DataGridView _leakGrid = Ui.Grid();
    private readonly List<Button> _adminButtons = [];
    private bool _active;

    public MemoryPage()
    {
        var explain = new FlowCard("ليه \"تنظيف الرام\" غالبًا مش هو الحل؟");
        explain.Add(Ui.Label(MemoryManager.ExplanationText));

        var state = new FlowCard("حالة الذاكرة الآن");
        _available = state.AddField("المتاح لأي برنامج (Free + Standby)");
        _free = state.AddField("فارغة فعليًا (Free + Zeroed)");
        _standby = state.AddField("Standby (كاش قابل للاسترداد فورًا)");
        _byPriority = state.AddField("Standby حسب الأولوية (0 → 7)");
        _modified = state.AddField("Modified (لازم يتكتب على الديسك قبل استخدامه)");
        _commit = state.AddField("Commit (الضغط الحقيقي)");

        var purgeFull = Ui.Button("تفريغ Standby (كامل)", false, (_, _) => Purge(false));
        var purgeLow = Ui.Button("تفريغ الأولوية المنخفضة فقط", false, (_, _) => Purge(true));
        _adminButtons.Add(purgeFull);
        _adminButtons.Add(purgeLow);
        var manual = new FlowCard("تفريغ يدوي");
        manual.Add(Ui.Label("استخدمه فقط لو برنامج معين بيعلق لما الـ Free يقل، أو لاختبار. النتيجة اللي هتظهر مقاسة قبل وبعد.", UiTheme.Base, UiTheme.Muted));
        manual.AddButtons(purgeFull, purgeLow);
        manual.Add(_lastResult);

        var auto = new FlowCard("التفريغ التلقائي المشروط (فكرة ISLC)");
        auto.Add(Ui.Label("بيتنفذ فقط لو الشرطين اتحققوا مع بعض: الذاكرة الفارغة أقل من الحد، والـ Standby أكبر من الحد — ومع فترة تهدئة بين كل مرة.", UiTheme.Base, UiTheme.Muted));
        auto.Add(_autoEnabled);
        auto.AddButtons(Ui.Label("نفّذ لو Free أقل من (م.ب):"), _freeThreshold, Ui.Label("و Standby أكبر من (م.ب):"), _standbyMin);
        auto.AddButtons(Ui.Label("فترة التهدئة (ثانية):"), _cooldown, Ui.Label("الفحص كل (ثانية):"), _interval);
        auto.Add(_lowOnly);
        var saveAuto = Ui.Button("حفظ وتطبيق", true, (_, _) => SaveAuto());
        _adminButtons.Add(saveAuto);
        auto.AddButtons(saveAuto);
        auto.Add(_autoStatus);

        var procCard = new Card("تقليص Working Set لبروسيس واحد") { Height = 380 };
        _procGrid.Columns.AddRange(
            Ui.TextColumn("Name", "الاسم", 120),
            Ui.TextColumn("Pid", "PID", 50),
            Ui.TextColumn("WS", "Working Set", 80),
            Ui.TextColumn("Private", "الذاكرة الخاصة", 80),
            Ui.TextColumn("Publisher", "الناشر", 140));
        var trimBtn = Ui.Button("تقليص البروسيس المختار", false, (_, _) => TrimSelected());
        _adminButtons.Add(trimBtn);
        var bar = Ui.Toolbar();
        bar.Controls.Add(trimBtn);
        bar.Controls.Add(Ui.Button("تحديث القائمة", false, (_, _) => LoadProcesses()));
        procCard.AddContent(Ui.Note("الصفحات بتتنقل لـ Standby ومش بتتمسح — البرنامج هيرجع ياخدها لما يشتغل. مفيد بس لبرنامج متساب في الخلفية.", UiTheme.Muted, UiTheme.Small));
        procCard.AddContent(bar);
        procCard.AddContent(_procGrid);

        var leakCard = new Card("اشتباه تسريب ذاكرة") { Height = 220 };
        _leakGrid.Columns.AddRange(
            Ui.TextColumn("Name", "البروسيس", 120),
            Ui.TextColumn("Pid", "PID", 50),
            Ui.TextColumn("Start", "في البداية", 80),
            Ui.TextColumn("Now", "الآن", 80),
            Ui.TextColumn("Growth", "الزيادة", 80),
            Ui.TextColumn("Window", "خلال", 90));
        var ls = SettingsStore.Current.LeakDetection;
        leakCard.AddContent(Ui.Label($"الشرط: Private Bytes بتزيد في {ls.IncreasingRatio:P0} من العينات على مدار {ls.WindowSamples} عينة (كل {ls.SampleIntervalSeconds} ث) وبزيادة ≥ {ls.MinGrowthMb} م.ب. ده اشتباه مش حكم.", UiTheme.Small, UiTheme.Muted));
        leakCard.AddContent(_leakGrid);

        Stack(explain, state, manual, auto, procCard, leakCard);

        LoadAutoSettings();
        AppServices.Instance.Updated += OnUpdate;
        AppServices.Instance.AutoPurge.Purged += OnAutoPurged;
        Disposed += (_, _) =>
        {
            AppServices.Instance.Updated -= OnUpdate;
            AppServices.Instance.AutoPurge.Purged -= OnAutoPurged;
        };

        if (!AppEnvironment.IsAdministrator)
            foreach (var b in _adminButtons) b.Enabled = false;
    }

    public override void OnActivated()
    {
        _active = true;
        RenderLists();
        LoadProcesses();
        UpdateAutoStatus();
    }

    public override void OnDeactivated() => _active = false;

    private void OnUpdate(MonitorUpdate u)
    {
        if (!_active) return;
        OnUi(() =>
        {
            RenderLists();
            _leakGrid.Rows.Clear();
            foreach (var l in u.LeakSuspects)
                _leakGrid.Rows.Add(l.Name, l.Pid, Format.Bytes(l.StartBytes), Format.Bytes(l.CurrentBytes), "+" + Format.Bytes(l.Growth), Format.Duration(l.Window));
        });
    }

    private void RenderLists()
    {
        var snap = AppServices.Instance.Last?.Snapshot;
        if (snap?.Memory is { } m)
        {
            _available.Text = Format.Bytes(m.AvailablePhysical);
            _commit.Text = $"{Format.Bytes(m.CommitTotal)} من {Format.Bytes(m.CommitLimit)} ({snap.CommitPercent:0}%)";
            _commit.ForeColor = snap.CommitPercent > 90 ? UiTheme.Danger : snap.CommitPercent > 80 ? UiTheme.Warning : UiTheme.Text;
        }
        if (snap?.Lists is { } l)
        {
            _free.Text = Format.Bytes(l.Free + l.Zeroed);
            _standby.Text = Format.Bytes(l.Standby);
            _modified.Text = Format.Bytes(l.Modified);
            _byPriority.Text = string.Join(" | ", l.StandbyByPriority.Select(b => Format.Mb(b)));
        }
        else
        {
            _free.Text = _standby.Text = _modified.Text = _byPriority.Text = "يحتاج صلاحيات مسؤول";
        }
    }

    private void Purge(bool lowOnly)
    {
        if (!RequireAdmin()) return;
        if (!Dialogs.Confirm(FindForm(),
                "تفريغ Standby بيمسح كاش مفيد، والبرامج اللي هتفتحها بعده ممكن تفتح أبطأ شوية.\n\nمتأكد إنك عايز تكمل؟", "تأكيد التفريغ", true))
            return;

        _ = RunGuardedAsync(async _ =>
        {
            var outcome = await Task.Run(() => MemoryManager.PurgeStandby(lowOnly, "يدوي"));
            if (outcome.Success)
            {
                _lastResult.ForeColor = UiTheme.Success;
                _lastResult.Text = outcome.Before is null || outcome.After is null
                    ? "تم التفريغ"
                    : $"تم — الذاكرة الفارغة زادت {Format.Bytes(Math.Max(0, outcome.FreeGained))} (Standby: {Format.Bytes(outcome.Before.Standby)} ← {Format.Bytes(outcome.After.Standby)})";
            }
            else
            {
                _lastResult.ForeColor = UiTheme.Danger;
                _lastResult.Text = "فشل: " + outcome.Message;
            }
            RenderLists();
        });
    }

    private void LoadAutoSettings()
    {
        var m = SettingsStore.Current.Memory;
        _autoEnabled.Checked = m.AutoPurgeEnabled;
        _freeThreshold.Value = Math.Clamp(m.FreeMemoryThresholdMb, (int)_freeThreshold.Minimum, (int)_freeThreshold.Maximum);
        _standbyMin.Value = Math.Clamp(m.StandbyMinimumMb, (int)_standbyMin.Minimum, (int)_standbyMin.Maximum);
        _cooldown.Value = Math.Clamp(m.CooldownSeconds, (int)_cooldown.Minimum, (int)_cooldown.Maximum);
        _interval.Value = Math.Clamp(m.CheckIntervalSeconds, (int)_interval.Minimum, (int)_interval.Maximum);
        _lowOnly.Checked = m.LowPriorityOnly;
    }

    private void SaveAuto()
    {
        if (!RequireAdmin()) return;
        var s = SettingsStore.Current;
        s.Memory.AutoPurgeEnabled = _autoEnabled.Checked;
        s.Memory.FreeMemoryThresholdMb = (int)_freeThreshold.Value;
        s.Memory.StandbyMinimumMb = (int)_standbyMin.Value;
        s.Memory.CooldownSeconds = (int)_cooldown.Value;
        s.Memory.CheckIntervalSeconds = (int)_interval.Value;
        s.Memory.LowPriorityOnly = _lowOnly.Checked;
        SettingsStore.TrySave(s);

        var svc = AppServices.Instance.AutoPurge;
        if (s.Memory.AutoPurgeEnabled) svc.Start(s.Memory);
        else svc.Stop();
        UpdateAutoStatus();
    }

    private void OnAutoPurged(PurgeOutcome o) => OnUi(UpdateAutoStatus);

    private void UpdateAutoStatus()
    {
        var svc = AppServices.Instance.AutoPurge;
        _autoStatus.Text = svc.IsRunning
            ? $"الحالة: شغال — عدد مرات التفريغ في الجلسة دي: {svc.PurgeCount}{(svc.LastPurge is { } t ? " — آخر مرة: " + t.ToLocalTime().ToString("HH:mm:ss") : "")}"
            : "الحالة: متوقف";
        _autoStatus.ForeColor = svc.IsRunning ? UiTheme.Success : UiTheme.Muted;
    }

    private void LoadProcesses()
    {
        var procs = AppServices.Instance.Last?.Processes ?? [];
        _procGrid.Rows.Clear();
        foreach (var p in procs.OrderByDescending(p => p.WorkingSet).Take(30))
        {
            int i = _procGrid.Rows.Add(p.Name, p.Pid, Format.Bytes(p.WorkingSet), Format.Bytes(p.PrivateBytes), p.Publisher ?? "—");
            _procGrid.Rows[i].Tag = p;
        }
    }

    private void TrimSelected()
    {
        if (!RequireAdmin()) return;
        if (_procGrid.CurrentRow?.Tag is not ProcessInfo p) return;
        if (!Dialogs.Confirm(FindForm(), $"تقليص Working Set للبروسيس \"{p.Name}\" (PID {p.Pid})؟\n\nلو البرنامج ده شغال قدامك دلوقتي هتحس بتهنيج لحظي وهو بيرجع صفحاته.", "تأكيد"))
            return;
        var r = MemoryManager.TrimProcess(p.Pid, p.Name);
        if (r.Success) Dialogs.Info(FindForm(), "تم تقليص Working Set. الرقم هيتحدث في التحديث الجاي.");
        else Dialogs.Error(FindForm(), r.Message);
    }
}
