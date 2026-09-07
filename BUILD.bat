@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ========================================
echo FindUpTo POS - reproducible build
echo ========================================

where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: .NET 8 SDK is required.
  exit /b 1
)
where flutter >nul 2>nul
if errorlevel 1 (
  echo ERROR: Flutter SDK is required for the complete app build.
  exit /b 1
)

set "TEST_ASPNETCORE_ENVIRONMENT=Development"
set "TEST_POS_JWT_KEY=integration-test-jwt-key-must-be-at-least-32-chars"
set "TEST_INITIAL_OWNER_PASSWORD=Owner-Test-Password-123!"
set "TEST_INITIAL_ADMIN_PASSWORD=Admin-Test-Password-123!"
set "TEST_INITIAL_WAITER_PASSWORD=Waiter-Test-Password-123!"
set "TEST_INITIAL_COUNTER_PASSWORD=Counter-Test-Password-123!"

if exist "artifacts" rmdir /s /q "artifacts"
mkdir "artifacts"

echo [1/7] Restore .NET...
dotnet restore FindUpTo.Pos.Server.sln
if errorlevel 1 exit /b 1

echo [2/7] Build .NET...
dotnet build FindUpTo.Pos.Server.sln --configuration Release --no-restore
if errorlevel 1 exit /b 1

echo [3/7] Run server tests...
set "ASPNETCORE_ENVIRONMENT=%TEST_ASPNETCORE_ENVIRONMENT%"
set "POS_JWT_KEY=%TEST_POS_JWT_KEY%"
set "INITIAL_OWNER_PASSWORD=%TEST_INITIAL_OWNER_PASSWORD%"
set "INITIAL_ADMIN_PASSWORD=%TEST_INITIAL_ADMIN_PASSWORD%"
set "INITIAL_WAITER_PASSWORD=%TEST_INITIAL_WAITER_PASSWORD%"
set "INITIAL_COUNTER_PASSWORD=%TEST_INITIAL_COUNTER_PASSWORD%"
dotnet test tests\FindUpTo.Pos.Server.Tests\FindUpTo.Pos.Server.Tests.csproj --configuration Release --no-build
if errorlevel 1 exit /b 1

echo [4/7] Publish self-contained server EXE...
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\publish.ps1
if errorlevel 1 exit /b 1

cd mobile\flutter_app
call flutter create . --platforms=android,windows
if errorlevel 1 exit /b 1
if not exist "android\gradle.properties" type nul > "android\gradle.properties"
>>"android\gradle.properties" echo kotlin.incremental=false
>>"android\gradle.properties" echo kotlin.compiler.execution.strategy=in-process
powershell -NoProfile -Command "$p='android\app\src\main\AndroidManifest.xml'; $s=Get-Content $p -Raw; if($s -notmatch 'usesCleartextTraffic'){ $s=$s -replace '<application ', '<application android:usesCleartextTraffic=\"true\" '; Set-Content $p $s }"
call flutter clean
if errorlevel 1 exit /b 1
call flutter pub get
if errorlevel 1 exit /b 1
call flutter analyze
if errorlevel 1 exit /b 1
call flutter test
if errorlevel 1 exit /b 1

echo [6/7] Build Windows desktop app...
call flutter build windows --release --dart-define=POS_SERVER_URL=http://127.0.0.1:5000
if errorlevel 1 exit /b 1

echo [7/7] Build Android APK...
if exist "%LOCALAPPDATA%\Android\sdk\ndk" (
  for /d %%D in ("%LOCALAPPDATA%\Android\sdk\ndk\*") do (
    if exist "%%~fD" if not exist "%%~fD\source.properties" rmdir /s /q "%%~fD"
  )
)
if exist "android\gradlew.bat" call android\gradlew.bat --stop >nul 2>nul
call flutter clean
if errorlevel 1 exit /b 1
call flutter pub get
if errorlevel 1 exit /b 1
rem Select the IPv4 address attached to a real interface with a default gateway.
rem This deliberately avoids Docker/WSL/Hyper-V virtual addresses such as 172.17.x.x.
set "POS_ANDROID_SERVER_IP="
for /f "usebackq delims=" %%I in (`powershell -NoProfile -Command "$c=Get-NetIPConfiguration | Where-Object {$_.IPv4DefaultGateway -and $_.IPv4Address} | Select-Object -First 1; if($c){$c.IPv4Address.IPAddress}"`) do set "POS_ANDROID_SERVER_IP=%%I"
if not defined POS_ANDROID_SERVER_IP set "POS_ANDROID_SERVER_IP=10.0.2.2"
echo Android POS server URL: http://%POS_ANDROID_SERVER_IP%:5000
call flutter build apk --release --dart-define=POS_SERVER_URL=http://%POS_ANDROID_SERVER_IP%:5000
if errorlevel 1 exit /b 1

cd ..\..

echo [PACKAGING] Collecting distributable EXE/APK files...
if not exist "artifacts\windows-server" mkdir "artifacts\windows-server"
if not exist "artifacts\windows-pos-app" mkdir "artifacts\windows-pos-app"
if not exist "artifacts\android" mkdir "artifacts\android"
copy /y "deploy\windows\publish\FindUpTo.Pos.Server.exe" "artifacts\windows-server\FindUpTo.Pos.Server.exe" >nul
if errorlevel 1 exit /b 1
copy /y "mobile\flutter_app\build\windows\x64\runner\Release\findupto_pos_mobile.exe" "artifacts\windows-pos-app\findupto_pos_mobile.exe" >nul
if errorlevel 1 exit /b 1
copy /y "mobile\flutter_app\build\app\outputs\flutter-apk\app-release.apk" "artifacts\android\findupto_pos_mobile.apk" >nul
if errorlevel 1 exit /b 1

rem Permit LAN Android devices to reach the development API.
netsh advfirewall firewall add rule name="FindUpTo POS TCP 5000" dir=in action=allow protocol=TCP localport=5000 >nul 2>nul

echo.
echo BUILD COMPLETE
echo Server EXE: artifacts\windows-server\FindUpTo.Pos.Server.exe
echo Windows app: artifacts\windows-pos-app\findupto_pos_mobile.exe
echo Android APK: artifacts\android\findupto_pos_mobile.apk
echo Android server URL embedded in APK: http://%POS_ANDROID_SERVER_IP%:5000
echo.
