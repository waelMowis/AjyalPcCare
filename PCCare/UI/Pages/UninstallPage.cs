using System.Text;
using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Safety;
using AjyalPcCare.Core.Uninstall;
using AjyalPcCare.UI.Controls;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Pages;

/// <summary>
/// إلغاء تثبيت البرامج من جذورها:
///   1. نقطة استعادة   2. برنامج الإلغاء الرسمي   3. فحص البقايا   4. مراجعة   5. إزالة قابلة للرجوع
/// "إزالة إجبارية": من غير برنامج الإلغاء (لما يكون بايظ أو مش موجود) — فحص البقايا ومسحها على طول.
/// </summary>
public sealed class UninstallPage : PageBase
{
    public override string PageTitle => "إلغاء تثبيت البرامج";
    public override string PageDescription => "برنامج الإلغاء الرسمي الأول، وبعده فحص البقايا ومراجعتها — كل حاجة بتتمسح قابلة للرجوع";

    private readonly DataGridView _grid = Ui.Grid();
    private readonly TextBox _search = new() { PlaceholderText = "بحث بالاسم أو الناشر...", Width = UiTheme.Px(260) };
    private readonly CheckBox _showSystem = new() { Text = "إظهار مكونات النظام", AutoSize = true, Margin = new Padding(UiTheme.Px(10), UiTheme.Px(8), 0, 0) };
    private readonly CheckBox _quiet = new() { Text = "إلغاء صامت لو البرنامج بيدعمه", AutoSize = true, Margin = new Padding(UiTheme.Px(10), UiTheme.Px(8), 0, 0) };
    private readonly Label _status = Ui.Note("", UiTheme.Muted);
    private readonly ProgressBar _progress = Ui.Progress();
    private readonly Button _refresh, _uninstall, _force, _open;
    private List<InstalledProgram> _all = [];
    private bool _loaded;

    public UninstallPage()
    {
        _grid.Columns.AddRange(
            Ui.TextColumn("Name", "البرنامج", 220),
            Ui.TextColumn("Publisher", "الناشر", 130),
            Ui.TextColumn("Version", "الإصدار", 70),
            Ui.TextColumn("Size", "الحجم", 60),
            Ui.TextColumn("Date", "تاريخ التثبيت", 70),
            Ui.TextColumn("Scope", "النطاق", 80),
            Ui.TextColumn("Location", "المكان", 200));
        _grid.MultiSelect = false;
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await UninstallAsync(force: false); };
        _grid.SortCompare += (_, e) =>
        {
            // الحجم يتفرز بالأرقام مش بالنص
            if (e.Column.Name == "Size" && _grid.Rows[e.RowIndex1].Tag is InstalledProgram a && _grid.Rows[e.RowIndex2].Tag is InstalledProgram b)
            {
                e.SortResult = a.SizeBytes.CompareTo(b.SizeBytes);
                e.Handled = true;
            }
        };

        _refresh = Ui.Button("تحديث", false, (_, _) => Reload(force: true));
        _uninstall = Ui.Button("إلغاء التثبيت", true, async (_, _) => await UninstallAsync(force: false));
        _force = Ui.DangerButton("إزالة إجبارية", async (_, _) => await UninstallAsync(force: true));
        _open = Ui.Button("فتح المكان", false, (_, _) => OpenLocation());
        _search.TextChanged += (_, _) => Render();
        _showSystem.CheckedChanged += (_, _) => Reload(force: true);

        var bar = Ui.Toolbar();
        bar.Controls.AddRange([_uninstall, _force, _open, _refresh, _search, _showSystem, _quiet]);

        var card = new Card { Dock = DockStyle.Fill };
        card.AddContent(bar);
        card.AddContent(_progress);
        card.AddContent(_status);
        _grid.Dock = DockStyle.Fill;
        card.AddContent(_grid);

        var legend = Ui.Note("دبل كليك = إلغاء التثبيت • \"إزالة إجبارية\" لما برنامج الإلغاء بايظ أو مش موجود: بتدوّر على البقايا وتشيلها من غير ما تشغّله • " +
                             "قبل أول عملية في الجلسة بتتعمل نقطة استعادة", UiTheme.Muted, UiTheme.Small);
        legend.Dock = DockStyle.Bottom;
        legend.Padding = new Padding(0, UiTheme.Px(8), 0, 0);
        Ui.FitHeight(legend);

        Body.Controls.Add(card);
        Body.Controls.Add(legend);
        UpdateButtons();
    }

    public override void OnActivated()
    {
        if (!_loaded) Reload(force: false);
    }

    private void Reload(bool force)
    {
        if (_loaded && !force) return;
        try
        {
            _all = InstalledPrograms.Scan(_showSystem.Checked);
            _loaded = true;
            Render();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Uninstall", "تعذر جرد البرامج", ex);
            _status.Text = "تعذر قراءة قايمة البرامج: " + ex.Message;
        }
    }

    private void Render()
    {
        string q = _search.Text.Trim();
        var view = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(p => p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || p.Publisher.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();

        string? keep = (_grid.CurrentRow?.Tag as InstalledProgram)?.Id;
        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var p in view)
        {
            int i = _grid.Rows.Add(p.Name, p.Publisher, p.Version, p.SizeBytes > 0 ? Format.Bytes(p.SizeBytes) : "",
                p.InstallDate?.ToString("yyyy-MM-dd") ?? "", p.ScopeDisplay, p.InstallLocation ?? "");
            var row = _grid.Rows[i];
            row.Tag = p;
            if (!p.HasUninstaller) row.DefaultCellStyle.ForeColor = UiTheme.Warning;
            if (p.IsSystemComponent) row.DefaultCellStyle.ForeColor = UiTheme.Muted;
            if (p.Id == keep) _grid.CurrentCell = row.Cells[0];
        }
        _grid.ResumeLayout();

        long total = _all.Sum(p => p.SizeBytes);
        _status.Text = $"البرامج: {_all.Count}{(view.Count != _all.Count ? $" — المعروض: {view.Count}" : "")} — الحجم المسجّل: {Format.Bytes(total)}" +
                       (_all.Any(p => !p.HasUninstaller) ? $" — بالبرتقالي: مالهوش برنامج إلغاء ({_all.Count(p => !p.HasUninstaller)})" : "");
        UpdateButtons();
    }

    private InstalledProgram? Selected => _grid.CurrentRow?.Tag as InstalledProgram;

    private void UpdateButtons()
    {
        var p = Selected;
        bool idle = !IsBusy;
        bool admin = AppEnvironment.IsAdministrator || p?.Scope == ProgramScope.User;
        _uninstall.Enabled = idle && p is not null && p.HasUninstaller && admin;
        _force.Enabled = idle && p is not null && admin;
        _open.Enabled = idle && p?.InstallLocation is { } l && Directory.Exists(l);
        _refresh.Enabled = idle;
    }

    private void SetBusy(bool busy)
    {
        _progress.Visible = busy;
        _grid.Enabled = !busy;
        _search.Enabled = _showSystem.Enabled = _quiet.Enabled = !busy;
        UpdateButtons();
    }

    private Progress<ModuleProgress> MakeProgress() => new(p =>
    {
        _progress.Value = Math.Clamp(p.Percent, 0, 100);
        _status.Text = p.Message;
    });

    private async Task UninstallAsync(bool force)
    {
        var p = Selected;
        if (p is null || IsBusy) return;
        if (p.Scope != ProgramScope.User && !RequireAdmin()) return;

        if (p.NoRemove && !force &&
            !Dialogs.Confirm(FindForm(), $"\"{p.Name}\" معلَّم إنه مايتشالش (NoRemove) — غالبًا جزء من برنامج تاني. تكمّل برضه؟", "تحذير", true))
            return;

        string question = force
            ? $"إزالة إجبارية لـ \"{p.Name}\"؟\n\nمش هيتشغّل برنامج الإلغاء — هدوّر على البقايا (ومنها مدخله في قايمة البرامج) وتراجعها قبل المسح.\n" +
              "استخدمها بس لو برنامج الإلغاء بايظ أو مش موجود."
            : $"إلغاء تثبيت \"{p.Name}\" {p.Version}؟\n\nهيتشغّل برنامج الإلغاء بتاعه — كمّل خطواته في نافذته، وبعدها هدوّر على البقايا.";
        if (!Dialogs.Confirm(FindForm(), question, "تأكيد", force)) return;

        await RunGuardedAsync(async ct =>
        {
            var progress = MakeProgress();

            if (AppEnvironment.IsAdministrator)
            {
                _status.Text = "نقطة استعادة (أول مرة في الجلسة)...";
                var rp = await RestorePointService.EnsureForSessionAsync("AJYAL PC Care - قبل إلغاء تثبيت برنامج", ct);
                if (!rp.Created && !rp.Skipped &&
                    !Dialogs.Confirm(FindForm(), rp.Message + "\n\nتكمّل من غير نقطة استعادة؟ (البقايا برضه هيتاخدلها نسخ احتياطية)", "نقطة الاستعادة", true))
                    return;
            }

            string message;
            if (force)
            {
                message = "إزالة إجبارية — برنامج الإلغاء ماتشغّلش.";
                AppLogger.Info("Uninstall", $"إزالة إجبارية: {p.Name}");
            }
            else
            {
                bool quiet = _quiet.Checked && (p.IsMsi || !string.IsNullOrWhiteSpace(p.QuietUninstallString));
                var outcome = await UninstallRunner.RunAsync(p, quiet, progress, ct);
                if (!outcome.Started)
                {
                    if (Dialogs.Confirm(FindForm(), outcome.Message + "\n\nتعمل إزالة إجبارية (فحص البقايا ومسحها)؟", "برنامج الإلغاء", true))
                        message = "برنامج الإلغاء ماشتغلش — إزالة إجبارية.";
                    else return;
                }
                else if (outcome.Cancelled)
                {
                    _status.Text = "اتلغى من نافذة برنامج الإلغاء — ماتمسحش حاجة.";
                    return;
                }
                else if (outcome.StillRegistered)
                {
                    if (!Dialogs.Confirm(FindForm(),
                            $"برنامج الإلغاء خلص بس \"{p.Name}\" لسه متسجل كمتثبت (يمكن اتلغى أو فشل).\n\nتدوّر على البقايا برضه؟ (بيتعرضوا عليك قبل أي مسح)", "تنبيه", true))
                    {
                        Reload(force: true);
                        return;
                    }
                    message = "برنامج الإلغاء خلص بس البرنامج لسه متسجل.";
                }
                else
                {
                    message = outcome.NeedsRestart ? "اتشال بنجاح — ومحتاج إعادة تشغيل للجهاز." : "برنامج الإلغاء خلص بنجاح.";
                }
            }

            _status.Text = "بدوّر على البقايا...";
            var leftovers = await LeftoverScanner.ScanAsync(p, progress, ct);

            using var dlg = new LeftoversDialog(p, leftovers, message);
            if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
            {
                var results = await LeftoverRemover.RemoveAsync(p, leftovers, progress, ct);
                ShowResults(p, results);
            }
            else
            {
                _status.Text = leftovers.Count == 0 ? $"\"{p.Name}\" اتشال ومفيش بقايا ✓" : "البقايا اتسابت زي ما هي.";
            }

            Reload(force: true);
        }, SetBusy);
    }

    private void ShowResults(InstalledProgram p, List<RemovalItemResult> results)
    {
        int ok = results.Count(r => r.Success);
        var failed = results.Where(r => !r.Success).ToList();
        long freed = results.Where(r => r.Success).Sum(r => r.Item.SizeBytes);
        _status.Text = $"\"{p.Name}\": اتشال {ok} من {results.Count} عنصر — {Format.Bytes(freed)} في سلة المحذوفات";

        if (failed.Count == 0)
        {
            Dialogs.Info(FindForm(), $"تم ✓\n\nاتشال {ok} عنصر.\nالفولدرات في سلة المحذوفات، والريجستري ليه نسخ احتياطية في صفحة \"السجل والتراجع\".", "إلغاء التثبيت");
            return;
        }

        var sb = new StringBuilder($"اتشال {ok} عنصر، و{failed.Count} ماتشالوش:\n\n");
        foreach (var f in failed.Take(12)) sb.Append($"• {f.Item.Path}\n   {f.Message}\n");
        if (failed.Count > 12) sb.Append($"… و{failed.Count - 12} كمان (التفاصيل في اللوج)\n");
        sb.Append("\nالملفات المستخدمة غالبًا بتتشال بعد إعادة تشغيل الجهاز وتكرار الإزالة الإجبارية.");
        Dialogs.Info(FindForm(), sb.ToString(), "إلغاء التثبيت");
    }

    private void OpenLocation()
    {
        if (Selected?.InstallLocation is { } l && Directory.Exists(l)) Ui.OpenPath(l);
    }
}
