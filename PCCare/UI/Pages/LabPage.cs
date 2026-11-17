using AjyalPcCare.Cli;
using AjyalPcCare.Core.Cleanup;
using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Scheduling;
using AjyalPcCare.UI.Controls;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Pages;

public sealed class LabPage : PageBase
{
    public override string PageTitle => "وضع المعامل";
    public override string PageDescription => "صيانة صامتة مجدولة لكل أجهزة المعمل مع تقرير لكل جهاز في مكان واحد";

    private readonly TextBox _folder = new() { PlaceholderText = @"\\server\share\reports", RightToLeft = RightToLeft.No };
    private readonly ComboBox _day = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly DateTimePicker _time = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 90 };
    private readonly Dictionary<string, CheckBox> _cats = new();
    private readonly CheckBox _purge = new() { Text = "تفريغ Standby في الآخر", AutoSize = true };
    private readonly CheckBox _optimize = new() { Text = "تحسين الدرايفات (TRIM/Defrag)", AutoSize = true };
    private readonly CheckBox _dism = new() { Text = "DISM Cleanup (بطيء — يُفضل شهريًا مش أسبوعيًا)", AutoSize = true };
    private readonly CheckBox _autoUpdate = new() { Text = "تحديث الأداة تلقائيًا من GitHub بعد الصيانة (كل الأجهزة تتحدّث لوحدها)", AutoSize = true };
    private readonly TextBox _command = new() { ReadOnly = true, BackColor = UiTheme.Background, RightToLeft = RightToLeft.No };
    private readonly Label _taskStatus = Ui.Label("جاري القراءة...", UiTheme.Base, UiTheme.Muted);
    private readonly Label _runStatus = Ui.Label("", UiTheme.Bold);
    private readonly Button _register, _remove, _runNow, _save;

    private static readonly (string Value, string Text)[] Days =
    [
        ("Sunday", "الأحد"), ("Monday", "الإثنين"), ("Tuesday", "الثلاثاء"), ("Wednesday", "الأربعاء"),
        ("Thursday", "الخميس"), ("Friday", "الجمعة"), ("Saturday", "السبت")
    ];

    private static readonly (string Id, string Text)[] Categories =
    [
        (CleanupCategories.UserTemp, "Temp المستخدمين"),
        (CleanupCategories.WindowsTemp, "Windows Temp"),
        (CleanupCategories.BrowserCache, "كاش المتصفحات"),
        (CleanupCategories.RecycleBin, "سلة المحذوفات"),
        (CleanupCategories.WindowsUpdate, "ملفات Windows Update"),
        (CleanupCategories.DeliveryOptimization, "Delivery Optimization"),
        (CleanupCategories.CrashDumps, "ملفات الأعطال")
    ];

    public LabPage()
    {
        foreach (var d in Days) _day.Items.Add(d.Text);

        var where = new FlowCard("1) مكان التقارير");
        where.Add(Ui.Label("مسار شبكة مشترك (\\\\server\\share\\reports) عشان كل الأجهزة تكتب في نفس المكان. لو فاضي: التقارير بتتحفظ محليًا جنب الأداة.", UiTheme.Base, UiTheme.Muted));
        where.Add(_folder, stretch: true);
        where.AddButtons(Ui.Button("استعراض...", false, (_, _) => Browse()));
        where.Add(Ui.Label("مهم: المهمة بتشتغل بحساب SYSTEM، فعلى الشبكة بتظهر بهوية الجهاز (DOMAIN\\PC$). لازم المجلد المشترك يدّي صلاحية Modify لـ \"Domain Computers\" — في Workgroup من غير دومين الأسهل تحفظ محليًا وتجمع التقارير بـ Veyon.", UiTheme.Small, UiTheme.Warning));

        var what = new FlowCard("2) العمليات");
        var catFlow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Color.Transparent };
        foreach (var c in Categories)
        {
            var cb = new CheckBox { Text = c.Text, AutoSize = true, Margin = new Padding(6, 4, 12, 4) };
            cb.CheckedChanged += (_, _) => UpdateCommand();
            _cats[c.Id] = cb;
            catFlow.Controls.Add(cb);
        }
        what.Add(Ui.Label("التنظيف:", UiTheme.Bold));
        what.Add(catFlow, stretch: true);
        what.Add(_optimize);
        what.Add(_dism);
        what.Add(_purge);
        what.Add(_autoUpdate);
        what.Add(Ui.Label("ملاحظة: تفريغ Standby في مهمة مجدولة مالوش قيمة حقيقية لأن الكاش بيتملي تاني فورًا — متساب كخيار بس مش موصى بيه.", UiTheme.Small, UiTheme.Muted));
        _purge.CheckedChanged += (_, _) => UpdateCommand();
        _optimize.CheckedChanged += (_, _) => UpdateCommand();
        _dism.CheckedChanged += (_, _) => UpdateCommand();
        _autoUpdate.CheckedChanged += (_, _) => UpdateCommand();
        _folder.TextChanged += (_, _) => UpdateCommand();

        var when = new FlowCard("3) الميعاد");
        when.AddButtons(Ui.Label("كل يوم:"), _day, Ui.Label("الساعة:"), _time);
        when.Add(Ui.Label("لو الجهاز كان مقفول وقت الميعاد، المهمة بتتنفذ أول ما يشتغل (StartWhenAvailable). اختار وقت المعمل فيه فاضي.", UiTheme.Small, UiTheme.Muted));

        _save = Ui.Button("حفظ الإعدادات", false, (_, _) => Save());
        _register = Ui.Button("حفظ وتسجيل المهمة المجدولة", true, async (_, _) => await RegisterAsync());
        _remove = Ui.DangerButton("حذف المهمة", async (_, _) => await RemoveAsync());
        _runNow = Ui.Button("تشغيل الآن (صامت) وفتح التقرير", false, async (_, _) => await RunNowAsync());
        var act = new FlowCard("4) التنفيذ");
        act.Add(Ui.Label("سطر الأوامر الناتج (تقدر تستخدمه في GPO أو Veyon أو أي أداة نشر):", UiTheme.Bold));
        act.Add(_command, stretch: true);
        act.AddButtons(Ui.Button("نسخ الأمر", false, (_, _) => { if (_command.Text.Length > 0) Clipboard.SetText(QuotedCommand()); }));
        act.AddButtons(_save, _register, _remove, _runNow);
        act.Add(_taskStatus);
        act.Add(_runStatus);

        var deploy = new FlowCard("النشر على أجهزة المعمل");
        deploy.Add(Ui.Label(
            "1. انسخ مجلد الأداة (PCCare.exe + pccare.settings.json + مجلد Fonts) لمسار ثابت على كل جهاز، مثلًا C:\\Tools\\PCCare\n" +
            "2. ظبط الإعدادات مرة واحدة هنا على جهاز المدرس واحفظ — ملف pccare.settings.json هو اللي هيتنسخ.\n" +
            "3. على كل جهاز شغّل مرة واحدة كمسؤول:  C:\\Tools\\PCCare\\PCCare.exe /install-schedule\n" +
            "   • عن طريق GPO: Computer Configuration ← Policies ← Windows Settings ← Scripts ← Startup\n" +
            "   • عن طريق Veyon: \"تشغيل برنامج\" على كل الأجهزة المختارة بنفس الأمر\n" +
            "4. أو بدل المهمة المجدولة: استخدم سطر الأوامر اللي فوق مباشرة في GPO / Veyon وقت ما تحب.\n" +
            "5. افتح summary.csv في مجلد التقارير بـ Excel — سطر لكل جهاز في كل تشغيل.",
            UiTheme.Base, UiTheme.Text));

        Stack(where, what, when, act, deploy);
        LoadSettings();
        if (!AppEnvironment.IsAdministrator) _register.Enabled = _remove.Enabled = false;
    }

    public override void OnActivated() => _ = RefreshTaskAsync();

    private void LoadSettings()
    {
        var lab = SettingsStore.Current.Lab;
        _folder.Text = lab.ReportFolder;
        int di = Array.FindIndex(Days, d => d.Value.Equals(lab.ScheduleDay, StringComparison.OrdinalIgnoreCase));
        _day.SelectedIndex = di >= 0 ? di : 5;
        _time.Value = DateTime.Today.Add(TimeSpan.TryParse(lab.ScheduleTime, out var t) ? t : new TimeSpan(14, 0, 0));
        foreach (var (id, cb) in _cats) cb.Checked = lab.CleanCategories.Contains(id, StringComparer.OrdinalIgnoreCase);
        _purge.Checked = lab.PurgeStandby;
        _optimize.Checked = lab.OptimizeDrives;
        _dism.Checked = lab.RunDism;
        _autoUpdate.Checked = lab.AutoUpdate;
        UpdateCommand();
    }

    private LabSettings Collect() => new()
    {
        ReportFolder = _folder.Text.Trim(),
        ScheduleDay = Days[Math.Max(0, _day.SelectedIndex)].Value,
        ScheduleTime = _time.Value.ToString("HH:mm"),
        CleanCategories = _cats.Where(kv => kv.Value.Checked).Select(kv => kv.Key).ToList(),
        PurgeStandby = _purge.Checked,
        OptimizeDrives = _optimize.Checked,
        RunDism = _dism.Checked,
        AutoUpdate = _autoUpdate.Checked
    };

    private void UpdateCommand() => _command.Text = CliOptions.BuildScheduledArguments(Collect());

    private string QuotedCommand() => $"\"{AppEnvironment.ExecutablePath}\" {_command.Text}";

    private bool Save()
    {
        var s = SettingsStore.Current;
        s.Lab = Collect();
        if (!string.IsNullOrEmpty(s.Lab.ReportFolder) && !Directory.Exists(s.Lab.ReportFolder) &&
            !Dialogs.Confirm(FindForm(), "المجلد ده مش موجود أو مش متاح من الجهاز ده دلوقتي. تحفظ برضه؟"))
            return false;
        bool ok = SettingsStore.TrySave(s);
        if (!ok) Dialogs.Error(FindForm(), "تعذر حفظ ملف الإعدادات — غالبًا الأداة في مجلد محمي. شغلها كمسؤول.");
        else _runStatus.Text = "تم حفظ الإعدادات في " + AppEnvironment.SettingsPath;
        return ok;
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog { Description = "اختار مجلد التقارير", UseDescriptionForTitle = true, SelectedPath = _folder.Text };
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) _folder.Text = dlg.SelectedPath;
    }

    private async Task RefreshTaskAsync()
    {
        await RunGuardedAsync(async ct =>
        {
            var info = await ScheduledTaskService.QueryAsync(ct);
            _taskStatus.Text = info.Exists
                ? $"المهمة مسجلة — الحالة: {info.State} — التشغيل الجاي: {info.NextRun} — آخر تشغيل: {(string.IsNullOrEmpty(info.LastRun) ? "لم تعمل بعد" : info.LastRun)} (الكود {info.LastResult})"
                : "المهمة غير مسجلة على الجهاز ده";
            _taskStatus.ForeColor = info.Exists ? UiTheme.Success : UiTheme.Muted;
        });
    }

    private async Task RegisterAsync()
    {
        if (!RequireAdmin() || !Save()) return;
        var lab = SettingsStore.Current.Lab;
        if (lab.CleanCategories.Count == 0 && !lab.OptimizeDrives && !lab.RunDism && !lab.PurgeStandby)
        {
            Dialogs.Info(FindForm(), "اختار عملية واحدة على الأقل.");
            return;
        }
        await RunGuardedAsync(async ct =>
        {
            var (ok, msg) = await ScheduledTaskService.CreateOrUpdateAsync(AppEnvironment.ExecutablePath,
                CliOptions.BuildScheduledArguments(lab), lab.ScheduleDay, lab.ScheduleTime, ct);
            if (ok) Dialogs.Info(FindForm(), msg);
            else Dialogs.Error(FindForm(), msg);
        });
        await RefreshTaskAsync();
    }

    private async Task RemoveAsync()
    {
        if (!RequireAdmin() || !Dialogs.Confirm(FindForm(), "حذف المهمة المجدولة من الجهاز ده؟")) return;
        await RunGuardedAsync(async ct =>
        {
            var (ok, msg) = await ScheduledTaskService.RemoveAsync(ct);
            if (!ok) Dialogs.Error(FindForm(), msg);
        });
        await RefreshTaskAsync();
    }

    private async Task RunNowAsync()
    {
        var lab = Collect();
        // التشغيل من جوه الواجهة: من غير تحديث (المثبّت كان هيقفل الواجهة نفسها). التحديث من زرار "التحقق من التحديثات".
        lab.AutoUpdate = false;
        if (!Dialogs.Confirm(FindForm(), "تشغيل نفس العمليات دي دلوقتي على الجهاز ده بدون أسئلة إضافية؟\n\n" + CliOptions.BuildScheduledArguments(lab))) return;
        await RunGuardedAsync(async ct =>
        {
            _runStatus.ForeColor = UiTheme.Text;
            _runStatus.Text = "جاري التنفيذ... (تابع اللوج في صفحة السجل)";
            var args = CliOptions.BuildScheduledArguments(lab);
            var opts = CliOptions.Parse(SplitArgs(args));
            int code = await Task.Run(() => CliRunner.RunAsync(opts, ct), ct);
            _runStatus.ForeColor = code == ExitCodes.Success ? UiTheme.Success : UiTheme.Warning;
            _runStatus.Text = code switch
            {
                ExitCodes.Success => "اكتمل بنجاح",
                ExitCodes.PartialFailure => "اكتمل مع أخطاء جزئية — راجع التقرير",
                ExitCodes.AlreadyRunning => "فيه تشغيل صامت تاني شغال حاليًا",
                _ => $"فشل (كود {code}) — راجع اللوج"
            };
            string folder = string.IsNullOrWhiteSpace(lab.ReportFolder) ? AppEnvironment.ReportsDirectory : lab.ReportFolder;
            var latest = Directory.Exists(folder)
                ? new DirectoryInfo(folder).GetFiles($"{Environment.MachineName}_*.html").OrderByDescending(f => f.LastWriteTime).FirstOrDefault()
                : null;
            if (latest is null && Directory.Exists(AppEnvironment.ReportsDirectory))
                latest = new DirectoryInfo(AppEnvironment.ReportsDirectory).GetFiles($"{Environment.MachineName}_*.html").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            if (latest is not null) Ui.OpenPath(latest.FullName);
        });
    }

    /// <summary>تقسيم سطر أوامر مع احترام الـ quotes.</summary>
    private static List<string> SplitArgs(string s)
    {
        var list = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool q = false;
        foreach (char c in s)
        {
            if (c == '"') { q = !q; continue; }
            if (c == ' ' && !q)
            {
                if (cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); }
                continue;
            }
            cur.Append(c);
        }
        if (cur.Length > 0) list.Add(cur.ToString());
        return list;
    }
}
