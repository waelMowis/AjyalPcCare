using AjyalPcCare.Core.Common;
using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Memory;

/// <summary>
/// تفريغ تلقائي مشروط (فكرة ISLC):
/// يتنفذ فقط لو Free قليل فعلًا AND Standby كبير، ومع فترة تهدئة بين كل مرة.
/// السبب: التفريغ الدوري الأعمى بيمسح كاش مفيد ويبطّأ الجهاز.
/// </summary>
public sealed class AutoPurgeService : IDisposable
{
    private readonly object _lock = new();
    private System.Threading.Timer? _timer;
    private DateTime _lastPurge = DateTime.MinValue;
    private MemorySettings _settings;

    public event Action<PurgeOutcome>? Purged;

    public bool IsRunning => _timer is not null;
    public DateTime? LastPurge => _lastPurge == DateTime.MinValue ? null : _lastPurge;
    public int PurgeCount { get; private set; }

    public AutoPurgeService(MemorySettings settings) => _settings = settings;

    public void Start(MemorySettings settings)
    {
        lock (_lock)
        {
            _settings = settings;
            _timer?.Dispose();
            var period = TimeSpan.FromSeconds(Math.Max(2, settings.CheckIntervalSeconds));
            _timer = new System.Threading.Timer(_ => Tick(), null, period, period);
            AppLogger.Info("Memory", $"تشغيل التفريغ التلقائي: Free < {settings.FreeMemoryThresholdMb} م.ب و Standby > {settings.StandbyMinimumMb} م.ب، تهدئة {settings.CooldownSeconds} ث");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_timer is null) return;
            _timer.Dispose();
            _timer = null;
            AppLogger.Info("Memory", "إيقاف التفريغ التلقائي");
        }
    }

    private int _busy;

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var s = _settings;
            if ((DateTime.UtcNow - _lastPurge).TotalSeconds < s.CooldownSeconds) return;

            var lists = MemoryApi.GetMemoryLists();
            if (lists is null) return;

            ulong freeMb = (lists.Free + lists.Zeroed) / 1024 / 1024;
            ulong standbyMb = lists.Standby / 1024 / 1024;
            if (freeMb >= (ulong)s.FreeMemoryThresholdMb || standbyMb <= (ulong)s.StandbyMinimumMb) return;

            var outcome = MemoryManager.PurgeStandby(s.LowPriorityOnly, $"تلقائي (Free={freeMb} م.ب)");
            _lastPurge = DateTime.UtcNow;
            if (outcome.Success) PurgeCount++;
            Purged?.Invoke(outcome);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Memory", "خطأ في التفريغ التلقائي", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Dispose() => Stop();
}
