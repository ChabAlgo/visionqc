@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-RestMethod -Uri 'http://127.0.0.1:17891/api/agent/exit' -Method Post -ContentType 'application/json' -Body '{}' | Out-Null; Write-Host '[OK] Agent exit requested.' -ForegroundColor Green } catch { Write-Host '[INFO] Agent is not running or could not be reached.' -ForegroundColor Yellow }"
timeout /t 1 /nobreak >nul
