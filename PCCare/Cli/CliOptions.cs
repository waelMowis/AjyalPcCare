using AjyalPcCare.Core.Cleanup;
using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Cli;

/// <summary>
/// خيارات سطر الأوامر. أمثلة:
///   PCCare.exe /silent
///   PCCare.exe /silent /clean:temp,browser,recycle /purge-standby /report:\\server\share\reports
///   PCCare.exe /silent /clean:all /optimize /dism /min-age:48
///   PCCare.exe /install-schedule
/// </summary>
public sealed class CliOptions
{
    public bool Silent { get; private set; }
    public bool ShowHelp { get; private set; }
    public List<string> CleanCategories { get; } = [];
    public bool PurgeStandby { get; private set; }
    public bool PurgeStandbyLowOnly { get; private set; }
    public bool Optimize { get; private set; }
    public bool Dism { get; private set; }
    public bool Health { get; private set; } = true;
    public string? ReportFolder { get; private set; }
    public int? MinAgeHours { get; private set; }
    public bool InstallSchedule { get; private set; }
    public bool RemoveSchedule { get; private set; }
    /// <summary>/update: التحقق من التحديثات وتثبيتها صامت (لوحده أو بعد الصيانة).</summary>
    public bool Update { get; private set; }
    public List<string> Errors { get; } = [];

    /// <summary>هل اتحدد أي إجراء صراحة؟ لو لأ في وضع /silent نستخدم إعدادات وضع المعامل.</summary>
    public bool HasExplicitActions { get; private set; }

    public bool IsCliInvocation => Silent || ShowHelp || InstallSchedule || RemoveSchedule || Update;

    public static CliOptions Parse(IEnumerable<string> args)
    {
        var o = new CliOptions();
        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string a = raw.Trim();
            if (a.StartsWith("--")) a = a[2..];
            else if (a.StartsWith('/') || a.StartsWith('-')) a = a[1..];
            else
            {
                o.Errors.Add("خيار غير معروف: " + raw);
                continue;
            }

            int colon = a.IndexOf(':');
            string name = (colon >= 0 ? a[..colon] : a).ToLowerInvariant();
            string? value = colon >= 0 ? a[(colon + 1)..].Trim('"') : null;

            switch (name)
            {
                case "silent":
                case "s":
                    o.Silent = true;
                    break;
                case "help":
                case "?":
                case "h":
                    o.ShowHelp = true;
                    break;
                case "clean":
                    o.HasExplicitActions = true;
                    foreach (var part in (value ?? "all").Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var cat = CleanupCategories.FromCliName(part);
                        if (cat is null) o.Errors.Add("فئة تنظيف غير معروفة: " + part);
                        else if (cat == "all") o.CleanCategories.AddRange(CleanupCategories.All);
                        else o.CleanCategories.Add(cat);
                    }
                    break;
                case "purge-standby":
                    o.HasExplicitActions = true;
                    o.PurgeStandby = true;
                    o.PurgeStandbyLowOnly = string.Equals(value, "low", StringComparison.OrdinalIgnoreCase);
                    break;
                case "optimize":
                    o.HasExplicitActions = true;
                    o.Optimize = true;
                    break;
                case "dism":
                    o.HasExplicitActions = true;
                    o.Dism = true;
                    break;
                case "no-health":
                    o.Health = false;
                    break;
                case "report":
                    o.ReportFolder = value;
                    break;
                case "min-age":
                    if (int.TryParse(value, out int h) && h >= 0) o.MinAgeHours = h;
                    else o.Errors.Add("قيمة min-age غير صالحة: " + value);
                    break;
                case "install-schedule":
                    o.InstallSchedule = true;
                    break;
                case "remove-schedule":
                    o.RemoveSchedule = true;
                    break;
                case "update":
                    o.Update = true;
                    break;
                default:
                    o.Errors.Add("خيار غير معروف: " + raw);
                    break;
            }
        }

        var distinct = o.CleanCategories.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        o.CleanCategories.Clear();
        o.CleanCategories.AddRange(distinct);
        return o;
    }

    /// <summary>لو /silent بدون إجراءات صريحة: الإجراءات من قسم Lab في الإعدادات.</summary>
    public void ApplyLabDefaults(LabSettings lab)
    {
        if (HasExplicitActions) return;
        Update = Update || lab.AutoUpdate;
        foreach (var c in lab.CleanCategories)
        {
            var cat = CleanupCategories.FromCliName(c) ?? (CleanupCategories.All.Contains(c) ? c : null);
            if (cat is not null && cat != "all" && !CleanCategories.Contains(cat)) CleanCategories.Add(cat);
        }
        PurgeStandby = lab.PurgeStandby;
        Optimize = lab.OptimizeDrives;
        Dism = lab.RunDism;
    }

    /// <summary>بناء سطر الأوامر للمهمة المجدولة من إعدادات Lab.</summary>
    public static string BuildScheduledArguments(LabSettings lab)
    {
        var parts = new List<string> { "/silent" };
        var cli = lab.CleanCategories.Select(ToCliName).Where(n => n is not null).ToList();
        if (cli.Count > 0) parts.Add("/clean:" + string.Join(',', cli));
        if (lab.PurgeStandby) parts.Add("/purge-standby");
        if (lab.OptimizeDrives) parts.Add("/optimize");
        if (lab.RunDism) parts.Add("/dism");
        if (lab.AutoUpdate) parts.Add("/update");
        if (!string.IsNullOrWhiteSpace(lab.ReportFolder)) parts.Add($"\"/report:{lab.ReportFolder}\"");
        return string.Join(' ', parts);
    }

    private static string? ToCliName(string category) => category switch
    {
        CleanupCategories.UserTemp => "temp",
        CleanupCategories.WindowsTemp => "wintemp",
        CleanupCategories.BrowserCache => "browser",
        CleanupCategories.RecycleBin => "recycle",
        CleanupCategories.WindowsUpdate => "wu",
        CleanupCategories.DeliveryOptimization => "do",
        CleanupCategories.CrashDumps => "dumps",
        _ => null
    };

    public const string HelpText = """
AJYAL PC Care — أوامر سطر الأوامر

  /silent                    تشغيل صامت بدون واجهة (لو مفيش إجراءات، بيستخدم إعدادات Lab)
  /clean:<فئات>              temp, wintemp, browser, recycle, wu, do, dumps, all
  /purge-standby[:low]       تفريغ قائمة Standby (low = الأولوية المنخفضة فقط)
  /optimize                  TRIM للـ SSD / Defrag للـ HDD
  /dism                      DISM /StartComponentCleanup (بياخد وقت)
  /min-age:<ساعات>           أقل عمر للملفات المؤقتة اللي تتمسح (افتراضي 24)
  /report:<مجلد>             مكان حفظ التقرير (JSON + HTML + summary.csv) — يقبل مسار شبكة
  /no-health                 بدون فحص الصحة في التقرير
  /update                    التحقق من التحديثات على GitHub وتثبيتها صامت (لوحده أو مع /silent بعد الصيانة)
  /install-schedule          تسجيل المهمة الأسبوعية من إعدادات Lab
  /remove-schedule           حذف المهمة الأسبوعية

أكواد الخروج: 0 نجاح، 1 نجاح مع أخطاء جزئية، 2 فشل، 3 نسخة أخرى شغالة، 4 خيارات غير صالحة، 5 يحتاج صلاحيات مسؤول

مثال:
  PCCare.exe /silent /clean:temp,browser,recycle /purge-standby /report:\\server\share\reports
""";
}
