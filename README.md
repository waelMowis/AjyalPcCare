# AJYAL PC Care

أداة صيانة وتسريع أجهزة لمعامل وأجهزة موظفين **مدارس أجيال اليرموك الأهلية**.
المبدأ الحاكم: **مفيش أي "تحسين" وهمي.** كل رقم بتعرضه الأداة مقاس فعليًا، وكل عملية ليها أثر جانبي بتتوضح قبل التنفيذ.

## ما لا تفعله الأداة (عن قصد)

| ممنوع | السبب |
|---|---|
| Registry cleaning | مالوش أثر على الأداء وممكن يكسر برامج |
| EmptyWorkingSet لكل البروسيسات | بيطرد صفحات البرامج الشغالة → تهنيج بعدها |
| قتل البروسيسات تلقائيًا | ممكن يضيّع شغل المستخدم |
| تعطيل خدمات ويندوز افتراضيًا | SysMain و WSearch و Windows Update ليهم وظايف حقيقية |
| مسح Prefetch | ويندوز بيستخدمه لتسريع فتح البرامج |
| Defrag على SSD | مالوش فايدة وبيستهلك عمر الذاكرة |
| مسح WinSxS يدويًا | الطريقة الرسمية الوحيدة هي DISM |
| عداد "تم تسريع جهازك X%" | مش مبني على قياس حقيقي |

## الهيكل (مشروع واحد)

```
E:\CLEANER
│  PCCare.sln
│  ReleaseNotes.md              ← الجديد في الإصدار — تعديله + Push = إصدار جديد على GitHub
│  publish.cmd                  ← نشر إصدار جديد (دبل كليك)
│  build.cmd                    ← مثبّت للتجربة على جهازك بس
├─ Releases                     ← آخر مثبّت رسمي بس (ملف واحد) — مش بيترفع على git
├─ tools\publish-release.ps1    ← سكريبت النشر
├─ .github\workflows            ← build (فحص) / release (نشر) / cleanup-releases (تنظيف)
├─ installer\PCCare.iss         ← المثبّت (Inno Setup)
└─ PCCare
   │  PCCare.csproj             ← المشروع الوحيد (WinExe — الناتج PCCare.exe)
   │  app.manifest
   │  pccare.settings.json
   ├─ Fonts                     ← ملفات Readex Pro (اختياري)
   ├─ Native                    ← كل P/Invoke في مكان واحد (SafeHandle)
   ├─ Core                      ← المنطق كله — ممنوع يستخدم أي حاجة من WinForms
   │   ├─ Common     العقد الموحد IMaintenanceModule، اللوج، الإعدادات، تشغيل الأدوات
   │   ├─ Monitoring المعالج/الذاكرة/الديسك/الشبكة، البروسيسات، كاشف التسريب
   │   ├─ Memory     تفريغ Standby (يدوي/مشروط)، Trim لبروسيس واحد
   │   ├─ Cleanup    تنظيف الملفات (Dry-Run أولًا)، DISM
   │   ├─ Startup    جرد Run/RunOnce/Startup/Tasks/Services + تعطيل قابل للتراجع
   │   ├─ Health     صحة الديسكات SMART، المساحات، خطة الطاقة، المؤشر، TRIM/Defrag
   │   ├─ Safety     نقطة استعادة + سجل التغييرات (للتراجع)
   │   ├─ Reports    تقرير JSON + HTML + summary.csv
   │   └─ Scheduling المهمة المجدولة الأسبوعية (SYSTEM)
   ├─ Cli                       ← الوضع الصامت (خيارات سطر الأوامر + التنفيذ)
   │   └─ Update     التحقق من GitHub + التنزيل + التحقق من البصمة
   └─ UI                        ← WinForms عربي RTL (Program.cs + MainForm + الصفحات + نافذة التحديث + SmokeTest)
```

الفصل بالفولدرات والـ namespaces (`AjyalPcCare.Native` / `AjyalPcCare.Core.*` / `AjyalPcCare.Cli` / `AjyalPcCare.UI.*`) بدل مشاريع منفصلة.
القاعدة الوحيدة اللي لازم تتحافظ عليها يدويًا: **ملفات Core و Native ما تستخدمش أي نوع من WinForms**.

**مفيش ولا حزمة NuGet خارجية** — عدادات الأداء عن طريق PDH مباشرة، الخدمات عن طريق PowerShell/sc، اللوج مكتوب يدوي.

## العقد الموحد

كل وحدة بتطبق `IMaintenanceModule`:

```
AnalyzeAsync()  →  AnalysisResult (= Preview: عناصر بأحجام حقيقية + تحذيرات)
                →  المستخدم يختار (Selected)
ExecuteAsync()  →  ExecutionResult (المحرر فعليًا، المتخطى، الأخطاء، المدة)
```

`ModuleRunner.SafeAnalyzeAsync / SafeExecuteAsync` بيلفوا أي وحدة — ولا Exception بتطلع برا.

## البناء والتشغيل

المتطلبات: Visual Studio 2026 (أو .NET 10 SDK) على ويندوز.

- **تطوير:** افتح `PCCare.sln` واضغط F5 (Visual Studio لازم يكون مفتوح **كمسؤول**، لأن الـ manifest بيطلب صلاحيات مسؤول لحسابات الـ Admin).

## نشر إصدار جديد (نفس نظام «مسار» بالظبط)

**خطوتين بس:**
1. اكتب الجديد في `ReleaseNotes.md` واحفظ.
2. دبل كليك على **`publish.cmd`** — أو من Visual Studio: **Publish ← بروفايل GitHub**.

اللي بيحصل لوحده:
1. Commit + Push لـ GitHub.
2. GitHub Actions (`release.yml`) يبني، ويعمل **فحص فتح** (`--smoke-test`: يفتح الواجهة وكل الصفحات) — لو وقع الإصدار مايتنشرش والسكريبت يقولك فين الخطأ.
3. ينشر الإصدار الرسمي `PCCare-Setup-1.0.N.exe` (N = رقم مرة النشر)، و**يمسح الإصدارات القديمة** — على GitHub بيفضل آخر إصدار بس.
4. السكريبت ينزّل المثبّت ويتأكد من بصمته (sha256)، ويحطه في **`E:\CLEANER\Releases`** ويمسح أي مثبّت قديم —
   **الفولدر فيه ملف واحد بس دايمًا: آخر نسخة**، وده اللي تنقله لأي جهاز جديد.
5. الأجهزة المتثبت عليها البرنامج تشوف «فيه تحديث» في أول تحقق.

لو ماكتبتش جديد في `ReleaseNotes.md` السكريبت مش هينشر (بيفتحلك الملف تكتب فيه).
ولو الإصدار اتنشر بس المثبّت مانزلش (النت قطع مثلًا)، شغّل `publish.cmd` تاني — هينزّله بس.

**مثبّت للتجربة على جهازك بس** (مش للمستخدمين): `build.cmd` ← `Releases\test\PCCare-Setup-test-التاريخ.exe`
(محتاج Inno Setup: `winget install JRSoftware.InnoSetup`). رقمه `1.0.0` فالجهاز اللي يتثبت عليه بيعرض التحديث للرسمي على طول.

**على الأجهزة:**
- **الواجهة:** بعد الفتح بـ 6 ثواني بتسأل GitHub عن آخر إصدار. لو أحدث: نافذة فيها الجديد ← "حدّث دلوقتي" ينزّل المثبّت ويتأكد منه
  (الرابط من نفس المستودع + الحجم + بصمة sha256) ← البرنامج يقفل ← التحديث يتثبت ← البرنامج يفتح تاني لوحده.
  "تخطّى الإصدار ده" بيوقف النافذة لحد الإصدار اللي بعده (الزرار الأصفر في القايمة بيفضل موجود).
- **المعامل:** المهمة المجدولة فيها `/update` (لو "تحديث الأداة تلقائيًا" متعلّم في صفحة وضع المعامل):
  بعد الصيانة والتقرير بتتحقق وتثبّت **صامت** (`/VERYSILENT`) من غير أي نافذة. أو يدويًا على كل الأجهزة عن طريق Veyon: `PCCare.exe /update`.
- التحديث مش بيلمس: `pccare.settings.json`، واللوج، والتقارير، وسجل التغييرات، والمهمة المجدولة.

**المستودع:** `waelMowis/AjyalPcCare` (في `PCCare.csproj` ← `PcGitHubRepo`) — لازم يكون **Public** عشان الأجهزة تنزّل من غير تسجيل دخول.
تقدر تغيّره من غير إعادة بناء: `pccare.settings.json` ← `Update` ← `Repo`.

**تنظيف الإصدارات:** بيحصل لوحده بعد كل نشر. ولو احتجته بإيدك: GitHub ← Actions ← «تنظيف الإصدارات» ← Run workflow.

## الصلاحيات

الـ manifest على `highestAvailable` (مش `requireAdministrator`) — والسبب موضح جوه `app.manifest`:
- حساب Admin ← UAC وصلاحيات كاملة.
- حساب طالب عادي ← يفتح في **وضع القراءة فقط** (مراقبة وفحص بس) مع زرار "إعادة التشغيل كمسؤول".

## سطر الأوامر (وضع المعامل)

```bat
PCCare.exe /silent
PCCare.exe /silent /clean:temp,browser,recycle /purge-standby /report:\\server\share\reports
PCCare.exe /silent /clean:all /optimize /dism /min-age:48
PCCare.exe /install-schedule
PCCare.exe /remove-schedule
PCCare.exe /help
```

| الخيار | المعنى |
|---|---|
| `/silent` | بدون واجهة. لو مفيش إجراءات صريحة ← يستخدم قسم `Lab` في الإعدادات |
| `/clean:` | `temp, wintemp, browser, recycle, wu, do, dumps, all` |
| `/purge-standby[:low]` | تفريغ Standby (low = الأولوية المنخفضة بس) |
| `/optimize` | TRIM للـ SSD / Defrag للـ HDD |
| `/dism` | DISM /StartComponentCleanup |
| `/min-age:N` | أقل عمر بالساعات للملفات المؤقتة اللي تتمسح (افتراضي 24) |
| `/report:path` | مكان التقارير (يقبل مسار شبكة) — لو فشل يحفظ محليًا |
| `/no-health` | من غير فحص الصحة في التقرير |
| `/update` | التحقق من التحديثات على GitHub وتثبيتها صامت (لوحده أو بعد الصيانة مع `/silent`) |

أكواد الخروج: `0` نجاح، `1` أخطاء جزئية، `2` فشل، `3` نسخة تانية شغالة، `4` خيارات غلط، `5` محتاج Admin.

### النشر على المعمل
1. ثبّت `PCCare-Setup-x.y.z.exe` على كل جهاز. صامت عن طريق Veyon أو GPO: `PCCare-Setup-1.0.5.exe /VERYSILENT /SUPPRESSMSGBOXES`
   (بيتثبت في `C:\Program Files\AJYAL PC Care`).
2. ظبط الإعدادات من صفحة "وضع المعامل" على جهاز واحد واحفظ، وانسخ `pccare.settings.json` لنفس الفولدر على باقي الأجهزة
   (المثبّت مش بيكتب فوقه في التحديثات).
3. على كل جهاز: `"C:\Program Files\AJYAL PC Care\PCCare.exe" /install-schedule`
4. التقارير: ملف HTML + JSON لكل جهاز، و`summary.csv` فيه سطر لكل جهاز في كل تشغيل.

> المهمة بتشتغل بحساب **SYSTEM** — على الشبكة بتظهر بهوية الجهاز `DOMAIN\PC$`.
> لازم المجلد المشترك يدّي صلاحية **Modify** لـ `Domain Computers`. في Workgroup الأسهل التقارير تتحفظ محليًا.

## مؤشر الصحة (قواعد معلنة)

يبدأ من 100 ويتخصم:

| القاعدة | الشرط | الخصم |
|---|---|---|
| R1 | مساحة درايف النظام < 5% / < 15% | 35 / 20 |
| R2 | درايف تاني < 5% | 10 (أقصى 20) |
| R3 | Commit > 90% / > 80% | 20 / 10 |
| R4 | ديسك Unhealthy / Warning | 40 / 20 |
| R5 | ويندوز على HDD | 10 |
| R6 | Startup مفعّل > ضعف الحد / > الحد | 15 / 10 |
| R7 | Uptime > 7 أيام | 5 |
| R8 | آخر تحديث > 60 يوم | 10 |
| R9 | خطة توفير الطاقة | 5 |

كل الحدود قابلة للتعديل من `pccare.settings.json` (قسم `Health`).

## الأمان والتراجع
- نقطة استعادة قبل أول تعديل على Startup/الخدمات في كل جلسة (ويندوز بيسمح بنقطة كل 24 ساعة — لو اتخطت بنبلغك).
- التعطيل بآلية `StartupApproved` نفسها بتاعة Task Manager — المدخل الأصلي ما بيتمسحش.
- الخدمات بتتحول لـ **Manual** مش Disabled، ونوعها الأصلي بيتحفظ.
- كل تغيير في `changes.json` ويقدر يترجع من صفحة "السجل والتراجع".
- اللوج اليومي في `logs\pccare-yyyyMMdd.log` (بيتمسح بعد 30 يوم).

## أماكن الملفات
- الإعدادات: `pccare.settings.json` جنب الـ exe.
- اللوج والسجل والتقارير المحلية: `%ProgramData%\AjyalPcCare` لو متثبت في Program Files، وجنب الـ exe لو شغال من فولدر عادي (تطوير).
