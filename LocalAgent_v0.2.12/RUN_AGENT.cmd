@echo off
cd /d "%~dp0"
set "EXE=%~dp0bin\x64\Release\VisionQC.LocalAgent.exe"
if not exist "%EXE%" (
  echo [ERROR] Build first: BUILD_RELEASE_x64.cmd
  pause
  exit /b 1
)
start "" "%EXE%"
