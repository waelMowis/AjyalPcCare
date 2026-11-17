namespace AjyalPcCare.Core.Common;

/// <summary>درجة خطورة العنصر — بتتعرض للمستخدم قبل التنفيذ.</summary>
public enum ItemRisk
{
    /// <summary>آمن تمامًا (ملفات مؤقتة قديمة مثلًا).</summary>
    Safe,
    /// <summary>آمن بس ليه أثر جانبي مؤقت (زي فتح المتصفح أبطأ أول مرة بعد مسح الكاش).</summary>
    Caution,
    /// <summary>عملية متقدمة: وقت طويل أو أثر على النظام (DISM، تفريغ Standby).</summary>
    Advanced
}

public enum Severity
{
    Info,
    Warning,
    Critical
}

/// <summary>عنصر واحد في نتيجة التحليل (فئة تنظيف، عملية، إلخ).</summary>
public sealed class AnalysisItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public bool Selected { get; set; }
    public ItemRisk Risk { get; init; } = ItemRisk.Safe;
    /// <summary>تحذير لازم يظهر للمستخدم قبل التنفيذ (لو موجود).</summary>
    public string? Warning { get; set; }
    public bool Available { get; set; } = true;
    public string? UnavailableReason { get; set; }
    /// <summary>بيانات إضافية خاصة بالوحدة (مسارات مثلًا) — مش للعرض.</summary>
    public Dictionary<string, string> Tags { get; } = new();
}

/// <summary>نتيجة Analyze — وهي نفسها الـ Preview اللي المستخدم يختار منها قبل Execute.</summary>
public sealed class AnalysisResult
{
    public required string ModuleId { get; init; }
    public DateTime AnalyzedAt { get; init; } = DateTime.Now;
    public List<AnalysisItem> Items { get; init; } = [];
    public List<string> Notes { get; init; } = [];
    public long TotalBytes => Items.Where(i => i.Available).Sum(i => i.SizeBytes);
    public long SelectedBytes => Items.Where(i => i.Available && i.Selected).Sum(i => i.SizeBytes);
}

/// <summary>نتيجة Execute — أرقام حقيقية مقاسة، مش تقديرات.</summary>
public sealed class ExecutionResult
{
    public required string ModuleId { get; init; }
    public bool Success { get; set; } = true;
    public long BytesFreed { get; set; }
    public int ItemsProcessed { get; set; }
    public int ItemsSkipped { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Messages { get; } = [];
    public List<string> Errors { get; } = [];

    public static ExecutionResult Failed(string moduleId, string error)
    {
        var r = new ExecutionResult { ModuleId = moduleId, Success = false };
        r.Errors.Add(error);
        return r;
    }
}

public readonly record struct ModuleProgress(int Percent, string Message);

/// <summary>
/// العقد الموحد لكل وحدة: Analyze → Preview (AnalysisResult) → Execute → Result.
/// قاعدة: ولا وحدة ترمي Exception لبره — أي خطأ يرجع جوه النتيجة.
/// </summary>
public interface IMaintenanceModule
{
    string Id { get; }
    string DisplayName { get; }
    bool RequiresAdmin { get; }

    Task<AnalysisResult> AnalyzeAsync(IProgress<ModuleProgress>? progress, CancellationToken ct);

    Task<ExecutionResult> ExecuteAsync(AnalysisResult preview, IProgress<ModuleProgress>? progress, CancellationToken ct);
}

/// <summary>غلاف بيضمن إن أي Exception جوه وحدة ما يوقعش الأداة.</summary>
public static class ModuleRunner
{
    public static async Task<AnalysisResult> SafeAnalyzeAsync(IMaintenanceModule module, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        try
        {
            return await module.AnalyzeAsync(progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var r = new AnalysisResult { ModuleId = module.Id };
            r.Notes.Add("تم إلغاء التحليل");
            return r;
        }
        catch (Exception ex)
        {
            AppLogger.Error(module.Id, "فشل التحليل", ex);
            var r = new AnalysisResult { ModuleId = module.Id };
            r.Notes.Add("فشل التحليل: " + ex.Message);
            return r;
        }
    }

    public static async Task<ExecutionResult> SafeExecuteAsync(IMaintenanceModule module, AnalysisResult preview, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (module.RequiresAdmin && !AppEnvironment.IsAdministrator)
                return ExecutionResult.Failed(module.Id, "العملية دي محتاجة صلاحيات مسؤول (Administrator)");

            var result = await module.ExecuteAsync(preview, progress, ct).ConfigureAwait(false);
            result.Duration = sw.Elapsed;
            AppLogger.Operation(module.Id, "تنفيذ", result.Success, result.BytesFreed,
                $"processed={result.ItemsProcessed} skipped={result.ItemsSkipped} errors={result.Errors.Count}");
            return result;
        }
        catch (OperationCanceledException)
        {
            var r = ExecutionResult.Failed(module.Id, "تم الإلغاء بواسطة المستخدم");
            r.Duration = sw.Elapsed;
            AppLogger.Warn(module.Id, "تم إلغاء التنفيذ");
            return r;
        }
        catch (Exception ex)
        {
            AppLogger.Error(module.Id, "فشل التنفيذ", ex);
            var r = ExecutionResult.Failed(module.Id, ex.Message);
            r.Duration = sw.Elapsed;
            return r;
        }
    }
}
