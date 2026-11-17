using System.Text;

namespace AjyalPcCare.Core.Common;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public sealed record LogEntry(DateTime Time, LogLevel Level, string Module, string Message);

/// <summary>
/// Logger بسيط بدون اعتماديات: ملف يومي منظم + حدث للواجهة.
/// صيغة السطر: 2026-11-17 10:15:02.123 [INF] [Cleanup] الرسالة
/// السبب في عدم استخدام Serilog: الأداة بتتنشر على 50+ جهاز كـ Single-file،
/// وكل اعتمادية زيادة = حجم + نقطة فشل، والاحتياج هنا بسيط.
/// </summary>
public static class AppLogger
{
    private static readonly object _lock = new();
    private static string? _dir;
    private static int _retentionDays = 30;

    public static event Action<LogEntry>? EntryWritten;

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public static string CurrentLogFile => Path.Combine(_dir ?? AppEnvironment.LogsDirectory, $"pccare-{DateTime.Now:yyyyMMdd}.log");

    public static void Initialize(string? directory = null, int retentionDays = 30)
    {
        _dir = directory ?? AppEnvironment.LogsDirectory;
        _retentionDays = Math.Max(1, retentionDays);
        Directory.CreateDirectory(_dir);
        CleanupOldLogs();
        Info("App", $"بدء التشغيل — الإصدار {AppEnvironment.Version} — مسؤول: {(AppEnvironment.IsAdministrator ? "نعم" : "لا")} — الجهاز: {Environment.MachineName}");
    }

    public static void Debug(string module, string message) => Write(LogLevel.Debug, module, message);
    public static void Info(string module, string message) => Write(LogLevel.Info, module, message);
    public static void Warn(string module, string message) => Write(LogLevel.Warn, module, message);

    public static void Error(string module, string message, Exception? ex = null) =>
        Write(LogLevel.Error, module, ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    /// <summary>سطر عملية موحد: العملية + النتيجة + المساحة المحررة.</summary>
    public static void Operation(string module, string action, bool success, long bytesFreed = 0, string? details = null)
    {
        var sb = new StringBuilder();
        sb.Append(action).Append(" | النتيجة=").Append(success ? "نجاح" : "فشل");
        if (bytesFreed > 0) sb.Append(" | المحرر=").Append(Format.Bytes(bytesFreed)).Append(" (").Append(bytesFreed).Append(" B)");
        if (!string.IsNullOrEmpty(details)) sb.Append(" | ").Append(details);
        Write(success ? LogLevel.Info : LogLevel.Warn, module, sb.ToString());
    }

    private static void Write(LogLevel level, string module, string message)
    {
        if (level < MinimumLevel) return;
        var entry = new LogEntry(DateTime.Now, level, module, message);
        string tag = level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warn => "WRN",
            _ => "ERR"
        };
        string line = $"{entry.Time:yyyy-MM-dd HH:mm:ss.fff} [{tag}] [{module}] {message.Replace(Environment.NewLine, " ⏎ ")}";

        lock (_lock)
        {
            try
            {
                File.AppendAllText(CurrentLogFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // اللوج عمره ما يوقع الأداة
            }
        }

        try
        {
            EntryWritten?.Invoke(entry);
        }
        catch
        {
            // ignore subscriber errors
        }
    }

    private static void CleanupOldLogs()
    {
        try
        {
            if (_dir is null) return;
            var cutoff = DateTime.Now.AddDays(-_retentionDays);
            foreach (var f in Directory.EnumerateFiles(_dir, "pccare-*.log"))
            {
                if (File.GetLastWriteTime(f) < cutoff) File.Delete(f);
            }
        }
        catch
        {
            // ignore
        }
    }
}
