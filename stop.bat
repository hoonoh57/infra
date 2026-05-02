@echo off
chcp 65001 > nul
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM stop.bat
REM MainApp / CybosServer / KiwoomServer 모든 인스턴스 종료
REM 관리자 권한으로 열린 서버까지 종료하기 위해 자체 관리자 권한 상승
REM ============================================================

net session >nul 2>&1
if not "%ERRORLEVEL%"=="0" (
    echo [INFO] Request administrator permission...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set "CYBOS_PROC=CybosServer.exe"
set "KIWOOM_PROC=KiwoomServer.exe"
set "MAIN_PROC=MainApp.exe"

echo.
echo ============================================================
echo  AutoTrading System STOP
echo ============================================================
echo.

call :KillProc "%MAIN_PROC%" "MainApp"
call :KillProc "%KIWOOM_PROC%" "KiwoomServer"
call :KillProc "%CYBOS_PROC%" "CybosServer"

echo.
echo [INFO] Re-check remaining processes...
tasklist /FI "IMAGENAME eq %MAIN_PROC%" 2>nul | find /I "%MAIN_PROC%" >nul && echo [WARN] MainApp still running.
tasklist /FI "IMAGENAME eq %KIWOOM_PROC%" 2>nul | find /I "%KIWOOM_PROC%" >nul && echo [WARN] KiwoomServer still running.
tasklist /FI "IMAGENAME eq %CYBOS_PROC%" 2>nul | find /I "%CYBOS_PROC%" >nul && echo [WARN] CybosServer still running.

echo.
echo ============================================================
echo  STOP COMPLETE
echo ============================================================
echo.
pause
endlocal
exit /b


:KillProc
set "PROC=%~1"
set "NAME=%~2"

tasklist /FI "IMAGENAME eq %PROC%" 2>nul | find /I "%PROC%" >nul
if errorlevel 1 (
    echo [SKIP] %NAME% not running.
    exit /b 0
)

echo [KILL] %NAME% - %PROC%
taskkill /F /T /IM "%PROC%" >nul 2>&1

timeout /t 1 /nobreak > nul

tasklist /FI "IMAGENAME eq %PROC%" 2>nul | find /I "%PROC%" >nul
if errorlevel 1 (
    echo [OK] %NAME% stopped.
) else (
    echo [WARN] %NAME% may still be running.
)

exit /b 0