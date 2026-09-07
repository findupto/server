@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ========================================
echo FindUpTo POS - local readiness check
 echo ========================================

set "FAIL=0"
set "SERVER_EXE=%~dp0artifacts\windows-server\FindUpTo.Pos.Server.exe"
set "POS_EXE=%~dp0artifacts\windows-pos-app\findupto_pos_mobile.exe"
set "APK=%~dp0artifacts\android\findupto_pos_mobile.apk"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [FAIL] dotnet SDK/runtime not found.
  set "FAIL=1"
) else echo [OK] dotnet available.

where flutter >nul 2>nul
if errorlevel 1 (
  echo [FAIL] Flutter not found.
  set "FAIL=1"
) else echo [OK] Flutter available.

if exist "%SERVER_EXE%" (echo [OK] Server EXE exists.) else (echo [FAIL] Server EXE missing: %SERVER_EXE% & set "FAIL=1")
if exist "%POS_EXE%" (echo [OK] Windows POS EXE exists.) else (echo [FAIL] Windows POS EXE missing: %POS_EXE% & set "FAIL=1")
if exist "%APK%" (echo [OK] Android APK exists.) else (echo [FAIL] Android APK missing: %APK% & set "FAIL=1")

if not exist "%SERVER_EXE%" goto :done

set "POS_JWT_KEY=findupto-local-development-jwt-key-please-change"
set "INITIAL_OWNER_PASSWORD=Owner-Test-Password-123!"
set "INITIAL_ADMIN_PASSWORD=Admin-Test-Password-123!"
set "INITIAL_WAITER_PASSWORD=Waiter-Test-Password-123!"
set "INITIAL_COUNTER_PASSWORD=Counter-Test-Password-123!"
set "ASPNETCORE_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=http://127.0.0.1:5000"
set "POS_ALLOW_SCHEMA_CREATE=true"

set "SERVER_LOG=%TEMP%\findupto-pos-check.log"
del /q "%SERVER_LOG%" >nul 2>nul

echo.
echo [SERVER] Starting isolated local server check...
start "FindUpTo POS CHECK" /b "%SERVER_EXE%" >"%SERVER_LOG%" 2>&1
set "HEALTH_OK=0"
for /l %%N in (1,1,30) do (
  powershell -NoProfile -Command "try { $r=Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:5000/health' -TimeoutSec 1; if($r.StatusCode -eq 200){exit 0}else{exit 1} } catch { exit 1 }" >nul 2>nul
  if not errorlevel 1 (
    set "HEALTH_OK=1"
    goto :health_done
  )
  timeout /t 1 /nobreak >nul
)
:health_done
if "%HEALTH_OK%"=="1" (
  echo [OK] Server health endpoint returned HTTP 200.
) else (
  echo [FAIL] Server did not become healthy at http://127.0.0.1:5000/health
  echo ----- server log -----
  type "%SERVER_LOG%"
  echo ---------------------
  set "FAIL=1"
)

taskkill /f /im FindUpTo.Pos.Server.exe >nul 2>nul

if exist "%APK%" (
  echo.
  echo [APK] Inspecting APK package...
  where aapt >nul 2>nul
  if not errorlevel 1 (
    aapt dump badging "%APK%" | findstr /i "package: application-label:" >nul
    if errorlevel 1 (
      echo [FAIL] APK package inspection failed.
      set "FAIL=1"
    ) else echo [OK] APK package is readable by aapt.
  ) else (
    echo [INFO] aapt not found; APK installability will be checked by Android during installation.
  )
)

:done
echo.
if "%FAIL%"=="0" (
  echo POS LOCAL CHECK PASSED
  echo PC server EXE, Windows POS EXE, and Android APK are present.
  echo Server health check passed. No Android device installation is performed.
  exit /b 0
)

echo POS LOCAL CHECK FAILED
exit /b 1
