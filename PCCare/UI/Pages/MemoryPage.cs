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
    public override string PageDescription => "تنظيف بضغطة واحدة بأرقام حقيقية قبل وبعد — وتنظيف تلقائي وقت ما الذاكرة الفاضية تقل";

    private readonly Label _free, _standby, _modified, _commit, _available, _byPriority;
    private readonly Label _lastResult = Ui.Note("", UiTheme.Success, UiTheme.Bold);
    private readonly Label _inUseNow, _freeNow, _standbyNow;
    private readonly Button _cleanBtn;
    private readonly CheckBox _optStandby = new() { Text = "تفريغ الكاش (Standby) — بيزوّد الذاكرة الفاضية فعلًا", Checked = true, AutoSize = true };
    private readonly CheckBox _optModified = new() { Text = "كتابة الصفحات المعدّلة (Modified) على الديسك — آمن", Checked = true, AutoSize = true };
    private readonly CheckBox _optTrim = new() { Text = "تقليص البرامج الخاملة في الخلفية — بينزّل نسبة \"المستخدم\" (البرنامج اللي تفتحه بعدها يبطّأ لحظة)", AutoSize = true };
    private readonly CheckBox _autoEnabled = new() { Text = "شغّل التنظيف التلقائي (لما الذاكرة الفاضية تقل عن الحد)", AutoSize = true };
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
    private bool _loadingSettings;

    public MemoryPage()
    {
        // ========== 1) نظّف الذاكرة الآن (أول حاجة في الصفحة) ==========
        _cleanBtn = Ui.Button("🧹  نظّف الذاكرة الآن", true, async (_, _) => await CleanNowAsync());
        _cleanBtn.Font = UiTheme.Subtitle;
        _cleanBtn.MinimumSize = new Size(UiTheme.Px(240), Measure.LineHeight(UiTheme.Subtitle) + UiTheme.Px(22));
        _adminButtons.Add(_cleanBtn);
        var clean = new FlowCard("نظّف الذاكرة الآن");
        _inUseNow = clean.AddField("المستخدم فعليًا (زي Task Manager)");
        _freeNow = clean.AddField("الفاضية فعليًا (Free)");
        _standbyNow = clean.AddField("الكاش (Standby)");
        clean.AddButtons(_cleanBtn);
        clean.Add(_optStandby);
        clean.Add(_optModified);
        clean.Add(_optTrim);
        clean.Add(_lastResult);
        clean.Add(Ui.Note("ملحوظة صريحة: الكاش (Standby) مش محسوب أصلًا في نسبة \"المستخدم\" — فتفريغه بيزوّد الفاضي بس ومابينزّلش النسبة. " +
                          "النسبة بتنزل بس مع \"تقليص البرامج الخاملة\". وويندوز بيملا الكاش تاني مع الاستخدام، وده طبيعي ومفيد.", UiTheme.Muted, UiTheme.Small));
        _optTrim.Checked = SettingsStore.Current.Memory.OptimizeTrimIdle;

        var explain = new FlowCard("ليه الأرقام بتتصرف كده؟");
        explain.Add(Ui.Label(MemoryManager.ExplanationText));

        var state = new FlowCard("تفاصيل الذاكرة");
        _available = state.AddField("المتاح لأي برنامج (Free + Standby)");
        _free = state.AddField("فارغة فعليًا (Free + Zeroed)");
        _standby = state.AddField("Standby (كاش قابل للاسترداد فورًا)");
        _byPriority = state.AddField("Standby حسب الأولوية (0 → 7)");
        _modified = state.AddField("Modified (لازم يتكتب على الديسك قبل استخدامه)");
        _commit = state.AddField("Commit (الضغط الحقيقي)");

        // ========== 2) التنظيف التلقائي — التعليم بيشتغل فورًا ==========
        var auto = new FlowCard("التنظيف التلقائي");
        auto.Add(_autoEnabled);
        auto.Add(_autoStatus);
        auto.Add(Ui.Note("بيفرّغ الكاش لوحده لما الشرطين يتحققوا مع بعض: الذاكرة الفاضية أقل من الحد، والكاش أكبر من الحد — ومع فترة تهدئة بين كل مرة. " +
                         "الإعدادات دي مناسبة لأغلب الأجهزة، ولو غيّرتها دوس \"حفظ\".", UiTheme.Muted));
        auto.AddButtons(Ui.Label("نفّذ لو Free أقل من (م.ب):"), _freeThreshold, Ui.Label("و Standby أكبر من (م.ب):"), _standbyMin);
        auto.AddButtons(Ui.Label("فترة التهدئة (ثانية):"), _cooldown, Ui.Label("الفحص كل (ثانية):"), _interval);
        auto.Add(_lowOnly);
        var saveAuto = Ui.Button("حفظ الإعدادات", false, (_, _) => SaveAuto());
        _adminButtons.Add(saveAuto);
        auto.AddButtons(saveAuto);

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

        Stack(clean, auto, state, procCard, leakCard, explain);

        LoadAutoSettings();
        // التعليم على التنظيف التلقائي بيشتغل/يقف فورًا (من غير زرار حفظ)
        _autoEnabled.CheckedChanged += (_, _) => { if (!_loadingSettings) SaveAuto(); };
        _optTrim.CheckedChanged += (_, _) =>
        {
            var st = SettingsStore.Current;
            st.Memory.OptimizeTrimIdle = _optTrim.Checked;
            SettingsStore.TrySave(st);
        };
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
        if (snap?.Memory is { } mm)
        {
            _inUseNow.Text = $"{Format.Bytes(snap.UsedPhysical)} من {Format.Bytes(mm.TotalPhysical)} ({snap.MemoryPercent:0}%)";
            _inUseNow.ForeColor = snap.MemoryPercent >= 90 ? UiTheme.Danger : snap.MemoryPercent >= 75 ? UiTheme.Warning : UiTheme.Text;
        }
        if (snap?.Lists is { } ll)
        {
            _freeNow.Text = Format.Bytes(ll.Free + ll.Zeroed);
            _standbyNow.Text = Format.Bytes(ll.Standby);
        }
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

    private async Task CleanNowAsync()
    {
        if (!RequireAdmin()) return;
        if (!_optStandby.Checked && !_optModified.Checked && !_optTrim.Checked)
        {
            Dialogs.Info(FindForm(), "اختار حاجة واحدة على الأقل من الاختيارات.");
            return;
        }

        await RunGuardedAsync(async ct =>
        {
            _lastResult.ForeColor = UiTheme.Muted;
            _lastResult.Text = "بنضّف...";
            var options = new OptimizeOptions
            {
                PurgeStandby = _optStandby.Checked,
                FlushModified = _optModified.Checked,
                TrimIdleProcesses = _optTrim.Checked
            };
            var r = await MemoryOptimizer.OptimizeAsync(options, AppServices.Instance.Last?.Processes, ct);

            var lines = new List<string>
            {
                $"✓ الفاضية: {Format.Bytes(r.Before.Free)} ← {Format.Bytes(r.After.Free)}  (+{Format.Bytes(Math.Max(0, r.FreeGained))})",
                $"المستخدم فعليًا: {Format.Bytes(r.Before.InUse)} ({r.Before.InUsePercent:0}%) ← {Format.Bytes(r.After.InUse)} ({r.After.InUsePercent:0}%)" +
                    (r.InUseReduced > 0 ? $"  (−{Format.Bytes(r.InUseReduced)})" : "")
            };
            lines.AddRange(r.Steps);
            lines.AddRange(r.Errors.Select(e => "✖ " + e));
            _lastResult.ForeColor = r.Errors.Count == 0 ? UiTheme.Success : UiTheme.Warning;
            _lastResult.Text = string.Join("\n", lines);
            RenderLists();
        }, busy => _cleanBtn.Enabled = !busy && AppEnvironment.IsAdministrator);
    }

    private void LoadAutoSettings()
    {
        _loadingSettings = true;
        var m = SettingsStore.Current.Memory;
        _autoEnabled.Checked = m.AutoPurgeEnabled;
        _freeThreshold.Value = Math.Clamp(m.FreeMemoryThresholdMb, (int)_freeThreshold.Minimum, (int)_freeThreshold.Maximum);
        _standbyMin.Value = Math.Clamp(m.StandbyMinimumMb, (int)_standbyMin.Minimum, (int)_standbyMin.Maximum);
        _cooldown.Value = Math.Clamp(m.CooldownSeconds, (int)_cooldown.Minimum, (int)_cooldown.Maximum);
        _interval.Value = Math.Clamp(m.CheckIntervalSeconds, (int)_interval.Minimum, (int)_interval.Maximum);
        _lowOnly.Checked = m.LowPriorityOnly;
        _loadingSettings = false;
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
