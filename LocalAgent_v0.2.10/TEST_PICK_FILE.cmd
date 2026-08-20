@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "$body = '{\"initialPath\":\"\"}'; try { $r = Invoke-RestMethod -Uri 'http://127.0.0.1:17891/api/pick/file' -Method Post -ContentType 'text/plain;charset=UTF-8' -Body $body; $r | ConvertTo-Json -Depth 5 } catch { Write-Host '[ERROR]' $_.Exception.Message -ForegroundColor Red }"
pause
