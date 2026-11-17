using AjyalPcCare.Core.Common;
using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Startup;

/// <summary>
/// غلاف الـ Startup بالعقد الموحد: Analyze يجرد كل العناصر، Execute يعطّل المختار فقط.
/// ولا عنصر بيتختار افتراضيًا — التعطيل قرار المستخدم.
/// </summary>
public sealed class StartupModule : IMaintenanceModule
{
    private List<StartupEntry> _entries = [];

    public string Id => "Startup";
    public string DisplayName => "برامج بدء التشغيل";
    public bool RequiresAdmin => false;

    public IReadOnlyList<StartupEntry> Entries => _entries;

    public async Task<AnalysisResult> AnalyzeAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        _entries = await StartupScanner.ScanAsync(progress, ct).ConfigureAwait(false);
        var result = new AnalysisResult { ModuleId = Id };
        foreach (var e in _entries)
        {
            result.Items.Add(new AnalysisItem
            {
                Id = e.Id,
                Title = e.Name,
                Description = $"{e.SourceDisplay} — {e.Publisher ?? "ناشر غير معروف"} — {e.SignatureDisplay}",
                Selected = false,
                Risk = e.IsMicrosoft ? ItemRisk.Advanced : ItemRisk.Caution,
                Available = e.CanToggle && e.Enabled,
                UnavailableReason = !e.CanToggle ? e.Note : (!e.Enabled ? "متعطل بالفعل" : null),
                Warning = e.IsBroken ? "مدخل مكسور: الملف غير موجود" : (e.Signature == SignatureState.Unsigned ? "ملف غير موقّع رقميًا" : null)
            });
        }

        int enabled = _entries.Count(e => e.Enabled);
        int broken = _entries.Count(e => e.IsBroken);
        result.Notes.Add($"إجمالي: {_entries.Count} — مفعّل: {enabled} — مكسور: {broken}");
        return result;
    }

    public async Task<ExecutionResult> ExecuteAsync(AnalysisResult preview, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var r = new ExecutionResult { ModuleId = Id };
        var selected = preview.Items.Where(i => i.Selected && i.Available).Select(i => i.Id).ToHashSet();
        var targets = _entries.Where(e => selected.Contains(e.Id)).ToList();
        int n = 0;
        foreach (var e in targets)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ModuleProgress(n * 100 / Math.Max(1, targets.Count), "تعطيل: " + e.Name));
            var t = await StartupManager.SetEnabledAsync(e, false, ct).ConfigureAwait(false);
            if (t.Success) { r.ItemsProcessed++; r.Messages.Add($"{e.Name}: {t.Message}"); }
            else { r.ItemsSkipped++; r.Errors.Add($"{e.Name}: {t.Message}"); }
            n++;
        }
        r.Success = r.Errors.Count == 0;
        return r;
    }
}
