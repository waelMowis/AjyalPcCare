using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Monitoring;
using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Startup;

/// <summary>
/// يجمع كل مصادر التشغيل التلقائي: Run/RunOnce (64 و32 بت)، مجلدات Startup،
/// المهام المجدولة عند الدخول/الإقلاع، والخدمات التلقائية غير التابعة لمايكروسوفت.
/// </summary>
public static class StartupScanner
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    internal const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    internal const string RunOnce32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce";
    internal const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public static async Task<List<StartupEntry>> ScanAsync(IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var list = new List<StartupEntry>();

        progress?.Report(new ModuleProgress(5, "قراءة الريجستري..."));
        list.AddRange(ScanRegistry(Registry.CurrentUser, RunKey, StartupSource.RegistryRunUser, "Run", true));
        list.AddRange(ScanRegistry(Registry.LocalMachine, RunKey, StartupSource.RegistryRunMachine, "Run", true));
        list.AddRange(ScanRegistry(Registry.LocalMachine, Run32Key, StartupSource.RegistryRunMachine32, "Run32", true));
        list.AddRange(ScanRegistry(Registry.CurrentUser, RunOnceKey, StartupSource.RegistryRunOnceUser, null, false));
        list.AddRange(ScanRegistry(Registry.LocalMachine, RunOnceKey, StartupSource.RegistryRunOnceMachine, null, false));
        list.AddRange(ScanRegistry(Registry.LocalMachine, RunOnce32Key, StartupSource.RegistryRunOnceMachine, null, false));

        progress?.Report(new ModuleProgress(20, "قراءة مجلدات Startup..."));
        list.AddRange(ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.StartupFolderUser, Registry.CurrentUser));
        list.AddRange(ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.StartupFolderCommon, Registry.LocalMachine));

        progress?.Report(new ModuleProgress(35, "قراءة المهام المجدولة..."));
        list.AddRange(await ScanScheduledTasksAsync(ct).ConfigureAwait(false));

        progress?.Report(new ModuleProgress(55, "قراءة الخدمات..."));
        var managed = Safety.ChangeJournal.All
            .Where(r => r.UndoKind == Safety.UndoKind.ServiceStartType && !r.Undone)
            .Select(r => r.Target)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        list.AddRange(ScanServices(managed));

        progress?.Report(new ModuleProgress(70, "التحقق من الملفات والتوقيعات الرقمية..."));
        await Task.Run(() => Parallel.ForEach(list, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 }, Enrich), ct).ConfigureAwait(false);

        // الخدمات: بنعرض غير مايكروسوفت فقط (خدمات ويندوز مش مكانها هنا)
        list.RemoveAll(e => e.Source == StartupSource.Service && e.IsMicrosoft && e.Signature == SignatureState.Valid);

        progress?.Report(new ModuleProgress(100, $"تم العثور على {list.Count} عنصر"));
        return list.OrderBy(e => e.Source).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void Enrich(StartupEntry e)
    {
        try
        {
            if (string.IsNullOrEmpty(e.ExecutablePath)) e.ExecutablePath = CommandLineParser.ExtractExecutable(e.Command);
            e.FileExists = !string.IsNullOrEmpty(e.ExecutablePath) && File.Exists(e.ExecutablePath);
            if (!e.FileExists)
            {
                e.Signature = SignatureState.FileMissing;
                e.Note ??= "مسار الملف غير موجود (مدخل مكسور)";
                return;
            }
            e.Publisher ??= ProcessMonitor.GetPublisher(e.ExecutablePath);
            e.Signature = TrustApi.Verify(e.ExecutablePath!);
        }
        catch (Exception ex)
        {
            AppLogger.Debug("Startup", $"Enrich {e.Name}: {ex.Message}");
        }
    }

    public static string MakeId(StartupSource source, string location, string name)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes($"{source}|{location}|{name}".ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..16];
    }

    // ---------------------------- Registry ----------------------------

    private static IEnumerable<StartupEntry> ScanRegistry(RegistryKey hive, string keyPath, StartupSource source, string? approvedSub, bool canToggle)
    {
        var result = new List<StartupEntry>();
        try
        {
            using var key = hive.OpenSubKey(keyPath);
            if (key is null) return result;
            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name)) continue;
                string command = key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
                string location = $@"{hive.Name}\{keyPath}";
                result.Add(new StartupEntry
                {
                    Id = MakeId(source, location, name),
                    Name = name,
                    Source = source,
                    Location = location,
                    Command = command,
                    Enabled = approvedSub is null || StartupApproval.IsEnabled(hive, approvedSub, name),
                    CanToggle = canToggle,
                    Note = canToggle ? null : "RunOnce بيتنفذ مرة واحدة ويتمسح تلقائيًا — للعرض فقط"
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", $"تعذر قراءة {hive.Name}\\{keyPath}: {ex.Message}");
        }
        return result;
    }

    // ---------------------------- Startup folders ----------------------------

    private static IEnumerable<StartupEntry> ScanFolder(string folder, StartupSource source, RegistryKey approvedHive)
    {
        var result = new List<StartupEntry>();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return result;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                string? target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutResolver.Resolve(file, out var args) : file;
                result.Add(new StartupEntry
                {
                    Id = MakeId(source, folder, fileName),
                    Name = Path.GetFileNameWithoutExtension(file),
                    Source = source,
                    Location = file,
                    Command = target ?? file,
                    ExecutablePath = target,
                    Enabled = StartupApproval.IsEnabled(approvedHive, "StartupFolder", fileName),
                    CanToggle = true
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", $"تعذر قراءة {folder}: {ex.Message}");
        }
        return result;
    }

    // ---------------------------- Scheduled tasks ----------------------------

    private sealed class TaskDto
    {
        public string? TaskName { get; set; }
        public string? TaskPath { get; set; }
        public string? State { get; set; }
        public string? Execute { get; set; }
        public string? Arguments { get; set; }
        public string? Author { get; set; }
        public string? Triggers { get; set; }
    }

    private static async Task<List<StartupEntry>> ScanScheduledTasksAsync(CancellationToken ct)
    {
        const string script =
            "Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\\Microsoft\\*' -and $_.TaskPath -notlike '\\AJYAL PC Care\\*' } | " +
            "ForEach-Object { $t = $_; $kinds = @($t.Triggers | ForEach-Object { $_.CimClass.CimClassName }); " +
            "if ($kinds -contains 'MSFT_TaskLogonTrigger' -or $kinds -contains 'MSFT_TaskBootTrigger') { " +
            "$a = $t.Actions | Select-Object -First 1; " +
            "[pscustomobject]@{ TaskName = $t.TaskName; TaskPath = $t.TaskPath; State = [string]$t.State; Execute = [string]$a.Execute; Arguments = [string]$a.Arguments; Author = [string]$t.Author; Triggers = ($kinds -join ',') } } }";

        var tasks = await PowerShell.RunJsonListAsync<TaskDto>(script, ct, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        return tasks.Where(t => !string.IsNullOrEmpty(t.TaskName)).Select(t =>
        {
            string location = (t.TaskPath ?? "\\") + t.TaskName;
            string exec = CommandLineParser.NormalizeSystemPath(t.Execute?.Trim('"')) ?? "";
            return new StartupEntry
            {
                Id = MakeId(StartupSource.ScheduledTask, location, t.TaskName!),
                Name = t.TaskName!,
                Source = StartupSource.ScheduledTask,
                Location = location,
                Command = string.IsNullOrEmpty(t.Arguments) ? exec : $"\"{exec}\" {t.Arguments}",
                ExecutablePath = string.IsNullOrEmpty(exec) ? null : exec,
                Enabled = !string.Equals(t.State, "Disabled", StringComparison.OrdinalIgnoreCase),
                CanToggle = true,
                Note = (t.Triggers ?? "").Contains("Boot") ? "تعمل عند إقلاع الجهاز" : "تعمل عند تسجيل الدخول"
            };
        }).ToList();
    }

    // ---------------------------- Services ----------------------------

    private static IEnumerable<StartupEntry> ScanServices(HashSet<string> managedByTool)
    {
        var result = new List<StartupEntry>();
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null) return result;

            foreach (var name in services.GetSubKeyNames())
            {
                try
                {
                    using var svc = services.OpenSubKey(name);
                    if (svc is null) continue;
                    if (svc.GetValue("Type") is not int type || (type & 0x30) == 0) continue; // Win32 services فقط (مش Drivers)
                    if (svc.GetValue("Start") is not int start) continue;
                    // Automatic فقط + الخدمات اللي الأداة نفسها حولتها لـ Manual (عشان تقدر ترجعها)
                    if (start != 2 && !(start == 3 && managedByTool.Contains(name))) continue;

                    string image = svc.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
                    string? exe = CommandLineParser.ExtractExecutable(CommandLineParser.NormalizeSystemPath(image));

                    // الخدمات المستضافة في svchost — الملف الحقيقي هو ServiceDll
                    if (exe is not null && Path.GetFileName(exe).Equals("svchost.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        using var parameters = svc.OpenSubKey("Parameters");
                        if (parameters?.GetValue("ServiceDll") is string dll)
                            exe = CommandLineParser.NormalizeSystemPath(dll);
                    }

                    string display = svc.GetValue("DisplayName")?.ToString() ?? name;
                    if (display.StartsWith('@')) display = name; // نص مترجم من Resource — نكتفي بالاسم

                    result.Add(new StartupEntry
                    {
                        Id = MakeId(StartupSource.Service, name, name),
                        Name = $"{display} ({name})",
                        Source = StartupSource.Service,
                        Location = name,
                        Command = image,
                        ExecutablePath = exe,
                        Enabled = start == 2,
                        CanToggle = true,
                        ServiceStartType = ServiceControl.GetStartType(name),
                        Note = "التعطيل هنا = تحويلها لـ Manual (تشتغل عند الطلب) مش Disabled"
                    });
                }
                catch
                {
                    // خدمة محمية
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Startup", "تعذر قراءة الخدمات: " + ex.Message);
        }
        return result;
    }
}

/// <summary>آلية StartupApproved نفسها اللي بيستخدمها Task Manager — قابلة للتراجع 100%.</summary>
public static class StartupApproval
{
    public static bool IsEnabled(RegistryKey hive, string sub, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey($@"{StartupScanner.ApprovedRoot}\{sub}");
            if (key?.GetValue(valueName) is not byte[] data || data.Length == 0) return true; // مفيش قيمة = مفعّل
            return (data[0] & 0x01) == 0; // 0x02/0x06 = مفعّل، 0x03/0x07 = متعطل
        }
        catch
        {
            return true;
        }
    }

    public static byte[]? GetRaw(RegistryKey hive, string sub, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey($@"{StartupScanner.ApprovedRoot}\{sub}");
            return key?.GetValue(valueName) as byte[];
        }
        catch
        {
            return null;
        }
    }

    public static void Set(RegistryKey hive, string sub, string valueName, bool enabled)
    {
        using var key = hive.CreateSubKey($@"{StartupScanner.ApprovedRoot}\{sub}", true);
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            // Task Manager بيكتب وقت التعطيل كـ FILETIME في آخر 8 بايت
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        }
        key.SetValue(valueName, data, RegistryValueKind.Binary);
    }

    public static void SetRaw(RegistryKey hive, string sub, string valueName, byte[]? raw)
    {
        using var key = hive.CreateSubKey($@"{StartupScanner.ApprovedRoot}\{sub}", true);
        if (raw is null) key.DeleteValue(valueName, false);
        else key.SetValue(valueName, raw, RegistryValueKind.Binary);
    }
}

/// <summary>قراءة هدف ملف اختصار .lnk عن طريق WScript.Shell (COM موجود على كل ويندوز).</summary>
public static class ShortcutResolver
{
    public static string? Resolve(string lnkPath, out string arguments)
    {
        arguments = "";
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            try
            {
                dynamic link = shell.CreateShortcut(lnkPath);
                arguments = (string)link.Arguments;
                string target = (string)link.TargetPath;
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
        catch
        {
            return null;
        }
    }
}
