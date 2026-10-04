@echo off
rem Plays Catan on this computer. No server needed. Any options are passed on, e.g. play.cmd --players 4 --seed 42
dotnet run --project "%~dp0Catan.Local" -c Release -- %*
