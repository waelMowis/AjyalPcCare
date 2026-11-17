using System.Globalization;

namespace AjyalPcCare.Core.Common;

public static class Format
{
    private static readonly string[] Units = ["بايت", "ك.ب", "م.ب", "ج.ب", "ت.ب"];

    public static string Bytes(long bytes) => Bytes((double)bytes);

    public static string Bytes(ulong bytes) => Bytes((double)bytes);

    public static string Bytes(double bytes)
    {
        if (bytes < 0) return "-" + Bytes(-bytes);
        int i = 0;
        while (bytes >= 1024 && i < Units.Length - 1)
        {
            bytes /= 1024;
            i++;
        }
        return bytes.ToString(i == 0 ? "0" : "0.##", CultureInfo.InvariantCulture) + " " + Units[i];
    }

    public static string Rate(double bytesPerSecond) => Bytes(bytesPerSecond) + "/ث";

    public static string Percent(double value) => value.ToString("0.#", CultureInfo.InvariantCulture) + "%";

    public static string Duration(TimeSpan ts)
    {
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays} يوم و {ts.Hours} ساعة";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours} ساعة و {ts.Minutes} دقيقة";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes} دقيقة و {ts.Seconds} ثانية";
        return $"{ts.TotalSeconds:0.#} ثانية";
    }

    public static string Mb(ulong bytes) => (bytes / 1024d / 1024d).ToString("N0", CultureInfo.InvariantCulture) + " م.ب";
}
