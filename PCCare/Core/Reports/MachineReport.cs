using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Health;

namespace AjyalPcCare.Core.Reports;

public sealed class ModuleRunSummary
{
    public string Module { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Success { get; set; }
    public long BytesFreed { get; set; }
    public int ItemsProcessed { get; set; }
    public int ItemsSkipped { get; set; }
    public double DurationSeconds { get; set; }
    public List<string> Messages { get; set; } = [];
    public List<string> Errors { get; set; } = [];

    public static ModuleRunSummary From(IMaintenanceModule m, ExecutionResult r) => new()
    {
        Module = m.Id,
        DisplayName = m.DisplayName,
        Success = r.Success,
        BytesFreed = r.BytesFreed,
        ItemsProcessed = r.ItemsProcessed,
        ItemsSkipped = r.ItemsSkipped,
        DurationSeconds = Math.Round(r.Duration.TotalSeconds, 1),
        Messages = r.Messages,
        Errors = r.Errors
    };
}

/// <summary>تقرير جهاز واحد — نفس البيانات بتطلع JSON (للتجميع) و HTML (للقراءة).</summary>
public sealed class MachineReport
{
    public string MachineName { get; set; } = Environment.MachineName;
    public string UserName { get; set; } = Environment.UserName;
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public string ToolVersion { get; set; } = AppEnvironment.Version;
    public string Mode { get; set; } = "Interactive";
    public bool RanAsAdmin { get; set; } = AppEnvironment.IsAdministrator;

    public string? Os { get; set; }
    public string? Cpu { get; set; }
    public long RamTotal { get; set; }
    public double UptimeHours { get; set; }

    public int? HealthScore { get; set; }
    public string? HealthGrade { get; set; }
    public List<HealthFinding> Findings { get; set; } = [];
    public List<PhysicalDiskInfo> Disks { get; set; } = [];
    public List<VolumeInfo> Volumes { get; set; } = [];
    public DateTime? LastWindowsUpdate { get; set; }
    public int? StartupEnabledCount { get; set; }

    public long DiskSpaceFreed => Modules.Where(m => m.Module != "Memory").Sum(m => m.BytesFreed);
    public List<ModuleRunSummary> Modules { get; set; } = [];
    public List<string> Problems { get; set; } = [];

    public void ApplyHealth(HealthReport h)
    {
        Os = $"{h.System.OsName} {h.System.OsVersion} (Build {h.System.Build})";
        Cpu = $"{h.System.CpuName} — {h.System.LogicalCores} أنوية منطقية";
        RamTotal = (long)h.System.TotalRam;
        UptimeHours = Math.Round(h.System.Uptime.TotalHours, 1);
        HealthScore = h.Score;
        HealthGrade = h.Grade;
        Findings = h.Findings;
        Disks = h.Disks;
        Volumes = h.Volumes;
        LastWindowsUpdate = h.Extras?.LastUpdateInstalled;
        StartupEnabledCount = h.StartupEnabledCount;
        foreach (var f in h.Findings.Where(f => f.Severity != Severity.Info))
            Problems.Add($"{f.Title}: {f.Detail}");
    }
}
