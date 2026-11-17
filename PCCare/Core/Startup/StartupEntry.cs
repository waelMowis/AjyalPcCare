using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Startup;

public enum StartupSource
{
    RegistryRunUser,
    RegistryRunMachine,
    RegistryRunMachine32,
    RegistryRunOnceUser,
    RegistryRunOnceMachine,
    StartupFolderUser,
    StartupFolderCommon,
    ScheduledTask,
    Service
}

public sealed class StartupEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required StartupSource Source { get; init; }

    /// <summary>مكان التعريف: مسار مفتاح ريجستري / مسار ملف اختصار / مسار المهمة / اسم الخدمة.</summary>
    public required string Location { get; init; }

    public string Command { get; init; } = "";
    public string? ExecutablePath { get; set; }
    public string? Publisher { get; set; }
    public SignatureState Signature { get; set; } = SignatureState.Unknown;
    public bool FileExists { get; set; }
    public bool Enabled { get; set; }
    public bool CanToggle { get; init; } = true;
    public string? Note { get; set; }

    /// <summary>للخدمات: نوع التشغيل الحالي (auto / delayed-auto / demand ...).</summary>
    public string? ServiceStartType { get; set; }

    public bool IsBroken => !FileExists && !string.IsNullOrEmpty(ExecutablePath);

    public bool IsMicrosoft => Publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true;

    public string SourceDisplay => Source switch
    {
        StartupSource.RegistryRunUser => "ريجستري (المستخدم)",
        StartupSource.RegistryRunMachine => "ريجستري (الجهاز)",
        StartupSource.RegistryRunMachine32 => "ريجستري (الجهاز 32-بت)",
        StartupSource.RegistryRunOnceUser => "RunOnce (المستخدم)",
        StartupSource.RegistryRunOnceMachine => "RunOnce (الجهاز)",
        StartupSource.StartupFolderUser => "مجلد Startup (المستخدم)",
        StartupSource.StartupFolderCommon => "مجلد Startup (كل المستخدمين)",
        StartupSource.ScheduledTask => "مهمة مجدولة",
        StartupSource.Service => "خدمة تلقائية",
        _ => Source.ToString()
    };

    public string SignatureDisplay => Signature switch
    {
        SignatureState.Valid => "موقّع وسليم",
        SignatureState.Unsigned => "غير موقّع",
        SignatureState.Invalid => "توقيع غير صالح",
        SignatureState.FileMissing => "الملف غير موجود",
        _ => "غير معروف"
    };
}
