using System.Diagnostics;
using System.Text.RegularExpressions;
using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Uninstall;

public sealed record UninstallOutcome(bool Started, int? ExitCode, bool StillRegistered, TimeSpan Duration, string Message)
{
    /// <summary>
    /// أكواد Windows Installer: 0 نجاح، 1605 مش متثبت أصلًا، 3010/1641 نجاح ومحتاج Restart، 1602 المستخدم لغى.
    /// برامج الإلغاء العادية: غير 0 مش بالضرورة فشل (كتير منها بيرجع أكواد عشوائية) — المرجع الحقيقي: هل لسه متسجل؟
    /// </summary>
    public bool Cancelled => ExitCode == 1602;
    public bool NeedsRestart => ExitCode is 3010 or 1641;
}

/// <summary>
/// تشغيل برنامج الإلغاء الرسمي للبرنامج والاستنى لحد ما يخلص فعلًا.
/// مشكلة معروفة: برامج إلغاء كتير (NSIS / Inno / InstallShield) بتنسخ نفسها لـ Temp وتقفل الأصلي فورًا —
/// فبعد ما الأصلي يخرج بنستنى أي بروسيس اتشغّل بعده من Temp أو باسم uninstaller معروف.
/// </summary>
public static partial class UninstallRunner
{
    [GeneratedRegex(@"\{[0-9A-Fa-f\-]{36}\}")]
    private static partial Regex GuidInText();

    public static async Task<UninstallOutcome> RunAsync(InstalledProgram p, bool quiet, IProgress<ModuleProgress>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (!p.HasUninstaller)
            return new UninstallOutcome(false, null, p.StillRegistered(), sw.Elapsed, "البرنامج مالوش برنامج إلغاء — استخدم الإزالة الإجبارية");

        (string file, string args) = BuildCommand(p, quiet);
        if (Path.IsPathRooted(file) && !File.Exists(file))
            return new UninstallOutcome(false, null, p.StillRegistered(), sw.Elapsed, $"برنامج الإلغاء مش موجود: {file} — استخدم الإزالة الإجبارية");

        AppLogger.Info("Uninstall", $"إلغاء تثبيت \"{p.Name}\" {p.Version} — {(quiet ? "صامت" : "عادي")}: {file} {args}");
        progress?.Report(new ModuleProgress(10, "شغّال برنامج الإلغاء الخاص بالبرنامج — كمّل خطواته في نافذته..."));

        DateTime launchedAt = DateTime.Now.AddSeconds(-1);
        Process proc;
        try
        {
            proc = Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = true,
                WorkingDirectory = File.Exists(file) ? Path.GetDirectoryName(file)! : Environment.SystemDirectory
            }) ?? throw new InvalidOperationException("Process.Start رجّع null");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Uninstall", "تعذر تشغيل برنامج الإلغاء", ex);
            return new UninstallOutcome(false, null, p.StillRegistered(), sw.Elapsed, "تعذر تشغيل برنامج الإلغاء: " + ex.Message);
        }

        int? exit = null;
        using (proc)
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            try { exit = proc.ExitCode; } catch { /* ignore */ }
        }

        progress?.Report(new ModuleProgress(60, "مستني برنامج الإلغاء يخلص..."));
        await WaitForHelpersAsync(launchedAt, ct).ConfigureAwait(false);

        // بعض برامج الإلغاء بتمسح مدخلها متأخر شوية
        bool still = p.StillRegistered();
        for (int i = 0; still && i < 5; i++)
        {
            await Task.Delay(1000, ct).ConfigureAwait(false);
            still = p.StillRegistered();
        }

        var outcome = new UninstallOutcome(true, exit, still, sw.Elapsed,
            exit == 1602 ? "اتلغى من نافذة برنامج الإلغاء"
            : still ? "برنامج الإلغاء خلص بس البرنامج لسه متسجل (ممكن يكون اتلغى أو فشل)"
            : exit is 3010 or 1641 ? "اتشال — ومحتاج إعادة تشغيل الجهاز"
            : "اتشال");
        AppLogger.Operation("Uninstall", $"إلغاء تثبيت {p.Name}", !still, 0, $"exit={exit} مدة={outcome.Duration:mm\\:ss} {outcome.Message}");
        progress?.Report(new ModuleProgress(100, outcome.Message));
        return outcome;
    }

    /// <summary>تكوين أمر الإلغاء: MSI ← msiexec /x {كود}، غير كده UninstallString (أو Quiet لو مطلوب ومتاح).</summary>
    public static (string File, string Args) BuildCommand(InstalledProgram p, bool quiet)
    {
        string? code = p.MsiProductCode;
        if (code is null && p.UninstallString?.Contains("msiexec", StringComparison.OrdinalIgnoreCase) == true)
        {
            var m = GuidInText().Match(p.UninstallString);
            if (m.Success) code = m.Value;
        }
        if (code is not null && (p.IsMsi || p.UninstallString?.Contains("msiexec", StringComparison.OrdinalIgnoreCase) == true))
        {
            // /I في UninstallString بتاع MSI معناها "تعديل/إصلاح" مش إلغاء — دايمًا /x
            return ("msiexec.exe", quiet ? $"/x {code} /qn /norestart" : $"/x {code}");
        }

        string cmd = (quiet && !string.IsNullOrWhiteSpace(p.QuietUninstallString) ? p.QuietUninstallString : p.UninstallString)!.Trim();
        return Split(cmd);
    }

    private static (string, string) Split(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd);
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            if (end > 1) return (cmd[1..end], cmd[(end + 1)..].Trim());
        }
        // مسار من غير quotes وفيه مسافات: لحد أول .exe
        int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe > 0) return (cmd[..(exe + 4)], cmd[(exe + 4)..].Trim());
        int sp = cmd.IndexOf(' ');
        return sp > 0 ? (cmd[..sp], cmd[(sp + 1)..]) : (cmd, "");
    }

    // msiexec مش هنا عن قصد: خدمة Windows Installer بتفضل شغالة دقايق بعد أي عملية، وكنا هنستناها على الفاضي
    private static readonly string[] HelperPrefixes = ["au_", "un_", "unins", "uninst", "_iu", "_un", "setup"];

    /// <summary>نستنى أي بروسيس شكله برنامج إلغاء اتشغّل بعد ما بدأنا (لحد 30 دقيقة).</summary>
    private static async Task WaitForHelpersAsync(DateTime since, CancellationToken ct)
    {
        string temp = Path.GetTempPath().TrimEnd('\\');
        string winTemp = Path.Combine(AppEnvironment.WindowsDirectory, "Temp");
        var deadline = DateTime.Now.AddMinutes(30);
        int quietRounds = 0;
        while (DateTime.Now < deadline && quietRounds < 3)
        {
            ct.ThrowIfCancellationRequested();
            bool any = false;
            foreach (var proc in Process.GetProcesses())
            {
                using (proc)
                {
                    try
                    {
                        if (proc.Id == Environment.ProcessId) continue;
                        DateTime start;
                        try { start = proc.StartTime; } catch { continue; }
                        if (start < since) continue;
                        string name = proc.ProcessName.ToLowerInvariant();
                        string? path = Native.ProcessApi.GetImagePath(proc.Id);
                        bool fromTemp = path is not null && (LeftoverRules.IsInside(path, temp) || LeftoverRules.IsInside(path, winTemp));
                        if (fromTemp || HelperPrefixes.Any(name.StartsWith))
                        {
                            any = true;
                            break;
                        }
                    }
                    catch { /* البروسيس قفل */ }
                }
            }
            quietRounds = any ? 0 : quietRounds + 1;
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
    }
}
