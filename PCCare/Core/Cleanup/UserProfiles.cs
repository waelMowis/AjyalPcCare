using Microsoft.Win32;
using AjyalPcCare.Core.Common;

namespace AjyalPcCare.Core.Cleanup;

/// <summary>
/// حسابات المستخدمين على الجهاز. في المعامل كل طالب ممكن يكون ليه حساب،
/// والأداة لما تشتغل كـ SYSTEM (Scheduled Task) الـ %TEMP% بتاعها هو C:\Windows\Temp
/// — عشان كده لازم نلف على كل البروفايلات من ProfileList.
/// </summary>
public static class UserProfiles
{
    public sealed record Profile(string Sid, string Path)
    {
        public string LocalAppData => System.IO.Path.Combine(Path, "AppData", "Local");
        public string RoamingAppData => System.IO.Path.Combine(Path, "AppData", "Roaming");
        public string Name => System.IO.Path.GetFileName(Path);
    }

    public static List<Profile> GetProfiles(bool allUsers)
    {
        var result = new List<Profile>();
        string currentLocal = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (allUsers && AppEnvironment.IsAdministrator)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
                if (key is not null)
                {
                    foreach (var sid in key.GetSubKeyNames())
                    {
                        // S-1-5-21-* = حسابات مستخدمين حقيقية (محلية أو دومين)، بنستبعد SYSTEM/LocalService/NetworkService
                        if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)) continue;
                        using var sub = key.OpenSubKey(sid);
                        if (sub?.GetValue("ProfileImagePath") is not string path) continue;
                        path = Environment.ExpandEnvironmentVariables(path);
                        if (Directory.Exists(path)) result.Add(new Profile(sid, path));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Cleanup", "تعذر قراءة قائمة البروفايلات: " + ex.Message);
            }
        }

        bool isSystemAccount = currentLocal.Contains(@"\systemprofile", StringComparison.OrdinalIgnoreCase);
        if (!isSystemAccount && !result.Any(p => p.Path.Equals(currentLocal, StringComparison.OrdinalIgnoreCase)) && Directory.Exists(currentLocal))
            result.Add(new Profile("current", currentLocal));

        return result;
    }
}
