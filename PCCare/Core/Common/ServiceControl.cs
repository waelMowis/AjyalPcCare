using Microsoft.Win32;

namespace AjyalPcCare.Core.Common;

public enum ServiceState
{
    Unknown,
    Stopped,
    Running,
    Other
}

/// <summary>
/// التحكم في الخدمات بدون حزمة ServiceController:
/// - الحالة/الإيقاف/التشغيل عن طريق PowerShell (أسماء الحالات إنجليزي ثابتة حتى على ويندوز عربي).
/// - نوع التشغيل من الريجستري + sc.exe config (الـ exit code ثابت بغض النظر عن لغة النظام).
/// </summary>
public static class ServiceControl
{
    public static async Task<ServiceState> QueryStateAsync(string name, CancellationToken ct)
    {
        var o = await PowerShell.RunAsync($"(Get-Service -Name {PowerShell.Quote(name)} -ErrorAction Stop).Status.ToString()", ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return o.StdOut.Trim() switch
        {
            "Running" => ServiceState.Running,
            "Stopped" => ServiceState.Stopped,
            "" => ServiceState.Unknown,
            _ => ServiceState.Other
        };
    }

    public static async Task<bool> StopAsync(string name, CancellationToken ct)
    {
        var o = await PowerShell.RunAsync($"Stop-Service -Name {PowerShell.Quote(name)} -Force -ErrorAction Stop", ct, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        AppLogger.Operation("Services", $"إيقاف الخدمة {name}", o.Success, 0, o.Success ? null : o.StdErr.Trim());
        return o.Success;
    }

    public static async Task<bool> StartAsync(string name, CancellationToken ct)
    {
        var o = await PowerShell.RunAsync($"Start-Service -Name {PowerShell.Quote(name)} -ErrorAction Stop", ct, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        AppLogger.Operation("Services", $"تشغيل الخدمة {name}", o.Success, 0, o.Success ? null : o.StdErr.Trim());
        return o.Success;
    }

    /// <summary>نوع التشغيل الحالي بصيغة sc.exe: auto / delayed-auto / demand / disabled / boot / system.</summary>
    public static string? GetStartType(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
            if (key?.GetValue("Start") is not int start) return null;
            bool delayed = key.GetValue("DelayedAutostart") is int d && d == 1;
            return start switch
            {
                0 => "boot",
                1 => "system",
                2 => delayed ? "delayed-auto" : "auto",
                3 => "demand",
                4 => "disabled",
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> SetStartTypeAsync(string name, string scStartType, CancellationToken ct)
    {
        string sc = Path.Combine(Environment.SystemDirectory, "sc.exe");
        var o = await ProcessRunner.RunAsync(sc, $"config \"{name}\" start= {scStartType}", ct, timeout: TimeSpan.FromSeconds(30), outputEncoding: ConsoleEncoding.Oem).ConfigureAwait(false);
        AppLogger.Operation("Services", $"تغيير نوع تشغيل {name} إلى {scStartType}", o.Success, 0, o.Success ? null : o.StdOut.Trim());
        return o.Success;
    }
}
