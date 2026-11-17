using AjyalPcCare.Core.Cleanup;
using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Health;
using AjyalPcCare.Core.Memory;
using AjyalPcCare.Core.Reports;
using AjyalPcCare.Core.Scheduling;
using AjyalPcCare.Core.Update;

namespace AjyalPcCare.Cli;

public static class ExitCodes
{
    public const int Success = 0;
    public const int PartialFailure = 1;
    public const int Failure = 2;
    public const int AlreadyRunning = 3;
    public const int InvalidArguments = 4;
    public const int NotAdmin = 5;
}

/// <summary>
/// تشغيل وضع المعامل بدون واجهة: نفس الوحدات بالظبط، بنفس عقد Analyze → Execute،
/// مع اختيار الفئات من الخيارات بدل المستخدم، وفي الآخر تقرير.
/// </summary>
public static class CliRunner
{
    public static async Task<int> RunAsync(CliOptions options, CancellationToken ct = default)
    {
        var settings = SettingsStore.Current;

        if (options.Errors.Count > 0)
        {
            foreach (var e in options.Errors) AppLogger.Error("CLI", e);
            return ExitCodes.InvalidArguments;
        }

        if (options.InstallSchedule || options.RemoveSchedule)
            return await HandleScheduleAsync(options, settings, ct).ConfigureAwait(false);

        // /update لوحده (من غير /silent): تحديث بس
        if (options.Update && !options.Silent)
        {
            await UpdateChecker.RunSilentUpdateAsync(ct).ConfigureAwait(false);
            return ExitCodes.Success;
        }

        // منع تشغيلين في نفس الوقت (مثلًا المهمة المجدولة + تشغيل يدوي عن طريق GPO).
        // Semaphore مش Mutex: الـ Mutex مربوط بالـ Thread، والكود async بيكمل على Threads مختلفة.
        using var gate = new Semaphore(1, 1, @"Global\AjyalPcCare.Silent");
        bool acquired = gate.WaitOne(TimeSpan.Zero);
        if (!acquired)
        {
            AppLogger.Warn("CLI", "فيه نسخة صامتة تانية شغالة — خروج");
            return ExitCodes.AlreadyRunning;
        }

        try
        {
            return await RunMaintenanceAsync(options, settings, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error("CLI", "خطأ غير متوقع في الوضع الصامت", ex);
            return ExitCodes.Failure;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<int> RunMaintenanceAsync(CliOptions options, AppSettings settings, CancellationToken ct)
    {
        options.ApplyLabDefaults(settings.Lab);
        if (options.MinAgeHours.HasValue) settings.Cleanup.MinFileAgeHours = options.MinAgeHours.Value;

        AppLogger.Info("CLI", $"بدء الصيانة الصامتة — تنظيف=[{string.Join(",", options.CleanCategories)}] Standby={options.PurgeStandby} Optimize={options.Optimize} DISM={options.Dism}");
        if (!AppEnvironment.IsAdministrator)
            AppLogger.Warn("CLI", "التشغيل بدون صلاحيات مسؤول — العمليات اللي تحتاج Admin هتتخطى");

        var report = new MachineReport { Mode = "Silent" };
        var progress = new Progress<ModuleProgress>(p => AppLogger.Debug("CLI", $"{p.Percent}% {p.Message}"));

        var modules = new List<IMaintenanceModule>();
        if (options.CleanCategories.Count > 0) modules.Add(new CleanupModule(settings.Cleanup, options.CleanCategories));
        if (options.Dism) modules.Add(new DismCleanupModule());
        if (options.Optimize) modules.Add(new DriveOptimizeModule());
        // تفريغ الذاكرة آخر حاجة — بعد ما العمليات التقيلة تخلص
        if (options.PurgeStandby) modules.Add(new StandbyPurgeModule(options.PurgeStandbyLowOnly));

        foreach (var m in modules)
        {
            ct.ThrowIfCancellationRequested();
            var preview = await ModuleRunner.SafeAnalyzeAsync(m, progress, ct).ConfigureAwait(false);

            // في الوضع الصامت: بننفذ اللي اتحدد بس، ومفيش عنصر مش متاح بيتنفذ
            if (m is CleanupModule)
                foreach (var item in preview.Items)
                    item.Selected = item.Available && options.CleanCategories.Contains(item.Id, StringComparer.OrdinalIgnoreCase);

            var result = await ModuleRunner.SafeExecuteAsync(m, preview, progress, ct).ConfigureAwait(false);
            report.Modules.Add(ModuleRunSummary.From(m, result));
        }

        if (options.Health)
        {
            var health = await HealthService.AnalyzeAsync(settings.Health, progress, ct).ConfigureAwait(false);
            report.ApplyHealth(health);
        }

        string folder = !string.IsNullOrWhiteSpace(options.ReportFolder) ? options.ReportFolder! : settings.Lab.ReportFolder;
        try
        {
            await ReportWriter.WriteAsync(report, folder, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // لو مسار الشبكة مش متاح: نحفظ محليًا عشان التقرير ما يضيعش
            AppLogger.Error("CLI", $"تعذر الحفظ في {folder} — سيتم الحفظ محليًا", ex);
            await ReportWriter.WriteAsync(report, AppEnvironment.ReportsDirectory, ct).ConfigureAwait(false);
        }

        bool anyFailed = report.Modules.Any(m => !m.Success);
        AppLogger.Info("CLI", $"انتهت الصيانة — المساحة المحررة {Format.Bytes(report.DiskSpaceFreed)} — {(anyFailed ? "مع أخطاء" : "نجاح")}");

        // التحديث في الآخر خالص: بعد ما التقرير اتحفظ. المثبّت بيستنى البرنامج يخرج ويستبدل الملفات.
        if (options.Update) await UpdateChecker.RunSilentUpdateAsync(ct).ConfigureAwait(false);
        return anyFailed ? ExitCodes.PartialFailure : ExitCodes.Success;
    }

    private static async Task<int> HandleScheduleAsync(CliOptions options, AppSettings settings, CancellationToken ct)
    {
        if (!AppEnvironment.IsAdministrator) return ExitCodes.NotAdmin;
        (bool ok, string msg) = options.RemoveSchedule
            ? await ScheduledTaskService.RemoveAsync(ct).ConfigureAwait(false)
            : await ScheduledTaskService.CreateOrUpdateAsync(AppEnvironment.ExecutablePath, CliOptions.BuildScheduledArguments(settings.Lab),
                settings.Lab.ScheduleDay, settings.Lab.ScheduleTime, ct).ConfigureAwait(false);
        AppLogger.Info("CLI", msg);
        return ok ? ExitCodes.Success : ExitCodes.Failure;
    }
}
