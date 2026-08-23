@echo off

setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%src\DataEntry\DataEntry.vbproj"

if "%~1"=="" (
    set "OUTPUT_DIR=%SCRIPT_DIR%publish\win-x64"
) else (
    set "OUTPUT_DIR=%~1"
)

echo Publishing for Windows x64 to: %OUTPUT_DIR%

dotnet publish "%PROJECT%" ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  -p:PublishAot=true ^
  -p:StripSymbols=true ^
  --output "%OUTPUT_DIR%"

if %ERRORLEVEL% neq 0 (
    echo Publish failed.
    exit /b %ERRORLEVEL%
)

echo Done.
echo.
echo Published files:
dir /b "%OUTPUT_DIR%\*.exe" 2>nul
