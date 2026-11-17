using Microsoft.Win32;
using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Safety;

namespace AjyalPcCare.Core.Startup;

public sealed record ToggleResult(bool Success, string Message, RestorePointResult? RestorePoint = null);

/// <summary>
/// تفعيل/تعطيل عناصر الـ Startup بطرق قابلة للتراجع فقط:
/// - Run / مجلدات Startup: StartupApproved (نفس Task Manager) — المدخل الأصلي ما بيتمسحش.
/// - المهام المجدولة: Disable/Enable-ScheduledTask.
/// - الخدمات: تحويل لـ Manual (demand) مع حفظ النوع الأصلي — مش Disabled.
/// قبل أول تعديل في الجلسة: نقطة استعادة.
/// </summary>
public static class StartupManager
{
    public static async Task<ToggleResult> SetEnabledAsync(StartupEntry entry, bool enabled, CancellationToken ct, bool createRestorePoint = true)
    {
        if (!entry.CanToggle) return new ToggleResult(false, "العنصر ده للعرض فقط");
        if (entry.Enabled == enabled) return new ToggleResult(true, "الحالة مطابقة بالفعل");

        bool needsAdmin = entry.Source is not (StartupSource.RegistryRunUser or StartupSource.StartupFolderUser);
        if (needsAdmin && !AppEnvironment.IsAdministrator)
            return new ToggleResult(false, "تعديل العنصر ده يحتاج صلاحيات مسؤول");

        RestorePointResult? rp = null;
        if (createRestorePoint && AppEnvironment.IsAdministrator)
            rp = await RestorePointService.EnsureForSessionAsync("AJYAL PC Care - قبل تعديل Startup", ct).ConfigureAwait(false);

        try
        {
            ToggleResult result = entry.Source switch
            {
                StartupSource.RegistryRunUser => ToggleApproved(entry, Registry.CurrentUser, "Run", entry.Name, enabled),
                StartupSource.RegistryRunMachine => ToggleApproved(entry, Registry.LocalMachine, "Run", entry.Name, enabled),
                StartupSource.RegistryRunMachine32 => ToggleApproved(entry, Registry.LocalMachine, "Run32", entry.Name, enabled),
                StartupSource.StartupFolderUser => ToggleApproved(entry, Registry.CurrentUser, "StartupFolder", Path.GetFileName(entry.Location), enabled),
                StartupSource.StartupFolderCommon => ToggleApproved(entry, Registry.LocalMachine, "StartupFolder", Path.GetFileName(entry.Location), enabled),
                StartupSource.ScheduledTask => await ToggleTaskAsync(entry, enabled, ct).ConfigureAwait(false),
                StartupSource.Service => await ToggleServiceAsync(entry, enabled, ct).ConfigureAwait(false),
                _ => new ToggleResult(false, "مصدر غير مدعوم")
            };

            if (result.Success) entry.Enabled = enabled;
            AppLogger.Operation("Startup", $"{(enabled ? "تفعيل" : "تعطيل")} {entry.Name} [{entry.SourceDisplay}]", result.Success, 0, result.Message);
            return result with { RestorePoint = rp };
        }
        catch (Exception ex)
        {
            AppLogger.Error("Startup", $"فشل تعديل {entry.Name}", ex);
            return new ToggleResult(false, ex.Message, rp);
        }
    }

    private static ToggleResult ToggleApproved(StartupEntry entry, RegistryKey hive, string sub, string valueName, bool enabled)
    {
        byte[]? previous = StartupApproval.GetRaw(hive, sub, valueName);
        StartupApproval.Set(hive, sub, valueName, enabled);

        ChangeJournal.Add(new ChangeRecord
        {
            Module = "Startup",
            Action = enabled ? "تفعيل عنصر Startup" : "تعطيل عنصر Startup",
            Target = entry.Name,
            Description = $"{entry.SourceDisplay}: {entry.Command}",
            UndoKind = UndoKind.StartupApproved,
            UndoData =
            {
                ["Hive"] = hive == Registry.CurrentUser ? "HKCU" : "HKLM",
                ["Sub"] = sub,
                ["ValueName"] = valueName,
                ["Previous"] = previous is null ? "" : Convert.ToBase64String(previous)
            }
        });
        return new ToggleResult(true, enabled ? "تم التفعيل" : "تم التعطيل (المدخل الأصلي محفوظ — تقدر ترجّعه في أي وقت)");
    }

    private static async Task<ToggleResult> ToggleTaskAsync(StartupEntry entry, bool enabled, CancellationToken ct)
    {
        int slash = entry.Location.LastIndexOf('\\');
        string taskPath = entry.Location[..(slash + 1)];
        string taskName = entry.Location[(slash + 1)..];
        var o = await RunTaskToggleAsync(taskPath, taskName, enabled, ct).ConfigureAwait(false);
        if (!o.Success) return new ToggleResult(false, "فشل: " + o.StdErr.Trim());

        ChangeJournal.Add(new ChangeRecord
        {
            Module = "Startup",
            Action = enabled ? "تفعيل مهمة مجدولة" : "تعطيل مهمة مجدولة",
            Target = entry.Location,
            Description = entry.Command,
            UndoKind = UndoKind.ScheduledTask,
            UndoData = { ["TaskPath"] = taskPath, ["TaskName"] = taskName, ["PreviousEnabled"] = (!enabled).ToString() }
        });
        return new ToggleResult(true, enabled ? "تم تفعيل المهمة" : "تم تعطيل المهمة");
    }

    private static Task<ProcessOutput> RunTaskToggleAsync(string taskPath, string taskName, bool enabled, CancellationToken ct) =>
        PowerShell.RunAsync(
            $"{(enabled ? "Enable" : "Disable")}-ScheduledTask -TaskPath {PowerShell.Quote(taskPath)} -TaskName {PowerShell.Quote(taskName)} -ErrorAction Stop | Out-Null",
            ct, TimeSpan.FromMinutes(1));

    private static async Task<ToggleResult> ToggleServiceAsync(StartupEntry entry, bool enabled, CancellationToken ct)
    {
        string name = entry.Location;
        string current = ServiceControl.GetStartType(name) ?? "auto";
        string target;
        if (enabled)
        {
            // نرجّع النوع الأصلي المسجّل في السجل لو موجود
            var last = ChangeJournal.All.FirstOrDefault(r => r.UndoKind == UndoKind.ServiceStartType && r.Target.Equals(name, StringComparison.OrdinalIgnoreCase) && !r.Undone);
            target = last?.UndoData.GetValueOrDefault("Previous") ?? "auto";
        }
        else
        {
            target = "demand";
        }

        if (!await ServiceControl.SetStartTypeAsync(name, target, ct).ConfigureAwait(false))
            return new ToggleResult(false, "فشل تغيير نوع تشغيل الخدمة");

        if (enabled)
        {
            // التفعيل هنا هو عمليًا تراجع عن آخر تعطيل
            var lastDisable = ChangeJournal.All.FirstOrDefault(r => r.UndoKind == UndoKind.ServiceStartType && r.Target.Equals(name, StringComparison.OrdinalIgnoreCase) && !r.Undone);
            if (lastDisable is not null) ChangeJournal.MarkUndone(lastDisable.Id);
        }

        ChangeJournal.Add(new ChangeRecord
        {
            Module = "Startup",
            Action = enabled ? "إرجاع خدمة للتشغيل التلقائي" : "تحويل خدمة إلى Manual",
            Target = name,
            Description = entry.Name,
            UndoKind = UndoKind.ServiceStartType,
            UndoData = { ["Previous"] = current }
        });
        entry.ServiceStartType = target;
        return new ToggleResult(true, enabled ? $"الخدمة رجعت {target}" : "الخدمة اتحولت لـ Manual — هتشتغل لما برنامج يطلبها");
    }

    /// <summary>التراجع عن تغيير مسجل في السجل.</summary>
    public static async Task<ToggleResult> UndoAsync(ChangeRecord record, CancellationToken ct)
    {
        if (record.Undone) return new ToggleResult(false, "تم التراجع عن التغيير ده بالفعل");
        if (!record.Reversible) return new ToggleResult(false, "التغيير ده غير قابل للتراجع (مثل مسح الملفات)");

        try
        {
            ToggleResult r;
            switch (record.UndoKind)
            {
                case UndoKind.StartupApproved:
                {
                    var hive = record.UndoData["Hive"] == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                    string prev = record.UndoData.GetValueOrDefault("Previous") ?? "";
                    StartupApproval.SetRaw(hive, record.UndoData["Sub"], record.UndoData["ValueName"], prev.Length == 0 ? null : Convert.FromBase64String(prev));
                    r = new ToggleResult(true, "تم التراجع");
                    break;
                }
                case UndoKind.ScheduledTask:
                {
                    bool prevEnabled = bool.Parse(record.UndoData["PreviousEnabled"]);
                    var o = await RunTaskToggleAsync(record.UndoData["TaskPath"], record.UndoData["TaskName"], prevEnabled, ct).ConfigureAwait(false);
                    r = o.Success ? new ToggleResult(true, "تم التراجع") : new ToggleResult(false, o.StdErr.Trim());
                    break;
                }
                case UndoKind.ServiceStartType:
                {
                    bool ok = await ServiceControl.SetStartTypeAsync(record.Target, record.UndoData["Previous"], ct).ConfigureAwait(false);
                    r = ok ? new ToggleResult(true, "تم التراجع") : new ToggleResult(false, "فشل إرجاع نوع التشغيل");
                    break;
                }
                default:
                    r = new ToggleResult(false, "نوع غير مدعوم");
                    break;
            }

            if (r.Success) ChangeJournal.MarkUndone(record.Id);
            AppLogger.Operation("Journal", $"تراجع: {record.Action} — {record.Target}", r.Success, 0, r.Message);
            return r;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Journal", "فشل التراجع", ex);
            return new ToggleResult(false, ex.Message);
        }
    }
}
