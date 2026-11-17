using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Scheduling;

public sealed record ScheduleInfo(bool Exists, string? State, string? NextRun, string? LastRun, string? LastResult, string? Arguments);

/// <summary>
/// تسجيل الصيانة الدورية كـ Scheduled Task بحساب SYSTEM.
/// بنستخدم Register-ScheduledTask بدل schtasks لأن schtasks /TR محدود بـ 261 حرف (المسار + الخيارات + مسار الشبكة بيعدّوه بسهولة).
/// ملاحظة مهمة: حساب SYSTEM بيكتب على الشبكة بهوية الجهاز (DOMAIN\PCNAME$) —
/// لازم مجلد التقارير المشترك يدّي صلاحية كتابة لـ "Domain Computers" (أو الأجهزة المعنية).
/// </summary>
public static class ScheduledTaskService
{
    public const string TaskPath = @"\AJYAL PC Care\";
    public const string TaskName = "Weekly Maintenance";

    private static readonly string[] ValidDays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    public static async Task<(bool Success, string Message)> CreateOrUpdateAsync(string exePath, string arguments, string day, string time, CancellationToken ct)
    {
        if (!AppEnvironment.IsAdministrator) return (false, "إنشاء مهمة بحساب SYSTEM يحتاج صلاحيات مسؤول");
        if (!ValidDays.Contains(day, StringComparer.OrdinalIgnoreCase)) return (false, "يوم غير صالح: " + day);
        if (!TimeSpan.TryParse(time, out var at)) return (false, "وقت غير صالح: " + time);

        string script =
            $"$a = New-ScheduledTaskAction -Execute {PowerShell.Quote(exePath)} -Argument {PowerShell.Quote(arguments)} -WorkingDirectory {PowerShell.Quote(Path.GetDirectoryName(exePath) ?? "C:\\")}; " +
            $"$t = New-ScheduledTaskTrigger -Weekly -DaysOfWeek {day} -At {PowerShell.Quote(at.ToString(@"hh\:mm"))}; " +
            "$p = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest; " +
            // StartWhenAvailable: لو الجهاز كان مقفول وقت الميعاد، تتنفذ أول ما يشتغل
            "$s = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 3) -MultipleInstances IgnoreNew; " +
            $"Register-ScheduledTask -TaskPath {PowerShell.Quote(TaskPath)} -TaskName {PowerShell.Quote(TaskName)} -Action $a -Trigger $t -Principal $p -Settings $s " +
            "-Description 'صيانة دورية — AJYAL PC Care' -Force -ErrorAction Stop | Out-Null; 'OK'";

        var o = await PowerShell.RunAsync(script, ct, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        bool ok = o.Success && o.StdOut.Contains("OK");
        AppLogger.Operation("Schedule", $"تسجيل مهمة مجدولة {day} {time}: {arguments}", ok, 0, ok ? null : o.StdErr.Trim());
        return ok ? (true, $"تم تسجيل المهمة: كل {day} الساعة {time}") : (false, o.StdErr.Trim());
    }

    public static async Task<(bool Success, string Message)> RemoveAsync(CancellationToken ct)
    {
        var o = await PowerShell.RunAsync(
            $"Unregister-ScheduledTask -TaskPath {PowerShell.Quote(TaskPath)} -TaskName {PowerShell.Quote(TaskName)} -Confirm:$false -ErrorAction Stop; 'OK'",
            ct, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        bool ok = o.Success && o.StdOut.Contains("OK");
        AppLogger.Operation("Schedule", "حذف المهمة المجدولة", ok, 0, ok ? null : o.StdErr.Trim());
        return ok ? (true, "تم حذف المهمة") : (false, o.StdErr.Trim());
    }

    private sealed class Dto
    {
        public string? State { get; set; }
        public string? NextRun { get; set; }
        public string? LastRun { get; set; }
        public string? LastResult { get; set; }
        public string? Arguments { get; set; }
    }

    public static async Task<ScheduleInfo> QueryAsync(CancellationToken ct)
    {
        string script =
            $"$t = Get-ScheduledTask -TaskPath {PowerShell.Quote(TaskPath)} -TaskName {PowerShell.Quote(TaskName)} -ErrorAction SilentlyContinue; " +
            "if ($t) { $i = $t | Get-ScheduledTaskInfo; [pscustomobject]@{ State = [string]$t.State; " +
            "NextRun = $(if ($i.NextRunTime) { $i.NextRunTime.ToString('yyyy-MM-dd HH:mm') } else { '' }); " +
            "LastRun = $(if ($i.LastRunTime -and $i.LastRunTime.Year -gt 2000) { $i.LastRunTime.ToString('yyyy-MM-dd HH:mm') } else { '' }); " +
            "LastResult = [string]$i.LastTaskResult; Arguments = [string]($t.Actions | Select-Object -First 1).Arguments } }";
        var list = await PowerShell.RunJsonListAsync<Dto>(script, ct, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        var d = list.FirstOrDefault();
        return d is null ? new ScheduleInfo(false, null, null, null, null, null) : new ScheduleInfo(true, d.State, d.NextRun, d.LastRun, d.LastResult, d.Arguments);
    }
}
