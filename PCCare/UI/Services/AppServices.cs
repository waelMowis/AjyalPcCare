using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Memory;
using AjyalPcCare.Core.Monitoring;

namespace AjyalPcCare.UI.Services;

public sealed record MonitorUpdate(SystemSnapshot Snapshot, IReadOnlyList<ProcessInfo>? Processes, IReadOnlyList<LeakSuspect> LeakSuspects);

/// <summary>
/// خدمات مشتركة لكل الواجهة: عيّنة واحدة للنظام كل ثانية (مش كل صفحة تعمل عيّنة لوحدها)،
/// البروسيسات كل ثانيتين، وكاشف التسريب شغال في الخلفية حتى لو المستخدم في صفحة تانية.
/// </summary>
public sealed class AppServices : IDisposable
{
    private static AppServices? _instance;
    public static AppServices Instance => _instance ??= new AppServices();

    public SystemMonitor Monitor { get; } = new(60);
    public ProcessMonitor Processes { get; } = new();
    public LeakDetector Leaks { get; }
    public AutoPurgeService AutoPurge { get; }

    public event Action<MonitorUpdate>? Updated;

    public MonitorUpdate? Last { get; private set; }

    private readonly System.Threading.Timer _timer;
    private int _busy;
    private int _tick;
    private IReadOnlyList<ProcessInfo> _lastProcesses = [];
    private IReadOnlyList<LeakSuspect> _lastSuspects = [];

    private AppServices()
    {
        var s = SettingsStore.Current;
        Leaks = new LeakDetector(s.LeakDetection);
        AutoPurge = new AutoPurgeService(s.Memory);
        if (s.Memory.AutoPurgeEnabled && AppEnvironment.IsAdministrator) AutoPurge.Start(s.Memory);
        _timer = new System.Threading.Timer(_ => Sample(), null, 500, s.Ui.RefreshIntervalMs);
    }

    private void Sample()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var snap = Monitor.Sample();
            IReadOnlyList<ProcessInfo>? procs = null;
            if (_tick++ % 2 == 0)
            {
                procs = Processes.Sample();
                _lastProcesses = procs;
                if (Leaks.Feed(procs)) _lastSuspects = Leaks.GetSuspects(procs);
            }
            var update = new MonitorUpdate(snap, procs ?? _lastProcesses, _lastSuspects);
            Last = update;
            Updated?.Invoke(update);
        }
        catch (Exception ex)
        {
            AppLogger.Debug("Monitor", ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        AutoPurge.Dispose();
        Monitor.Dispose();
    }
}
