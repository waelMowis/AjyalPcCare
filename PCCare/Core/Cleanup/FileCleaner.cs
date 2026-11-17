using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Cleanup;

public sealed class ScanTotals
{
    public long Bytes;
    public int Files;
}

public sealed class DeleteTotals
{
    public long BytesFreed;
    public int Deleted;
    public int Skipped;
    public readonly List<string> SkippedSamples = [];
}

/// <summary>
/// فحص ومسح ملفات بأمان:
/// - ما بيتبعش Junctions / Symlinks (ممكن توديه لمكان تاني خالص على الديسك).
/// - أي ملف مقفول أو ممنوع بيتخطى ويتسجل — من غير ما الأداة تقع.
/// - حارس مسارات: يرفض أي جذر خطير (Prefetch، Downloads، Documents، جذر الدرايف...).
/// </summary>
public static class FileCleaner
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline,
        ReturnSpecialDirectories = false
    };

    public static bool IsSafeRoot(string path, IReadOnlyCollection<string>? excluded = null)
    {
        try
        {
            string full = Path.GetFullPath(path).TrimEnd('\\');
            if (full.Length <= 3) return false; // جذر درايف

            string win = AppEnvironment.WindowsDirectory.TrimEnd('\\');
            string[] forbiddenExact =
            [
                win,
                Path.Combine(win, "System32"),
                Path.Combine(win, "SysWOW64"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            ];
            if (forbiddenExact.Any(f => !string.IsNullOrEmpty(f) && full.Equals(f.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                return false;

            // ممنوع نهائيًا: Prefetch وملفات المستخدم
            string[] forbiddenSegments = [@"\Prefetch", @"\Downloads", @"\Documents", @"\Desktop", @"\Pictures", @"\Videos", @"\Music", @"\OneDrive"];
            if (forbiddenSegments.Any(s => full.Contains(s + @"\", StringComparison.OrdinalIgnoreCase) || full.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                return false;

            if (excluded is not null && excluded.Any(e => !string.IsNullOrWhiteSpace(e) &&
                    full.StartsWith(Path.GetFullPath(e).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<FileInfo> EnumerateFiles(string root, string pattern = "*")
    {
        if (!Directory.Exists(root)) yield break;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, pattern, Options);
        }
        catch
        {
            yield break;
        }

        using var e = files.GetEnumerator();
        while (true)
        {
            FileInfo? fi = null;
            try
            {
                if (!e.MoveNext()) break;
                fi = new FileInfo(e.Current);
            }
            catch
            {
                continue;
            }
            if (fi is not null) yield return fi;
        }
    }

    public static bool IsOldEnough(FileInfo f, TimeSpan minAge)
    {
        if (minAge <= TimeSpan.Zero) return true;
        try
        {
            var last = f.LastWriteTimeUtc > f.CreationTimeUtc ? f.LastWriteTimeUtc : f.CreationTimeUtc;
            return DateTime.UtcNow - last >= minAge;
        }
        catch
        {
            return false;
        }
    }

    public static void Scan(string root, TimeSpan minAge, ScanTotals totals, CancellationToken ct, string pattern = "*")
    {
        foreach (var f in EnumerateFiles(root, pattern))
        {
            ct.ThrowIfCancellationRequested();
            if (!IsOldEnough(f, minAge)) continue;
            try
            {
                totals.Bytes += f.Length;
                totals.Files++;
            }
            catch { /* اتمسح أثناء الفحص */ }
        }
    }

    public static void Delete(string root, TimeSpan minAge, DeleteTotals totals, CancellationToken ct, string pattern = "*", bool removeEmptyDirs = true)
    {
        if (!IsSafeRoot(root, SettingsStore.Current.Cleanup.ExcludedPaths))
        {
            AppLogger.Warn("Cleanup", "تم رفض مسار غير آمن: " + root);
            return;
        }

        foreach (var f in EnumerateFiles(root, pattern).ToList())
        {
            ct.ThrowIfCancellationRequested();
            if (!IsOldEnough(f, minAge)) continue;
            DeleteFile(f, totals);
        }

        if (removeEmptyDirs) RemoveEmptyDirectories(root);
    }

    public static bool DeleteFile(FileInfo f, DeleteTotals totals)
    {
        long size = 0;
        try
        {
            size = f.Length;
            if (f.IsReadOnly) f.IsReadOnly = false;
            f.Delete();
            totals.BytesFreed += size;
            totals.Deleted++;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            totals.Skipped++;
            if (totals.SkippedSamples.Count < 50)
                totals.SkippedSamples.Add($"{f.FullName} ({(ex is IOException ? "مقفول/مستخدم" : "ممنوع")})");
            return false;
        }
    }

    /// <summary>يمسح الفولدرات الفاضية تحت الجذر (من الأعمق للأعلى) — الجذر نفسه ما بيتمسحش.</summary>
    public static void RemoveEmptyDirectories(string root)
    {
        try
        {
            var dirs = Directory.EnumerateDirectories(root, "*", Options)
                .OrderByDescending(d => d.Length)
                .ToList();
            foreach (var d in dirs)
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d, false);
                }
                catch { /* مستخدم */ }
            }
        }
        catch { /* ignore */ }
    }
}
