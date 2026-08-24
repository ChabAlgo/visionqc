@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo ============================================================
echo  VisionQC Local Agent v1.2.2 - Build x64 Release
echo ============================================================
set "COGNEX=%COGNEX_VPDL_DLL_DIR%"
if not "%~1"=="" set "COGNEX=%~1"
if not defined COGNEX if exist "C:\Program Files\Cognex\VisionPro Deep Learning\4.2\Cognex Deep Learning Studio\ViDi.NET.Local.dll" set "COGNEX=C:\Program Files\Cognex\VisionPro Deep Learning\4.2\Cognex Deep Learning Studio"
if not defined COGNEX if exist "C:\Program Files\Cognex\VisionPro Deep Learning\4.1\Cognex Deep Learning Studio\ViDi.NET.Local.dll" set "COGNEX=C:\Program Files\Cognex\VisionPro Deep Learning\4.1\Cognex Deep Learning Studio"
if not defined COGNEX if exist "C:\Program Files\Cognex\VisionPro Deep Learning\4.0\Cognex Deep Learning Studio\ViDi.NET.Local.dll" set "COGNEX=C:\Program Files\Cognex\VisionPro Deep Learning\4.0\Cognex Deep Learning Studio"
if not defined COGNEX if exist "C:\Program Files\Cognex\VisionPro Deep Learning\5.0\Cognex Deep Learning Studio\ViDi.NET.Local.dll" set "COGNEX=C:\Program Files\Cognex\VisionPro Deep Learning\5.0\Cognex Deep Learning Studio"
if not exist "%COGNEX%\ViDi.NET.Local.dll" (
  echo [ERROR] VPDL SDK DLL not found:
  echo %COGNEX%
  echo.
  echo Usage: BUILD_RELEASE_x64.cmd "D:\path\to\Cognex Deep Learning Studio"
  echo Or set COGNEX_VPDL_DLL_DIR before running this script.
  pause
  exit /b 1
)
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
  echo [ERROR] vswhere.exe not found. Install Visual Studio 2022 Build Tools.
  pause
  exit /b 1
)
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
if not defined MSBUILD (
  echo [ERROR] MSBuild not found.
  pause
  exit /b 1
)
"%MSBUILD%" VisionQC.LocalAgent.csproj /m /t:Rebuild /p:Configuration=Release /p:Platform=x64 /p:CognexDir="%COGNEX%"
if errorlevel 1 (
  echo.
  echo [FAILED]
  pause
  exit /b 1
)
echo.
echo [OK] bin\x64\Release\VisionQC.LocalAgent.exe
echo Next: run REGISTER_PROTOCOL.cmd once.
pause
