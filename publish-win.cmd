@echo off
:: publish-win.cmd — Build a self-contained single-file release for Windows x64.
:: Run this on a Windows x64 machine.
::
:: Usage:
::   publish-win.cmd                  -> publish\win-x64\
::   publish-win.cmd .\my\output      -> .\my\output\
::
:: Package contents:
::   DataEntry.exe           - the compiler (no .NET install required on target)
::   libonigwrap.dll         - native regex helper (must live beside DataEntry.exe)
::   Samples\*.def           - sample form definitions
::   MANUAL.md               - user manual
::   sample.def              - quick-start example
::   DataEntry-win-x64.zip   - zip of the above, ready to distribute

setlocal EnableDelayedExpansion

set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%src\DataEntry\DataEntry.vbproj"

if "%~1"=="" (
    set "OUTPUT_DIR=%SCRIPT_DIR%publish\win-x64"
) else (
    set "OUTPUT_DIR=%~1"
)

echo.
echo ========================================================
echo   DataEntry  --  Windows x64 publish
echo   Output : %OUTPUT_DIR%
echo ========================================================
echo.

echo Cleaning previous output...
if exist "%OUTPUT_DIR%" rd /s /q "%OUTPUT_DIR%"
mkdir "%OUTPUT_DIR%"

echo Publishing...
dotnet publish "%PROJECT%" ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:DebugType=none ^
  -p:DebugSymbols=false ^
  --output "%OUTPUT_DIR%"
if %ERRORLEVEL% neq 0 ( echo. & echo [FAIL] dotnet publish failed. & endlocal & exit /b 1 )

echo Copying docs...
if exist "%SCRIPT_DIR%MANUAL.md"  copy /Y "%SCRIPT_DIR%MANUAL.md"  "%OUTPUT_DIR%\MANUAL.md"  >nul
if exist "%SCRIPT_DIR%sample.def" copy /Y "%SCRIPT_DIR%sample.def" "%OUTPUT_DIR%\sample.def" >nul

echo Creating archive...
set "ZIP=%SCRIPT_DIR%publish\DataEntry-win-x64.zip"
if exist "%ZIP%" del /f "%ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path '%OUTPUT_DIR%\*' -DestinationPath '%ZIP%' -Force"
if %ERRORLEVEL% neq 0 ( echo [WARN] Archive creation failed ^(non-fatal^). ) else ( echo Archive : %ZIP% )

echo.
echo Done.
echo   Executable  : %OUTPUT_DIR%\DataEntry.exe
echo   Runtime dep : %OUTPUT_DIR%\libonigwrap.dll
echo.
endlocal
