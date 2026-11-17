using System.Net;
using System.Text;
using System.Text.Json;
using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Reports;

public sealed record ReportFiles(string JsonPath, string HtmlPath, string? SummaryCsvPath);

/// <summary>
/// كتابة تقرير الجهاز: JSON + HTML (عربي RTL بخط Readex Pro) + سطر في summary.csv المشترك
/// — عشان مدير المعمل يفتح ملف واحد ويشوف الـ 50 جهاز.
/// </summary>
public static class ReportWriter
{
    public static async Task<ReportFiles> WriteAsync(MachineReport report, string? folder, CancellationToken ct)
    {
        string dir = string.IsNullOrWhiteSpace(folder) ? AppEnvironment.ReportsDirectory : folder;
        Directory.CreateDirectory(dir);
        string stamp = report.GeneratedAt.ToString("yyyyMMdd_HHmm");
        string baseName = $"{Sanitize(report.MachineName)}_{stamp}";

        string jsonPath = Path.Combine(dir, baseName + ".json");
        string htmlPath = Path.Combine(dir, baseName + ".html");

        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, SettingsStore.JsonOptions), Encoding.UTF8, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(htmlPath, BuildHtml(report), Encoding.UTF8, ct).ConfigureAwait(false);

        string? csv = null;
        try
        {
            csv = Path.Combine(dir, "summary.csv");
            await AppendSummaryAsync(csv, report, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Report", "تعذر تحديث summary.csv: " + ex.Message);
            csv = null;
        }

        AppLogger.Info("Report", $"تم حفظ التقرير: {htmlPath}");
        return new ReportFiles(jsonPath, htmlPath, csv);
    }

    private static string Sanitize(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>أجهزة كتير بتكتب في نفس الملف على الشبكة في نفس الوقت — Retry مع قفل حصري.</summary>
    private static async Task AppendSummaryAsync(string path, MachineReport r, CancellationToken ct)
    {
        string line = string.Join(",",
            Csv(r.GeneratedAt.ToString("yyyy-MM-dd HH:mm")), Csv(r.MachineName), Csv(r.HealthScore?.ToString() ?? ""), Csv(r.HealthGrade ?? ""),
            Csv(Math.Round(r.DiskSpaceFreed / 1024d / 1024d, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Csv(string.Join(" | ", r.Problems)), Csv(r.Modules.Any(m => !m.Success) ? "مع أخطاء" : "نجاح"), Csv(r.ToolVersion));

        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                bool isNew = !File.Exists(path);
                await using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None);
                await using var w = new StreamWriter(fs, new UTF8Encoding(isNew)); // BOM لأول مرة عشان Excel يقرا العربي صح
                if (isNew) await w.WriteLineAsync("التاريخ,الجهاز,مؤشر الصحة,التقييم,المساحة المحررة (م.ب),المشاكل,النتيجة,الإصدار").ConfigureAwait(false);
                await w.WriteLineAsync(line).ConfigureAwait(false);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(Random.Shared.Next(200, 1500), ct).ConfigureAwait(false);
            }
        }
        throw new IOException("الملف مقفول من أجهزة أخرى");
    }

    private static string Csv(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string BuildHtml(MachineReport r)
    {
        var sb = new StringBuilder();
        string scoreColor = r.HealthScore switch { >= 85 => "#1a7f37", >= 70 => "#2f6fdf", >= 50 => "#b7791f", null => "#666", _ => "#c53030" };

        sb.Append("""
<!DOCTYPE html>
<html lang="ar" dir="rtl">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link href="https://fonts.googleapis.com/css2?family=Readex+Pro:wght@300;400;600;700&display=swap" rel="stylesheet">
""");
        sb.Append($"<title>تقرير {H(r.MachineName)} — AJYAL PC Care</title>");
        sb.Append("""
<style>
 :root { --bg:#f5f7fa; --card:#fff; --ink:#1d2533; --muted:#667085; --line:#e4e7ec; --brand:#0f5c4d; }
 * { box-sizing:border-box; }
 body { margin:0; font-family:'Readex Pro','Segoe UI',Tahoma,sans-serif; background:var(--bg); color:var(--ink); line-height:1.7; }
 header { background:var(--brand); color:#fff; padding:24px 32px; }
 header h1 { margin:0; font-size:22px; font-weight:600; }
 header p { margin:4px 0 0; opacity:.85; font-size:14px; }
 main { max-width:1100px; margin:0 auto; padding:24px; display:grid; gap:18px; }
 .card { background:var(--card); border:1px solid var(--line); border-radius:12px; padding:18px 22px; }
 .card h2 { margin:0 0 12px; font-size:17px; font-weight:600; }
 .grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(200px,1fr)); gap:12px; }
 .kv span { display:block; color:var(--muted); font-size:13px; }
 .kv b { font-weight:600; font-size:15px; }
 .score { font-size:44px; font-weight:700; }
 table { width:100%; border-collapse:collapse; font-size:14px; }
 th,td { text-align:right; padding:8px 10px; border-bottom:1px solid var(--line); vertical-align:top; }
 th { color:var(--muted); font-weight:400; font-size:13px; }
 .sev-Critical { color:#c53030; font-weight:600; } .sev-Warning { color:#b7791f; font-weight:600; } .sev-Info { color:#2f6fdf; }
 .ok { color:#1a7f37; } .bad { color:#c53030; }
 .muted { color:var(--muted); font-size:13px; }
 footer { text-align:center; color:var(--muted); font-size:12px; padding:20px; }
</style>
</head>
<body>
""");
        sb.Append($"<header><h1>تقرير صيانة الجهاز: {H(r.MachineName)}</h1><p>{r.GeneratedAt:yyyy-MM-dd HH:mm} — الوضع: {(r.Mode == "Silent" ? "تلقائي (وضع المعامل)" : "تفاعلي")} — صلاحيات مسؤول: {(r.RanAsAdmin ? "نعم" : "لا")}</p></header><main>");

        // الملخص
        sb.Append("<section class=\"card\"><div class=\"grid\">");
        sb.Append($"<div class=\"kv\"><span>مؤشر الصحة</span><div class=\"score\" style=\"color:{scoreColor}\">{(r.HealthScore?.ToString() ?? "—")}</div><b>{H(r.HealthGrade)}</b></div>");
        sb.Append($"<div class=\"kv\"><span>المساحة المحررة</span><b>{Format.Bytes(r.DiskSpaceFreed)}</b></div>");
        sb.Append($"<div class=\"kv\"><span>نظام التشغيل</span><b>{H(r.Os)}</b></div>");
        sb.Append($"<div class=\"kv\"><span>المعالج</span><b>{H(r.Cpu)}</b></div>");
        sb.Append($"<div class=\"kv\"><span>الذاكرة</span><b>{Format.Bytes(r.RamTotal)}</b></div>");
        sb.Append($"<div class=\"kv\"><span>مدة التشغيل</span><b>{Format.Duration(TimeSpan.FromHours(r.UptimeHours))}</b></div>");
        sb.Append($"<div class=\"kv\"><span>آخر تحديث ويندوز</span><b>{(r.LastWindowsUpdate?.ToString("yyyy-MM-dd") ?? "غير معروف")}</b></div>");
        sb.Append($"<div class=\"kv\"><span>برامج Startup مفعّلة</span><b>{(r.StartupEnabledCount?.ToString() ?? "—")}</b></div>");
        sb.Append("</div></section>");

        // الملاحظات
        sb.Append("<section class=\"card\"><h2>الملاحظات وطريقة حساب المؤشر</h2>");
        if (r.Findings.Count == 0) sb.Append("<p class=\"ok\">لا توجد مشاكل مكتشفة.</p>");
        else
        {
            sb.Append("<table><tr><th>القاعدة</th><th>الملاحظة</th><th>التفاصيل</th><th>الخصم</th><th>التوصية</th></tr>");
            foreach (var f in r.Findings)
                sb.Append($"<tr><td>{H(f.Rule)}</td><td class=\"sev-{f.Severity}\">{H(f.Title)}</td><td>{H(f.Detail)}</td><td>-{f.Penalty}</td><td>{H(f.Recommendation)}</td></tr>");
            sb.Append("</table>");
        }
        sb.Append("<p class=\"muted\">المؤشر يبدأ من 100 ويتخصم منه حسب القواعد المعلنة فقط — مفيش أرقام تقديرية.</p></section>");

        // الديسكات
        sb.Append("<section class=\"card\"><h2>الديسكات</h2><table><tr><th>الاسم</th><th>النوع</th><th>الحالة</th><th>الحجم</th><th>الحرارة</th><th>الاستهلاك (Wear)</th><th>ساعات التشغيل</th><th>الدرايفات</th></tr>");
        foreach (var d in r.Disks)
            sb.Append($"<tr><td>{H(d.FriendlyName)}{(d.IsSystemDisk ? " <span class=\"muted\">(النظام)</span>" : "")}</td><td>{H(d.MediaDisplay)}</td><td class=\"{(d.IsHealthy ? "ok" : "bad")}\">{H(d.HealthDisplay)}</td><td>{Format.Bytes(d.Size)}</td><td>{(d.Temperature is int t && t > 0 ? t + "°" : "—")}</td><td>{(d.Wear is int w ? w + "%" : "—")}</td><td>{(d.PowerOnHours?.ToString() ?? "—")}</td><td>{H(d.DriveLetters)}</td></tr>");
        sb.Append("</table><br><table><tr><th>الدرايف</th><th>الاسم</th><th>نظام الملفات</th><th>المساحة الكلية</th><th>الفاضي</th></tr>");
        foreach (var v in r.Volumes)
            sb.Append($"<tr><td>{H(v.Letter)}</td><td>{H(v.Label)}</td><td>{H(v.FileSystem)}</td><td>{Format.Bytes(v.Total)}</td><td>{Format.Bytes(v.Free)} ({v.FreePercent:0.#}%)</td></tr>");
        sb.Append("</table></section>");

        // العمليات
        sb.Append("<section class=\"card\"><h2>العمليات المنفذة</h2>");
        if (r.Modules.Count == 0) sb.Append("<p class=\"muted\">لم يتم تنفيذ عمليات (تقرير فحص فقط).</p>");
        foreach (var m in r.Modules)
        {
            sb.Append($"<h3 style=\"font-size:15px;margin:14px 0 6px\">{H(m.DisplayName)} — <span class=\"{(m.Success ? "ok" : "bad")}\">{(m.Success ? "نجاح" : "مع أخطاء")}</span> <span class=\"muted\">({m.DurationSeconds} ث)</span></h3><ul>");
            foreach (var msg in m.Messages) sb.Append($"<li>{H(msg)}</li>");
            foreach (var e in m.Errors) sb.Append($"<li class=\"bad\">{H(e)}</li>");
            sb.Append("</ul>");
        }
        sb.Append("</section>");

        sb.Append($"</main><footer>AJYAL PC Care {H(r.ToolVersion)} — مدارس أجيال اليرموك الأهلية — إدارة تكنولوجيا المعلومات</footer></body></html>");
        return sb.ToString();
    }
}
