using System.Reflection;
using System.Security.Principal;

namespace AjyalPcCare.Core.Common;

public static class AppEnvironment
{
    private static readonly Lazy<bool> _isAdmin = new(() =>
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    });

    private static readonly Lazy<string> _dataDir = new(ResolveDataDir);

    public static bool IsAdministrator => _isAdmin.Value;

    public static string Version =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>مجلد الـ exe (مكان ملف الإعدادات).</summary>
    public static string BaseDirectory => AppContext.BaseDirectory;

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(BaseDirectory, "PCCare.exe");

    public static string SettingsPath => Path.Combine(BaseDirectory, "pccare.settings.json");

    /// <summary>
    /// مجلد البيانات (اللوج + سجل التغييرات + التقارير المحلية).
    /// جنب الـ exe لو قابل للكتابة، وإلا %ProgramData%\AjyalPcCare
    /// (لو الأداة متسطبة في Program Files المستخدم العادي مش هيقدر يكتب جنبها).
    /// </summary>
    public static string DataDirectory => _dataDir.Value;

    public static string LogsDirectory => EnsureDir(Path.Combine(DataDirectory, "logs"));

    public static string ReportsDirectory => EnsureDir(Path.Combine(DataDirectory, "reports"));

    public static string WindowsDirectory => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public static string SystemDrive => Path.GetPathRoot(WindowsDirectory) ?? @"C:\";

    private static string ResolveDataDir()
    {
        string local = BaseDirectory;
        // متسطب من المثبّت في Program Files: البيانات دايمًا في ProgramData
        // (عشان التحديث يستبدل ملفات البرنامج من غير ما يلمس اللوج والسجل والتقارير)
        bool installed = IsUnder(local, Environment.SpecialFolder.ProgramFiles) || IsUnder(local, Environment.SpecialFolder.ProgramFilesX86);
        if (!installed && IsWritable(local)) return local;

        string pd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AjyalPcCare");
        try
        {
            Directory.CreateDirectory(pd);
            return pd;
        }
        catch
        {
            string fallback = Path.Combine(Path.GetTempPath(), "AjyalPcCare");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    private static bool IsUnder(string path, Environment.SpecialFolder folder)
    {
        string root = Environment.GetFolderPath(folder);
        return !string.IsNullOrEmpty(root) && path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".write_probe_{Environment.ProcessId}.tmp");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
