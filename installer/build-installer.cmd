@echo off
setlocal
rem Builds installer\output\CatanSetup-<version>.exe, a setup program that installs Catan with
rem Start menu and desktop shortcuts and an uninstaller. The game runs without .NET installed.
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
dotnet publish "%HERE%..\src\Catan.Client" -c Release -r win-x64 --self-contained true -p:DebugType=none -p:Version=%VERSION% -o "%PUBLISH%"
if errorlevel 1 exit /b 1

"%ISCC%" /Q /DAppVersion=%VERSION% "%HERE%Catan.iss"
if errorlevel 1 exit /b 1

echo.
echo Done: %HERE%output\CatanSetup-%VERSION%.exe
