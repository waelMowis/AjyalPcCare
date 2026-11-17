using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Uninstall;

public enum ProgramScope
{
    Machine64,
    Machine32,
    User
}

/// <summary>برنامج متثبت — من مفاتيح Uninstall في الريجستري (نفس مصدر "البرامج والميزات").</summary>
public sealed class InstalledProgram
{
    public required string KeyName { get; init; }
    public required ProgramScope Scope { get; init; }
    public required string Name { get; init; }
    public string Version { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string? InstallLocation { get; set; }
    public bool InstallLocationGuessed { get; set; }
    public string? UninstallString { get; init; }
    public string? QuietUninstallString { get; init; }
    public string? DisplayIcon { get; init; }
    public long SizeBytes { get; init; }
    public DateTime? InstallDate { get; init; }
    public bool IsMsi { get; init; }
    public string? MsiProductCode { get; init; }
    public bool IsSystemComponent { get; init; }
    public bool NoRemove { get; init; }

    public string Id => $"{Scope}|{KeyName}";

    public RegistryKey Hive => Scope == ProgramScope.User ? Registry.CurrentUser : Registry.LocalMachine;

    public string UninstallKeyPath => Scope == ProgramScope.Machine32
        ? $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{KeyName}"
        : $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{KeyName}";

    public string RegistryDisplayPath => $@"{(Scope == ProgramScope.User ? "HKCU" : "HKLM")}\{UninstallKeyPath}";

    public string ScopeDisplay => Scope switch
    {
        ProgramScope.Machine64 => "الجهاز (64-بت)",
        ProgramScope.Machine32 => "الجهاز (32-بت)",
        _ => "المستخدم الحالي"
    };

    public bool HasUninstaller => IsMsi || !string.IsNullOrWhiteSpace(UninstallString);

    public bool StillRegistered()
    {
        try
        {
            using var k = Hive.OpenSubKey(UninstallKeyPath);
            return k is not null;
        }
        catch
        {
            return false;
        }
    }
}

public static partial class InstalledPrograms
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallPath32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    [GeneratedRegex(@"^\{[0-9A-Fa-f\-]{36}\}$")]
    private static partial Regex GuidRegex();

    /// <summary>
    /// كل البرامج المتثبتة (جهاز 64 + 32 + المستخدم الحالي).
    /// بيستبعد التحديثات (ParentKeyName / ReleaseType) زي "البرامج والميزات" بالظبط،
    /// ومكونات النظام (SystemComponent=1) إلا لو includeSystem.
    /// </summary>
    public static List<InstalledProgram> Scan(bool includeSystem = false)
    {
        var list = new List<InstalledProgram>();
        Read(Registry.LocalMachine, UninstallPath, ProgramScope.Machine64, list);
        Read(Registry.LocalMachine, UninstallPath32, ProgramScope.Machine32, list);
        Read(Registry.CurrentUser, UninstallPath, ProgramScope.User, list);

        var result = list
            .Where(p => includeSystem || !p.IsSystemComponent)
            // نفس البرنامج ممكن يتسجل مرتين (مثلًا MSI + غلاف EXE) — نسيب اللي ليه مسار إلغاء
            .GroupBy(p => (p.Name.ToLowerInvariant(), p.Version))
            .Select(g => g.OrderByDescending(p => p.HasUninstaller).ThenBy(p => p.IsSystemComponent).First())
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        AppLogger.Debug("Uninstall", $"جرد البرامج: {result.Count}");
        return result;
    }

    private static void Read(RegistryKey hive, string path, ProgramScope scope, List<InstalledProgram> list)
    {
        try
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) return;
            foreach (var keyName in root.GetSubKeyNames())
            {
                try
                {
                    using var k = root.OpenSubKey(keyName);
                    if (k is null) continue;
                    string? name = (k.GetValue("DisplayName") as string)?.Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (k.GetValue("ParentKeyName") is string { Length: > 0 }) continue; // تحديث تابع لبرنامج
                    string releaseType = k.GetValue("ReleaseType") as string ?? "";
                    if (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) || releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)) continue;

                    string? uninstall = (k.GetValue("UninstallString") as string)?.Trim();
                    bool isMsi = k.GetValue("WindowsInstaller") is int wi && wi == 1
                                 || (uninstall?.Contains("msiexec", StringComparison.OrdinalIgnoreCase) ?? false);
                    string? productCode = GuidRegex().IsMatch(keyName) ? keyName : null;

                    var p = new InstalledProgram
                    {
                        KeyName = keyName,
                        Scope = scope,
                        Name = name,
                        Version = (k.GetValue("DisplayVersion") as string ?? "").Trim(),
                        Publisher = (k.GetValue("Publisher") as string ?? "").Trim(),
                        InstallLocation = CleanPath(k.GetValue("InstallLocation") as string),
                        UninstallString = uninstall,
                        QuietUninstallString = (k.GetValue("QuietUninstallString") as string)?.Trim(),
                        DisplayIcon = k.GetValue("DisplayIcon") as string,
                        SizeBytes = k.GetValue("EstimatedSize") is int kb && kb > 0 ? kb * 1024L : 0,
                        InstallDate = ParseDate(k.GetValue("InstallDate") as string),
                        IsMsi = isMsi && productCode is not null,
                        MsiProductCode = productCode,
                        IsSystemComponent = k.GetValue("SystemComponent") is int sc && sc == 1,
                        NoRemove = k.GetValue("NoRemove") is int nr && nr == 1
                    };

                    if (string.IsNullOrEmpty(p.InstallLocation))
                    {
                        p.InstallLocation = GuessInstallLocation(p);
                        p.InstallLocationGuessed = p.InstallLocation is not null;
                    }
                    list.Add(p);
                }
                catch
                {
                    // مفتاح محمي أو بايظ
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Uninstall", $"تعذر قراءة {hive.Name}\\{path}: {ex.Message}");
        }
    }

    private static string? CleanPath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        p = Environment.ExpandEnvironmentVariables(p.Trim().Trim('"')).TrimEnd('\\');
        return p.Length > 3 && Directory.Exists(p) ? p : null;
    }

    private static DateTime? ParseDate(string? s) =>
        s is { Length: 8 } && DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>
    /// برامج كتير مابتكتبش InstallLocation. نستنتجه من الأيقونة أو برنامج الإلغاء — بس لو جوه Program Files أو AppData
    /// (مش Windows\Installer ولا Package Cache ولا Temp، دول مخازن مشتركة).
    /// </summary>
    private static string? GuessInstallLocation(InstalledProgram p)
    {
        foreach (var raw in new[] { p.DisplayIcon, p.IsMsi ? null : p.UninstallString })
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string s = raw.Trim();
            int comma = s.LastIndexOf(',');
            if (comma > 3 && !s[(comma + 1)..].Contains('\\')) s = s[..comma];
            string? exe = Startup.CommandLineParser.ExtractExecutable(s);
            if (string.IsNullOrEmpty(exe)) continue;
            string? dir = Path.GetDirectoryName(exe);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            if (!LeftoverRules.IsPlausibleInstallDir(dir)) continue;
            return dir.TrimEnd('\\');
        }
        return null;
    }
}
