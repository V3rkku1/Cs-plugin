@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0skin-test.ps1" on %*
pause
