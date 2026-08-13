@echo off
cd /d "%~dp0"
if not exist "node_modules\vite\bin\vite.js" call npm install
node "node_modules\vite\bin\vite.js" build
pause
