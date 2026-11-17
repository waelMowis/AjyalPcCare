using System.Text.RegularExpressions;
using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Health;

public sealed record PowerPlan(Guid Id, string Name, bool Active);

/// <summary>
/// خطط الطاقة عن طريق powercfg. التحليل مبني على الـ GUID والأقواس فقط
/// عشان يشتغل على ويندوز عربي وإنجليزي (النصوص بتتغير، الـ GUID لأ).
/// </summary>
public static partial class PowerPlanService
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\((.*?)\)\s*(\*)?")]
    private static partial Regex PlanRegex();

    private static string PowerCfg => Path.Combine(Environment.SystemDirectory, "powercfg.exe");

    public static async Task<List<PowerPlan>> ListAsync(CancellationToken ct)
    {
        // powercfg بيكتب بترميز OEM — بنستخدم ترميز الكونسول الحالي
        var enc = ConsoleEncoding.Oem;
        var o = await ProcessRunner.RunAsync(PowerCfg, "/list", ct, timeout: TimeSpan.FromSeconds(30), outputEncoding: enc).ConfigureAwait(false);
        var list = new List<PowerPlan>();
        foreach (Match m in PlanRegex().Matches(o.StdOut))
            list.Add(new PowerPlan(Guid.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(), m.Groups[3].Success));
        return list;
    }

    public static async Task<bool> SetActiveAsync(Guid id, CancellationToken ct)
    {
        var o = await ProcessRunner.RunAsync(PowerCfg, $"/setactive {id}", ct, timeout: TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        AppLogger.Operation("Health", $"تغيير خطة الطاقة إلى {id}", o.Success);
        return o.Success;
    }

    public static string Advice(Guid active) =>
        active == PowerSaver
            ? "خطة توفير الطاقة بتحد من سرعة المعالج — لأجهزة المعامل المكتبية الأفضل Balanced."
            : active == HighPerformance
                ? "High Performance بتمنع المعالج ينزل لسرعات منخفضة — فرق الأداء الفعلي بسيط في الاستخدام العادي، ومعاها استهلاك كهرباء وحرارة أعلى."
                : "Balanced هي الخيار الصحيح لأغلب الأجهزة: المعالج بيرفع سرعته فورًا وقت الحاجة.";
}
