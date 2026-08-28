@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================================
echo  VisionQC Local Agent v1.3.1 - Multi VPDL Worker Build
echo ============================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0BUILD_VPDL_WORKERS.ps1"
if errorlevel 1 (
  echo.
  echo [FAILED] VPDL Worker build
  pause
  exit /b 1
)
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo [FAILED] vswhere.exe not found
  pause
  exit /b 1
)
for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%I"
if not defined MSBUILD (
  echo [FAILED] MSBuild.exe not found
  pause
  exit /b 1
)
"%MSBUILD%" "%~dp0OfflineInstaller\VisionQC.AgentInstaller.csproj" /m /t:Rebuild /p:Configuration=Release /p:Platform=x64
if errorlevel 1 (
  echo [FAILED] Offline Installer build
  pause
  exit /b 1
)
set "INSTALLER=%~dp0OfflineInstaller\bin\x64\Release\VisionQC_Agent_Installer.exe"
set "DOWNLOAD=%~dp0..\downloads\VisionQC_Agent_Installer_v1.3.1.exe"
if not exist "%~dp0..\downloads" mkdir "%~dp0..\downloads"
copy /y "%INSTALLER%" "%DOWNLOAD%" >nul
echo.
echo [OK] Launcher\bin\x64\Release\VisionQC.LocalAgent.exe
echo [OK] Launcher\bin\x64\Release\Workers\{API}\VisionQC.VpdlWorker.exe
echo [OK] %DOWNLOAD%
pause
