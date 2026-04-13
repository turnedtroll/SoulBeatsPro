@echo off
setlocal

cd /d "%~dp0"

:: Read version from csproj
for /f "tokens=2 delims=<>" %%a in ('findstr "<Version>" RoBeatsPro.csproj') do set VERSION=%%a

if "%VERSION%"=="" (
    echo ERROR: Could not read version from csproj
    pause
    exit /b 1
)

echo.
echo === SoulBeats Pro Release Script ===
echo Version: v%VERSION%
echo.

:: Build
echo [1/3] Building...
dotnet publish -c Release --nologo
if errorlevel 1 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)

:: Copy exe to a clean name
copy /y "bin\Release\net8.0-windows\win-x64\publish\SoulBeatsPro.exe" "SoulBeatsPro.exe" >nul

:: Create GitHub release
echo [2/3] Creating GitHub release v%VERSION%...
"C:\Program Files\GitHub CLI\gh.exe" release create "v%VERSION%" "SoulBeatsPro.exe" --repo turnedtroll/SoulBeatsPro --title "v%VERSION%" --notes "version %VERSION%" --latest
if errorlevel 1 (
    echo ERROR: Release failed! Make sure you're logged in (gh auth login) and the tag doesn't already exist.
    pause
    exit /b 1
)

:: Cleanup
del /f "SoulBeatsPro.exe" >nul 2>&1

echo.
echo [3/3] Done! v%VERSION% is live.
echo Your friends will get the update automatically.
echo.
pause
