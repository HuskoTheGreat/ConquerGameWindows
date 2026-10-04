@echo off
rem Builds a standalone ConquerLocal.exe in local\dist that runs on Windows without .NET installed.
dotnet publish "%~dp0Conquer.Local" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0dist"
if errorlevel 1 exit /b 1
echo.
echo Done: %~dp0dist\ConquerLocal.exe
