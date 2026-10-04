@echo off
setlocal
rem Builds installer\output\ConquerSetup-<version>.exe, a setup program that installs the Conquer launcher with
rem Start menu and desktop shortcuts and an uninstaller. The launcher downloads the newest game build from GitHub
rem each time it starts, so the setup only needs rebuilding when the launcher itself changes.
rem Set UPDATE_URL to download builds from somewhere other than this repository's game-latest release.
rem Needs the .NET 8 SDK and Inno Setup 6 (https://jrsoftware.org/isdl.php, or: winget install JRSoftware.InnoSetup).
rem Usage: build-installer.cmd [version]

set "VERSION=%~1"
if "%VERSION%"=="" set "VERSION=1.0.0"
set "HERE=%~dp0"
set "PUBLISH=%HERE%publish"

set "ISCC="
for %%P in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe") do (
  if exist "%%~P" set "ISCC=%%~P"
)
if not defined ISCC (
  where iscc >nul 2>nul && set "ISCC=iscc"
)
if not defined ISCC (
  echo Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup
  exit /b 1
)

if exist "%PUBLISH%" rmdir /s /q "%PUBLISH%"
set "URLPROP="
if defined UPDATE_URL set "URLPROP=-p:UpdateUrl=%UPDATE_URL%"
dotnet publish "%HERE%Conquer.Launcher" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:DebugType=none -p:Version=%VERSION% %URLPROP% -o "%PUBLISH%"
if errorlevel 1 exit /b 1

"%ISCC%" /Q /DAppVersion=%VERSION% "%HERE%Conquer.iss"
if errorlevel 1 exit /b 1

echo.
echo Done: %HERE%output\ConquerSetup-%VERSION%.exe
