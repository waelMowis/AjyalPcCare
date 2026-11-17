using System.Globalization;
using System.Text.RegularExpressions;
using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Cleanup;

/// <summary>
/// DISM /StartComponentCleanup — تنظيف مخزن مكونات ويندوز (WinSxS) بالطريقة الرسمية.
/// ممنوع نمسح من WinSxS يدويًا أبدًا. ومش بنستخدم /ResetBase افتراضيًا
/// لأنه بيمنع إلغاء تسطيب التحديثات الحالية.
/// </summary>
public sealed partial class DismCleanupModule : IMaintenanceModule
{
    public string Id => "Dism";
    public string DisplayName => "تنظيف مخزن مكونات ويندوز (DISM)";
    public bool RequiresAdmin => true;

    [GeneratedRegex(@"(\d{1,3}(?:[.,]\d)?)\s*%")]
    private static partial Regex PercentRegex();

    public Task<AnalysisResult> AnalyzeAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var r = new AnalysisResult { ModuleId = Id };
        var item = new AnalysisItem
        {
            Id = "StartComponentCleanup",
            Title = "DISM /StartComponentCleanup",
            Description = "إزالة النسخ القديمة من مكونات ويندوز اللي اتستبدلت بتحديثات",
            Risk = ItemRisk.Advanced,
            Warning = "العملية ممكن تاخد من 5 لـ 30 دقيقة والجهاز يكون تقيل أثناءها. ما تقفلش الجهاز في النص.",
            Selected = true
        };
        if (!AppEnvironment.IsAdministrator)
        {
            item.Available = false;
            item.Selected = false;
            item.UnavailableReason = "يحتاج صلاحيات مسؤول";
        }
        r.Items.Add(item);
        r.Notes.Add("المساحة المتوقعة غير معروفة مسبقًا — استخدم \"تحليل مخزن المكونات\" لو محتاج رقم (بياخد دقايق).");
        return Task.FromResult(r);
    }

    /// <summary>تحليل اختياري: DISM /AnalyzeComponentStore — بيرجع النص الكامل للتقرير.</summary>
    public static async Task<string> AnalyzeComponentStoreAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var o = await RunDismAsync("/Online /Cleanup-Image /AnalyzeComponentStore", progress, ct).ConfigureAwait(false);
        return o.StdOut;
    }

    public async Task<ExecutionResult> ExecuteAsync(AnalysisResult preview, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var result = new ExecutionResult { ModuleId = Id };
        if (!preview.Items.Any(i => i.Selected && i.Available))
        {
            result.Messages.Add("لم يتم اختيار DISM");
            return result;
        }

        var drive = new DriveInfo(AppEnvironment.SystemDrive);
        long freeBefore = drive.AvailableFreeSpace;

        var o = await RunDismAsync("/Online /Cleanup-Image /StartComponentCleanup", progress, ct).ConfigureAwait(false);

        drive = new DriveInfo(AppEnvironment.SystemDrive); // DriveInfo بيقرا القيم لحظة الاستدعاء
        // تقدير المساحة = فرق المساحة الفاضية على درايف النظام (أقرب قياس حقيقي متاح)
        result.BytesFreed = Math.Max(0, drive.AvailableFreeSpace - freeBefore);
        result.Success = o.Success;
        if (o.Success)
        {
            result.ItemsProcessed = 1;
            result.Messages.Add($"اكتمل DISM — زادت المساحة الفاضية على {AppEnvironment.SystemDrive} بمقدار {Format.Bytes(result.BytesFreed)}");
        }
        else
        {
            result.Errors.Add(o.TimedOut ? "انتهت المهلة (60 دقيقة)" : $"DISM انتهى بكود {o.ExitCode}: {LastLines(o.StdOut, 3)}");
        }
        return result;
    }

    private static Task<ProcessOutput> RunDismAsync(string args, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        string dism = Path.Combine(Environment.SystemDirectory, "dism.exe");
        return ProcessRunner.RunAsync(dism, args + " /English", ct, line =>
        {
            var m = PercentRegex().Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                progress?.Report(new ModuleProgress((int)Math.Clamp(pct, 0, 100), $"DISM: {pct:0.#}%"));
        }, TimeSpan.FromMinutes(60), System.Text.Encoding.UTF8);
    }

    private static string LastLines(string text, int n) =>
        string.Join(" | ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(n));
}
