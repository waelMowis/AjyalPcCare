@echo off
chcp 65001 >nul
cd /d "%~dp0"
REM ==== AJYAL PC Care - نشر إصدار جديد ====
REM 1) اكتب الجديد في ReleaseNotes.md واحفظ
REM 2) دبل كليك على الملف ده
REM    ← يرفع على GitHub، يستنى البناء والفحص والنشر، وينزّل آخر مثبّت في فولدر Releases (ملف واحد بس)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\publish-release.ps1"
