using System.Diagnostics;
using AjyalPcCare.Core.Common;
using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Cleanup;

public static class CleanupCategories
{
    public const string UserTemp = "UserTemp";
    public const string WindowsTemp = "WindowsTemp";
    public const string BrowserCache = "BrowserCache";
    public const string RecycleBin = "RecycleBin";
    public const string WindowsUpdate = "WindowsUpdate";
    public const string DeliveryOptimization = "DeliveryOptimization";
    public const string CrashDumps = "CrashDumps";

    public static readonly string[] All = [UserTemp, WindowsTemp, BrowserCache, RecycleBin, WindowsUpdate, DeliveryOptimization, CrashDumps];

    /// <summary>أسماء مختصرة للـ CLI: temp,wintemp,browser,recycle,wu,do,dumps</summary>
    public static string? FromCliName(string name) => name.Trim().ToLowerInvariant() switch
    {
        "temp" or "usertemp" => UserTemp,
        "wintemp" or "windowstemp" => WindowsTemp,
        "browser" or "browsercache" => BrowserCache,
        "recycle" or "recyclebin" => RecycleBin,
        "wu" or "windowsupdate" => WindowsUpdate,
        "do" or "deliveryoptimization" => DeliveryOptimization,
        "dumps" or "crashdumps" => CrashDumps,
        "all" => "all",
        _ => null
    };
}

/// <summary>هدف تنظيف واحد: مسار + نمط + هل يطبق شرط العمر.</summary>
internal sealed record CleanupTarget(string Path, string Pattern = "*", bool ApplyMinAge = true, bool RemoveEmptyDirs = true);

/// <summary>
/// وحدة تنظيف الملفات: Analyze = Dry-Run حقيقي (يحسب الحجم الفعلي) → المستخدم يختار → Execute.
/// </summary>
public sealed class CleanupModule : IMaintenanceModule
{
    private readonly CleanupSettings _settings;
    private readonly HashSet<string>? _preselect;

    public string Id => "Cleanup";
    public string DisplayName => "تنظيف الملفات";
    public bool RequiresAdmin => false; // Temp المستخدم الحالي ما يحتاجش Admin — الفئات اللي تحتاج بتتعلم Unavailable

    /// <param name="preselect">الفئات المختارة افتراضيًا (null = حسب الإعدادات).</param>
    public CleanupModule(CleanupSettings settings, IEnumerable<string>? preselect = null)
    {
        _settings = settings;
        _preselect = preselect is null ? null : new HashSet<string>(preselect, StringComparer.OrdinalIgnoreCase);
    }

    private bool IsPreselected(string id) =>
        _preselect is not null ? _preselect.Contains(id) : _settings.DefaultCategories.Contains(id, StringComparer.OrdinalIgnoreCase);

    private TimeSpan MinAge => TimeSpan.FromHours(_settings.MinFileAgeHours);

    // ============================ تعريف الأهداف ============================

    private List<CleanupTarget> TargetsFor(string category)
    {
        var profiles = UserProfiles.GetProfiles(_settings.AllUserProfiles);
        string win = AppEnvironment.WindowsDirectory;
        var list = new List<CleanupTarget>();

        switch (category)
        {
            case CleanupCategories.UserTemp:
                foreach (var p in profiles) list.Add(new(Path.Combine(p.LocalAppData, "Temp")));
                break;

            case CleanupCategories.WindowsTemp:
                list.Add(new(Path.Combine(win, "Temp")));
                break;

            case CleanupCategories.BrowserCache:
                foreach (var p in profiles)
                {
                    foreach (var root in new[] { @"Google\Chrome\User Data", @"Microsoft\Edge\User Data", @"BraveSoftware\Brave-Browser\User Data" })
                    {
                        string ud = Path.Combine(p.LocalAppData, root);
                        if (!Directory.Exists(ud)) continue;
                        foreach (var prof in SafeDirs(ud).Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ", StringComparison.Ordinal)))
                        {
                            // الكاش بس — ممنوع نلمس Cookies أو History أو Login Data
                            list.Add(new(Path.Combine(prof, "Cache", "Cache_Data"), ApplyMinAge: false, RemoveEmptyDirs: false));
                            list.Add(new(Path.Combine(prof, "Code Cache"), ApplyMinAge: false, RemoveEmptyDirs: false));
                            list.Add(new(Path.Combine(prof, "GPUCache"), ApplyMinAge: false, RemoveEmptyDirs: false));
                        }
                    }
                    string ff = Path.Combine(p.LocalAppData, @"Mozilla\Firefox\Profiles");
                    foreach (var prof in SafeDirs(ff))
                        list.Add(new(Path.Combine(prof, "cache2"), ApplyMinAge: false, RemoveEmptyDirs: false));
                }
                break;

            case CleanupCategories.WindowsUpdate:
                list.Add(new(Path.Combine(win, @"SoftwareDistribution\Download"), ApplyMinAge: false));
                break;

            case CleanupCategories.DeliveryOptimization:
                list.Add(new(Path.Combine(win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"), ApplyMinAge: false));
                break;

            case CleanupCategories.CrashDumps:
                list.Add(new(Path.Combine(win, "Minidump"), "*.dmp", ApplyMinAge: false));
                list.Add(new(Path.Combine(win, "LiveKernelReports"), "*.dmp", ApplyMinAge: false));
                list.Add(new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER\ReportArchive"), ApplyMinAge: false));
                list.Add(new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER\ReportQueue"), ApplyMinAge: false));
                foreach (var p in profiles)
                {
                    list.Add(new(Path.Combine(p.LocalAppData, "CrashDumps"), ApplyMinAge: false));
                    list.Add(new(Path.Combine(p.LocalAppData, @"Microsoft\Windows\WER\ReportArchive"), ApplyMinAge: false));
                    list.Add(new(Path.Combine(p.LocalAppData, @"Microsoft\Windows\WER\ReportQueue"), ApplyMinAge: false));
                }
                break;
        }

        return list.Where(t => Directory.Exists(t.Path)).ToList();
    }

    private static IEnumerable<string> SafeDirs(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path) : [];
        }
        catch
        {
            return [];
        }
    }

    // ============================ Analyze ============================

    public async Task<AnalysisResult> AnalyzeAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var result = new AnalysisResult { ModuleId = Id };
        bool admin = AppEnvironment.IsAdministrator;
        var defs = new (string Id, string Title, string Desc, ItemRisk Risk, bool NeedsAdmin, string? Warning)[]
        {
            (CleanupCategories.UserTemp, "الملفات المؤقتة للمستخدمين", $"مجلد Temp لكل حساب — الملفات الأقدم من {_settings.MinFileAgeHours} ساعة فقط", ItemRisk.Safe, false, null),
            (CleanupCategories.WindowsTemp, "ملفات ويندوز المؤقتة", @"C:\Windows\Temp", ItemRisk.Safe, true, null),
            (CleanupCategories.BrowserCache, "كاش المتصفحات", "Chrome / Edge / Brave / Firefox — الكاش فقط (بدون كوكيز أو سجل أو كلمات مرور)", ItemRisk.Caution, false,
                "المواقع هتحمّل أبطأ شوية أول مرة بعد المسح. المتصفح المفتوح هيتم تخطيه."),
            (CleanupCategories.RecycleBin, "سلة المحذوفات", "إفراغ السلة نهائيًا", ItemRisk.Caution, false, "الملفات اللي في السلة مش هترجع تاني."),
            (CleanupCategories.WindowsUpdate, "ملفات تحميل Windows Update", @"SoftwareDistribution\Download — ويندوز بيعيد تحميل اللي محتاجه", ItemRisk.Caution, true,
                "خدمة Windows Update هتتوقف مؤقتًا أثناء المسح. ما تشغلهاش أثناء تسطيب تحديثات."),
            (CleanupCategories.DeliveryOptimization, "كاش Delivery Optimization", "ملفات مشاركة التحديثات بين الأجهزة", ItemRisk.Safe, true, null),
            (CleanupCategories.CrashDumps, "ملفات الأعطال وتقارير الأخطاء", "Minidump / CrashDumps / WER", ItemRisk.Safe, true,
                "لو بتحقق في مشكلة شاشة زرقا حالية، احتفظ بملفات Minidump قبل المسح."),
        };

        int i = 0;
        foreach (var d in defs)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ModuleProgress(i * 100 / defs.Length, "فحص: " + d.Title));
            var item = new AnalysisItem
            {
                Id = d.Id,
                Title = d.Title,
                Description = d.Desc,
                Risk = d.Risk,
                Warning = d.Warning,
                Selected = IsPreselected(d.Id)
            };

            if (d.NeedsAdmin && !admin)
            {
                item.Available = false;
                item.Selected = false;
                item.UnavailableReason = "يحتاج صلاحيات مسؤول";
            }
            else
            {
                await Task.Run(() => Measure(item, ct), ct).ConfigureAwait(false);
            }

            result.Items.Add(item);
            i++;
        }

        progress?.Report(new ModuleProgress(100, "اكتمل الفحص"));
        result.Notes.Add($"إجمالي المساحة القابلة للتحرير: {Format.Bytes(result.TotalBytes)}");
        AppLogger.Info(Id, $"Dry-Run: {Format.Bytes(result.TotalBytes)} في {result.Items.Sum(x => x.FileCount)} ملف");
        return result;
    }

    private void Measure(AnalysisItem item, CancellationToken ct)
    {
        if (item.Id == CleanupCategories.RecycleBin)
        {
            var (bytes, items) = RecycleBinApi.Query();
            item.SizeBytes = bytes;
            item.FileCount = (int)Math.Min(int.MaxValue, items);
            return;
        }

        var totals = new ScanTotals();
        foreach (var t in TargetsFor(item.Id))
            FileCleaner.Scan(t.Path, t.ApplyMinAge ? MinAge : TimeSpan.Zero, totals, ct, t.Pattern);
        item.SizeBytes = totals.Bytes;
        item.FileCount = totals.Files;

        if (item.Id == CleanupCategories.BrowserCache)
        {
            var running = RunningBrowsers();
            if (running.Count > 0)
                item.Warning = $"مفتوح حاليًا: {string.Join("، ", running)} — هيتم تخطيه. اقفله الأول لو عايز تمسح الكاش بتاعه.";
        }
    }

    // ============================ Execute ============================

    public async Task<ExecutionResult> ExecuteAsync(AnalysisResult preview, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var result = new ExecutionResult { ModuleId = Id };
        var selected = preview.Items.Where(x => x.Selected && x.Available).ToList();
        if (selected.Count == 0)
        {
            result.Messages.Add("لم يتم اختيار أي فئة");
            return result;
        }

        int i = 0;
        foreach (var item in selected)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ModuleProgress(i * 100 / selected.Count, "تنظيف: " + item.Title));
            var totals = new DeleteTotals();
            try
            {
                switch (item.Id)
                {
                    case CleanupCategories.RecycleBin:
                        var (before, _) = RecycleBinApi.Query();
                        var r = RecycleBinApi.Empty();
                        if (r.Success) totals.BytesFreed = before;
                        else result.Errors.Add(r.Message);
                        break;

                    case CleanupCategories.WindowsUpdate:
                        await CleanWindowsUpdateAsync(totals, result, ct).ConfigureAwait(false);
                        break;

                    case CleanupCategories.DeliveryOptimization:
                        await CleanDeliveryOptimizationAsync(item, totals, result, ct).ConfigureAwait(false);
                        break;

                    case CleanupCategories.BrowserCache:
                        await Task.Run(() => CleanBrowsers(totals, result, ct), ct).ConfigureAwait(false);
                        break;

                    default:
                        await Task.Run(() =>
                        {
                            foreach (var t in TargetsFor(item.Id))
                                FileCleaner.Delete(t.Path, t.ApplyMinAge ? MinAge : TimeSpan.Zero, totals, ct, t.Pattern, t.RemoveEmptyDirs);
                        }, ct).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{item.Title}: {ex.Message}");
                AppLogger.Error(Id, "خطأ في " + item.Title, ex);
            }

            result.BytesFreed += totals.BytesFreed;
            result.ItemsProcessed += totals.Deleted;
            result.ItemsSkipped += totals.Skipped;
            result.Messages.Add($"{item.Title}: تم تحرير {Format.Bytes(totals.BytesFreed)} ({totals.Deleted} ملف، تم تخطي {totals.Skipped})");
            AppLogger.Operation(Id, "تنظيف " + item.Title, true, totals.BytesFreed, $"deleted={totals.Deleted} skipped={totals.Skipped}");
            foreach (var s in totals.SkippedSamples.Take(20)) AppLogger.Debug(Id, "تخطي: " + s);
            i++;
        }

        result.Success = result.Errors.Count == 0;
        progress?.Report(new ModuleProgress(100, "اكتمل التنظيف"));
        return result;
    }

    private static readonly (string Process, string Name, string Marker)[] Browsers =
    [
        ("chrome", "Chrome", @"Google\Chrome"),
        ("msedge", "Edge", @"Microsoft\Edge"),
        ("brave", "Brave", "BraveSoftware"),
        ("firefox", "Firefox", "Mozilla")
    ];

    private static List<string> RunningBrowsers()
    {
        var list = new List<string>();
        foreach (var b in Browsers)
        {
            var procs = Process.GetProcessesByName(b.Process);
            if (procs.Length > 0) list.Add(b.Name);
            foreach (var p in procs) p.Dispose();
        }
        return list;
    }

    private void CleanBrowsers(DeleteTotals totals, ExecutionResult result, CancellationToken ct)
    {
        var running = Browsers.Where(b => RunningBrowsers().Contains(b.Name)).ToList();
        // ما بنقفلش المتصفح — قتل البروسيسات تلقائيًا ممنوع (ممكن يضيّع شغل المستخدم)
        foreach (var b in running)
            result.Messages.Add($"تم تخطي كاش {b.Name} لأنه مفتوح");

        foreach (var t in TargetsFor(CleanupCategories.BrowserCache))
        {
            if (running.Any(b => t.Path.Contains(b.Marker, StringComparison.OrdinalIgnoreCase))) continue;
            FileCleaner.Delete(t.Path, TimeSpan.Zero, totals, ct, t.Pattern, t.RemoveEmptyDirs);
        }
    }

    private async Task CleanWindowsUpdateAsync(DeleteTotals totals, ExecutionResult result, CancellationToken ct)
    {
        // لو TrustedInstaller شغال غالبًا فيه تسطيب تحديثات حاليًا — ما نلمسش حاجة
        var ti = Process.GetProcessesByName("TrustedInstaller");
        bool installing = ti.Length > 0;
        foreach (var p in ti) p.Dispose();
        if (installing)
        {
            result.Messages.Add("تم تخطي Windows Update: فيه تسطيب تحديثات شغال حاليًا (TrustedInstaller)");
            return;
        }

        var state = await ServiceControl.QueryStateAsync("wuauserv", ct).ConfigureAwait(false);
        bool wasRunning = state == ServiceState.Running;
        if (wasRunning)
        {
            if (!await ServiceControl.StopAsync("wuauserv", ct).ConfigureAwait(false))
            {
                result.Errors.Add("تعذر إيقاف خدمة Windows Update — تم تخطي الفئة");
                return;
            }
        }

        try
        {
            foreach (var t in TargetsFor(CleanupCategories.WindowsUpdate))
                FileCleaner.Delete(t.Path, TimeSpan.Zero, totals, ct, t.Pattern, t.RemoveEmptyDirs);
        }
        finally
        {
            // نرجّع الخدمة لحالتها الأصلية بس — ما نشغلهاش لو كانت واقفة
            if (wasRunning && !await ServiceControl.StartAsync("wuauserv", CancellationToken.None).ConfigureAwait(false))
                result.Errors.Add("تحذير: تعذر إعادة تشغيل خدمة Windows Update — شغّلها يدويًا أو أعد تشغيل الجهاز");
        }
    }

    private async Task CleanDeliveryOptimizationAsync(AnalysisItem item, DeleteTotals totals, ExecutionResult result, CancellationToken ct)
    {
        // الطريقة الرسمية: Delete-DeliveryOptimizationCache — الخدمة بتدير ملفاتها بنفسها
        long before = item.SizeBytes;
        var o = await PowerShell.RunAsync("Delete-DeliveryOptimizationCache -Force", ct, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
        if (o.Success)
        {
            var after = new ScanTotals();
            foreach (var t in TargetsFor(CleanupCategories.DeliveryOptimization))
                FileCleaner.Scan(t.Path, TimeSpan.Zero, after, ct);
            totals.BytesFreed = Math.Max(0, before - after.Bytes);
            totals.Deleted = Math.Max(0, item.FileCount - after.Files);
        }
        else
        {
            result.Errors.Add("Delivery Optimization: " + (string.IsNullOrWhiteSpace(o.StdErr) ? "فشل الأمر" : o.StdErr.Trim()));
        }
    }
}
