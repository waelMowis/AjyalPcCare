using AjyalPcCare.Core.Common;
using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Memory;

public sealed record PurgeOutcome(bool Success, string Message, MemoryLists? Before, MemoryLists? After)
{
    /// <summary>الزيادة الفعلية في الذاكرة الفاضية (Free + Zeroed) — مقاسة قبل وبعد.</summary>
    public long FreeGained =>
        Before is null || After is null ? 0 : (long)(After.Free + After.Zeroed) - (long)(Before.Free + Before.Zeroed);
}

public static class MemoryManager
{
    public const string ExplanationText =
        "ويندوز بيستخدم الرام الفاضية ككاش (Standby) للملفات والبرامج اللي اتفتحت قبل كده، عشان لما تفتحها تاني تفتح فورًا من الرام بدل الديسك. " +
        "الكاش ده بيتنازل عنه ويندوز تلقائيًا وفورًا لأي برنامج محتاج ذاكرة، يعني هو مش \"رام مستهلكة\" ومش سبب للبطء.\n\n" +
        "تفريغ Standby بيخلّي رقم الرام الفاضية يكبر، لكن البرامج اللي هتفتحها بعدها هتتقرا من الديسك تاني، فممكن تحس ببطء مؤقت. " +
        "الحالة الوحيدة اللي التفريغ بيفيد فيها: برامج معينة (ألعاب أو برامج قديمة) بتتعامل غلط لما الـ Free قليل جدًا حتى لو Standby كبير، " +
        "وده سبب وجود خيار التفريغ التلقائي المشروط (زي فكرة ISLC).\n\n" +
        "لو الجهاز فعلًا بطيء بسبب الرام: شوف عمود \"Commit\" — لو قريب من الحد الأقصى، الحل إنك تقفل البرنامج اللي واكل الذاكرة أو تزوّد الرام، مش التنظيف.";

    public static MemoryLists? GetLists() => MemoryApi.GetMemoryLists();

    public static PurgeOutcome PurgeStandby(bool lowPriorityOnly, string trigger)
    {
        var before = MemoryApi.GetMemoryLists();
        var res = MemoryApi.PurgeStandbyList(lowPriorityOnly);
        // نستنى لحظة عشان مدير الذاكرة يحدّث القوائم قبل القياس
        Thread.Sleep(300);
        var after = MemoryApi.GetMemoryLists();
        var outcome = new PurgeOutcome(res.Success, res.Message, before, after);

        AppLogger.Operation("Memory",
            $"تفريغ Standby ({(lowPriorityOnly ? "أولوية منخفضة" : "كامل")}) — المصدر: {trigger}",
            res.Success, Math.Max(0, outcome.FreeGained),
            before is null || after is null
                ? res.Message
                : $"Standby قبل={Format.Bytes(before.Standby)} بعد={Format.Bytes(after.Standby)}");
        return outcome;
    }

    public static NativeResult TrimProcess(int pid, string name)
    {
        var res = ProcessApi.TrimWorkingSet(pid);
        AppLogger.Operation("Memory", $"تقليص Working Set للبروسيس {name} (PID {pid})", res.Success, 0, res.Message);
        return res;
    }
}
