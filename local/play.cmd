@echo off
rem Plays Conquer on this computer. No server needed. Any options are passed on, e.g. play.cmd --players 4 --seed 42
dotnet run --project "%~dp0Conquer.Local" -c Release -- %*
