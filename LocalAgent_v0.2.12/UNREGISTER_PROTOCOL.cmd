@echo off
cd /d "%~dp0"
set "EXE=%~dp0bin\x64\Release\VisionQC.LocalAgent.exe"
if exist "%EXE%" "%EXE%" --unregister
pause
