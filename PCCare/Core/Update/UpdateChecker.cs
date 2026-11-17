using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Update;

/// <summary>
/// التحديث التلقائي من GitHub Releases (نفس طريقة «مسار»):
/// آخر إصدار ← مقارنة بالحالي ← تنزيل المثبّت والتأكد منه (نفس المستودع + الحجم + sha256) ← تشغيله.
/// المستودع بيتكتب جوه البرنامج وقت البناء (PcGitHubRepo في الـ csproj)، والإعدادات تقدر تغيّره.
/// </summary>
public static class UpdateChecker
{
    public sealed record Release(
        Version Version,
        string Tag,
        string Url,
        string Notes,
        string? SetupUrl,
        string? SetupName,
        long SetupSize,
        string? SetupSha256);

    private const string UserAgent = "AjyalPcCare-Updater";

    /// <summary>المستودع المكتوب جوه البرنامج وقت البناء.</summary>
    public static string? BuiltInRepo =>
        typeof(UpdateChecker).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateRepo")?.Value is { Length: > 0 } r ? r.Trim() : null;

    /// <summary>المستودع الفعلي: اللي في الإعدادات، وإلا اللي جوه البرنامج.</summary>
    public static string? Repo
    {
        get
        {
            string configured = SettingsStore.Current.Update.Repo?.Trim().Trim('/') ?? "";
            return configured.Contains('/') ? configured : BuiltInRepo;
        }
    }

    public static Version CurrentVersion =>
        typeof(UpdateChecker).Assembly.GetName().Version is { } v ? new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build)) : new Version(1, 0, 0);

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    public static async Task<Release?> GetLatestAsync(string repo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/')) return null;
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await http.GetAsync($"https://api.github.com/repos/{repo.Trim()}/releases/latest", ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // مفيش إصدارات لسه
        resp.EnsureSuccessStatusCode();
        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseRelease(json);
    }

    /// <summary>تحليل رد GitHub (public عشان يتجرّب لوحده).</summary>
    public static Release? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (!TryParseVersion(tag, out var version)) return null;

        string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string? setupUrl = null, setupName = null, sha = null;
        long setupSize = 0;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string? download = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                if (download is null) continue;
                // المثبّت: PCCare-Setup-1.0.5.exe
                if (setupUrl is null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                {
                    setupUrl = download;
                    setupName = name;
                    setupSize = a.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : 0;
                    // GitHub بيحط «sha256:...» لكل ملف: بنتأكد إن الملف اتنزّل سليم ومحدش لعب فيه
                    if (a.TryGetProperty("digest", out var dg) && dg.GetString() is { } digest &&
                        digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        sha = digest[7..].Trim().ToLowerInvariant();
                }
            }
        }
        return new Release(version, tag, url, notes, setupUrl, setupName, setupSize, sha);
    }

    /// <summary>
    /// ينزّل المثبّت في فولدر مؤقت ويتأكد منه: الرابط من نفس المستودع، الحجم مطابق، وبصمة sha256 لو موجودة.
    /// </summary>
    public static async Task<string> DownloadSetupAsync(Release r, string repo, IProgress<int>? percent, CancellationToken ct)
    {
        if (r.SetupUrl is null || r.SetupName is null) throw new InvalidOperationException("الإصدار ده مالوش مثبّت على GitHub");
        string expectedPrefix = $"https://github.com/{repo.Trim().Trim('/')}/releases/download/";
        if (!r.SetupUrl.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("رابط التحديث مش من مستودع البرنامج، فمش هنزّله.");

        string dir = Path.Combine(Path.GetTempPath(), "AjyalPcCare-Update");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, string.Concat(r.SetupName.Split(Path.GetInvalidFileNameChars())));

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        using (var resp = await http.GetAsync(r.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? r.SetupSize;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(file);
            var buffer = new byte[81920];
            long done = 0;
            int read, last = -1;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0)
                {
                    int p = (int)(done * 100 / total);
                    if (p != last) { last = p; percent?.Report(p); }
                }
            }
        }

        var info = new FileInfo(file);
        if (r.SetupSize > 0 && info.Length != r.SetupSize)
        {
            File.Delete(file);
            throw new InvalidOperationException("ملف التحديث اتنزّل ناقص. جرّب تاني.");
        }
        if (r.SetupSha256 is not null)
        {
            string hash;
            await using (var fs = File.OpenRead(file))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false)).ToLowerInvariant();
            if (hash != r.SetupSha256)
            {
                File.Delete(file);
                throw new InvalidOperationException("بصمة ملف التحديث مش مطابقة لـ GitHub، فمش هثبّته.");
            }
        }

        AppLogger.Info("Update", $"تم تنزيل {r.Tag} ({Format.Bytes(info.Length)}) والتحقق منه: {file}");
        return file;
    }

    /// <summary>
    /// تشغيل المثبّت.
    /// relaunch = true (من الواجهة): /SILENT يبيّن التقدم، و/UPDATE=1 يفتح البرنامج تاني بعد ما يخلص.
    /// relaunch = false (وضع المعامل / SYSTEM): /VERYSILENT من غير أي نافذة، ومن غير ما يفتح الواجهة.
    /// </summary>
    public static bool LaunchInstaller(string file, bool relaunch)
    {
        try
        {
            string args = relaunch
                ? "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1"
                : "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Arguments = args });
            AppLogger.Info("Update", $"تشغيل المثبّت: {Path.GetFileName(file)} {args}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update", "تعذر تشغيل المثبّت", ex);
            return false;
        }
    }

    /// <summary>«v1.0.5» أو «1.0» ← Version.</summary>
    public static bool TryParseVersion(string tag, out Version version)
    {
        string s = tag.Trim().TrimStart('v', 'V');
        int dash = s.IndexOfAny(['-', '+']);
        if (dash >= 0) s = s[..dash];
        if (Version.TryParse(s, out var v)) { version = v; return true; }
        if (int.TryParse(s, out int major)) { version = new Version(major, 0); return true; }
        version = new Version(0, 0);
        return false;
    }

    public static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>
    /// تحديث صامت كامل (وضع المعامل): لو فيه إصدار أحدث ← تنزيل ← تحقق ← تشغيل المثبّت من غير أي نافذة.
    /// بيرجع true لو المثبّت اتشغّل (البرنامج لازم يخرج بعدها عشان المثبّت يقدر يستبدل الملفات).
    /// </summary>
    public static async Task<bool> RunSilentUpdateAsync(CancellationToken ct)
    {
        string? repo = Repo;
        if (repo is null)
        {
            AppLogger.Info("Update", "مفيش مستودع تحديثات متحدد — تم التخطي");
            return false;
        }
        try
        {
            var latest = await GetLatestAsync(repo, ct).ConfigureAwait(false);
            if (latest is null || !IsNewer(latest.Version, CurrentVersion))
            {
                AppLogger.Info("Update", $"على آخر إصدار ({CurrentVersionText})");
                return false;
            }
            if (latest.SetupUrl is null)
            {
                AppLogger.Warn("Update", $"الإصدار {latest.Tag} مالوش مثبّت — تم التخطي");
                return false;
            }
            AppLogger.Info("Update", $"إصدار جديد {latest.Tag} (الحالي {CurrentVersionText}) — جاري التنزيل");
            string file = await DownloadSetupAsync(latest, repo, null, ct).ConfigureAwait(false);
            return LaunchInstaller(file, relaunch: false);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update", "فشل التحديث الصامت", ex);
            return false;
        }
    }
}
