using System.Text;
using System.Text.RegularExpressions;
using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Uninstall;

/// <summary>
/// قواعد المطابقة والأمان لبقايا البرامج.
/// المبدأ: الغلط في إننا نسيب بقايا أهون مليون مرة من الغلط في إننا نمسح حاجة تبع برنامج تاني أو تبع ويندوز.
/// </summary>
public static partial class LeftoverRules
{
    /// <summary>أسماء فولدرات/مفاتيح مشتركة عمرها ما تتمسح كاملة (ممكن نمسح فولدر برنامج جواها بس).</summary>
    private static readonly HashSet<string> SharedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Microsoft Shared", "Common Files", "Microsoft.NET", "dotnet", "WindowsApps", "Windows Defender",
        "Windows NT", "Windows Mail", "Windows Media Player", "Windows Photo Viewer", "Windows Sidebar", "Internet Explorer",
        "Package Cache", "Packages", "Programs", "Temp", "Installer", "Downloaded Installations", "Microsoft SDKs", "Reference Assemblies",
        "MSBuild", "IIS", "IIS Express", "Uninstall Information", "ModifiableWindowsApps", "Intel", "Intel Corporation", "NVIDIA", "NVIDIA Corporation",
        "AMD", "ATI Technologies", "Realtek", "Google", "Mozilla", "Adobe", "Apple", "Apple Computer", "Oracle", "Java", "Common",
        "Classes", "Policies", "Clients", "RegisteredApplications", "ODBC", "WOW6432Node", "Wow6432Node", "Khronos", "Partner",
        "Description", "Setup", "Software", "Users", "Public", "Default", "All Users", "System32", "SysWOW64", "Fonts", "Desktop",
        "Start Menu", "Startup", "Administrative Tools", "Accessibility", "Accessories", "Maintenance", "System Tools", "Windows PowerShell",
        "Application Data", "Local Settings", "History", "Cache", "Logs", "Crashpad", "CrashDumps", "Squirrel", "Updater", "Update",
        "Licenses", "Help", "Documentation", "Tools", "Bin", "Lib", "Data", "Config", "Settings", "Plugins", "Extensions", "Runtime"
    };

    private static readonly string[] CompanySuffixes =
        ["incorporated", "inc", "ltd", "limited", "llc", "corporation", "corp", "gmbh", "co", "company", "sa", "ag", "bv", "srl", "pty", "plc", "llp", "software", "technologies", "technology", "group"];

    [GeneratedRegex(@"\(.*?\)|\[.*?\]")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"\bv?\d+(\.\d+)+[a-z]?\b|\b(x64|x86|amd64|arm64|64-bit|32-bit|64bit|32bit|win64|win32|version|setup|installer|update)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    /// <summary>"7-Zip 23.01 (x64)" ← "7-Zip" — اسم البرنامج من غير إصدار ومعمارية.</summary>
    public static string CoreName(string name)
    {
        string s = Brackets().Replace(name, " ");
        s = Noise().Replace(s, " ");
        s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '_', '.', ',');
        return s.Length >= 2 ? s : name.Trim();
    }

    public static string CorePublisher(string publisher)
    {
        var words = Regex.Split(publisher.ToLowerInvariant().Replace(",", " ").Replace(".", " "), @"\s+")
            .Where(w => w.Length > 0 && !CompanySuffixes.Contains(w)).ToList();
        return string.Join(" ", words);
    }

    /// <summary>
    /// للمقارنة: حروف وأرقام و + و # بس، lower-case ("7-Zip" ← "7zip", "Notepad++" ← "notepad++").
    /// الـ + و # جزء من الاسم: من غيرهم "Notepad++" كانت هتطابق "Notepad" بتاع ويندوز.
    /// </summary>
    public static string Key(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(c) || c is '+' or '#') sb.Append(c);
        return sb.ToString();
    }

    public static bool IsShared(string name) => SharedNames.Contains(name.Trim());

    /// <summary>اسم الفولدر/المفتاح هو نفس اسم البرنامج (بعد التطبيع).</summary>
    public static bool ExactMatch(string candidate, string productKey) =>
        productKey.Length >= 3 && Key(candidate) == productKey;

    /// <summary>اسم الفولدر بيحتوي اسم البرنامج كامل (أضعف — بيتعرض من غير علامة).</summary>
    public static bool ContainsMatch(string candidate, string productKey) =>
        productKey.Length >= 5 && Key(candidate).Contains(productKey);

    private static readonly Lazy<string[]> ProtectedRoots = new(() =>
    {
        string win = AppEnvironment.WindowsDirectory.TrimEnd('\\');
        var roots = new List<string>
        {
            win,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? @"C:\Users"
        };
        foreach (var prof in Cleanup.UserProfiles.GetProfiles(true))
        {
            roots.Add(prof.Path);
            roots.Add(prof.LocalAppData);
            roots.Add(prof.RoamingAppData);
            roots.Add(Path.Combine(prof.Path, "AppData", "LocalLow"));
            roots.Add(Path.Combine(prof.Path, "AppData"));
            roots.Add(Path.Combine(prof.Path, "Desktop"));
            roots.Add(Path.Combine(prof.Path, "Documents"));
            roots.Add(Path.Combine(prof.Path, "Downloads"));
            roots.Add(Path.Combine(prof.RoamingAppData, @"Microsoft\Windows\Start Menu\Programs"));
        }
        return roots.Where(r => !string.IsNullOrEmpty(r)).Select(r => r.TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    });

    /// <summary>
    /// الفولدر ده ينفع يتمسح؟ ممنوع: أي جذر محمي نفسه، أي حاجة جوه Windows، أي فولدر جذر درايف، أي فولدر اسمه مشترك.
    /// </summary>
    public static bool IsSafeFolder(string path)
    {
        try
        {
            string full = Path.GetFullPath(path).TrimEnd('\\');
            if (full.Length <= 3) return false;
            if (ProtectedRoots.Value.Any(r => full.Equals(r, StringComparison.OrdinalIgnoreCase))) return false;
            string win = AppEnvironment.WindowsDirectory.TrimEnd('\\') + "\\";
            if (full.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return false;
            if (IsShared(Path.GetFileName(full))) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>مكان تثبيت منطقي (عشان نستنتج InstallLocation): جوه Program Files أو AppData، ومش في مخزن مشترك.</summary>
    public static bool IsPlausibleInstallDir(string dir)
    {
        if (!IsSafeFolder(dir)) return false;
        string d = dir.ToLowerInvariant();
        if (d.Contains(@"\package cache\") || d.Contains(@"\installer\") || d.Contains(@"\temp\") || d.Contains(@"\downloaded installations\")) return false;
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).ToLowerInvariant();
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).ToLowerInvariant();
        return d.StartsWith(pf + "\\") || d.StartsWith(pf86 + "\\") || d.Contains(@"\appdata\");
    }

    public static bool IsInside(string path, string parent)
    {
        string a = path.TrimEnd('\\') + "\\";
        string b = parent.TrimEnd('\\') + "\\";
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }
}
