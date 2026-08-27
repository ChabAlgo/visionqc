@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "EXE=%~dp0Launcher\bin\x64\Release\VisionQC.LocalAgent.exe"

if not exist "%EXE%" (
  echo [ERROR] Build first: BUILD_RELEASE_x64.cmd
  pause
  exit /b 1
)

echo [1/3] Stopping any Agent currently using 127.0.0.1:17891...
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-RestMethod -Uri 'http://127.0.0.1:17891/api/agent/exit' -Method Post -ContentType 'text/plain' -Body '{}' -TimeoutSec 2 | Out-Null } catch {}"
timeout /t 1 /nobreak >nul

echo [2/3] Registering THIS Agent executable...
"%EXE%" --register

echo [3/3] Current protocol command:
reg query "HKCU\Software\Classes\visionqc-agent\shell\open\command" /ve
echo.
echo [OK] visionqc-agent://start now points to:
echo %EXE%
echo.
echo Next: RUN_AGENT.cmd
pause
