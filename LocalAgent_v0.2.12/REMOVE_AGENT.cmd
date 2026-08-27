@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================================
echo  VisionQC Local Agent - Remove registration and stop
echo ============================================================
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-RestMethod -Uri 'http://127.0.0.1:17891/api/agent/unregister' -Method Post -ContentType 'text/plain' -Body '{}' -TimeoutSec 3 | Out-Null; Write-Host '[OK] Agent registration removed and Agent stopped.' -ForegroundColor Green; exit 0 } catch {}"
set "EXE=%~dp0Launcher\bin\x64\Release\VisionQC.LocalAgent.exe"
if exist "%EXE%" "%EXE%" --unregister
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-RestMethod -Uri 'http://127.0.0.1:17891/api/agent/exit' -Method Post -ContentType 'text/plain' -Body '{}' -TimeoutSec 2 | Out-Null } catch {}"
echo.
echo Local files were NOT deleted.
echo Delete this Agent folder manually if no longer needed.
pause
