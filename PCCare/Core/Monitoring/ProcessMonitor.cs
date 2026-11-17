using System.Collections.Concurrent;
using System.Diagnostics;
using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Monitoring;

public sealed record ProcessInfo(
    int Pid,
    string Name,
    string? Path,
    string? Publisher,
    long WorkingSet,
    long PrivateBytes,
    double CpuPercent,
    DateTime? StartTime);

/// <summary>
/// قائمة البروسيسات مع نسبة المعالج (فرق TotalProcessorTime بين عينتين).
/// الناشر بيتقرا من FileVersionInfo ويتخزن مؤقتًا لكل مسار — عشان ما نقراش الملفات كل ثانية.
/// </summary>
public sealed class ProcessMonitor
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _lastCpu = new();
    private static readonly ConcurrentDictionary<string, string?> PublisherCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _cores = Environment.ProcessorCount;

    public List<ProcessInfo> Sample()
    {
        var now = DateTime.UtcNow;
        var list = new List<ProcessInfo>();
        var seen = new HashSet<int>();

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id is 0 or 4) continue; // Idle و System
                    seen.Add(p.Id);

                    double cpu = 0;
                    TimeSpan? total = null;
                    try { total = p.TotalProcessorTime; } catch { /* محمي */ }

                    if (total.HasValue)
                    {
                        if (_lastCpu.TryGetValue(p.Id, out var last))
                        {
                            double elapsedMs = (now - last.At).TotalMilliseconds;
                            if (elapsedMs > 0)
                                cpu = Math.Clamp((total.Value - last.Cpu).TotalMilliseconds / (elapsedMs * _cores) * 100, 0, 100);
                        }
                        _lastCpu[p.Id] = (total.Value, now);
                    }

                    DateTime? start = null;
                    try { start = p.StartTime; } catch { /* محمي */ }

                    string? path = ProcessApi.GetImagePath(p.Id);
                    list.Add(new ProcessInfo(p.Id, p.ProcessName, path, GetPublisher(path), p.WorkingSet64, p.PrivateMemorySize64, cpu, start));
                }
                catch
                {
                    // البروسيس قفل أثناء القراءة
                }
            }
        }

        foreach (var dead in _lastCpu.Keys.Where(k => !seen.Contains(k)).ToList())
            _lastCpu.Remove(dead);

        return list;
    }

    public static string? GetPublisher(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        return PublisherCache.GetOrAdd(path, static p =>
        {
            try
            {
                if (!File.Exists(p)) return null;
                var vi = FileVersionInfo.GetVersionInfo(p);
                return string.IsNullOrWhiteSpace(vi.CompanyName) ? null : vi.CompanyName.Trim();
            }
            catch
            {
                return null;
            }
        });
    }

    public static IReadOnlyList<ProcessInfo> TopByMemory(IEnumerable<ProcessInfo> all, int count = 10) =>
        all.OrderByDescending(p => p.PrivateBytes).Take(count).ToList();

    public static IReadOnlyList<ProcessInfo> TopByCpu(IEnumerable<ProcessInfo> all, int count = 10) =>
        all.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.PrivateBytes).Take(count).ToList();
}
