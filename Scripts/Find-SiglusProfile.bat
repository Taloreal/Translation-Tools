@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Find-SiglusProfile.ps1" %1
echo.
pause
