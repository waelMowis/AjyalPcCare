using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AjyalPcCare.Native;

/// <summary>نتيجة استدعاء Native بدون Exceptions: نجح أو فشل مع رسالة.</summary>
public readonly record struct NativeResult(bool Success, string Message, int ErrorCode = 0)
{
    public static NativeResult Ok(string message = "تم") => new(true, message);
    public static NativeResult Fail(string message, int code = 0) => new(false, message, code);
}

/// <summary>أرقام الذاكرة الفعلية بالبايت.</summary>
public sealed record MemoryStatus(
    ulong TotalPhysical,
    ulong AvailablePhysical,
    uint MemoryLoadPercent,
    ulong CommitTotal,
    ulong CommitLimit,
    ulong SystemCache,
    uint ProcessCount,
    uint ThreadCount,
    uint HandleCount);

/// <summary>
/// قوائم الذاكرة الداخلية (زي RAMMap). Standby = كاش مفيد، مش رام "مهدرة".
/// ممكن تكون null لو الاستعلام اترفض (من غير صلاحيات Admin).
/// </summary>
public sealed record MemoryLists(ulong Free, ulong Zeroed, ulong Modified, ulong Standby, ulong[] StandbyByPriority);

public static class MemoryApi
{
    public static MemoryStatus? GetStatus()
    {
        var ms = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref ms)) return null;

        ulong commitTotal = 0, commitLimit = 0, cache = 0;
        uint procs = 0, threads = 0, handles = 0;
        if (NativeMethods.GetPerformanceInfo(out var pi, (uint)Marshal.SizeOf<NativeMethods.PERFORMANCE_INFORMATION>()))
        {
            ulong page = (ulong)pi.PageSize;
            commitTotal = (ulong)pi.CommitTotal * page;
            commitLimit = (ulong)pi.CommitLimit * page;
            cache = (ulong)pi.SystemCache * page;
            procs = pi.ProcessCount;
            threads = pi.ThreadCount;
            handles = pi.HandleCount;
        }

        return new MemoryStatus(ms.ullTotalPhys, ms.ullAvailPhys, ms.dwMemoryLoad, commitTotal, commitLimit, cache, procs, threads, handles);
    }

    private static ulong PageSize()
    {
        return NativeMethods.GetPerformanceInfo(out var pi, (uint)Marshal.SizeOf<NativeMethods.PERFORMANCE_INFORMATION>())
            ? (ulong)pi.PageSize
            : 4096UL;
    }

    /// <summary>يتطلب SeProfileSingleProcessPrivilege (يعني Admin).</summary>
    public static unsafe MemoryLists? GetMemoryLists()
    {
        PrivilegeApi.TryEnable(PrivilegeApi.SeProfileSingleProcess);
        int status = NativeMethods.NtQuerySystemInformation(NativeMethods.SystemMemoryListInformation,
            out var info, Marshal.SizeOf<NativeMethods.SYSTEM_MEMORY_LIST_INFORMATION>(), out _);
        if (status != 0) return null;

        ulong page = PageSize();
        var byPriority = new ulong[8];
        ulong standby = 0;
        for (int i = 0; i < 8; i++)
        {
            byPriority[i] = info.PageCountByPriority[i] * page;
            standby += byPriority[i];
        }

        return new MemoryLists(
            Free: (ulong)info.FreePageCount * page,
            Zeroed: (ulong)info.ZeroPageCount * page,
            Modified: (ulong)info.ModifiedPageCount * page,
            Standby: standby,
            StandbyByPriority: byPriority);
    }

    /// <summary>
    /// يفضّي الـ Standby List. ملحوظة تقنية: ده بيمسح كاش مفيد — البرامج اللي هتتفتح بعده هتتقرا من الديسك.
    /// lowPriorityOnly = true يمسح أولوية 0 بس (أقل ضرر).
    /// </summary>
    public static NativeResult PurgeStandbyList(bool lowPriorityOnly = false)
    {
        var priv = PrivilegeApi.TryEnable(PrivilegeApi.SeProfileSingleProcess);
        if (!priv.Success) return priv;

        int command = lowPriorityOnly ? NativeMethods.MemoryPurgeLowPriorityStandbyList : NativeMethods.MemoryPurgeStandbyList;
        int status = NativeMethods.NtSetSystemInformation(NativeMethods.SystemMemoryListInformation, ref command, sizeof(int));
        return status == 0
            ? NativeResult.Ok("تم تفريغ قائمة Standby")
            : NativeResult.Fail($"NtSetSystemInformation رجّع NTSTATUS 0x{status:X8}", status);
    }
}

public static class PrivilegeApi
{
    public const string SeProfileSingleProcess = "SeProfileSingleProcessPrivilege";
    public const string SeIncreaseQuota = "SeIncreaseQuotaPrivilege";
    public const string SeDebug = "SeDebugPrivilege";

    public static NativeResult TryEnable(string privilege)
    {
        if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(),
                NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY, out var token))
            return NativeResult.Fail("تعذر فتح Token العملية", Marshal.GetLastWin32Error());

        using (token)
        {
            if (!NativeMethods.LookupPrivilegeValueW(null, privilege, out var luid))
                return NativeResult.Fail($"الصلاحية {privilege} غير معروفة", Marshal.GetLastWin32Error());

            var tp = new NativeMethods.TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = NativeMethods.SE_PRIVILEGE_ENABLED
            };

            if (!NativeMethods.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                return NativeResult.Fail("AdjustTokenPrivileges فشل", Marshal.GetLastWin32Error());

            // AdjustTokenPrivileges بيرجع true حتى لو الصلاحية مش موجودة في الـ Token
            int err = Marshal.GetLastWin32Error();
            if (err == NativeMethods.ERROR_NOT_ALL_ASSIGNED)
                return NativeResult.Fail($"الصلاحية {privilege} غير متاحة — شغّل الأداة كمسؤول (Administrator)", err);

            return NativeResult.Ok();
        }
    }
}

public static class ProcessApi
{
    public static string? GetImagePath(int pid)
    {
        using var h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h.IsInvalid) return null;
        var buffer = new char[1024];
        uint size = (uint)buffer.Length;
        return NativeMethods.QueryFullProcessImageNameW(h, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
    }

    /// <summary>
    /// EmptyWorkingSet لبروسيس واحد. الصفحات بتتنقل لـ Standby/Modified مش بتتمسح،
    /// والبروسيس هيعمل Page Faults لما يرجع يشتغل — عشان كده ده يدوي ولبروسيس واحد بس.
    /// </summary>
    public static NativeResult TrimWorkingSet(int pid)
    {
        PrivilegeApi.TryEnable(PrivilegeApi.SeDebug);
        using var h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_SET_QUOTA, false, pid);
        if (h.IsInvalid)
            return NativeResult.Fail("تعذر فتح البروسيس (غالبًا بروسيس نظام محمي)", Marshal.GetLastWin32Error());
        return NativeMethods.EmptyWorkingSet(h)
            ? NativeResult.Ok("تم تقليص Working Set")
            : NativeResult.Fail(new Win32Exception(Marshal.GetLastWin32Error()).Message, Marshal.GetLastWin32Error());
    }
}

public static class SystemTimesApi
{
    /// <summary>OEM Code Page للنظام (ترميز مخرجات أدوات الكونسول زي powercfg و sc).</summary>
    public static int OemCodePage => (int)NativeMethods.GetOEMCP();

    public static TimeSpan Uptime => TimeSpan.FromMilliseconds(NativeMethods.GetTickCount64());

    /// <summary>(idle, kernel, user) بوحدات 100ns. kernel تشمل idle.</summary>
    public static (ulong Idle, ulong Kernel, ulong User)? GetSystemTimes()
    {
        return NativeMethods.GetSystemTimes(out var i, out var k, out var u) ? (i.Value, k.Value, u.Value) : null;
    }
}

public static class RecycleBinApi
{
    public static (long Bytes, long Items) Query()
    {
        var info = new NativeMethods.SHQUERYRBINFO { cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
        int hr = NativeMethods.SHQueryRecycleBinW(null, ref info);
        return hr == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    public static NativeResult Empty()
    {
        int hr = NativeMethods.SHEmptyRecycleBinW(IntPtr.Zero, null,
            NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND);
        // E_UNEXPECTED (0x8000FFFF) بيرجع لما السلة تكون فاضية أصلًا
        return hr == 0 || hr == unchecked((int)0x8000FFFF)
            ? NativeResult.Ok("تم إفراغ سلة المحذوفات")
            : NativeResult.Fail($"SHEmptyRecycleBin رجّع 0x{hr:X8}", hr);
    }
}

public enum SignatureState
{
    Valid,
    Unsigned,
    Invalid,
    FileMissing,
    Unknown
}

public static class TrustApi
{
    /// <summary>التحقق من التوقيع الرقمي (Authenticode) بدون اتصال إنترنت (Cache only) عشان ما يعلّقش.</summary>
    public static SignatureState Verify(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return SignatureState.FileMissing;

        IntPtr pPath = Marshal.StringToCoTaskMemUni(path);
        var fileInfo = new NativeMethods.WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>(),
            pcwszFilePath = pPath
        };
        IntPtr pFile = Marshal.AllocCoTaskMem(Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new NativeMethods.WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_DATA>(),
                dwUIChoice = NativeMethods.WTD_UI_NONE,
                fdwRevocationChecks = NativeMethods.WTD_REVOKE_NONE,
                dwUnionChoice = NativeMethods.WTD_CHOICE_FILE,
                pFile = pFile,
                dwStateAction = NativeMethods.WTD_STATEACTION_VERIFY,
                dwProvFlags = NativeMethods.WTD_CACHE_ONLY_URL_RETRIEVAL
            };
            var action = NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2;
            int result = NativeMethods.WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            data.dwStateAction = NativeMethods.WTD_STATEACTION_CLOSE;
            NativeMethods.WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            return result switch
            {
                0 => SignatureState.Valid,
                unchecked((int)0x800B0100) => SignatureState.Unsigned, // TRUST_E_NOSIGNATURE
                unchecked((int)0x800B0003) => SignatureState.Unsigned, // TRUST_E_SUBJECT_FORM_UNKNOWN
                _ => SignatureState.Invalid
            };
        }
        catch
        {
            return SignatureState.Unknown;
        }
        finally
        {
            Marshal.DestroyStructure<NativeMethods.WINTRUST_FILE_INFO>(pFile);
            Marshal.FreeCoTaskMem(pFile);
            Marshal.FreeCoTaskMem(pPath);
        }
    }
}

/// <summary>SafeHandle لـ PDH Query.</summary>
public sealed class PdhQueryHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public PdhQueryHandle() : base(true) { }
    internal PdhQueryHandle(IntPtr h) : base(true) => SetHandle(h);
    protected override bool ReleaseHandle() => NativeMethods.PdhCloseQuery(handle) == 0;
}

/// <summary>
/// عدّادات أداء عن طريق PDH مباشرة بأسماء إنجليزي (تشتغل على ويندوز عربي)،
/// بدل حزمة System.Diagnostics.PerformanceCounter — صفر اعتماديات خارجية.
/// </summary>
public sealed class PdhCounterSet : IDisposable
{
    private readonly PdhQueryHandle _query;
    private readonly Dictionary<string, IntPtr> _counters = new(StringComparer.OrdinalIgnoreCase);

    public PdhCounterSet(IEnumerable<string> englishPaths)
    {
        if (NativeMethods.PdhOpenQueryW(null, IntPtr.Zero, out var q) != 0)
            throw new InvalidOperationException("PdhOpenQuery فشل");
        _query = new PdhQueryHandle(q);

        foreach (var path in englishPaths)
        {
            if (NativeMethods.PdhAddEnglishCounterW(q, path, IntPtr.Zero, out var c) == 0)
                _counters[path] = c;
        }

        NativeMethods.PdhCollectQueryData(q); // أول قراءة مرجعية — عدادات النسب بتحتاج قراءتين
    }

    public bool Has(string path) => _counters.ContainsKey(path);

    public void Collect()
    {
        if (!_query.IsInvalid) NativeMethods.PdhCollectQueryData(_query.DangerousGetHandle());
    }

    public double? Read(string path)
    {
        if (!_counters.TryGetValue(path, out var c)) return null;
        uint st = NativeMethods.PdhGetFormattedCounterValue(c, NativeMethods.PDH_FMT_DOUBLE | NativeMethods.PDH_FMT_NOCAP100, IntPtr.Zero, out var v);
        return st == 0 && (v.CStatus == 0 || v.CStatus == 1) ? v.doubleValue : null;
    }

    public void Dispose() => _query.Dispose();
}

public static class FontApi
{
    /// <summary>
    /// تحميل خط من ملف للبروسيس الحالي بس (من غير تسطيب). ضروري عشان GDI (TextRenderer اللي بتستخدمه
    /// عناصر WinForms) يشوف الخط — PrivateFontCollection لوحدها بتشتغل مع GDI+ بس.
    /// </summary>
    public static bool LoadPrivate(string path) => NativeMethods.AddFontResourceExW(path, NativeMethods.FR_PRIVATE, IntPtr.Zero) > 0;
}
