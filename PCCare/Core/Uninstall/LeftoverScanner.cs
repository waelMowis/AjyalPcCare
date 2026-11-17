using Microsoft.Win32;
using AjyalPcCare.Core.Cleanup;
using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Uninstall;

public enum LeftoverKind
{
    Folder,
    File,
    Shortcut,
    RegistryKey,
    RegistryValue,
    Service,
    ScheduledTask
}

public enum LeftoverConfidence
{
    /// <summary>مؤكد: جوه مكان تثبيت البرنامج أو اسمه مطابق بالظبط — بيتعلّم تلقائي.</summary>
    High,
    /// <summary>محتمل: الاسم بيحتوي اسم البرنامج — بيتعرض من غير علامة والمستخدم يقرر.</summary>
    Medium
}

public sealed class Leftover
{
    public required LeftoverKind Kind { get; init; }
    public required string Path { get; init; }
    public required LeftoverConfidence Confidence { get; init; }
    public required string Reason { get; init; }
    public long SizeBytes { get; set; }
    public bool Selected { get; set; }

    // للريجستري
    public RegistryKey? Hive { get; init; }
    public string? KeyPath { get; init; }
    public string? ValueName { get; init; }

    // للخدمات والمهام
    public string? ServiceName { get; init; }
    public string? TaskPath { get; init; }
    public string? TaskName { get; init; }

    public string KindDisplay => Kind switch
    {
        LeftoverKind.Folder => "فولدر",
        LeftoverKind.File => "ملف",
        LeftoverKind.Shortcut => "اختصار",
        LeftoverKind.RegistryKey => "مفتاح ريجستري",
        LeftoverKind.RegistryValue => "قيمة ريجستري",
        LeftoverKind.Service => "خدمة",
        LeftoverKind.ScheduledTask => "مهمة مجدولة",
        _ => Kind.ToString()
    };

    public string ConfidenceDisplay => Confidence == LeftoverConfidence.High ? "مؤكد" : "محتمل";
}

/// <summary>
/// البحث عن بقايا برنامج بعد إلغاء تثبيته.
/// مصادر الثقة العالية: مكان التثبيت نفسه، أو اسم مطابق بالظبط تحت جذر معروف، أو حاجة بتشاور جوه مكان التثبيت.
/// أي حاجة جوه مكان تثبيت برنامج تاني لسه متثبت (أو بتحتويه) بتتشال من النتائج — مهما كان اسمها.
/// </summary>
public static class LeftoverScanner
{
    public static async Task<List<Leftover>> ScanAsync(InstalledProgram program, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var results = new List<Leftover>();
        string core = LeftoverRules.CoreName(program.Name);
        string productKey = LeftoverRules.Key(core);
        string pubCore = LeftoverRules.CorePublisher(program.Publisher);
        string pubKey = LeftoverRules.Key(pubCore);
        string? loc = program.InstallLocation is { } l && Directory.Exists(l) ? l : program.InstallLocation;

        var all = InstalledPrograms.Scan(includeSystem: true);

        // لو فيه برنامج تاني متثبت بنفس الاسم (نسخة تانية مثلًا Python 3.11 و 3.12) — المطابقة بالاسم مش كفاية:
        // كل حاجة اتلقت بالاسم بس بتنزل "محتمل" ومابتتعلمش تلقائي
        bool sameNameInstalled = all.Any(p => p.Id != program.Id && LeftoverRules.Key(LeftoverRules.CoreName(p.Name)) == productKey);

        // أماكن البرامج التانية اللي لسه متثبتة — ممنوع نلمسها
        var others = all
            .Where(p => p.Id != program.Id && !string.IsNullOrEmpty(p.InstallLocation) && !p.InstallLocationGuessed)
            .Select(p => p.InstallLocation!.TrimEnd('\\'))
            .Where(p => p.Length > 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        AppLogger.Info("Uninstall", $"فحص بقايا \"{program.Name}\" — الاسم: \"{core}\" الناشر: \"{pubCore}\" المكان: {loc ?? "غير معروف"}");

        progress?.Report(new ModuleProgress(5, "الفولدرات..."));
        await Task.Run(() => ScanFolders(program, loc, productKey, pubKey, others, results, ct), ct).ConfigureAwait(false);

        progress?.Report(new ModuleProgress(45, "الاختصارات..."));
        await Task.Run(() => ScanShortcuts(loc, productKey, results, ct), ct).ConfigureAwait(false);

        progress?.Report(new ModuleProgress(60, "الريجستري..."));
        ScanRegistry(program, loc, productKey, pubKey, results);

        progress?.Report(new ModuleProgress(75, "الخدمات..."));
        ScanServices(loc, results);

        progress?.Report(new ModuleProgress(85, "المهام المجدولة..."));
        await ScanTasksAsync(loc, productKey, results, ct).ConfigureAwait(false);

        // لو فولدر اتضاف، الفولدرات والملفات اللي جواه مالهاش لازمة كعناصر منفصلة
        var folders = results.Where(r => r.Kind == LeftoverKind.Folder).Select(r => r.Path).ToList();
        results.RemoveAll(r => r.Kind is LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut &&
                               folders.Any(f => !f.Equals(r.Path, StringComparison.OrdinalIgnoreCase) && LeftoverRules.IsInside(r.Path, f)));

        // تكرار نفس المسار من أكتر من قاعدة: نسيب الأعلى ثقة
        var unique = results
            .GroupBy(r => (r.Kind, r.Path.ToLowerInvariant()))
            .Select(g => g.OrderBy(r => r.Confidence).First())
            .ToList();

        if (sameNameInstalled)
        {
            AppLogger.Info("Uninstall", "فيه برنامج تاني متثبت بنفس الاسم — المطابقات بالاسم اتحولت لـ محتمل");
            unique = unique.Select(r => r.Confidence == LeftoverConfidence.High && r.Reason.Contains("اسم")
                ? new Leftover
                {
                    Kind = r.Kind, Path = r.Path, Confidence = LeftoverConfidence.Medium, Reason = r.Reason + " (فيه نسخة تانية متثبتة بنفس الاسم)",
                    SizeBytes = r.SizeBytes, Hive = r.Hive, KeyPath = r.KeyPath, ValueName = r.ValueName,
                    ServiceName = r.ServiceName, TaskPath = r.TaskPath, TaskName = r.TaskName
                }
                : r).ToList();
        }

        foreach (var r in unique) r.Selected = r.Confidence == LeftoverConfidence.High;

        progress?.Report(new ModuleProgress(100, $"لقيت {unique.Count} عنصر"));
        AppLogger.Info("Uninstall", $"بقايا \"{program.Name}\": {unique.Count} عنصر ({unique.Count(r => r.Confidence == LeftoverConfidence.High)} مؤكد)");
        return unique.OrderBy(r => r.Confidence).ThenBy(r => r.Kind).ThenBy(r => r.Path).ToList();
    }

    // ------------------------------------------------------------------ الفولدرات

    private static bool TouchesOther(string path, List<string> others) =>
        others.Any(o => LeftoverRules.IsInside(o, path) || LeftoverRules.IsInside(path, o));

    private static void AddFolder(List<Leftover> results, string path, LeftoverConfidence conf, string reason, List<string> others, CancellationToken ct, bool userData = false)
    {
        if (!Directory.Exists(path) || !LeftoverRules.IsSafeFolder(path) || TouchesOther(path, others)) return;
        if (userData)
        {
            // بيانات المستخدم (بروفايلات، إعدادات، ملفات محفوظة) عمرها ما تتعلّم تلقائي — المستخدم يقرر
            conf = LeftoverConfidence.Medium;
            reason += " — بيانات مستخدم (إعدادات/ملفات شخصية)";
        }
        var totals = new ScanTotals();
        try { FileCleaner.Scan(path, TimeSpan.Zero, totals, ct); } catch (OperationCanceledException) { throw; } catch { }
        results.Add(new Leftover { Kind = LeftoverKind.Folder, Path = path.TrimEnd('\\'), Confidence = conf, Reason = reason, SizeBytes = totals.Bytes });
    }

    private static void ScanFolders(InstalledProgram program, string? loc, string productKey, string pubKey, List<string> others, List<Leftover> results, CancellationToken ct)
    {
        if (loc is not null && Directory.Exists(loc))
        {
            if (program.InstallLocationGuessed)
                AddFolder(results, loc, LeftoverConfidence.Medium, "مكان التثبيت (مستنتج من الأيقونة/برنامج الإلغاء)", others, ct);
            else
                AddFolder(results, loc, LeftoverConfidence.High, "مكان تثبيت البرنامج نفسه", others, ct);
        }

        // (الجذر، هل هو بيانات مستخدم؟) — "Programs" تحت LocalAppData مكان تثبيت مش بيانات، وقايمة Start اختصارات
        var roots = new List<(string Path, bool UserData)>
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), false),
            (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), false),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), false),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), false)
        };
        foreach (var prof in UserProfiles.GetProfiles(true))
        {
            roots.Add((prof.RoamingAppData, true));
            roots.Add((prof.LocalAppData, true));
            roots.Add((Path.Combine(prof.Path, "AppData", "LocalLow"), true));
            roots.Add((Path.Combine(prof.LocalAppData, "Programs"), false));
            roots.Add((Path.Combine(prof.RoamingAppData, @"Microsoft\Windows\Start Menu\Programs"), false));
        }

        foreach (var (root, userData) in roots.Where(r => !string.IsNullOrEmpty(r.Path) && Directory.Exists(r.Path)).DistinctBy(r => r.Path.ToLowerInvariant()))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var dir in SafeDirs(root))
            {
                string name = Path.GetFileName(dir);
                if (LeftoverRules.ExactMatch(name, productKey) && !LeftoverRules.IsShared(name))
                {
                    AddFolder(results, dir, LeftoverConfidence.High, $"اسم الفولدر = اسم البرنامج ({root})", others, ct, userData);
                }
                else if (pubKey.Length >= 3 && LeftoverRules.Key(name) == pubKey)
                {
                    // فولدر الناشر: بنبص جواه على فولدر البرنامج بس — فولدر الناشر نفسه ممكن يكون فيه برامج تانية
                    foreach (var sub in SafeDirs(dir))
                    {
                        string subName = Path.GetFileName(sub);
                        string subKey = LeftoverRules.Key(subName);
                        if (LeftoverRules.ExactMatch(subName, productKey) || (pubKey + subKey) == productKey)
                            AddFolder(results, sub, LeftoverConfidence.High, "فولدر باسم البرنامج جوه فولدر الناشر", others, ct, userData);
                        else if (!LeftoverRules.IsShared(subName) &&
                                 ((subKey.Length >= 4 && productKey.Contains(subKey)) || (subKey.Length >= 3 && productKey.StartsWith(subKey))))
                            AddFolder(results, sub, LeftoverConfidence.Medium, "فولدر جوه فولدر الناشر واسمه جزء من اسم البرنامج", others, ct, userData);
                    }
                }
                else if (LeftoverRules.ContainsMatch(name, productKey) && !LeftoverRules.IsShared(name))
                {
                    AddFolder(results, dir, LeftoverConfidence.Medium, "اسم الفولدر بيحتوي اسم البرنامج", others, ct, userData);
                }
            }
        }
    }

    private static IEnumerable<string> SafeDirs(string path)
    {
        try
        {
            return Directory.GetDirectories(path).Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0).ToArray();
        }
        catch
        {
            return [];
        }
    }

    // ------------------------------------------------------------------ الاختصارات

    private static void ScanShortcuts(string? loc, string productKey, List<Leftover> results, CancellationToken ct)
    {
        var dirs = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        };
        foreach (var prof in UserProfiles.GetProfiles(true))
        {
            dirs.Add(Path.Combine(prof.Path, "Desktop"));
            dirs.Add(Path.Combine(prof.RoamingAppData, @"Microsoft\Windows\Start Menu\Programs"));
            dirs.Add(Path.Combine(prof.RoamingAppData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"));
        }

        foreach (var dir in dirs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> files;
            try { files = Directory.GetFiles(dir, "*.lnk"); } catch { continue; }
            foreach (var lnk in files)
            {
                ct.ThrowIfCancellationRequested();
                string? target = Startup.ShortcutResolver.Resolve(lnk, out _);
                bool pointsInside = loc is not null && target is not null && LeftoverRules.IsInside(target, loc);
                bool brokenSameName = target is not null && !File.Exists(target) && !Directory.Exists(target) &&
                                      LeftoverRules.ExactMatch(Path.GetFileNameWithoutExtension(lnk), productKey);
                if (pointsInside)
                    results.Add(new Leftover { Kind = LeftoverKind.Shortcut, Path = lnk, Confidence = LeftoverConfidence.High, Reason = "بيشاور جوه مكان تثبيت البرنامج" });
                else if (brokenSameName)
                    results.Add(new Leftover { Kind = LeftoverKind.Shortcut, Path = lnk, Confidence = LeftoverConfidence.High, Reason = "اختصار مكسور بنفس اسم البرنامج" });
            }
        }
    }

    // ------------------------------------------------------------------ الريجستري

    private static void ScanRegistry(InstalledProgram program, string? loc, string productKey, string pubKey, List<Leftover> results)
    {
        // مفتاح الإلغاء نفسه لو لسه موجود (البرنامج اتشال بس سايب مدخله في "البرامج والميزات")
        if (program.StillRegistered())
            results.Add(new Leftover
            {
                Kind = LeftoverKind.RegistryKey, Path = program.RegistryDisplayPath, Confidence = LeftoverConfidence.High,
                Reason = "مدخل البرنامج في قايمة البرامج المتثبتة", Hive = program.Hive, KeyPath = program.UninstallKeyPath
            });

        var softwareRoots = new (RegistryKey Hive, string Path, string Label)[]
        {
            (Registry.LocalMachine, "SOFTWARE", "HKLM"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node", "HKLM"),
            (Registry.CurrentUser, "SOFTWARE", "HKCU")
        };

        foreach (var (hive, path, label) in softwareRoots)
        {
            try
            {
                using var root = hive.OpenSubKey(path);
                if (root is null) continue;
                foreach (var name in root.GetSubKeyNames())
                {
                    if (LeftoverRules.IsShared(name)) continue;
                    if (LeftoverRules.ExactMatch(name, productKey))
                    {
                        AddKey(results, hive, $@"{path}\{name}", label, LeftoverConfidence.High, "مفتاح باسم البرنامج");
                    }
                    else if (pubKey.Length >= 3 && LeftoverRules.Key(name) == pubKey)
                    {
                        using var pub = root.OpenSubKey(name);
                        if (pub is null) continue;
                        foreach (var sub in pub.GetSubKeyNames())
                        {
                            string subKey = LeftoverRules.Key(sub);
                            if (LeftoverRules.ExactMatch(sub, productKey) || (pubKey + subKey) == productKey)
                                AddKey(results, hive, $@"{path}\{name}\{sub}", label, LeftoverConfidence.High, "مفتاح باسم البرنامج جوه مفتاح الناشر");
                        }
                    }
                    else if (LeftoverRules.ContainsMatch(name, productKey))
                    {
                        AddKey(results, hive, $@"{path}\{name}", label, LeftoverConfidence.Medium, "اسم المفتاح بيحتوي اسم البرنامج");
                    }
                }
            }
            catch
            {
                // مفتاح محمي
            }
        }

        // قيم Run اللي بتشغّل حاجة من مكان البرنامج
        if (loc is null) return;
        var runKeys = new (RegistryKey Hive, string Path, string Label)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU")
        };
        foreach (var (hive, path, label) in runKeys)
        {
            try
            {
                using var k = hive.OpenSubKey(path);
                if (k is null) continue;
                foreach (var v in k.GetValueNames())
                {
                    string? exe = Startup.CommandLineParser.ExtractExecutable(k.GetValue(v)?.ToString());
                    if (exe is not null && LeftoverRules.IsInside(exe, loc))
                        results.Add(new Leftover
                        {
                            Kind = LeftoverKind.RegistryValue, Path = $@"{label}\{path} ← {v}", Confidence = LeftoverConfidence.High,
                            Reason = "تشغيل تلقائي لملف من مكان البرنامج", Hive = hive, KeyPath = path, ValueName = v
                        });
                }
            }
            catch { /* ignore */ }
        }
    }

    private static void AddKey(List<Leftover> results, RegistryKey hive, string keyPath, string label, LeftoverConfidence conf, string reason)
    {
        if (hive == Registry.CurrentUser)
        {
            // إعدادات المستخدم: مابتتعلّمش تلقائي
            conf = LeftoverConfidence.Medium;
            reason += " — إعدادات المستخدم";
        }
        // حماية: مفيش مفتاح على عمق أقل من SOFTWARE\X
        if (keyPath.Count(c => c == '\\') < 1) return;
        results.Add(new Leftover { Kind = LeftoverKind.RegistryKey, Path = $@"{label}\{keyPath}", Confidence = conf, Reason = reason, Hive = hive, KeyPath = keyPath });
    }

    // ------------------------------------------------------------------ الخدمات

    private static void ScanServices(string? loc, List<Leftover> results)
    {
        if (loc is null) return;
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null) return;
            foreach (var name in services.GetSubKeyNames())
            {
                try
                {
                    using var svc = services.OpenSubKey(name);
                    string image = svc?.GetValue("ImagePath", "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
                    string? exe = Startup.CommandLineParser.ExtractExecutable(Startup.CommandLineParser.NormalizeSystemPath(image));
                    if (exe is not null && LeftoverRules.IsInside(exe, loc))
                        results.Add(new Leftover
                        {
                            Kind = LeftoverKind.Service, Path = $"{name} — {exe}", Confidence = LeftoverConfidence.High,
                            Reason = "خدمة ملفها جوه مكان البرنامج", ServiceName = name
                        });
                }
                catch { /* محمي */ }
            }
        }
        catch { /* ignore */ }
    }

    // ------------------------------------------------------------------ المهام المجدولة

    private sealed class TaskDto
    {
        public string? TaskName { get; set; }
        public string? TaskPath { get; set; }
        public string? Execute { get; set; }
    }

    private static async Task ScanTasksAsync(string? loc, string productKey, List<Leftover> results, CancellationToken ct)
    {
        const string script =
            "Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\\Microsoft\\*' } | ForEach-Object { " +
            "[pscustomobject]@{ TaskName = $_.TaskName; TaskPath = $_.TaskPath; Execute = [string](($_.Actions | Select-Object -First 1).Execute) } }";
        var tasks = await PowerShell.RunJsonListAsync<TaskDto>(script, ct, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        foreach (var t in tasks)
        {
            if (string.IsNullOrEmpty(t.TaskName) || string.IsNullOrEmpty(t.TaskPath)) continue;
            if (t.TaskPath.StartsWith(@"\AJYAL PC Care", StringComparison.OrdinalIgnoreCase)) continue;
            string exe = Startup.CommandLineParser.NormalizeSystemPath(t.Execute?.Trim('"')) ?? "";
            bool inside = loc is not null && exe.Length > 0 && LeftoverRules.IsInside(exe, loc);
            bool named = LeftoverRules.ContainsMatch(t.TaskName, productKey);
            if (!inside && !named) continue;
            results.Add(new Leftover
            {
                Kind = LeftoverKind.ScheduledTask, Path = t.TaskPath + t.TaskName,
                Confidence = inside ? LeftoverConfidence.High : LeftoverConfidence.Medium,
                Reason = inside ? "مهمة بتشغّل ملف من مكان البرنامج" : "اسم المهمة بيحتوي اسم البرنامج",
                TaskPath = t.TaskPath, TaskName = t.TaskName
            });
        }
    }
}
