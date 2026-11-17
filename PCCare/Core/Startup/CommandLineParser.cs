namespace AjyalPcCare.Core.Startup;

/// <summary>استخراج مسار الملف التنفيذي من سطر أوامر Startup (بيتعامل مع الـ quotes والمسافات و rundll32).</summary>
public static class CommandLineParser
{
    private static readonly string[] Executables = [".exe", ".com", ".bat", ".cmd", ".vbs", ".js", ".ps1", ".lnk", ".dll", ".scr"];

    public static string? ExtractExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string cmd = Environment.ExpandEnvironmentVariables(command.Trim());

        string? first;
        string rest;
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            first = end > 1 ? cmd[1..end] : cmd.Trim('"');
            rest = end > 1 && end + 1 < cmd.Length ? cmd[(end + 1)..].Trim() : "";
        }
        else
        {
            first = GuessUnquoted(cmd, out rest);
        }

        first = NormalizeSystemPath(first);

        // rundll32 file.dll,Entry → الملف الحقيقي هو الـ dll
        if (first is not null && Path.GetFileName(first).Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase) && rest.Length > 0)
        {
            string arg = rest.Trim('"');
            int comma = arg.IndexOf(',');
            if (comma > 0) arg = arg[..comma];
            arg = NormalizeSystemPath(arg.Trim('"')) ?? arg;
            if (File.Exists(arg)) return arg;
        }

        if (first is not null && !Path.IsPathRooted(first))
        {
            string sys = Path.Combine(Environment.SystemDirectory, first);
            if (File.Exists(sys)) return sys;
            if (!Path.HasExtension(first) && File.Exists(sys + ".exe")) return sys + ".exe";
        }

        return first;
    }

    /// <summary>مسار غير محاط بـ quotes وفيه مسافات: نجرب نقصّه عند كل مسافة لحد ما نلاقي ملف موجود.</summary>
    private static string? GuessUnquoted(string cmd, out string rest)
    {
        var parts = cmd.Split(' ');
        for (int i = parts.Length; i >= 1; i--)
        {
            string candidate = string.Join(' ', parts.Take(i));
            if (File.Exists(candidate) || (!Path.HasExtension(candidate) && File.Exists(candidate + ".exe")))
            {
                rest = string.Join(' ', parts.Skip(i));
                return File.Exists(candidate) ? candidate : candidate + ".exe";
            }
        }

        // مفيش ملف موجود — نرجع أول جزء بينتهي بامتداد تنفيذي (عشان نعلّم المدخل كـ Broken)
        for (int i = 1; i <= parts.Length; i++)
        {
            string candidate = string.Join(' ', parts.Take(i));
            if (Executables.Any(e => candidate.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
            {
                rest = string.Join(' ', parts.Skip(i));
                return candidate;
            }
        }

        rest = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : "";
        return parts[0];
    }

    /// <summary>مسارات الخدمات بتيجي بأشكال زي \SystemRoot\... أو \??\C:\...</summary>
    public static string? NormalizeSystemPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        string p = path.Trim();
        if (p.StartsWith(@"\??\", StringComparison.Ordinal)) p = p[4..];
        if (p.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), p[12..]);
        else if (p.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), p);
        return Environment.ExpandEnvironmentVariables(p);
    }
}
