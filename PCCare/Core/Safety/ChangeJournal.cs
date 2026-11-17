using System.Text.Json;
using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Safety;

public enum UndoKind
{
    None,
    StartupApproved,
    ScheduledTask,
    ServiceStartType
}

/// <summary>سجل تغيير واحد مع بيانات التراجع.</summary>
public sealed class ChangeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Time { get; set; } = DateTime.Now;
    public string Module { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Description { get; set; } = "";
    public UndoKind UndoKind { get; set; }
    public Dictionary<string, string> UndoData { get; set; } = new();
    public bool Reversible => UndoKind != UndoKind.None;
    public bool Undone { get; set; }
    public DateTime? UndoneAt { get; set; }
    public string MachineName { get; set; } = Environment.MachineName;
    public string UserName { get; set; } = Environment.UserName;
}

/// <summary>
/// سجل دائم لكل التغييرات (changes.json في مجلد البيانات).
/// التنفيذ الفعلي للتراجع عند الوحدة صاحبة التغيير (StartupManager) — السجل بيخزن البيانات بس.
/// </summary>
public static class ChangeJournal
{
    private static readonly object Lock = new();
    private static List<ChangeRecord>? _cache;

    public static string FilePath => Path.Combine(AppEnvironment.DataDirectory, "changes.json");

    public static event Action? Changed;

    public static IReadOnlyList<ChangeRecord> All
    {
        get
        {
            lock (Lock) return Load().OrderByDescending(r => r.Time).ToList();
        }
    }

    public static ChangeRecord Add(ChangeRecord record)
    {
        lock (Lock)
        {
            Load().Add(record);
            Save();
        }
        AppLogger.Info("Journal", $"تسجيل تغيير: {record.Action} — {record.Target}");
        Changed?.Invoke();
        return record;
    }

    public static void MarkUndone(Guid id)
    {
        lock (Lock)
        {
            var r = Load().FirstOrDefault(x => x.Id == id);
            if (r is null) return;
            r.Undone = true;
            r.UndoneAt = DateTime.Now;
            Save();
        }
        Changed?.Invoke();
    }

    private static List<ChangeRecord> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            if (File.Exists(FilePath))
                _cache = JsonSerializer.Deserialize<List<ChangeRecord>>(File.ReadAllText(FilePath), SettingsStore.JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Journal", "تعذر قراءة سجل التغييرات: " + ex.Message);
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch { /* ignore */ }
        }
        return _cache ??= [];
    }

    private static void Save()
    {
        try
        {
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_cache, SettingsStore.JsonOptions));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Journal", "تعذر حفظ سجل التغييرات", ex);
        }
    }
}
