@echo off
chcp 65001 > nul
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM start.bat
REM CybosServer / KiwoomServer / MainApp 실행
REM 서버가 이미 실행 중이면 MainApp만 실행
REM 서버가 없으면 CybosServer -> KiwoomServer -> MainApp 순서로 관리자 실행
REM ============================================================

set "ROOT=E:\2026\infra"

set "CYBOS_EXE=%ROOT%\CybosServer\bin\x86\Debug\net481\CybosServer.exe"
set "KIWOOM_EXE=%ROOT%\KiwoomServer\bin\x86\Debug\net481\KiwoomServer.exe"
set "MAIN_EXE=%ROOT%\MainApp\bin\x64\Debug\net481\MainApp.exe"

set "CYBOS_PROC=CybosServer.exe"
set "KIWOOM_PROC=KiwoomServer.exe"
set "MAIN_PROC=MainApp.exe"

echo.
echo ============================================================
echo  AutoTrading System START
echo ============================================================
echo ROOT=%ROOT%
echo.

call :CheckFile "%CYBOS_EXE%" "CybosServer"
if errorlevel 1 goto :END

call :CheckFile "%KIWOOM_EXE%" "KiwoomServer"
if errorlevel 1 goto :END

call :CheckFile "%MAIN_EXE%" "MainApp"
if errorlevel 1 goto :END

call :IsRunning "%CYBOS_PROC%"
set "CYBOS_RUNNING=%ERRORLEVEL%"

call :IsRunning "%KIWOOM_PROC%"
set "KIWOOM_RUNNING=%ERRORLEVEL%"

if "%CYBOS_RUNNING%"=="0" if "%KIWOOM_RUNNING%"=="0" (
    echo [OK] CybosServer / KiwoomServer already running.
    echo [RUN] MainApp only.
    call :StartAdmin "%MAIN_EXE%" "MainApp"
    goto :END
)

echo [INFO] One or more servers are not running.
echo [INFO] Start order: CybosServer -^> KiwoomServer -^> MainApp
echo.

if not "%CYBOS_RUNNING%"=="0" (
    call :StartAdmin "%CYBOS_EXE%" "CybosServer"
    timeout /t 5 /nobreak > nul
) else (
    echo [SKIP] CybosServer already running.
)

if not "%KIWOOM_RUNNING%"=="0" (
    call :StartAdmin "%KIWOOM_EXE%" "KiwoomServer"
    timeout /t 5 /nobreak > nul
) else (
    echo [SKIP] KiwoomServer already running.
)

call :StartAdmin "%MAIN_EXE%" "MainApp"

goto :END


:CheckFile
if not exist "%~1" (
    echo [ERROR] %~2 exe not found:
    echo         %~1
    exit /b 1
)
exit /b 0


:IsRunning
tasklist /FI "IMAGENAME eq %~1" 2>nul | find /I "%~1" >nul
if errorlevel 1 (
    exit /b 1
) else (
    exit /b 0
)


:StartAdmin
echo [START] %~2
echo         %~1
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~1' -WorkingDirectory '%~dp1' -Verb RunAs"
exit /b 0


:END
echo.
echo ============================================================
echo  START COMPLETE
echo ============================================================
echo.
pause
endlocal