@echo off
rem Convenience wrapper: builds the Windows package (needs the .NET 8 SDK).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
