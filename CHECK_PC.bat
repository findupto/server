@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ========================================
echo FindUpTo POS - local pre-install check
echo ========================================
echo.
echo This check validates the packaged server, Windows runtime bundle,
echo APK archive, local API, authentication and catalog endpoints.
echo.

echo [1/5] Checking packaged files...
if not exist "artifacts\windows-server\FindUpTo.Pos.Server.exe" goto MissingServer
if not exist "artifacts\windows-pos-app\findupto_pos_mobile.exe" goto MissingExe
if not exist "artifacts\windows-pos-app\flutter_windows.dll" goto MissingFlutterDll
if not exist "artifacts\android\findupto_pos_mobile.apk" goto MissingApk

for %%F in ("artifacts\windows-server\FindUpTo.Pos.Server.exe") do echo Server EXE: %%~zF bytes
for %%F in ("artifacts\windows-pos-app\findupto_pos_mobile.exe") do echo Windows POS EXE: %%~zF bytes
for %%F in ("artifacts\windows-pos-app\flutter_windows.dll") do echo Flutter runtime DLL: %%~zF bytes
for %%F in ("artifacts\android\findupto_pos_mobile.apk") do echo Android APK: %%~zF bytes

powershell -NoProfile -Command "try { Add-Type -AssemblyName System.IO.Compression.FileSystem; $z=[IO.Compression.ZipFile]::OpenRead('artifacts\android\findupto_pos_mobile.apk'); if($z.Entries.Count -lt 1){throw 'APK archive is empty'}; if(-not ($z.Entries.FullName -contains 'AndroidManifest.xml')){throw 'APK has no AndroidManifest.xml'}; $z.Dispose(); Write-Host 'APK archive: OK' } catch { Write-Host ('APK archive: FAILED - ' + $_.Exception.Message); exit 1 }"
if errorlevel 1 exit /b 1

echo [2/5] Starting or reusing local server...
if not defined POS_JWT_KEY set "POS_JWT_KEY=findupto-local-development-jwt-key-please-change"
if not defined INITIAL_OWNER_PASSWORD set "INITIAL_OWNER_PASSWORD=Owner-Test-Password-123!"
if not defined INITIAL_ADMIN_PASSWORD set "INITIAL_ADMIN_PASSWORD=Admin-Test-Password-123!"
if not defined INITIAL_WAITER_PASSWORD set "INITIAL_WAITER_PASSWORD=Waiter-Test-Password-123!"
if not defined INITIAL_COUNTER_PASSWORD set "INITIAL_COUNTER_PASSWORD=Counter-Test-Password-123!"
if not defined ASPNETCORE_ENVIRONMENT set "ASPNETCORE_ENVIRONMENT=Development"
if not defined ASPNETCORE_URLS set "ASPNETCORE_URLS=http://127.0.0.1:5000"
if not defined POS_ALLOW_SCHEMA_CREATE set "POS_ALLOW_SCHEMA_CREATE=true"

powershell -NoProfile -Command "try{$r=Invoke-RestMethod 'http://127.0.0.1:5000/health' -TimeoutSec 2;if($r.status -eq 'ok'){Write-Host 'Existing local server: OK';exit 0}}catch{};exit 1"
if errorlevel 1 start "FindUpTo POS Server" /min "%CD%\artifacts\windows-server\FindUpTo.Pos.Server.exe"

powershell -NoProfile -Command "$ok=$false; 1..30 | ForEach-Object { try { $r=Invoke-RestMethod 'http://127.0.0.1:5000/health' -TimeoutSec 2; if($r.status -eq 'ok'){$ok=$true;return} } catch {}; Start-Sleep -Seconds 1 }; if(-not $ok){Write-Host 'Health check: FAILED'; exit 1}; Write-Host 'Health check: OK'"
if errorlevel 1 goto ServerFailed

echo [3/5] Checking authentication...
powershell -NoProfile -Command "$body=@{username='Malik';password=$env:INITIAL_OWNER_PASSWORD}|ConvertTo-Json; try{$r=Invoke-RestMethod 'http://127.0.0.1:5000/api/auth/login' -Method Post -ContentType 'application/json' -Body $body; if([string]::IsNullOrWhiteSpace($r.token)){throw 'No JWT returned'}; Write-Host 'Owner login: OK'}catch{Write-Host ('Owner login: FAILED - '+$_.Exception.Message); exit 1}"
if errorlevel 1 goto ServerFailed

echo [4/5] Checking public catalog endpoints...
powershell -NoProfile -Command "try{$c=Invoke-RestMethod 'http://127.0.0.1:5000/api/categories';$p=Invoke-RestMethod 'http://127.0.0.1:5000/api/products';Write-Host ('Categories endpoint: OK ('+$c.Count+' records)');Write-Host ('Products endpoint: OK ('+$p.Count+' records)')}catch{Write-Host ('Catalog check: FAILED - '+$_.Exception.Message);exit 1}"
if errorlevel 1 goto ServerFailed

echo [5/5] Launching Windows POS for manual UI check...
start "FindUpTo POS App" /d "%CD%\artifacts\windows-pos-app" "findupto_pos_mobile.exe"
echo.
echo LOCAL CHECK PASSED
echo Server: http://127.0.0.1:5000/health
echo Login: Malik / local development password
echo Windows POS: launched with bundled Flutter runtime
 echo Android APK: valid archive and ready for device testing
echo.
echo MANUAL UI CHECK: In the Windows app, sign in and test the main POS screens.
echo Keep this window open while testing the Windows app.
exit /b 0

:MissingServer
echo ERROR: Missing artifacts\windows-server\FindUpTo.Pos.Server.exe
echo Run BUILD.bat first.
exit /b 1
:MissingExe
echo ERROR: Missing artifacts\windows-pos-app\findupto_pos_mobile.exe
echo Run BUILD.bat first.
exit /b 1
:MissingFlutterDll
echo ERROR: Missing artifacts\windows-pos-app\flutter_windows.dll
 echo The Windows app was packaged without its Flutter runtime.
echo Run the latest BUILD.bat again.
exit /b 1
:MissingApk
echo ERROR: Missing artifacts\android\findupto_pos_mobile.apk
echo Run BUILD.bat first.
exit /b 1
:ServerFailed
echo ERROR: Local server smoke test failed.
exit /b 1
