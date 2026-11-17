using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Monitoring;
using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Memory;

public sealed class OptimizeOptions
{
    /// <summary>كتابة الصفحات المعدّلة على الديسك (آمن تمامًا).</summary>
    public bool FlushModified { get; set; } = true;

    /// <summary>تفريغ الكاش (Standby) — بيزوّد "الفارغ" فعلًا، والبرامج بتفتح أبطأ شوية أول مرة بعده.</summary>
    public bool PurgeStandby { get; set; } = true;

    /// <summary>
    /// تقليص Working Set للبرامج الخاملة في الخلفية — ده اللي بينزّل نسبة "المستخدم" اللي في Task Manager.
    /// مقفول افتراضيًا: البرنامج اللي يتقلص بيرجع ياخد صفحاته لما تفتحه (تهنيج لحظي).
    /// </summary>
    public bool TrimIdleProcesses { get; set; }

    /// <summary>البرنامج يعتبر خامل لو استهلاكه للمعالج أقل من كده.</summary>
    public double IdleCpuPercent { get; set; } = 0.5;
}

public sealed record MemorySnapshot(ulong Total, ulong InUse, ulong Available, ulong Free, ulong Standby, ulong Modified, bool HasLists)
{
    public double InUsePercent => Total == 0 ? 0 : InUse * 100d / Total;

    public static MemorySnapshot Take()
    {
        var st = MemoryApi.GetStatus();
        var l = MemoryApi.GetMemoryLists();
        ulong total = st?.TotalPhysical ?? 0;
        ulong avail = st?.AvailablePhysical ?? 0;
        return new MemorySnapshot(total, total - avail, avail, l is null ? 0 : l.Free + l.Zeroed, l?.Standby ?? 0, l?.Modified ?? 0, l is not null);
    }
}

public sealed record OptimizeResult(MemorySnapshot Before, MemorySnapshot After, int TrimmedProcesses, long TrimmedBytes, List<string> Steps, List<string> Errors)
{
    public long FreeGained => (long)After.Free - (long)Before.Free;
    public long InUseReduced => (long)Before.InUse - (long)After.InUse;
}

/// <summary>
/// "نظّف الذاكرة الآن" — بالترتيب الصح وبقياس حقيقي قبل وبعد:
///   1. تقليص البرامج الخاملة (اختياري) ← صفحاتها بتروح Modified/Standby
///   2. كتابة Modified على الديسك ← بتبقى Standby
///   3. تفريغ Standby ← بتبقى Free
/// الأرقام اللي بتطلع حقيقية: Free بيزيد فعلًا، و"المستخدم" بينزل بس لو التقليص متفعّل.
/// </summary>
public static class MemoryOptimizer
{
    /// <summary>بروسيسات النظام اللي ممنوع نلمسها مهما كانت خاملة.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso",
        "dwm", "fontdrvhost", "audiodg", "svchost", "MsMpEng", "NisSrv", "SecurityHealthService", "sihost", "ctfmon", "explorer",
        "TextInputHost", "StartMenuExperienceHost", "ShellExperienceHost", "SearchHost", "RuntimeBroker", "conhost", "PCCare"
    };

    public static async Task<OptimizeResult> OptimizeAsync(OptimizeOptions options, IReadOnlyList<ProcessInfo>? processes, CancellationToken ct)
    {
        var steps = new List<string>();
        var errors = new List<string>();
        var before = MemorySnapshot.Take();
        int trimmed = 0;
        long trimmedBytes = 0;

        await Task.Run(() =>
        {
            if (options.TrimIdleProcesses && processes is not null)
            {
                int? fg = ForegroundApi.ForegroundProcessId();
                foreach (var p in processes)
                {
                    ct.ThrowIfCancellationRequested();
                    if (p.Pid <= 4 || p.Pid == Environment.ProcessId || p.Pid == fg) continue;
                    if (Protected.Contains(p.Name) || p.CpuPercent >= options.IdleCpuPercent) continue;
                    if (p.WorkingSet < 20L * 1024 * 1024) continue; // أقل من 20 ميجا: مالوش لازمة
                    var r = ProcessApi.TrimWorkingSet(p.Pid);
                    if (r.Success)
                    {
                        trimmed++;
                        trimmedBytes += p.WorkingSet;
                    }
                }
                steps.Add($"اتقلّص {trimmed} برنامج خامل في الخلفية (كانوا ماسكين {Format.Bytes(trimmedBytes)})");
            }

            if (options.FlushModified)
            {
                var r = MemoryApi.FlushModifiedList();
                if (r.Success) steps.Add("اتكتبت الصفحات المعدّلة (Modified) على الديسك");
                else errors.Add("Modified: " + r.Message);
            }

            if (options.PurgeStandby)
            {
                var r = MemoryApi.PurgeStandbyList(lowPriorityOnly: false);
                if (r.Success) steps.Add("اتفرّغ الكاش (Standby)");
                else errors.Add("Standby: " + r.Message);
            }
        }, ct).ConfigureAwait(false);

        await Task.Delay(700, ct).ConfigureAwait(false); // مدير الذاكرة يحدّث القوايم قبل القياس
        var after = MemorySnapshot.Take();
        var result = new OptimizeResult(before, after, trimmed, trimmedBytes, steps, errors);

        AppLogger.Operation("Memory", "تنظيف الذاكرة الآن", errors.Count == 0, Math.Max(0, result.FreeGained),
            $"Free {Format.Bytes(before.Free)} ← {Format.Bytes(after.Free)} | InUse {Format.Bytes(before.InUse)} ← {Format.Bytes(after.InUse)} | trimmed={trimmed}" +
            (errors.Count > 0 ? " | " + string.Join("; ", errors) : ""));
        return result;
    }
}
