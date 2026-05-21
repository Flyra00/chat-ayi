@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0clean-build-artifacts.ps1" %*

endlocal
