@echo off
setlocal
cd /d "%~dp0"
set PYTHONIOENCODING=utf-8
python tools/verify_all.py
if errorlevel 1 exit /b 1
echo Build and offline acceptance passed. Live game tests remain manual.
