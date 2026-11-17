using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Memory;

/// <summary>وحدة تفريغ Standby بالعقد الموحد — بتستخدم في وضع المعامل (CLI).</summary>
public sealed class StandbyPurgeModule(bool lowPriorityOnly = false) : IMaintenanceModule
{
    public string Id => "Memory";
    public string DisplayName => "تفريغ قائمة Standby";
    public bool RequiresAdmin => true;

    public Task<AnalysisResult> AnalyzeAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var lists = MemoryManager.GetLists();
        var result = new AnalysisResult { ModuleId = Id };
        var item = new AnalysisItem
        {
            Id = "StandbyList",
            Title = lowPriorityOnly ? "تفريغ Standby (أولوية منخفضة فقط)" : "تفريغ Standby (كامل)",
            Description = "تحويل ذاكرة الكاش إلى ذاكرة فارغة",
            Risk = ItemRisk.Advanced,
            Selected = true,
            Warning = "العملية دي بتمسح كاش مفيد — البرامج هتفتح أبطأ شوية أول مرة بعدها.",
            SizeBytes = lists is null ? 0 : (long)(lowPriorityOnly ? lists.StandbyByPriority[0] : lists.Standby)
        };
        if (lists is null)
        {
            item.Available = false;
            item.UnavailableReason = "قراءة قوائم الذاكرة تحتاج صلاحيات مسؤول";
        }
        result.Items.Add(item);
        return Task.FromResult(result);
    }

    public Task<ExecutionResult> ExecuteAsync(AnalysisResult preview, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var r = new ExecutionResult { ModuleId = Id };
        var item = preview.Items.FirstOrDefault(i => i.Id == "StandbyList");
        if (item is null || !item.Selected || !item.Available)
        {
            r.Messages.Add("لم يتم اختيار تفريغ الذاكرة");
            return Task.FromResult(r);
        }

        progress?.Report(new ModuleProgress(10, "جاري تفريغ Standby..."));
        var outcome = MemoryManager.PurgeStandby(lowPriorityOnly, "تنفيذ وحدة");
        r.Success = outcome.Success;
        r.ItemsProcessed = outcome.Success ? 1 : 0;
        // ملاحظة: BytesFreed هنا = زيادة الذاكرة الفارغة المقاسة، مش "مساحة ديسك"
        r.BytesFreed = Math.Max(0, outcome.FreeGained);
        if (outcome.Success) r.Messages.Add($"زادت الذاكرة الفارغة بمقدار {Format.Bytes(r.BytesFreed)}");
        else r.Errors.Add(outcome.Message);
        progress?.Report(new ModuleProgress(100, "تم"));
        return Task.FromResult(r);
    }
}
