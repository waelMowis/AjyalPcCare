#requires -Version 5.1
<#
  نشر إصدار جديد من AJYAL PC Care — خطوة واحدة (نفس نظام «مسار»):

    1. يتأكد إنك كتبت الجديد في ReleaseNotes.md
    2. Commit + Push لـ GitHub
    3. يستنى GitHub Actions يبني ويفحص (smoke test) وينشر الإصدار
       (والإصدارات القديمة على GitHub بتتمسح لوحدها — بيفضل آخر واحد بس)
    4. ينزّل المثبّت الجديد ويتأكد من بصمته، ويحطه في فولدر Releases
       ويمسح أي مثبّت قديم — الفولدر فيه ملف واحد بس دايمًا: آخر نسخة

  التشغيل: دبل كليك على publish.cmd  (أو من Visual Studio: Publish ← بروفايل GitHub)
#>
param(
    [switch]$NoPrompt   # من Visual Studio: من غير "اضغط Enter"
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest أسرع بكتير من غير شريط التقدم
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$releases = Join-Path $root 'Releases'
$notesFile = Join-Path $root 'ReleaseNotes.md'

function Step([string]$text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Ok([string]$text) { Write-Host "   ✓ $text" -ForegroundColor Green }
function Info([string]$text) { Write-Host "   $text" }
function Done([int]$code) {
    if (-not $NoPrompt) { Write-Host ''; Read-Host 'اضغط Enter للخروج' | Out-Null }
    exit $code
}
function Fail([string]$text) {
    Write-Host ''
    Write-Host "✖ $text" -ForegroundColor Red
    Done 1
}

# ---------------------------------------------------------------- المستودع
$repo = 'waelMowis/AjyalPcCare'
$csproj = Get-Content (Join-Path $root 'PCCare\PCCare.csproj') -Raw -Encoding UTF8
if ($csproj -match '<PcGitHubRepo>\s*([^<\s]+)\s*</PcGitHubRepo>') { $repo = $Matches[1] }
$headers = @{ 'User-Agent' = 'AjyalPcCare-Publisher'; 'Accept' = 'application/vnd.github+json' }

function Get-LatestRelease {
    try {
        return Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers $headers -TimeoutSec 20 -ErrorAction Stop
    }
    catch {
        $resp = $_.Exception.Response
        if ($resp -and [int]$resp.StatusCode -eq 404) { return $null }   # مفيش إصدارات لسه
        throw
    }
}

function Get-ReleaseRun([string]$sha) {
    try {
        $runs = Invoke-RestMethod "https://api.github.com/repos/$repo/actions/runs?head_sha=$sha&per_page=20" -Headers $headers -TimeoutSec 20 -ErrorAction Stop
        return $runs.workflow_runs | Where-Object { $_.path -like '*release.yml' } | Select-Object -First 1
    }
    catch { return $null }
}

# ---------------------------------------------------------------- تنزيل المثبّت لـ Releases (ملف واحد بس)
function Save-LatestSetup($latest) {
    Step '4) تنزيل المثبّت الجديد لفولدر Releases'
    $asset = $latest.assets | Where-Object { $_.name -like 'PCCare-Setup-*.exe' } | Select-Object -First 1
    if (-not $asset) { Fail 'الإصدار اتنشر من غير مثبّت!' }

    New-Item -ItemType Directory -Force $releases | Out-Null
    $tmp = Join-Path $releases ($asset.name + '.download')
    try {
        Invoke-WebRequest $asset.browser_download_url -OutFile $tmp -Headers @{ 'User-Agent' = 'AjyalPcCare-Publisher' } -UseBasicParsing -ErrorAction Stop
    }
    catch { Fail "تنزيل المثبّت فشل: $($_.Exception.Message) — شغّل النشر تاني (الإصدار نفسه اتنشر خلاص — هيتنزّل بس)." }

    $size = (Get-Item $tmp).Length
    if ($asset.size -and $size -ne [long]$asset.size) { Remove-Item $tmp -Force; Fail 'الملف اتنزّل ناقص. شغّل النشر تاني (الإصدار نفسه اتنشر خلاص — هيتنزّل بس).' }
    if ($asset.digest -and $asset.digest -like 'sha256:*') {
        $expected = $asset.digest.Substring(7).ToLowerInvariant()
        $actual = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) { Remove-Item $tmp -Force; Fail 'بصمة الملف مش مطابقة لـ GitHub — اتمسح.' }
        Ok 'البصمة (sha256) مطابقة'
    }

    # الفولدر فيه ملف واحد بس: آخر نسخة
    Get-ChildItem $releases -File -Filter '*.exe' | Where-Object { $_.Name -ne $asset.name } | ForEach-Object {
        Remove-Item $_.FullName -Force -ErrorAction Stop
        Info "اتمسح القديم: $($_.Name)"
    }
    $final = Join-Path $releases $asset.name
    Move-Item $tmp $final -Force -ErrorAction Stop
    Ok "$($asset.name)  ($([math]::Round($size / 1MB)) ميجا)"
    return $final
}

Write-Host ''
Write-Host '  AJYAL PC Care — نشر إصدار جديد' -ForegroundColor White
Write-Host "  المستودع: https://github.com/$repo"

# ---------------------------------------------------------------- 0) git
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Fail 'git مش متثبت. ثبّته من git-scm.com أو: winget install Git.Git'
}
# أوامر git بتكتب على stderr عادي — في PowerShell 5.1 مع Stop ده بيتعامل كخطأ، فبنحكم بـ $LASTEXITCODE بدل كده
$ErrorActionPreference = 'Continue'
if (-not (git remote 2>$null | Select-String -SimpleMatch 'origin')) {
    git remote add origin "https://github.com/$repo.git"
}

# ---------------------------------------------------------------- 1) ملاحظات الإصدار
Step '1) ملاحظات الإصدار (ReleaseNotes.md)'
$hasUpstream = $true
git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { $hasUpstream = $false }

$notesUncommitted = [bool](git status --porcelain -- ReleaseNotes.md)
$notesUnpushed = $false
if ($hasUpstream) { $notesUnpushed = [bool](git log '@{u}..HEAD' --oneline -- ReleaseNotes.md) }
else { $notesUnpushed = $true }   # أول رفع للمستودع: الملاحظات جديدة بطبيعتها

if (-not ($notesUncommitted -or $notesUnpushed)) {
    # الإصدار اتنشر قبل كده بس المثبّت مش في الفولدر (النت قطع مثلًا)؟ ننزّله بس
    $published = $null
    try { $published = Get-LatestRelease } catch { }
    $publishedAsset = if ($published) { $published.assets | Where-Object { $_.name -like 'PCCare-Setup-*.exe' } | Select-Object -First 1 } else { $null }
    if ($publishedAsset -and -not (Test-Path (Join-Path $releases $publishedAsset.name))) {
        Info "مفيش ملاحظات جديدة، بس آخر إصدار ($($published.tag_name)) مش موجود في Releases — هنزّله بس."
        $final = Save-LatestSetup $published
        Write-Host ''
        Write-Host "✓ المثبّت: $final" -ForegroundColor Green
        if (-not $NoPrompt) { Start-Process explorer.exe "/select,`"$final`"" }
        Done 0
    }
    Info 'GitHub بيبني إصدار جديد بس لما ReleaseNotes.md يتغيّر — والملف ده زي ما هو من آخر نشر.'
    Info 'هفتحهولك: اكتب فيه الجديد في الإصدار ده (مكان الكلام القديم)، احفظ، وشغّل النشر تاني.'
    if (-not $NoPrompt) { Start-Process notepad.exe $notesFile }
    Fail 'مفيش ملاحظات جديدة للإصدار.'
}
$title = (Get-Content $notesFile -Encoding UTF8 | Where-Object { $_.Trim() -ne '' } | Select-Object -First 1)
$title = ($title -replace '^[#\-\*\s]+', '').Trim()
if ($title.Length -gt 70) { $title = $title.Substring(0, 70) }
Ok "عنوان الإصدار: $title"

# ---------------------------------------------------------------- 2) Commit + Push
Step '2) رفع التعديلات على GitHub'
$previous = $null
try { $previous = Get-LatestRelease } catch { Info "تحذير: مقدرتش أقرا آخر إصدار من GitHub ($($_.Exception.Message)) — هكمّل" }
$previousTag = if ($previous) { $previous.tag_name } else { '' }
if ($previousTag) { Info "الإصدار الحالي على GitHub: $previousTag" } else { Info 'مفيش إصدارات على GitHub لسه — ده هيبقى أول إصدار' }

git add -A
if (git status --porcelain) {
    git commit -q -m "Release: $title"
    if ($LASTEXITCODE -ne 0) { Fail 'الـ commit فشل.' }
    Ok 'Commit'
}
git push -u origin HEAD
if ($LASTEXITCODE -ne 0) { Fail 'الرفع (push) فشل. اتأكد إنك مسجّل دخول على GitHub في git وإن المستودع موجود.' }
$sha = (git rev-parse HEAD).Trim()
Ok "اترفع ($($sha.Substring(0, 7)))"

# ---------------------------------------------------------------- 3) مستني GitHub
Step '3) GitHub بيبني ويفحص وينشر (عادة 4 لـ 8 دقايق)'
Info "تقدر تتابع هنا: https://github.com/$repo/actions"
$deadline = (Get-Date).AddMinutes(35)
$latest = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 20
    try { $latest = Get-LatestRelease } catch { $latest = $null }
    if ($latest -and $latest.tag_name -ne $previousTag) { break }
    $latest = $null

    $run = Get-ReleaseRun $sha
    if ($run -and $run.status -eq 'completed' -and $run.conclusion -ne 'success') {
        Fail "بناء الإصدار على GitHub فشل ($($run.conclusion)). افتح التفاصيل: $($run.html_url)"
    }
    $state = if ($run) { "$($run.status)" } else { 'لسه مابدأش' }
    Write-Host "   … $(Get-Date -Format 'HH:mm:ss')  حالة النشر: $state"
}
if (-not $latest) { Fail "عدّى 35 دقيقة ومفيش إصدار جديد. شوف: https://github.com/$repo/actions" }
Ok "اتنشر $($latest.tag_name): $($latest.html_url)"

$final = Save-LatestSetup $latest

Write-Host ''
Write-Host "✓ خلاص: الإصدار $($latest.tag_name) على GitHub، والأجهزة هتشوفه في التحقق الجاي من التحديثات." -ForegroundColor Green
Write-Host "  المثبّت: $final"
if (-not $NoPrompt) { Start-Process explorer.exe "/select,`"$final`"" }
Done 0
