using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Monitoring;

public sealed record LeakSuspect(int Pid, string Name, string? Path, long StartBytes, long CurrentBytes, int Samples, TimeSpan Window)
{
    public long Growth => CurrentBytes - StartBytes;
}

/// <summary>
/// كاشف تسريب ذاكرة محتمل: Private Bytes بتزيد بشكل شبه مستمر خلال نافذة زمنية.
/// ده "اشتباه" مش حكم — برامج زي المتصفحات وقواعد البيانات بتكبر طبيعي وهي شغالة،
/// عشان كده الشرط: زيادة في أغلب العينات + حد أدنى للنمو + نافذة طويلة كفاية.
/// </summary>
public sealed class LeakDetector
{
    private readonly Dictionary<string, Queue<(DateTime At, long Bytes)>> _samples = new();
    private DateTime _lastSample = DateTime.MinValue;

    public LeakDetectionSettings Settings { get; set; }

    public LeakDetector(LeakDetectionSettings settings) => Settings = settings;

    /// <summary>بيرجع true لو اتاخدت عينة جديدة (حسب الفاصل الزمني في الإعدادات).</summary>
    public bool Feed(IEnumerable<ProcessInfo> processes)
    {
        if (!Settings.Enabled) return false;
        var now = DateTime.UtcNow;
        if ((now - _lastSample).TotalSeconds < Settings.SampleIntervalSeconds) return false;
        _lastSample = now;

        var alive = new HashSet<string>();
        foreach (var p in processes)
        {
            string key = Key(p);
            alive.Add(key);
            if (!_samples.TryGetValue(key, out var q))
                _samples[key] = q = new Queue<(DateTime, long)>();
            q.Enqueue((now, p.PrivateBytes));
            while (q.Count > Settings.WindowSamples) q.Dequeue();
        }

        foreach (var k in _samples.Keys.Where(k => !alive.Contains(k)).ToList())
            _samples.Remove(k);

        return true;
    }

    public List<LeakSuspect> GetSuspects(IEnumerable<ProcessInfo> current)
    {
        var result = new List<LeakSuspect>();
        if (!Settings.Enabled) return result;
        long minGrowth = Settings.MinGrowthMb * 1024L * 1024L;

        foreach (var p in current)
        {
            if (!_samples.TryGetValue(Key(p), out var q) || q.Count < Settings.WindowSamples) continue;
            var arr = q.ToArray();
            int increases = 0;
            for (int i = 1; i < arr.Length; i++)
                if (arr[i].Bytes > arr[i - 1].Bytes) increases++;

            double ratio = increases / (double)(arr.Length - 1);
            long growth = arr[^1].Bytes - arr[0].Bytes;
            if (ratio >= Settings.IncreasingRatio && growth >= minGrowth)
                result.Add(new LeakSuspect(p.Pid, p.Name, p.Path, arr[0].Bytes, arr[^1].Bytes, arr.Length, arr[^1].At - arr[0].At));
        }

        return result.OrderByDescending(s => s.Growth).ToList();
    }

    // PID لوحده مش كفاية — ويندوز بيعيد استخدام الأرقام
    private static string Key(ProcessInfo p) => $"{p.Pid}|{p.StartTime?.Ticks ?? 0}";
}
