using System.Text;
using AjyalPcCare.Core.Cleanup;
using AjyalPcCare.Core.Common;
using AjyalPcCare.UI.Controls;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Pages;

public sealed class CleanupPage : PageBase
{
    public override string PageTitle => "تنظيف الملفات";
    public override string PageDescription => "فحص فعلي (Dry-Run) الأول — ولا ملف بيتمسح قبل ما تشوف الأرقام وتوافق";

    private readonly DataGridView _grid = Ui.Grid(readOnly: false);
    private readonly ProgressBar _progress = Ui.Progress();
    private readonly Label _status = Ui.Label("اضغط \"فحص\" عشان تشوف المساحة القابلة للتحرير", UiTheme.Bold);
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = UiTheme.Px(170), BackColor = UiTheme.Card, BorderStyle = BorderStyle.FixedSingle };
    private readonly Button _scanBtn, _cleanBtn, _cancelBtn, _dismAnalyzeBtn, _dismRunBtn;
    private readonly Label _dismStatus = Ui.Label("", UiTheme.Base, UiTheme.Muted);

    private CleanupModule? _module;
    private AnalysisResult? _preview;

    public CleanupPage()
    {
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Sel", HeaderText = "اختيار", FillWeight = 35 });
        _grid.Columns.AddRange(
            Ui.TextColumn("Title", "الفئة", 120),
            Ui.TextColumn("Desc", "الوصف", 200, wrap: true),
            Ui.TextColumn("Size", "الحجم", 60),
            Ui.TextColumn("Count", "الملفات", 50),
            Ui.TextColumn("Risk", "الخطورة", 50),
            Ui.TextColumn("Note", "ملاحظات / تحذير", 200, wrap: true));
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 0 && _grid.Rows[e.RowIndex].Tag is AnalysisItem item)
            {
                item.Selected = item.Available && (bool)(_grid.Rows[e.RowIndex].Cells[0].Value ?? false);
                UpdateSelectedTotal();
            }
        };

        _scanBtn = Ui.Button("فحص (Dry-Run)", true, async (_, _) => await ScanAsync());
        _cleanBtn = Ui.Button("تنظيف المحدد", false, async (_, _) => await CleanAsync());
        _cancelBtn = Ui.DangerButton("إلغاء", (_, _) => CancelRunning());
        _cleanBtn.Enabled = false;
        _cancelBtn.Enabled = false;

        var main = new Card("الفئات") { Height = 430 };
        var bar = Ui.Toolbar();
        bar.Controls.AddRange([_scanBtn, _cleanBtn, _cancelBtn]);
        main.AddContent(bar);
        main.AddContent(_progress);
        main.AddContent(_status);
        main.AddContent(_grid);

        var age = SettingsStore.Current.Cleanup;
        var rules = new FlowCard("قواعد الأمان المطبقة");
        rules.Add(Ui.Label(
            $"• الملفات المؤقتة الأحدث من {age.MinFileAgeHours} ساعة مش بتتمسح (ممكن تكون لبرنامج شغال أو تسطيب جاري).\n" +
            "• أي ملف مقفول أو مستخدم بيتخطى ويتسجل في اللوج — الأداة مش بتجبر أي حاجة.\n" +
            "• المتصفح المفتوح بيتخطى الكاش بتاعه — الأداة مش بتقفل أي برنامج.\n" +
            "• ممنوع تمامًا: Prefetch، Downloads، Documents، Desktop، والكوكيز وكلمات المرور في المتصفحات.\n" +
            "• Junctions و Symlinks مش بيتم تتبعها (ممكن تشاور على أماكن تانية خالص).\n" +
            (age.AllUserProfiles ? "• Temp بيتنظف لكل حسابات المستخدمين على الجهاز (مهم في المعامل)." : "• Temp للمستخدم الحالي فقط."),
            UiTheme.Base, UiTheme.Muted));

        _dismAnalyzeBtn = Ui.Button("تحليل مخزن المكونات", false, async (_, _) => await DismAnalyzeAsync());
        _dismRunBtn = Ui.Button("تشغيل DISM Cleanup", false, async (_, _) => await DismRunAsync());
        var dism = new FlowCard("تنظيف مخزن مكونات ويندوز (WinSxS) — متقدم");
        dism.Add(Ui.Label("الطريقة الرسمية الوحيدة لتنظيف WinSxS هي DISM. المسح اليدوي من المجلد ده بيبوّظ ويندوز. العملية ممكن تاخد من 5 لـ 30 دقيقة.", UiTheme.Base, UiTheme.Muted));
        dism.AddButtons(_dismAnalyzeBtn, _dismRunBtn);
        dism.Add(_dismStatus);
        if (!AppEnvironment.IsAdministrator)
        {
            _dismAnalyzeBtn.Enabled = _dismRunBtn.Enabled = false;
            _dismStatus.Text = "يحتاج صلاحيات مسؤول";
        }

        var logCard = new FlowCard("نتيجة آخر عملية");
        logCard.Add(_log, stretch: true);

        Stack(main, rules, dism, logCard);
    }

    private void SetBusy(bool busy)
    {
        _scanBtn.Enabled = !busy;
        _cleanBtn.Enabled = !busy && _preview is not null && _preview.Items.Any(i => i.Selected && i.Available);
        _cancelBtn.Enabled = busy;
        _dismAnalyzeBtn.Enabled = _dismRunBtn.Enabled = !busy && AppEnvironment.IsAdministrator;
        _grid.Enabled = !busy;
        _progress.Visible = busy;
    }

    private Progress<ModuleProgress> MakeProgress() => new(p =>
    {
        _progress.Value = Math.Clamp(p.Percent, 0, 100);
        _status.Text = p.Message;
    });

    private async Task ScanAsync()
    {
        await RunGuardedAsync(async ct =>
        {
            _module = new CleanupModule(SettingsStore.Current.Cleanup);
            _preview = await ModuleRunner.SafeAnalyzeAsync(_module, MakeProgress(), ct);
            FillGrid();
        }, SetBusy);
    }

    private void FillGrid()
    {
        _grid.Rows.Clear();
        if (_preview is null) return;
        foreach (var item in _preview.Items)
        {
            string risk = item.Risk switch { ItemRisk.Safe => "آمن", ItemRisk.Caution => "انتبه", _ => "متقدم" };
            string note = !item.Available ? "غير متاح: " + item.UnavailableReason : item.Warning ?? "";
            int i = _grid.Rows.Add(item.Selected && item.Available, item.Title, item.Description,
                item.Available ? Format.Bytes(item.SizeBytes) : "—", item.Available ? item.FileCount.ToString("N0") : "—", risk, note);
            var row = _grid.Rows[i];
            row.Tag = item;
            if (!item.Available)
            {
                row.DefaultCellStyle.ForeColor = UiTheme.Muted;
                row.Cells[0].ReadOnly = true;
            }
            else if (item.Risk != ItemRisk.Safe)
            {
                row.Cells["Risk"].Style.ForeColor = UiTheme.Warning;
            }
        }
        UpdateSelectedTotal();
        _status.Text = $"إجمالي القابل للتحرير: {Format.Bytes(_preview.TotalBytes)} — المحدد: {Format.Bytes(_preview.SelectedBytes)}";
    }

    private void UpdateSelectedTotal()
    {
        if (_preview is null) return;
        _status.Text = $"إجمالي القابل للتحرير: {Format.Bytes(_preview.TotalBytes)} — المحدد: {Format.Bytes(_preview.SelectedBytes)}";
        _cleanBtn.Enabled = !IsBusy && _preview.Items.Any(i => i.Selected && i.Available);
    }

    private async Task CleanAsync()
    {
        if (_module is null || _preview is null) return;
        var selected = _preview.Items.Where(i => i.Selected && i.Available).ToList();
        if (selected.Count == 0) return;

        var sb = new StringBuilder("هيتم تنظيف:\n\n");
        foreach (var i in selected)
        {
            sb.Append($"• {i.Title} — {Format.Bytes(i.SizeBytes)}\n");
            if (i.Warning is not null) sb.Append($"   ⚠ {i.Warning}\n");
        }
        sb.Append("\nالمسح نهائي (مش بيروح سلة المحذوفات). تكمل؟");
        if (!Dialogs.Confirm(FindForm(), sb.ToString(), "تأكيد التنظيف", selected.Any(i => i.Risk != ItemRisk.Safe))) return;

        await RunGuardedAsync(async ct =>
        {
            var result = await ModuleRunner.SafeExecuteAsync(_module, _preview, MakeProgress(), ct);
            ShowResult(result);
            _preview = null;
            _cleanBtn.Enabled = false;
        }, SetBusy);
        await ScanAsync(); // إعادة القياس بعد التنظيف — الأرقام الجديدة حقيقية
    }

    private void ShowResult(ExecutionResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {(r.Success ? "اكتمل" : "اكتمل مع أخطاء")} — تم تحرير {Format.Bytes(r.BytesFreed)} — {r.ItemsProcessed} ملف — تم تخطي {r.ItemsSkipped} (مقفول/مستخدم) — المدة {Format.Duration(r.Duration)}");
        foreach (var m in r.Messages) sb.AppendLine("  " + m);
        foreach (var e in r.Errors) sb.AppendLine("  ✖ " + e);
        _log.Text = sb.ToString().Replace("\n", Environment.NewLine);
    }

    private async Task DismAnalyzeAsync()
    {
        if (!RequireAdmin()) return;
        await RunGuardedAsync(async ct =>
        {
            _dismStatus.Text = "جاري التحليل (ممكن ياخد دقايق)...";
            string output = await DismCleanupModule.AnalyzeComponentStoreAsync(MakeProgress(), ct);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => l.Contains("Size", StringComparison.OrdinalIgnoreCase) || l.Contains("Recommended", StringComparison.OrdinalIgnoreCase) || l.Contains("Reclaimable", StringComparison.OrdinalIgnoreCase));
            _log.Text = string.Join(Environment.NewLine, lines);
            _dismStatus.Text = output.Contains("Component Store Cleanup Recommended : Yes", StringComparison.OrdinalIgnoreCase)
                ? "DISM بيوصي بالتنظيف — التفاصيل تحت"
                : "التحليل خلص — التفاصيل تحت";
        }, SetBusy);
    }

    private async Task DismRunAsync()
    {
        if (!RequireAdmin()) return;
        if (!Dialogs.Confirm(FindForm(), "تشغيل DISM /StartComponentCleanup؟\n\nممكن ياخد من 5 لـ 30 دقيقة والجهاز هيبقى تقيل أثناءها. ما تقفلش الجهاز في النص.", "تأكيد", true)) return;
        await RunGuardedAsync(async ct =>
        {
            var m = new DismCleanupModule();
            var preview = await m.AnalyzeAsync(null, ct);
            var r = await ModuleRunner.SafeExecuteAsync(m, preview, MakeProgress(), ct);
            ShowResult(r);
            _dismStatus.Text = r.Success ? "اكتمل" : "انتهى مع أخطاء";
        }, SetBusy);
    }
}
