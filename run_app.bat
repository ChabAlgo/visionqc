@echo off
cd /d "%~dp0"
title VisionQC DirectExport v4.4.10 STATIC
cls
echo ============================================================
echo  VisionQC DirectExport v4.4.10
echo  STATIC RUN MODE - no npm / no vite / no rollup
echo  RUNNING FOLDER: %CD%
echo ============================================================
echo.
where node >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Node.js was not found.
  echo Install Node.js or run this on the PC where Node is already installed.
  pause
  exit /b 1
)
node server_static.mjs
echo.
echo [INFO] Server closed.
pause
