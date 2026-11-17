using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Health;

public sealed class PhysicalDiskInfo
{
    public string? DeviceId { get; set; }
    public string? FriendlyName { get; set; }
    public string? MediaType { get; set; }
    public string? BusType { get; set; }
    public string? HealthStatus { get; set; }
    public string? OperationalStatus { get; set; }
    public long Size { get; set; }
    public int? Temperature { get; set; }
    public int? Wear { get; set; }
    public long? ReadErrorsTotal { get; set; }
    public long? WriteErrorsTotal { get; set; }
    public long? PowerOnHours { get; set; }
    public string? DriveLetters { get; set; }
    public bool IsSystemDisk { get; set; }

    public bool IsSsd => string.Equals(MediaType, "SSD", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(BusType, "NVMe", StringComparison.OrdinalIgnoreCase);
    public bool IsHdd => string.Equals(MediaType, "HDD", StringComparison.OrdinalIgnoreCase);
    public bool IsHealthy => string.IsNullOrEmpty(HealthStatus) || string.Equals(HealthStatus, "Healthy", StringComparison.OrdinalIgnoreCase);

    public string MediaDisplay => IsSsd ? (string.Equals(BusType, "NVMe", StringComparison.OrdinalIgnoreCase) ? "SSD (NVMe)" : "SSD") : IsHdd ? "HDD" : "غير محدد";

    public string HealthDisplay => HealthStatus switch
    {
        null or "" => "غير معروف",
        "Healthy" => "سليم",
        "Warning" => "تحذير",
        "Unhealthy" => "غير سليم — خذ نسخة احتياطية فورًا",
        _ => HealthStatus
    };
}

public sealed record VolumeInfo(string Letter, string Label, string FileSystem, long Total, long Free)
{
    public double FreePercent => Total == 0 ? 0 : Free * 100d / Total;
}

/// <summary>
/// صحة الديسكات: Get-PhysicalDisk + Get-StorageReliabilityCounter (Storage module — موجود في ويندوز 8+).
/// ملاحظة: كتير من الديسكات (خصوصًا عبر USB أو RAID) ما بتدّيش قيم SMART — بنعرض "غير معروف" بأمانة.
/// </summary>
public static class DiskHealthService
{
    private const string Script =
        "$sys = $null; try { $sys = (Get-Partition -DriveLetter ($env:SystemDrive.TrimEnd(':')) -ErrorAction Stop).DiskNumber } catch {}; " +
        "$letters = @{}; Get-Partition -ErrorAction SilentlyContinue | Where-Object { $_.DriveLetter } | Group-Object DiskNumber | ForEach-Object { $letters[[string]$_.Name] = (@($_.Group | ForEach-Object { [string]$_.DriveLetter }) -join ',') }; " +
        "Get-PhysicalDisk | ForEach-Object { $d = $_; $r = $null; try { $r = $d | Get-StorageReliabilityCounter -ErrorAction Stop } catch {}; " +
        "[pscustomobject]@{ DeviceId = [string]$d.DeviceId; FriendlyName = $d.FriendlyName; MediaType = [string]$d.MediaType; BusType = [string]$d.BusType; " +
        "HealthStatus = [string]$d.HealthStatus; OperationalStatus = (@($d.OperationalStatus) -join ','); Size = [int64]$d.Size; " +
        "Temperature = $r.Temperature; Wear = $r.Wear; ReadErrorsTotal = $r.ReadErrorsTotal; WriteErrorsTotal = $r.WriteErrorsTotal; PowerOnHours = $r.PowerOnHours; " +
        "DriveLetters = $letters[[string]$d.DeviceId]; IsSystemDisk = ([string]$d.DeviceId -eq [string]$sys) } }";

    public static async Task<List<PhysicalDiskInfo>> GetDisksAsync(CancellationToken ct)
    {
        try
        {
            return await PowerShell.RunJsonListAsync<PhysicalDiskInfo>(Script, ct, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Health", "تعذر قراءة الديسكات: " + ex.Message);
            return [];
        }
    }

    public static List<VolumeInfo> GetVolumes()
    {
        var list = new List<VolumeInfo>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                list.Add(new VolumeInfo(d.Name.TrimEnd('\\'), d.VolumeLabel, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace));
            }
            catch
            {
                // درايف مش جاهز
            }
        }
        return list;
    }
}
