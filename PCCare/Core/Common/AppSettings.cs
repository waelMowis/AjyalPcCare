using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AjyalPcCare.Core.Common;

public sealed class AppSettings
{
    public UiSettings Ui { get; set; } = new();
    public MemorySettings Memory { get; set; } = new();
    public LeakDetectionSettings LeakDetection { get; set; } = new();
    public CleanupSettings Cleanup { get; set; } = new();
    public HealthSettings Health { get; set; } = new();
    public LabSettings Lab { get; set; } = new();
    public UpdateSettings Update { get; set; } = new();
    public int LogRetentionDays { get; set; } = 30;
}

public sealed class UpdateSettings
{
    /// <summary>التحقق من التحديثات بعد فتح البرنامج بثواني.</summary>
    public bool CheckOnStartup { get; set; } = true;

    /// <summary>مستودع GitHub (owner/repo). فاضي = المستودع المكتوب جوه البرنامج وقت البناء.</summary>
    public string Repo { get; set; } = "";

    /// <summary>إصدار المستخدم قال «تخطّاه» — مايتعرضش لوحده تاني (بيتعرض لو طلب التحقق بنفسه).</summary>
    public string SkippedVersion { get; set; } = "";

    /// <summary>آخر إصدار اشتغل — عشان نعرض «اتحدّث ✓» أول مرة بعد التحديث.</summary>
    public string LastRunVersion { get; set; } = "";
}

public sealed class UiSettings
{
    public string FontName { get; set; } = "Readex Pro";
    public string FallbackFontName { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 10f;
    public int RefreshIntervalMs { get; set; } = 1000;
}

public sealed class MemorySettings
{
    /// <summary>التفريغ التلقائي مقفول افتراضيًا — لازم المستخدم يفعله بنفسه.</summary>
    public bool AutoPurgeEnabled { get; set; }

    /// <summary>يتنفذ التفريغ لو (Free + Zeroed) أقل من القيمة دي (م.ب).</summary>
    public int FreeMemoryThresholdMb { get; set; } = 1024;

    /// <summary>ومش هيتنفذ إلا لو الـ Standby أكبر من القيمة دي — وإلا مفيش حاجة تتفرغ أصلًا.</summary>
    public int StandbyMinimumMb { get; set; } = 1024;

    public int CheckIntervalSeconds { get; set; } = 10;

    /// <summary>أقل مدة بين تفريغين — يمنع "التفريغ المتكرر" اللي بيقتل الأداء.</summary>
    public int CooldownSeconds { get; set; } = 300;

    /// <summary>تفريغ الأولوية المنخفضة بس (أقل ضرر على الكاش المهم).</summary>
    public bool LowPriorityOnly { get; set; }

    /// <summary>"نظّف الذاكرة الآن": تقليص البرامج الخاملة كمان (مقفول افتراضيًا).</summary>
    public bool OptimizeTrimIdle { get; set; }
}

public sealed class LeakDetectionSettings
{
    public bool Enabled { get; set; } = true;
    public int SampleIntervalSeconds { get; set; } = 30;
    /// <summary>عدد العينات المتتالية المطلوبة للحكم (20 × 30ث = 10 دقايق).</summary>
    public int WindowSamples { get; set; } = 20;
    /// <summary>أقل زيادة في Private Bytes خلال النافذة عشان نعتبره تسريب محتمل (م.ب).</summary>
    public int MinGrowthMb { get; set; } = 200;
    /// <summary>نسبة العينات اللي لازم تكون زيادة (0.8 = 80%).</summary>
    public double IncreasingRatio { get; set; } = 0.8;
}

public sealed class CleanupSettings
{
    /// <summary>الملفات المؤقتة الأحدث من كده مش هتتمسح (ممكن تكون لبرنامج تسطيب شغال حاليًا).</summary>
    public int MinFileAgeHours { get; set; } = 24;

    /// <summary>تنظيف Temp لكل حسابات المستخدمين على الجهاز (يحتاج Admin) — مهم في المعامل.</summary>
    public bool AllUserProfiles { get; set; } = true;

    public List<string> DefaultCategories { get; set; } = ["UserTemp", "WindowsTemp", "RecycleBin", "CrashDumps"];

    /// <summary>مسارات المستخدم يستثنيها من أي تنظيف.</summary>
    public List<string> ExcludedPaths { get; set; } = [];
}

public sealed class HealthSettings
{
    public int UptimeWarningDays { get; set; } = 7;
    public int LowDiskPercent { get; set; } = 15;
    public int CriticalDiskPercent { get; set; } = 5;
    public int StartupWarningCount { get; set; } = 10;
}

public sealed class LabSettings
{
    public string ReportFolder { get; set; } = "";
    public string ScheduleDay { get; set; } = "Friday";
    public string ScheduleTime { get; set; } = "14:00";
    public List<string> CleanCategories { get; set; } = ["UserTemp", "WindowsTemp", "BrowserCache", "RecycleBin", "CrashDumps"];
    public bool PurgeStandby { get; set; }
    public bool OptimizeDrives { get; set; }
    public bool RunDism { get; set; }

    /// <summary>المهمة المجدولة تتحقق من التحديثات وتثبّتها صامت بعد الصيانة (كل أجهزة المعمل تتحدّث لوحدها).</summary>
    public bool AutoUpdate { get; set; } = true;
}

public static class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    /// <summary>
    /// يقرا الإعدادات من جنب الـ exe. لو الملف مش موجود يتعمل بالقيم الافتراضية.
    /// لو الملف بايظ: ياخد نسخة احتياطية ويكمل بالافتراضي (عمر الأداة ما تقف بسبب إعدادات).
    /// </summary>
    public static AppSettings Load(string? path = null)
    {
        path ??= AppEnvironment.SettingsPath;
        try
        {
            if (!File.Exists(path))
            {
                var def = new AppSettings();
                TrySave(def, path);
                return _current = def;
            }

            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            Normalize(s);
            return _current = s;
        }
        catch (Exception ex)
        {
            try
            {
                File.Copy(path, path + $".broken-{DateTime.Now:yyyyMMddHHmmss}", true);
            }
            catch { /* ignore */ }
            AppLogger.Warn("Settings", "ملف الإعدادات تالف — تم استخدام القيم الافتراضية: " + ex.Message);
            return _current = new AppSettings();
        }
    }

    public static bool TrySave(AppSettings settings, string? path = null)
    {
        path ??= AppEnvironment.SettingsPath;
        try
        {
            Normalize(settings);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tmp, path, true);
            _current = settings;
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Settings", "تعذر حفظ الإعدادات: " + ex.Message);
            return false;
        }
    }

    private static void Normalize(AppSettings s)
    {
        s.Ui ??= new();
        s.Memory ??= new();
        s.LeakDetection ??= new();
        s.Cleanup ??= new();
        s.Health ??= new();
        s.Lab ??= new();
        s.Update ??= new();
        s.Memory.CheckIntervalSeconds = Math.Clamp(s.Memory.CheckIntervalSeconds, 2, 3600);
        s.Memory.CooldownSeconds = Math.Clamp(s.Memory.CooldownSeconds, 30, 86400);
        s.Memory.FreeMemoryThresholdMb = Math.Clamp(s.Memory.FreeMemoryThresholdMb, 64, 1024 * 256);
        s.Ui.RefreshIntervalMs = Math.Clamp(s.Ui.RefreshIntervalMs, 500, 10000);
        s.Cleanup.MinFileAgeHours = Math.Clamp(s.Cleanup.MinFileAgeHours, 0, 24 * 365);
        s.LeakDetection.WindowSamples = Math.Clamp(s.LeakDetection.WindowSamples, 5, 1000);
        s.LeakDetection.IncreasingRatio = Math.Clamp(s.LeakDetection.IncreasingRatio, 0.5, 1.0);
    }
}
