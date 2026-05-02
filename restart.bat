@echo off
chcp 65001 > nul
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM restart.bat
REM CybosServer / KiwoomServer는 건드리지 않음
REM MainApp만 종료 후 관리자 권한으로 재실행
REM ============================================================

set "ROOT=E:\2026\infra"
set "MAIN_EXE=%ROOT%\MainApp\bin\x64\Debug\net481\MainApp.exe"
set "MAIN_PROC=MainApp.exe"

echo.
echo ============================================================
echo  AutoTrading System RESTART MAINAPP ONLY
echo ============================================================
echo.

if not exist "%MAIN_EXE%" (
    echo [ERROR] MainApp exe not found:
    echo         %MAIN_EXE%
    goto :END
)

echo [INFO] Servers are not touched.
echo [INFO] CybosServer / KiwoomServer will keep running.
echo.

tasklist /FI "IMAGENAME eq %MAIN_PROC%" 2>nul | find /I "%MAIN_PROC%" >nul
if not errorlevel 1 (
    echo [KILL] MainApp
    taskkill /F /T /IM "%MAIN_PROC%" >nul 2>&1
    timeout /t 2 /nobreak > nul
) else (
    echo [SKIP] MainApp not running.
)

echo [START] MainApp administrator mode
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%MAIN_EXE%' -WorkingDirectory '%~dp0' -Verb RunAs"

goto :END


:END
echo.
echo ============================================================
echo  RESTART COMPLETE
echo ============================================================
echo.
pause
endlocal