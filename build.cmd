@echo off
chcp 65001 >nul
cd /d "%~dp0"
REM ==== AJYAL PC Care - مثبّت للتجربة على جهازك بس (مش للنشر) ====
REM الناتج: Releases\test\PCCare-Setup-test-التاريخ.exe  (محتاج Inno Setup: winget install JRSoftware.InnoSetup)
REM للنشر الرسمي للمستخدمين: publish.cmd
dotnet publish PCCare\PCCare.csproj -p:PublishProfile=Installer
echo.
pause
