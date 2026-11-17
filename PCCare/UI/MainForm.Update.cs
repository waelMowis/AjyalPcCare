using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Update;
using AjyalPcCare.UI.Controls;
using AjyalPcCare.UI.Theme;
using AjyalPcCare.UI.Update;

namespace AjyalPcCare.UI;

/// <summary>
/// التحديث التلقائي من GitHub Releases (نفس طريقة «مسار»):
/// البرنامج يشوف آخر إصدار ← يسأل ← ينزّل المثبّت ويتأكد منه ← يقفل ← المثبّت يحدّث ← البرنامج يفتح تاني.
/// </summary>
public sealed partial class MainForm
{
    /// <summary>المثبّت اللي هيتشغّل بعد ما الواجهة تقفل (Program.cs بيشغّله).</summary>
    public static string? PendingInstaller { get; private set; }

    private readonly ToolStripStatusLabel _statusUpdate = new() { ForeColor = UiTheme.Brand };
    private Button _checkUpdatesButton = null!;
    private Button _updateAvailableButton = null!;
    private bool _updating;
    private bool _closingForUpdate;

    private void BuildUpdateNavButtons()
    {
        _checkUpdatesButton = NavFooterButton("التحقق من التحديثات", UiTheme.BrandDark, Color.FromArgb(200, 230, 222));
        _checkUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);

        _updateAvailableButton = NavFooterButton("⬇ فيه تحديث جديد", Color.FromArgb(255, 196, 61), UiTheme.Text);
        _updateAvailableButton.Font = UiTheme.Bold;
        _updateAvailableButton.Visible = false;
        _updateAvailableButton.Click += async (_, _) => await CheckForUpdatesAsync(manual: true);
    }

    private Button NavFooterButton(string text, Color back, Color fore)
    {
        var b = new Button
        {
            Text = text,
            Dock = DockStyle.Bottom,
            Height = Measure.LineHeight(UiTheme.Base) + UiTheme.Px(18),
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Font = UiTheme.Small,
            Cursor = Cursors.Hand,
            TextAlign = ContentAlignment.MiddleCenter
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = UiTheme.Brand;
        return b;
    }

    /// <summary>بعد ما الشباك يفتح: «اتحدّث ✓» لو ده أول تشغيل بعد تحديث، وبعد ثواني نتحقق من الجديد.</summary>
    private async void AfterStartupUpdateCheck()
    {
        try
        {
            var settings = SettingsStore.Current;
            string current = UpdateChecker.CurrentVersionText;
            if (!string.IsNullOrEmpty(settings.Update.LastRunVersion) && settings.Update.LastRunVersion != current)
            {
                _statusUpdate.Text = $"اتحدّث لـ {current} ✓";
                AppLogger.Info("Update", $"أول تشغيل بعد التحديث: {settings.Update.LastRunVersion} ← {current}");
            }
            if (settings.Update.LastRunVersion != current)
            {
                settings.Update.LastRunVersion = current;
                SettingsStore.TrySave(settings);
            }

            if (!settings.Update.CheckOnStartup || UpdateChecker.Repo is null) return;
            await Task.Delay(TimeSpan.FromSeconds(6)); // مانبطّأش فتح البرنامج
            if (!IsDisposed) await CheckForUpdatesAsync(manual: false);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update", "خطأ في التحقق عند الفتح", ex);
        }
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        string? repo = UpdateChecker.Repo;
        if (repo is null)
        {
            if (manual) Dialogs.Info(this, "مستودع التحديثات مش متحدد في النسخة دي.\nاكتبه في pccare.settings.json ← Update ← Repo بالشكل owner/repo.", "التحديثات");
            return;
        }
        if (_updating)
        {
            _statusUpdate.Text = "التحديث شغال بالفعل…";
            return;
        }

        try
        {
            _checkUpdatesButton.Enabled = false;
            if (manual) _statusUpdate.Text = "بشوف فيه تحديث ولا لأ…";
            var latest = await UpdateChecker.GetLatestAsync(repo, CancellationToken.None);
            if (IsDisposed) return;

            if (latest is null || !UpdateChecker.IsNewer(latest.Version, UpdateChecker.CurrentVersion))
            {
                _updateAvailableButton.Visible = false;
                if (manual)
                {
                    _statusUpdate.Text = "";
                    Dialogs.Info(this, $"إنت على آخر إصدار ({UpdateChecker.CurrentVersionText}) ✓", "التحديثات");
                }
                return;
            }

            _updateAvailableButton.Text = $"⬇ تحديث {latest.Version.ToString(3)} متاح";
            _updateAvailableButton.Visible = true;
            _statusUpdate.Text = $"فيه إصدار جديد: {latest.Tag}";

            // الإصدار اللي المستخدم قال «تخطّاه» مايفتحش نافذة لوحده — الزرار في القايمة بس
            var settings = SettingsStore.Current;
            if (!manual && string.Equals(settings.Update.SkippedVersion, latest.Tag, StringComparison.OrdinalIgnoreCase)) return;
            // لو فيه عملية شغالة (تنظيف/DISM) مانقاطعهاش — الزرار في القايمة بيكفي
            if (!manual && _pages.Values.Any(p => p.IsBusy)) return;

            using var dlg = new UpdateDialog(latest);
            switch (dlg.ShowDialog(this))
            {
                case DialogResult.OK:
                    await DownloadAndInstallAsync(latest, repo);
                    break;
                case DialogResult.Ignore:
                    settings.Update.SkippedVersion = latest.Tag;
                    SettingsStore.TrySave(settings);
                    _statusUpdate.Text = $"مش هفكّرك بـ {latest.Tag} تاني — التحديث متاح من زرار القايمة وقت ما تحب";
                    break;
                default:
                    _statusUpdate.Text = "التحديث متاح وقت ما تحب من زرار القايمة";
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update", "فشل التحقق من التحديثات", ex);
            if (manual)
            {
                _statusUpdate.Text = "";
                Dialogs.Error(this, "مقدرتش أتحقق من التحديثات:\n" + ex.Message + "\n\nاتأكد إن النت شغال.", "التحديثات");
            }
        }
        finally
        {
            if (!IsDisposed) _checkUpdatesButton.Enabled = true;
        }
    }

    private async Task DownloadAndInstallAsync(UpdateChecker.Release r, string repo)
    {
        if (r.SetupUrl is null)
        {
            Ui.OpenPath(r.Url);
            return;
        }
        if (_updating) return;
        if (!AppEnvironment.IsAdministrator)
        {
            Dialogs.Info(this, "التحديث بيتثبت في Program Files وده محتاج صلاحيات مسؤول.\nاعمل \"إعادة التشغيل كمسؤول\" من الشريط اللي فوق وجرّب تاني.", "التحديثات");
            return;
        }

        _updating = true;
        try
        {
            var progress = new Progress<int>(p => _statusUpdate.Text = $"بنزّل التحديث {r.Tag}… {p}%");
            string file = await UpdateChecker.DownloadSetupAsync(r, repo, progress, CancellationToken.None);
            if (IsDisposed) return;
            _statusUpdate.Text = "التحديث اتنزّل واتأكدنا منه ✓";

            if (_pages.Values.Any(p => p.IsBusy) &&
                !Dialogs.Confirm(this, "فيه عملية شغالة دلوقتي وهتتلغي لو كمّلنا التحديث. نكمّل؟", "تحديث", true))
            {
                _statusUpdate.Text = "التحديث متنزّل وجاهز — دوس على زرار التحديث في القايمة لما العملية تخلص";
                return;
            }

            if (!Dialogs.Confirm(this,
                    $"التحديث {r.Version.ToString(3)} جاهز.\n\nالبرنامج هيقفل، والتحديث هيتثبت، وبعدين يفتح تاني لوحده.\n\nنكمّل؟", "تحديث"))
            {
                _statusUpdate.Text = "التحديث متنزّل وجاهز يتثبت لما توافق";
                return;
            }

            PendingInstaller = file;
            _closingForUpdate = true;
            Close(); // Program.cs بيشغّل المثبّت بعد ما الواجهة تقفل
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update", "فشل تنزيل التحديث", ex);
            _statusUpdate.Text = "";
            if (Dialogs.Confirm(this, "مقدرتش أنزّل التحديث:\n" + ex.Message + "\n\nتحب تفتح صفحة التحميل في المتصفح؟", "تحديث"))
                Ui.OpenPath(r.Url);
        }
        finally
        {
            _updating = false;
        }
    }
}
