using System.Net.NetworkInformation;
using AjyalPcCare.Core.Common;
using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Monitoring;

public sealed record SystemSnapshot(
    DateTime Time,
    double CpuPercent,
    MemoryStatus? Memory,
    MemoryLists? Lists,
    double? DiskActivePercent,
    double? DiskReadBps,
    double? DiskWriteBps,
    double NetReceiveBps,
    double NetSendBps)
{
    public ulong UsedPhysical => Memory is null ? 0 : Memory.TotalPhysical - Memory.AvailablePhysical;
    public double MemoryPercent => Memory is null || Memory.TotalPhysical == 0 ? 0 : UsedPhysical * 100d / Memory.TotalPhysical;
    public double CommitPercent => Memory is null || Memory.CommitLimit == 0 ? 0 : Memory.CommitTotal * 100d / Memory.CommitLimit;
}

/// <summary>
/// قراءات النظام اللحظية. CPU من GetSystemTimes (دقيق ومن غير PerformanceCounter)،
/// الديسك من PDH، الشبكة من NetworkInterface، الذاكرة من GlobalMemoryStatusEx + قوائم الذاكرة.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    private const string DiskIdle = @"\PhysicalDisk(_Total)\% Idle Time";
    private const string DiskRead = @"\PhysicalDisk(_Total)\Disk Read Bytes/sec";
    private const string DiskWrite = @"\PhysicalDisk(_Total)\Disk Write Bytes/sec";

    private readonly PdhCounterSet? _pdh;
    private readonly Queue<SystemSnapshot> _history = new();
    private readonly object _lock = new();
    private (ulong Idle, ulong Kernel, ulong User)? _lastCpu;
    private long _lastNetRx, _lastNetTx;
    private DateTime _lastNetTime;
    private bool _listsSupported = true;

    public int HistoryLength { get; }

    public SystemMonitor(int historyLength = 60)
    {
        HistoryLength = historyLength;
        try
        {
            _pdh = new PdhCounterSet([DiskIdle, DiskRead, DiskWrite]);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Monitor", "عدادات الديسك غير متاحة: " + ex.Message);
        }
        _lastCpu = SystemTimesApi.GetSystemTimes();
        (_lastNetRx, _lastNetTx) = ReadNetworkTotals();
        _lastNetTime = DateTime.UtcNow;
    }

    public IReadOnlyList<SystemSnapshot> History
    {
        get
        {
            lock (_lock) return _history.ToArray();
        }
    }

    public SystemSnapshot Sample()
    {
        double cpu = ReadCpu();
        var mem = MemoryApi.GetStatus();

        MemoryLists? lists = null;
        if (_listsSupported)
        {
            lists = MemoryApi.GetMemoryLists();
            if (lists is null) _listsSupported = false; // من غير Admin — ما نحاولش كل ثانية
        }

        double? diskActive = null, diskRead = null, diskWrite = null;
        if (_pdh is not null)
        {
            _pdh.Collect();
            var idle = _pdh.Read(DiskIdle);
            if (idle.HasValue) diskActive = Math.Clamp(100 - idle.Value, 0, 100);
            diskRead = _pdh.Read(DiskRead);
            diskWrite = _pdh.Read(DiskWrite);
        }

        var now = DateTime.UtcNow;
        var (rx, tx) = ReadNetworkTotals();
        double secs = Math.Max(0.001, (now - _lastNetTime).TotalSeconds);
        double rxBps = Math.Max(0, (rx - _lastNetRx) / secs);
        double txBps = Math.Max(0, (tx - _lastNetTx) / secs);
        _lastNetRx = rx;
        _lastNetTx = tx;
        _lastNetTime = now;

        var snap = new SystemSnapshot(DateTime.Now, cpu, mem, lists, diskActive, diskRead, diskWrite, rxBps, txBps);
        lock (_lock)
        {
            _history.Enqueue(snap);
            while (_history.Count > HistoryLength) _history.Dequeue();
        }
        return snap;
    }

    private double ReadCpu()
    {
        var now = SystemTimesApi.GetSystemTimes();
        if (now is null || _lastCpu is null)
        {
            _lastCpu = now;
            return 0;
        }

        ulong idle = now.Value.Idle - _lastCpu.Value.Idle;
        ulong kernel = now.Value.Kernel - _lastCpu.Value.Kernel;
        ulong user = now.Value.User - _lastCpu.Value.User;
        _lastCpu = now;
        ulong total = kernel + user; // kernel بيشمل idle
        if (total == 0) return 0;
        return Math.Clamp((total - idle) * 100d / total, 0, 100);
    }

    private static (long Rx, long Tx) ReadNetworkTotals()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var st = ni.GetIPStatistics();
                rx += st.BytesReceived;
                tx += st.BytesSent;
            }
        }
        catch
        {
            // ignore
        }
        return (rx, tx);
    }

    public void Dispose() => _pdh?.Dispose();
}
