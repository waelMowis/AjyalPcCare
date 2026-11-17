using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Startup;
using AjyalPcCare.Native;
using AjyalPcCare.UI.Controls;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Pages;

public sealed class StartupPage : PageBase
{
    public override string PageTitle => "برامج بدء التشغيل";
    public override string PageDescription => "كل اللي بيشتغل مع الويندوز — التعطيل قابل للتراجع دايمًا، ونقطة استعادة قبل أول تعديل";

    private readonly DataGridView _grid = Ui.Grid();
    private readonly ProgressBar _progress = Ui.Progress();
    private readonly Label _status = Ui.Label("", UiTheme.Base, UiTheme.Muted);
    private readonly CheckBox _hideMs = new() { Text = "إخفاء عناصر مايكروسوفت", Checked = true, AutoSize = true, Margin = new Padding(8, 6, 8, 4) };
    private readonly CheckBox _brokenOnly = new() { Text = "المكسور فقط", AutoSize = true, Margin = new Padding(8, 6, 8, 4) };
    private readonly Button _refresh, _disable, _enable, _open;
    private List<StartupEntry> _entries = [];
    private bool _loaded;

    public StartupPage()
    {
        _grid.Columns.AddRange(
            Ui.TextColumn("State", "الحالة", 55),
            Ui.TextColumn("Name", "الاسم", 140),
            Ui.TextColumn("Source", "المصدر", 95),
            Ui.TextColumn("Publisher", "الناشر", 110),
            Ui.TextColumn("Signature", "التوقيع", 75),
            Ui.TextColumn("Command", "الأمر / المسار", 230),
            Ui.TextColumn("Note", "ملاحظة", 140, wrap: true));
        _grid.SelectionChanged += (_, _) => UpdateButtons();

        _refresh = Ui.Button("تحديث", true, async (_, _) => await LoadAsync());
        _disable = Ui.Button("تعطيل", false, async (_, _) => await ToggleAsync(false));
        _enable = Ui.Button("تفعيل", false, async (_, _) => await ToggleAsync(true));
        _open = Ui.Button("فتح مكان الملف", false, (_, _) => OpenLocation());
        _hideMs.CheckedChanged += (_, _) => Render();
        _brokenOnly.CheckedChanged += (_, _) => Render();

        var bar = Ui.Toolbar();
        bar.Controls.AddRange([_refresh, _disable, _enable, _open, _hideMs, _brokenOnly]);

        // الصفحة دي جدول بيملا المساحة كلها — مش Stack
        var card = new Card { Dock = DockStyle.Fill };
        card.AddContent(bar);
        card.AddContent(_progress);
        card.AddContent(_status);
        _grid.Dock = DockStyle.Fill;
        card.AddContent(_grid);

        var legend = Ui.Label("الصفوف الحمرا = مدخل مكسور (الملف مش موجود) • الرمادي = متعطل • الخدمات: التعطيل = Manual مش Disabled • RunOnce للعرض فقط",
            UiTheme.Small, UiTheme.Muted);
        legend.Dock = DockStyle.Bottom;
        legend.Padding = new Padding(0, UiTheme.Px(8), 0, 0);
        Ui.FitHeight(legend);

        Body.Controls.Add(card);
        Body.Controls.Add(legend);
        UpdateButtons();
    }

    public override void OnActivated()
    {
        if (!_loaded) _ = LoadAsync();
    }

    private void SetBusy(bool busy)
    {
        _progress.Visible = busy;
        _refresh.Enabled = !busy;
        _grid.Enabled = !busy;
        UpdateButtons();
    }

    private async Task LoadAsync()
    {
        await RunGuardedAsync(async ct =>
        {
            var progress = new Progress<ModuleProgress>(p => { _progress.Value = Math.Clamp(p.Percent, 0, 100); _status.Text = p.Message; });
            _entries = await StartupScanner.ScanAsync(progress, ct);
            _loaded = true;
            Render();
        }, SetBusy);
    }

    private void Render()
    {
        var keepId = (_grid.CurrentRow?.Tag as StartupEntry)?.Id;
        _grid.Rows.Clear();
        IEnumerable<StartupEntry> view = _entries;
        if (_hideMs.Checked) view = view.Where(e => !(e.IsMicrosoft && e.Signature == SignatureState.Valid));
        if (_brokenOnly.Checked) view = view.Where(e => e.IsBroken);

        foreach (var e in view)
        {
            int i = _grid.Rows.Add(e.Enabled ? "مفعّل" : "متعطل", e.Name, e.SourceDisplay, e.Publisher ?? "—", e.SignatureDisplay,
                e.Command, e.Note ?? (e.ServiceStartType is not null ? "نوع التشغيل: " + e.ServiceStartType : ""));
            var row = _grid.Rows[i];
            row.Tag = e;
            if (e.IsBroken) row.DefaultCellStyle.BackColor = UiTheme.DangerLight;
            else if (!e.Enabled) row.DefaultCellStyle.ForeColor = UiTheme.Muted;
            if (e.Signature is SignatureState.Unsigned or SignatureState.Invalid) row.Cells["Signature"].Style.ForeColor = UiTheme.Warning;
            row.Cells["State"].Style.ForeColor = e.Enabled ? UiTheme.Success : UiTheme.Muted;
            if (e.Id == keepId) _grid.CurrentCell = row.Cells[1];
        }

        int enabled = _entries.Count(e => e.Enabled);
        _status.Text = $"الإجمالي: {_entries.Count} — مفعّل: {enabled} — متعطل: {_entries.Count - enabled} — مكسور: {_entries.Count(e => e.IsBroken)} — غير موقّع: {_entries.Count(e => e.Signature == SignatureState.Unsigned)}";
        UpdateButtons();
    }

    private StartupEntry? Selected => _grid.CurrentRow?.Tag as StartupEntry;

    private void UpdateButtons()
    {
        var e = Selected;
        bool idle = !IsBusy;
        _disable.Enabled = idle && e is { CanToggle: true, Enabled: true };
        _enable.Enabled = idle && e is { CanToggle: true, Enabled: false };
        _open.Enabled = idle && e is not null && (e.FileExists || e.Source is StartupSource.StartupFolderUser or StartupSource.StartupFolderCommon);
    }

    private async Task ToggleAsync(bool enable)
    {
        var e = Selected;
        if (e is null) return;

        string warn = "";
        if (!enable && e.IsMicrosoft) warn = "\n\n⚠ العنصر ده من مايكروسوفت — اتأكد إنك عارف وظيفته قبل التعطيل.";
        if (!enable && e.Source == StartupSource.Service) warn += "\n\nالخدمة هتتحول لـ Manual: هتشتغل بس لما برنامج يطلبها.";
        if (!Dialogs.Confirm(FindForm(), $"{(enable ? "تفعيل" : "تعطيل")} \"{e.Name}\"؟\n\nالمصدر: {e.SourceDisplay}\nالأمر: {e.Command}{warn}\n\nتقدر تتراجع في أي وقت من صفحة \"السجل والتراجع\".",
                "تأكيد", !enable && e.IsMicrosoft))
            return;

        await RunGuardedAsync(async ct =>
        {
            _status.Text = "جاري التنفيذ (وإنشاء نقطة استعادة لو أول تعديل في الجلسة)...";
            var r = await StartupManager.SetEnabledAsync(e, enable, ct);
            string rp = r.RestorePoint is { } p ? "\n\nنقطة الاستعادة: " + p.Message : "";
            if (r.Success) _status.Text = r.Message;
            else Dialogs.Error(FindForm(), r.Message + rp);
            if (r.Success && r.RestorePoint is { Created: false, Skipped: false }) Dialogs.Info(FindForm(), "تم التعديل، لكن" + rp);
            Render();
        }, SetBusy);
    }

    private void OpenLocation()
    {
        var e = Selected;
        if (e is null) return;
        string? path = e.FileExists ? e.ExecutablePath : e.Location;
        if (!string.IsNullOrEmpty(path)) Ui.ShowInExplorer(path);
    }
}
