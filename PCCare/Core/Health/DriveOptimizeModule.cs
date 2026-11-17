using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Health;

/// <summary>
/// تحسين الدرايفات بالطريقة الصحيحة لكل نوع:
/// SSD → ReTrim فقط (Defrag على SSD ممنوع: مالوش فايدة وبيستهلك عمر الذاكرة).
/// HDD → Defrag.
/// غير محدد → Optimize-Volume بدون خيارات = ويندوز يختار العملية المناسبة بنفسه.
/// </summary>
public sealed class DriveOptimizeModule : IMaintenanceModule
{
    public string Id => "Optimize";
    public string DisplayName => "تحسين الدرايفات (TRIM / Defrag)";
    public bool RequiresAdmin => true;

    public async Task<AnalysisResult> AnalyzeAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var r = new AnalysisResult { ModuleId = Id };
        progress?.Report(new ModuleProgress(10, "تحديد نوع الديسكات..."));
        var disks = await DiskHealthService.GetDisksAsync(ct).ConfigureAwait(false);
        var volumes = DiskHealthService.GetVolumes();

        foreach (var v in volumes)
        {
            string letter = v.Letter.TrimEnd(':');
            var disk = disks.FirstOrDefault(d => (d.DriveLetters ?? "").Split(',').Contains(letter, StringComparer.OrdinalIgnoreCase));
            string op = disk is null ? "Auto" : disk.IsSsd ? "ReTrim" : disk.IsHdd ? "Defrag" : "Auto";
            var item = new AnalysisItem
            {
                Id = letter,
                Title = $"الدرايف {v.Letter} — {(disk?.MediaDisplay ?? "غير محدد")}",
                Description = op switch
                {
                    "ReTrim" => "TRIM: إبلاغ الـ SSD بالمساحات الفاضية (سريع وآمن)",
                    "Defrag" => "Defrag: إعادة ترتيب الملفات على الـ HDD (ممكن ياخد وقت)",
                    _ => "ويندوز هيختار العملية المناسبة لنوع الديسك"
                },
                Risk = op == "Defrag" ? ItemRisk.Advanced : ItemRisk.Safe,
                Selected = true,
                Warning = op == "Defrag" ? "Defrag ممكن ياخد من دقايق لساعات حسب حجم الديسك ودرجة التجزئة." : null
            };
            item.Tags["Op"] = op;
            if (!v.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase) && !v.FileSystem.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
            {
                item.Available = false;
                item.Selected = false;
                item.UnavailableReason = $"نظام الملفات {v.FileSystem} غير مدعوم";
            }
            r.Items.Add(item);
        }

        if (!AppEnvironment.IsAdministrator)
            foreach (var i in r.Items) { i.Available = false; i.Selected = false; i.UnavailableReason = "يحتاج صلاحيات مسؤول"; }

        progress?.Report(new ModuleProgress(100, "تم"));
        return r;
    }

    public async Task<ExecutionResult> ExecuteAsync(AnalysisResult preview, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var result = new ExecutionResult { ModuleId = Id };
        var items = preview.Items.Where(i => i.Selected && i.Available).ToList();
        int n = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            string op = item.Tags.GetValueOrDefault("Op", "Auto");
            progress?.Report(new ModuleProgress(n * 100 / Math.Max(1, items.Count), $"{op} على {item.Id}:"));

            // حماية إضافية: لو الـ Tag اتغير لأي سبب، ما نعملش Defrag على SSD أبدًا
            string flag = op switch { "ReTrim" => "-ReTrim", "Defrag" => "-Defrag", _ => "" };
            if (op == "Defrag" && item.Title.Contains("SSD", StringComparison.OrdinalIgnoreCase)) flag = "-ReTrim";

            var o = await PowerShell.RunAsync($"Optimize-Volume -DriveLetter {item.Id} {flag} -ErrorAction Stop", ct, TimeSpan.FromHours(4)).ConfigureAwait(false);
            if (o.Success)
            {
                result.ItemsProcessed++;
                result.Messages.Add($"{item.Id}: اكتمل {(flag.Length == 0 ? "التحسين" : flag.TrimStart('-'))}");
            }
            else
            {
                result.ItemsSkipped++;
                result.Errors.Add($"{item.Id}: {o.StdErr.Trim()}");
            }
            n++;
        }
        result.Success = result.Errors.Count == 0;
        progress?.Report(new ModuleProgress(100, "تم"));
        return result;
    }
}
