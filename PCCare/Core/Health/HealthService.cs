using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Startup;
using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Health;

public sealed record HealthFinding(string Rule, string Title, string Detail, int Penalty, Severity Severity, string Recommendation);

public sealed class HealthReport
{
    public DateTime GeneratedAt { get; init; } = DateTime.Now;
    public int Score { get; set; }
    public string Grade => Score switch
    {
        >= 85 => "ممتاز",
        >= 70 => "جيد",
        >= 50 => "يحتاج صيانة",
        _ => "حرج"
    };
    public List<HealthFinding> Findings { get; } = [];
    public required SystemInfo System { get; init; }
    public SystemExtras? Extras { get; set; }
    public List<PhysicalDiskInfo> Disks { get; set; } = [];
    public List<VolumeInfo> Volumes { get; set; } = [];
    public List<PowerPlan> PowerPlans { get; set; } = [];
    public int? StartupEnabledCount { get; set; }
    public int? StartupBrokenCount { get; set; }
    public MemoryStatus? Memory { get; set; }
}

/// <summary>
/// مؤشر صحة الجهاز — كل القواعد معلنة هنا بالخصم بتاعها، عشان الرقم يبقى قابل للتفسير
/// (مش رقم سحري). يبدأ من 100 ويتخصم حسب المشاكل المكتشفة فعلًا.
///
/// القواعد:
///  R1 مساحة درايف النظام: أقل من الحرج = -35، أقل من المنخفض = -20
///  R2 مساحة باقي الدرايفات: أقل من الحرج = -10 لكل درايف (حد أقصى -20)
///  R3 ضغط الذاكرة: Commit > 90% من الحد = -20، > 80% = -10
///  R4 صحة الديسك: Unhealthy = -40، Warning = -20 (لكل ديسك)
///  R5 ديسك النظام HDD = -10 (أكبر سبب بطء في الأجهزة القديمة)
///  R6 عناصر Startup المفعّلة: أكتر من الحد = -10، أكتر من ضعف الحد = -15
///  R7 مدة التشغيل من غير Restart: أكتر من الحد = -5
///  R8 آخر تحديث ويندوز: أقدم من 60 يوم = -10
///  R9 خطة الطاقة توفير الطاقة على جهاز مكتبي = -5
/// </summary>
public static class HealthService
{
    public static async Task<HealthReport> AnalyzeAsync(HealthSettings settings, IProgress<ModuleProgress>? progress, CancellationToken ct, bool includeStartup = true)
    {
        progress?.Report(new ModuleProgress(5, "معلومات النظام..."));
        var report = new HealthReport { System = SystemInfoService.Get(), Memory = MemoryApi.GetStatus() };

        progress?.Report(new ModuleProgress(15, "الديسكات والمساحات..."));
        report.Volumes = DiskHealthService.GetVolumes();
        report.Disks = await DiskHealthService.GetDisksAsync(ct).ConfigureAwait(false);

        progress?.Report(new ModuleProgress(45, "التحديثات وملف الصفحات..."));
        report.Extras = await SystemInfoService.GetExtrasAsync(ct).ConfigureAwait(false);

        progress?.Report(new ModuleProgress(60, "خطة الطاقة..."));
        report.PowerPlans = await PowerPlanService.ListAsync(ct).ConfigureAwait(false);

        if (includeStartup)
        {
            progress?.Report(new ModuleProgress(70, "برامج بدء التشغيل..."));
            var entries = await StartupScanner.ScanAsync(null, ct).ConfigureAwait(false);
            report.StartupEnabledCount = entries.Count(e => e.Enabled && e.Source != StartupSource.Service);
            report.StartupBrokenCount = entries.Count(e => e.IsBroken);
        }

        progress?.Report(new ModuleProgress(95, "حساب المؤشر..."));
        Evaluate(report, settings);
        progress?.Report(new ModuleProgress(100, "تم"));
        AppLogger.Info("Health", $"مؤشر الصحة = {report.Score} ({report.Grade}) — {report.Findings.Count} ملاحظة");
        return report;
    }

    public static void Evaluate(HealthReport r, HealthSettings s)
    {
        r.Findings.Clear();
        string sysLetter = AppEnvironment.SystemDrive.TrimEnd('\\');

        // R1 + R2
        int otherPenalty = 0;
        foreach (var v in r.Volumes)
        {
            bool isSys = v.Letter.Equals(sysLetter, StringComparison.OrdinalIgnoreCase);
            if (v.FreePercent < s.CriticalDiskPercent)
            {
                if (isSys)
                    Add(r, "R1", $"مساحة {v.Letter} حرجة", $"الفاضي {Format.Bytes(v.Free)} من {Format.Bytes(v.Total)} ({v.FreePercent:0.#}%)", 35, Severity.Critical,
                        "نظّف الملفات المؤقتة وملفات التحديث، وانقل الملفات الكبيرة لدرايف تاني. ويندوز بيحتاج مساحة للـ Page File والتحديثات.");
                else if (otherPenalty < 20)
                {
                    Add(r, "R2", $"مساحة {v.Letter} حرجة", $"الفاضي {v.FreePercent:0.#}%", 10, Severity.Warning, "انقل أو امسح ملفات من الدرايف ده.");
                    otherPenalty += 10;
                }
            }
            else if (v.FreePercent < s.LowDiskPercent && isSys)
            {
                Add(r, "R1", $"مساحة {v.Letter} منخفضة", $"الفاضي {Format.Bytes(v.Free)} ({v.FreePercent:0.#}%)", 20, Severity.Warning,
                    $"يُفضل يبقى فيه على الأقل {s.LowDiskPercent}% فاضي على درايف النظام.");
            }
        }

        // R3
        if (r.Memory is { CommitLimit: > 0 } m)
        {
            double commit = m.CommitTotal * 100d / m.CommitLimit;
            if (commit > 90)
                Add(r, "R3", "ضغط عالي على الذاكرة", $"الذاكرة المحجوزة (Commit) {commit:0}% من الحد الأقصى", 20, Severity.Critical,
                    "اقفل البرامج اللي واكلة ذاكرة (شوف لوحة المراقبة) أو زوّد الرام. التنظيف مش هيحل المشكلة دي.");
            else if (commit > 80)
                Add(r, "R3", "ضغط متوسط على الذاكرة", $"Commit {commit:0}% من الحد الأقصى", 10, Severity.Warning,
                    "راقب البرامج المفتوحة — لو بيتكرر، الجهاز محتاج رام أكتر.");
        }

        // R4 + R5
        foreach (var d in r.Disks)
        {
            if (string.Equals(d.HealthStatus, "Unhealthy", StringComparison.OrdinalIgnoreCase))
                Add(r, "R4", $"الديسك {d.FriendlyName} غير سليم", $"HealthStatus = Unhealthy ({d.OperationalStatus})", 40, Severity.Critical,
                    "خد نسخة احتياطية فورًا واستبدل الديسك.");
            else if (string.Equals(d.HealthStatus, "Warning", StringComparison.OrdinalIgnoreCase))
                Add(r, "R4", $"تحذير على الديسك {d.FriendlyName}", $"HealthStatus = Warning ({d.OperationalStatus})", 20, Severity.Warning,
                    "خد نسخة احتياطية وراقب الديسك — ممكن يكون قرب يبوظ.");

            if (d.IsSystemDisk && d.IsHdd)
                Add(r, "R5", "ويندوز متسطب على HDD", d.FriendlyName ?? "", 10, Severity.Info,
                    "الترقية لـ SSD هي أكبر تحسين أداء ممكن للجهاز ده — أكبر من أي برنامج تنظيف.");
        }

        // R6
        if (r.StartupEnabledCount is int sc)
        {
            if (sc > s.StartupWarningCount * 2)
                Add(r, "R6", "برامج كتير بتشتغل مع الويندوز", $"{sc} عنصر مفعّل", 15, Severity.Warning, "راجع صفحة Startup وعطّل اللي مش محتاجه.");
            else if (sc > s.StartupWarningCount)
                Add(r, "R6", "عدد برامج Startup أعلى من المعتاد", $"{sc} عنصر مفعّل", 10, Severity.Info, "راجع صفحة Startup.");
        }
        if (r.StartupBrokenCount is > 0)
            Add(r, "R6", "مدخلات Startup مكسورة", $"{r.StartupBrokenCount} عنصر بيشاور على ملفات مش موجودة", 0, Severity.Info,
                "مالهاش أثر على الأداء، بس ممكن تعطيلها للتنظيم.");

        // R7
        if (r.System.Uptime.TotalDays > s.UptimeWarningDays)
            Add(r, "R7", "الجهاز شغال من فترة طويلة", $"آخر Restart من {Format.Duration(r.System.Uptime)}", 5, Severity.Info,
                "اعمل Restart (مش Shut down — Fast Startup بيخلي Shut down مش بيصفّر النظام فعليًا).");

        // R8
        if (r.Extras?.LastUpdateInstalled is DateTime lu && (DateTime.Now - lu).TotalDays > 60)
            Add(r, "R8", "التحديثات قديمة", $"آخر تحديث اتسطب {lu:yyyy-MM-dd}", 10, Severity.Warning,
                "شغّل Windows Update — التحديثات فيها إصلاحات أداء وأمان.");

        // R9
        var active = r.PowerPlans.FirstOrDefault(p => p.Active);
        if (active is not null && active.Id == PowerPlanService.PowerSaver)
            Add(r, "R9", "خطة الطاقة: توفير الطاقة", active.Name, 5, Severity.Info, PowerPlanService.Advice(active.Id));

        r.Score = Math.Clamp(100 - r.Findings.Sum(f => f.Penalty), 0, 100);
    }

    private static void Add(HealthReport r, string rule, string title, string detail, int penalty, Severity sev, string rec) =>
        r.Findings.Add(new HealthFinding(rule, title, detail, penalty, sev, rec));
}
