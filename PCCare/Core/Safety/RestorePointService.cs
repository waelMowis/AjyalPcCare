using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Safety;

public sealed record RestorePointResult(bool Created, bool Skipped, string Message);

/// <summary>
/// نقطة استعادة قبل أي تعديل على الـ Startup أو الخدمات.
/// ملاحظة: ويندوز بيمنع إنشاء أكتر من نقطة كل 24 ساعة افتراضيًا (SystemRestorePointCreationFrequency) —
/// مش بنغيّر الإعداد ده، بنبلّغ المستخدم بس إن النقطة اتخطت وفيه نقطة حديثة.
/// </summary>
public static class RestorePointService
{
    private static bool _createdThisSession;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static bool CreatedThisSession => _createdThisSession;

    /// <summary>ينشئ نقطة مرة واحدة في الجلسة (أول تعديل).</summary>
    public static async Task<RestorePointResult> EnsureForSessionAsync(string description, CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_createdThisSession) return new RestorePointResult(true, false, "توجد نقطة استعادة لهذه الجلسة بالفعل");
            var r = await CreateAsync(description, ct).ConfigureAwait(false);
            if (r.Created || r.Skipped) _createdThisSession = true;
            return r;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<RestorePointResult> CreateAsync(string description, CancellationToken ct)
    {
        if (!AppEnvironment.IsAdministrator)
            return new RestorePointResult(false, false, "إنشاء نقطة استعادة يحتاج صلاحيات مسؤول");

        string script =
            $"$w = $null; Checkpoint-Computer -Description {PowerShell.Quote(description)} -RestorePointType 'MODIFY_SETTINGS' " +
            "-WarningVariable w -WarningAction SilentlyContinue -ErrorAction Stop; " +
            "if ($w) { 'SKIPPED' } else { 'OK' }";
        var o = await PowerShell.RunAsync(script, ct, TimeSpan.FromMinutes(5)).ConfigureAwait(false);
        string outText = o.StdOut.Trim();

        RestorePointResult result;
        if (o.Success && outText.EndsWith("OK", StringComparison.Ordinal))
            result = new RestorePointResult(true, false, "تم إنشاء نقطة استعادة: " + description);
        else if (o.Success && outText.EndsWith("SKIPPED", StringComparison.Ordinal))
            result = new RestorePointResult(false, true, "ويندوز تخطى إنشاء النقطة لوجود نقطة خلال آخر 24 ساعة — تقدر تستخدمها للرجوع");
        else
            result = new RestorePointResult(false, false,
                "تعذر إنشاء نقطة استعادة (غالبًا System Protection مقفولة على درايف النظام): " + o.StdErr.Trim());

        AppLogger.Operation("Safety", "نقطة استعادة", result.Created || result.Skipped, 0, result.Message);
        return result;
    }
}
