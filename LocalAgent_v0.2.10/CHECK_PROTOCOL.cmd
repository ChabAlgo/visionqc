@echo off
echo Current visionqc-agent protocol registration:
reg query "HKCU\Software\Classes\visionqc-agent\shell\open\command" /ve
echo.
echo Running Agent status:
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { $s=Invoke-RestMethod -Uri 'http://127.0.0.1:17891/api/status' -TimeoutSec 2; Write-Host ('AgentVersion: ' + $s.agentVersion); Write-Host ('EngineVersion: ' + $s.engineVersion) } catch { Write-Host 'Agent not running.' }"
pause
