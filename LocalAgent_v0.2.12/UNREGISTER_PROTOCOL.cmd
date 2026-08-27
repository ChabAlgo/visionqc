@echo off
cd /d "%~dp0"
set "EXE=%~dp0Launcher\bin\x64\Release\VisionQC.LocalAgent.exe"
if exist "%EXE%" "%EXE%" --unregister
pause
