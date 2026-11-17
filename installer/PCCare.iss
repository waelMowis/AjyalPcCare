; مثبّت AJYAL PC Care (Inno Setup 6)
; بيتبني تلقائيًا من GitHub Actions، أو بإيدك:
;   dotnet publish PCCare\PCCare.csproj -p:PublishProfile=Installer
;
; البرنامج بيتثبت في Program Files لكل المستخدمين (مش فولدر المستخدم زي «مسار») لأن:
;   - المهمة المجدولة بتشتغل بحساب SYSTEM ومحتاجة مسار ثابت على مستوى الجهاز.
;   - الأداة أصلًا محتاجة صلاحيات مسؤول.
; اللوج والتقارير وسجل التغييرات في %ProgramData%\AjyalPcCare — التحديث مش بيلمسهم.

#ifndef SourceDir
  #define SourceDir "..\Releases\_staging"
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{92F4C634-80D2-4788-8168-23B34BF11E99}
AppName=AJYAL PC Care
AppVersion={#AppVersion}
AppVerName=AJYAL PC Care {#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher=د. وائل عويس — مدارس أجيال اليرموك الأهلية
AppCopyright=© 2026 د. وائل عويس
VersionInfoCompany=مدارس أجيال اليرموك الأهلية
VersionInfoDescription=مثبّت AJYAL PC Care
DefaultDirName={autopf}\AJYAL PC Care
DefaultGroupName=AJYAL PC Care
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=PCCare-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\PCCare.exe
UninstallDisplayName=AJYAL PC Care
; التحديث: نفس الـ AppId ← بيتثبت فوق القديم في نفس الفولدر.
; لو البرنامج مفتوح المثبّت بيقفله الأول (Restart Manager).
CloseApplications=force
CloseApplicationsFilter=PCCare.exe
RestartApplications=no
UsePreviousAppDir=yes

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; ملف الإعدادات بيتحط مرة واحدة بس (أول تثبيت) — التحديثات عمرها ما تمسح إعدادات المعمل
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "pccare.settings.json,\logs,\logs\*,\reports,\reports\*,changes.json"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\pccare.settings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{group}\AJYAL PC Care"; Filename: "{app}\PCCare.exe"
Name: "{autodesktop}\AJYAL PC Care"; Filename: "{app}\PCCare.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PCCare.exe"; Description: "{cm:LaunchProgram,AJYAL PC Care}"; Flags: nowait postinstall skipifsilent
; التحديث من الواجهة (البرنامج بيشغّل المثبّت بـ /SILENT /UPDATE=1): نفتح البرنامج تاني بعد ما يخلص.
; التحديث الصامت من المعامل (/VERYSILENT من غير UPDATE) مايفتحش أي واجهة.
Filename: "{app}\PCCare.exe"; Flags: nowait; Check: IsAutoUpdate

[UninstallRun]
; إلغاء التثبيت: نشيل المهمة المجدولة الأسبوعية كمان
Filename: "{app}\PCCare.exe"; Parameters: "/remove-schedule"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveSchedule"

[Code]
function IsAutoUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;
