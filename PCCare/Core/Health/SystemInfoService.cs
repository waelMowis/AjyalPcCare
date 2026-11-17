using Microsoft.Win32;
using AjyalPcCare.Core.Common;
using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Health;

public sealed class PageFileInfo
{
    public string? Name { get; set; }
    public int AllocatedBaseSize { get; set; } // م.ب
    public int CurrentUsage { get; set; }      // م.ب
    public int PeakUsage { get; set; }         // م.ب
}

public sealed class SystemExtras
{
    public DateTime? LastUpdateInstalled { get; set; }
    public string? LastUpdateTitle { get; set; }
    public bool? AutomaticManagedPagefile { get; set; }
    public List<PageFileInfo> PageFiles { get; set; } = [];
}

public sealed record SystemInfo(
    string MachineName,
    string OsName,
    string OsVersion,
    string Build,
    string CpuName,
    int LogicalCores,
    ulong TotalRam,
    TimeSpan Uptime,
    string UserName);

public static class SystemInfoService
{
    public static SystemInfo Get()
    {
        string os = "Windows", ver = "", build = "", cpu = "";
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            os = k?.GetValue("ProductName")?.ToString() ?? os;
            ver = k?.GetValue("DisplayVersion")?.ToString() ?? k?.GetValue("ReleaseId")?.ToString() ?? "";
            build = $"{k?.GetValue("CurrentBuild")}.{k?.GetValue("UBR")}";
            // ويندوز 11 لسه بيكتب "Windows 10" في ProductName — رقم الـ Build هو الفيصل
            if (int.TryParse(k?.GetValue("CurrentBuild")?.ToString(), out int b) && b >= 22000)
                os = os.Replace("Windows 10", "Windows 11");
        }
        catch { /* ignore */ }

        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = k?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "";
        }
        catch { /* ignore */ }

        var mem = MemoryApi.GetStatus();
        return new SystemInfo(Environment.MachineName, os, ver, build, cpu, Environment.ProcessorCount,
            mem?.TotalPhysical ?? 0, SystemTimesApi.Uptime, Environment.UserName);
    }

    /// <summary>آخر تحديث (من سجل Windows Update نفسه) + ملف الصفحات — استدعاء PowerShell واحد.</summary>
    public static async Task<SystemExtras> GetExtrasAsync(CancellationToken ct)
    {
        const string script =
            "$last = $null; $title = $null; try { $s = (New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher(); $n = $s.GetTotalHistoryCount(); " +
            "if ($n -gt 0) { $h = $s.QueryHistory(0, [Math]::Min($n, 50)) | Where-Object { $_.ResultCode -eq 2 -and $_.Operation -eq 1 } | Sort-Object Date -Descending | Select-Object -First 1; " +
            "if ($h) { $last = $h.Date.ToString('o'); $title = $h.Title } } } catch {}; " +
            "if (-not $last) { try { $hf = Get-HotFix | Where-Object InstalledOn | Sort-Object InstalledOn -Descending | Select-Object -First 1; if ($hf) { $last = $hf.InstalledOn.ToString('o'); $title = $hf.HotFixID } } catch {} }; " +
            "$auto = $null; try { $auto = (Get-CimInstance Win32_ComputerSystem).AutomaticManagedPagefile } catch {}; " +
            "$pf = @(); try { $pf = @(Get-CimInstance Win32_PageFileUsage | ForEach-Object { [pscustomobject]@{ Name = $_.Name; AllocatedBaseSize = [int]$_.AllocatedBaseSize; CurrentUsage = [int]$_.CurrentUsage; PeakUsage = [int]$_.PeakUsage } }) } catch {}; " +
            "[pscustomobject]@{ LastUpdateInstalled = $last; LastUpdateTitle = $title; AutomaticManagedPagefile = $auto; PageFiles = $pf }";

        var list = await PowerShell.RunJsonListAsync<SystemExtras>(script, ct, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        return list.FirstOrDefault() ?? new SystemExtras();
    }
}
