using Microsoft.Win32;
using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Safety;
using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Uninstall;

public sealed record RemovalItemResult(Leftover Item, bool Success, string Message);

/// <summary>
/// إزالة البقايا المختارة — كل حاجة قابلة للرجوع:
///   فولدرات/ملفات/اختصارات ← سلة المحذوفات (مش مسح نهائي)
///   مفاتيح/قيم ريجستري    ← نسخة .reg قبل المسح + سجل تراجع (reg import)
///   خدمات                ← نسخة .reg لمفتاح الخدمة + إيقاف + sc delete (الرجوع: reg import ثم Restart)
///   مهام مجدولة           ← نسخة XML + Unregister (الرجوع: Register-ScheduledTask)
/// الملفات المقفولة أو المستخدمة بتتخطى وتتسجل — الأداة مابتقفلش برامج.
/// </summary>
public static class LeftoverRemover
{
    public static async Task<List<RemovalItemResult>> RemoveAsync(InstalledProgram program, IReadOnlyList<Leftover> items, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var results = new List<RemovalItemResult>();
        string backupDir = Path.Combine(AppEnvironment.DataDirectory, "backups", "uninstall",
            $"{DateTime.Now:yyyyMMdd_HHmmss}_{string.Concat(LeftoverRules.CoreName(program.Name).Split(Path.GetInvalidFileNameChars()))}");
        Directory.CreateDirectory(backupDir);

        // الترتيب: الخدمات والمهام الأول (عشان ماتقفلش ملفات)، بعدين الريجستري، وفي الآخر الملفات
        var ordered = items.Where(i => i.Selected)
            .OrderBy(i => i.Kind switch
            {
                LeftoverKind.Service => 0,
                LeftoverKind.ScheduledTask => 1,
                LeftoverKind.RegistryValue => 2,
                LeftoverKind.RegistryKey => 3,
                _ => 4
            }).ToList();

        int n = 0;
        foreach (var item in ordered)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new ModuleProgress(n * 100 / Math.Max(1, ordered.Count), $"إزالة: {item.Path}"));
            RemovalItemResult r;
            try
            {
                r = item.Kind switch
                {
                    LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut => Recycle(item),
                    LeftoverKind.RegistryKey => await RemoveKeyAsync(program, item, backupDir, n, ct).ConfigureAwait(false),
                    LeftoverKind.RegistryValue => await RemoveValueAsync(program, item, backupDir, n, ct).ConfigureAwait(false),
                    LeftoverKind.Service => await RemoveServiceAsync(program, item, backupDir, n, ct).ConfigureAwait(false),
                    LeftoverKind.ScheduledTask => await RemoveTaskAsync(program, item, backupDir, n, ct).ConfigureAwait(false),
                    _ => new RemovalItemResult(item, false, "نوع غير مدعوم")
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                r = new RemovalItemResult(item, false, ex.Message);
            }
            results.Add(r);
            AppLogger.Operation("Uninstall", $"إزالة بقايا [{item.KindDisplay}] {item.Path}", r.Success, r.Success ? item.SizeBytes : 0, r.Message);
            n++;
        }

        progress?.Report(new ModuleProgress(100, "تم"));
        return results;
    }

    // ------------------------------------------------------------------ ملفات

    private static RemovalItemResult Recycle(Leftover item)
    {
        bool exists = item.Kind == LeftoverKind.Folder ? Directory.Exists(item.Path) : File.Exists(item.Path);
        if (!exists) return new RemovalItemResult(item, true, "اتشال بالفعل");
        if (item.Kind == LeftoverKind.Folder && !LeftoverRules.IsSafeFolder(item.Path))
            return new RemovalItemResult(item, false, "مسار محمي — اترفض");

        var res = RecycleBinApi.SendToRecycleBin(item.Path);
        if (!res.Success) return new RemovalItemResult(item, false, res.Message);

        ChangeJournal.Add(new ChangeRecord
        {
            Module = "Uninstall",
            Action = $"نقل {item.KindDisplay} لسلة المحذوفات",
            Target = item.Path,
            Description = "الرجوع: من سلة المحذوفات ← استعادة",
            UndoKind = UndoKind.None
        });
        return new RemovalItemResult(item, true, "اتنقل لسلة المحذوفات");
    }

    // ------------------------------------------------------------------ ريجستري

    private static string HiveLabel(RegistryKey? hive) => hive == Registry.CurrentUser ? "HKCU" : "HKLM";

    /// <summary>reg export بالمسار الحرفي (/reg:64 عشان WOW6432Node يتكتب زي ما هو من غير تحويل).</summary>
    private static async Task<string?> ExportAsync(string regPath, string file, CancellationToken ct)
    {
        string reg = Path.Combine(Environment.SystemDirectory, "reg.exe");
        var o = await ProcessRunner.RunAsync(reg, $"export \"{regPath}\" \"{file}\" /y /reg:64", ct, timeout: TimeSpan.FromMinutes(2), outputEncoding: ConsoleEncoding.Oem).ConfigureAwait(false);
        return o.Success && File.Exists(file) ? file : null;
    }

    private static async Task<RemovalItemResult> RemoveKeyAsync(InstalledProgram program, Leftover item, string backupDir, int n, CancellationToken ct)
    {
        if (item.Hive is null || string.IsNullOrEmpty(item.KeyPath)) return new RemovalItemResult(item, false, "بيانات المفتاح ناقصة");
        int slash = item.KeyPath.LastIndexOf('\\');
        if (slash <= 0) return new RemovalItemResult(item, false, "مفتاح جذري — اترفض");

        using (var exists = item.Hive.OpenSubKey(item.KeyPath))
            if (exists is null) return new RemovalItemResult(item, true, "اتشال بالفعل");

        string regPath = $@"{HiveLabel(item.Hive)}\{item.KeyPath}";
        string? backup = await ExportAsync(regPath, Path.Combine(backupDir, $"{n:000}_key.reg"), ct).ConfigureAwait(false);
        if (backup is null) return new RemovalItemResult(item, false, "تعذر أخذ نسخة احتياطية من المفتاح — مااتمسحش");

        using (var parent = item.Hive.OpenSubKey(item.KeyPath[..slash], writable: true))
        {
            if (parent is null) return new RemovalItemResult(item, false, "تعذر فتح المفتاح الأب للكتابة");
            parent.DeleteSubKeyTree(item.KeyPath[(slash + 1)..], throwOnMissingSubKey: false);
        }

        JournalRegistry(program, item, backup, "مسح مفتاح ريجستري");
        return new RemovalItemResult(item, true, "اتمسح (ومعاه نسخة .reg للرجوع)");
    }

    private static async Task<RemovalItemResult> RemoveValueAsync(InstalledProgram program, Leftover item, string backupDir, int n, CancellationToken ct)
    {
        if (item.Hive is null || item.KeyPath is null || item.ValueName is null) return new RemovalItemResult(item, false, "بيانات القيمة ناقصة");
        string regPath = $@"{HiveLabel(item.Hive)}\{item.KeyPath}";
        // النسخة بتاعة المفتاح كله (reg import بيرجّع القيمة ومابيمسحش القيم اللي اتضافت بعدها)
        string? backup = await ExportAsync(regPath, Path.Combine(backupDir, $"{n:000}_value.reg"), ct).ConfigureAwait(false);
        if (backup is null) return new RemovalItemResult(item, false, "تعذر أخذ نسخة احتياطية — مااتمسحش");

        using var k = item.Hive.OpenSubKey(item.KeyPath, writable: true);
        if (k is null) return new RemovalItemResult(item, true, "اتشال بالفعل");
        k.DeleteValue(item.ValueName, throwOnMissingValue: false);
        JournalRegistry(program, item, backup, "مسح قيمة تشغيل تلقائي");
        return new RemovalItemResult(item, true, "اتمسحت (ومعاها نسخة .reg للرجوع)");
    }

    private static void JournalRegistry(InstalledProgram program, Leftover item, string backup, string action) =>
        ChangeJournal.Add(new ChangeRecord
        {
            Module = "Uninstall",
            Action = action,
            Target = item.Path,
            Description = $"بقايا {program.Name} — نسخة احتياطية: {backup}",
            UndoKind = UndoKind.RegistryImport,
            UndoData = { ["File"] = backup }
        });

    // ------------------------------------------------------------------ خدمات

    private static async Task<RemovalItemResult> RemoveServiceAsync(InstalledProgram program, Leftover item, string backupDir, int n, CancellationToken ct)
    {
        string name = item.ServiceName!;
        string? backup = await ExportAsync($@"HKLM\SYSTEM\CurrentControlSet\Services\{name}", Path.Combine(backupDir, $"{n:000}_service.reg"), ct).ConfigureAwait(false);
        if (backup is null) return new RemovalItemResult(item, false, "تعذر أخذ نسخة من إعدادات الخدمة — مااتمسحتش");

        if (await ServiceControl.QueryStateAsync(name, ct).ConfigureAwait(false) == ServiceState.Running)
            await ServiceControl.StopAsync(name, ct).ConfigureAwait(false);

        string sc = Path.Combine(Environment.SystemDirectory, "sc.exe");
        var o = await ProcessRunner.RunAsync(sc, $"delete \"{name}\"", ct, timeout: TimeSpan.FromSeconds(30), outputEncoding: ConsoleEncoding.Oem).ConfigureAwait(false);
        if (!o.Success) return new RemovalItemResult(item, false, "sc delete فشل: " + o.StdOut.Trim());

        ChangeJournal.Add(new ChangeRecord
        {
            Module = "Uninstall",
            Action = "مسح خدمة",
            Target = name,
            Description = $"بقايا {program.Name} — الرجوع بيرجّع الخدمة بعد إعادة تشغيل الجهاز. نسخة: {backup}",
            UndoKind = UndoKind.RegistryImport,
            UndoData = { ["File"] = backup }
        });
        return new RemovalItemResult(item, true, "اتمسحت (هتختفي نهائي بعد إعادة التشغيل لو كانت شغالة)");
    }

    // ------------------------------------------------------------------ مهام مجدولة

    private static async Task<RemovalItemResult> RemoveTaskAsync(InstalledProgram program, Leftover item, string backupDir, int n, CancellationToken ct)
    {
        string xmlFile = Path.Combine(backupDir, $"{n:000}_task.xml");
        string script =
            $"$t = Get-ScheduledTask -TaskPath {PowerShell.Quote(item.TaskPath!)} -TaskName {PowerShell.Quote(item.TaskName!)} -ErrorAction Stop; " +
            $"Export-ScheduledTask -TaskPath {PowerShell.Quote(item.TaskPath!)} -TaskName {PowerShell.Quote(item.TaskName!)} | Out-File -FilePath {PowerShell.Quote(xmlFile)} -Encoding Unicode; " +
            $"Unregister-ScheduledTask -TaskPath {PowerShell.Quote(item.TaskPath!)} -TaskName {PowerShell.Quote(item.TaskName!)} -Confirm:$false -ErrorAction Stop; 'OK'";
        var o = await PowerShell.RunAsync(script, ct, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        if (!o.Success || !o.StdOut.Contains("OK")) return new RemovalItemResult(item, false, "فشل: " + o.StdErr.Trim());

        ChangeJournal.Add(new ChangeRecord
        {
            Module = "Uninstall",
            Action = "مسح مهمة مجدولة",
            Target = item.Path,
            Description = $"بقايا {program.Name} — نسخة XML: {xmlFile}",
            UndoKind = UndoKind.ScheduledTaskXml,
            UndoData = { ["File"] = xmlFile, ["TaskPath"] = item.TaskPath!, ["TaskName"] = item.TaskName! }
        });
        return new RemovalItemResult(item, true, "اتمسحت (ومعاها نسخة XML للرجوع)");
    }

    // ------------------------------------------------------------------ التراجع

    public static async Task<bool> UndoRegistryImportAsync(string file, CancellationToken ct)
    {
        if (!File.Exists(file)) return false;
        string reg = Path.Combine(Environment.SystemDirectory, "reg.exe");
        var o = await ProcessRunner.RunAsync(reg, $"import \"{file}\" /reg:64", ct, timeout: TimeSpan.FromMinutes(2), outputEncoding: ConsoleEncoding.Oem).ConfigureAwait(false);
        return o.Success;
    }

    public static async Task<bool> UndoScheduledTaskAsync(string file, string taskPath, string taskName, CancellationToken ct)
    {
        if (!File.Exists(file)) return false;
        var o = await PowerShell.RunAsync(
            $"Register-ScheduledTask -Xml (Get-Content -Raw -LiteralPath {PowerShell.Quote(file)}) -TaskPath {PowerShell.Quote(taskPath)} -TaskName {PowerShell.Quote(taskName)} -Force -ErrorAction Stop | Out-Null; 'OK'",
            ct, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        return o.Success && o.StdOut.Contains("OK");
    }
}
