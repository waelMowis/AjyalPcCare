using AjyalPcCare.Cli;
using AjyalPcCare.Core.Common;
using AjyalPcCare.UI.Services;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI;

internal static class Program
{
    /// <summary>
    /// نقطة دخول واحدة لـ PCCare.exe:
    /// - فيه خيارات CLI (/silent, /install-schedule, ...) → تشغيل صامت بدون أي نافذة ويرجع Exit Code.
    /// - مفيش → الواجهة.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        var settings = SettingsStore.Load();
        AppLogger.Initialize(retentionDays: settings.LogRetentionDays);

        // فحص الفتح (GitHub Actions قبل أي نشر)
        if (args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            UiTheme.Initialize(settings.Ui);
            Application.SetDefaultFont(UiTheme.Base);
            int smoke = SmokeTest.Run();
            AppServices.Instance.Dispose();
            return smoke;
        }

        var options = CliOptions.Parse(args);
        if (options.IsCliInvocation || options.Errors.Count > 0)
        {
            if (options.ShowHelp || options.Errors.Count > 0)
            {
                string text = (options.Errors.Count > 0 ? string.Join("\n", options.Errors) + "\n\n" : "") + CliOptions.HelpText;
                MessageBox.Show(text, "AJYAL PC Care — سطر الأوامر", MessageBoxButtons.OK,
                    options.Errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
                return options.Errors.Count > 0 ? ExitCodes.InvalidArguments : ExitCodes.Success;
            }

            // الكود كله ConfigureAwait(false) ومفيش SynchronizationContext هنا — الانتظار المتزامن آمن
            int code = CliRunner.RunAsync(options).GetAwaiter().GetResult();
            AppLogger.Info("App", $"خروج الوضع الصامت بكود {code}");
            return code;
        }

        // نسخة واحدة من الواجهة لكل مستخدم
        using var single = new Mutex(true, @"Local\AjyalPcCare.UI", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("الأداة مفتوحة بالفعل.", "AJYAL PC Care", MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
            return ExitCodes.AlreadyRunning;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            AppLogger.Error("UI", "استثناء غير معالج", e.Exception);
            Dialogs.Error(null, "حصل خطأ غير متوقع واتسجل في اللوج:\n" + e.Exception.Message);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLogger.Error("App", "استثناء قاتل", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLogger.Error("App", "Task استثناء غير ملاحظ", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        UiTheme.Initialize(settings.Ui);
        Application.SetDefaultFont(UiTheme.Base);

        try
        {
            Application.Run(new MainForm());
        }
        finally
        {
            AppServices.Instance.Dispose();
            AppLogger.Info("App", "إغلاق الواجهة");
        }

        // تحديث متنزّل ومتأكد منه: المثبّت يشتغل بعد ما الواجهة قفلت، ويفتحها تاني لما يخلص
        if (MainForm.PendingInstaller is { } installer)
        {
            single.ReleaseMutex();
            Core.Update.UpdateChecker.LaunchInstaller(installer, relaunch: true);
        }
        return ExitCodes.Success;
    }
}
