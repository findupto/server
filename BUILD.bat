@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

echo ========================================
echo FindUpTo POS - verified build
 echo ========================================
where dotnet >nul 2>nul || goto DotnetMissing
where flutter >nul 2>nul || goto FlutterMissing

if exist "artifacts" rmdir /s /q "artifacts"
mkdir "artifacts"

set "ASPNETCORE_ENVIRONMENT=Development"
set "POS_JWT_KEY=integration-test-jwt-key-must-be-at-least-32-chars"
set "INITIAL_OWNER_PASSWORD=Owner-Test-Password-123!"
set "INITIAL_ADMIN_PASSWORD=Admin-Test-Password-123!"
set "INITIAL_WAITER_PASSWORD=Waiter-Test-Password-123!"
set "INITIAL_COUNTER_PASSWORD=Counter-Test-Password-123!"

 echo [1/7] Restore .NET...
dotnet restore FindUpTo.Pos.Server.sln || exit /b 1
 echo [2/7] Build .NET...
dotnet build FindUpTo.Pos.Server.sln --configuration Release --no-restore || exit /b 1
 echo [3/7] Run server tests...
dotnet test tests\FindUpTo.Pos.Server.Tests\FindUpTo.Pos.Server.Tests.csproj --configuration Release --no-build || exit /b 1
 echo [4/7] Publish server...
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\publish.ps1 || exit /b 1
if not exist "artifacts\windows-server-published\FindUpTo.Pos.Server.exe" goto ServerPublishFailed

cd mobile\flutter_app
call flutter create . --platforms=android,windows || exit /b 1
if not exist "android\gradle.properties" type nul > "android\gradle.properties"
>>"android\gradle.properties" echo kotlin.incremental=false
>>"android\gradle.properties" echo kotlin.compiler.execution.strategy=in-process
powershell -NoProfile -Command "$p='android\app\src\main\AndroidManifest.xml'; $s=Get-Content $p -Raw; if($s -notmatch 'usesCleartextTraffic'){ $s=$s -replace '<application ', '<application android:usesCleartextTraffic=\"true\" '; Set-Content $p $s }"
call flutter clean || exit /b 1
call flutter pub get || exit /b 1
call flutter analyze || exit /b 1
call flutter test || exit /b 1
 echo [6/7] Build Windows app...
call flutter build windows --release --dart-define=POS_SERVER_URL=http://127.0.0.1:5000 || exit /b 1
if not exist "build\windows\x64\runner\Release\findupto_pos_mobile.exe" goto WindowsBuildFailed
 echo [7/7] Build Android APK...
if exist "%LOCALAPPDATA%\Android\sdk\ndk" for /d %%D in ("%LOCALAPPDATA%\Android\sdk\ndk\*") do if exist "%%~fD" if not exist "%%~fD\source.properties" rmdir /s /q "%%~fD"
if exist "android\gradlew.bat" call android\gradlew.bat --stop >nul 2>nul
call flutter clean || exit /b 1
call flutter pub get || exit /b 1
set "POS_ANDROID_SERVER_IP="
for /f "usebackq delims=" %%I in (`powershell -NoProfile -Command "$c=Get-NetIPConfiguration | Where-Object {$_.IPv4DefaultGateway -and $_.IPv4Address} | Select-Object -First 1; if($c){$c.IPv4Address.IPAddress}"`) do set "POS_ANDROID_SERVER_IP=%%I"
if not defined POS_ANDROID_SERVER_IP set "POS_ANDROID_SERVER_IP=10.0.2.2"
echo Android POS server URL: http://%POS_ANDROID_SERVER_IP%:5000
call flutter build apk --release --dart-define=POS_SERVER_URL=http://%POS_ANDROID_SERVER_IP%:5000 || exit /b 1
if not exist "build\app\outputs\flutter-apk\app-release.apk" goto ApkBuildFailed

cd ..\..
mkdir "artifacts\windows-server" >nul 2>nul
mkdir "artifacts\windows-pos-app" >nul 2>nul
mkdir "artifacts\android" >nul 2>nul
copy /y "artifacts\windows-server-published\FindUpTo.Pos.Server.exe" "artifacts\windows-server\FindUpTo.Pos.Server.exe" >nul || exit /b 1
copy /y "mobile\flutter_app\build\windows\x64\runner\Release\findupto_pos_mobile.exe" "artifacts\windows-pos-app\findupto_pos_mobile.exe" >nul || exit /b 1
copy /y "mobile\flutter_app\build\app\outputs\flutter-apk\app-release.apk" "artifacts\android\findupto_pos_mobile.apk" >nul || exit /b 1
netsh advfirewall firewall add rule name="FindUpTo POS TCP 5000" dir=in action=allow protocol=TCP localport=5000 >nul 2>nul

if not exist "artifacts\windows-server\FindUpTo.Pos.Server.exe" exit /b 1
if not exist "artifacts\windows-pos-app\findupto_pos_mobile.exe" exit /b 1
if not exist "artifacts\android\findupto_pos_mobile.apk" exit /b 1

echo.
echo BUILD COMPLETE
 echo Server EXE: %CD%\artifacts\windows-server\FindUpTo.Pos.Server.exe
 echo Windows app: %CD%\artifacts\windows-pos-app\findupto_pos_mobile.exe
 echo Android APK: %CD%\artifacts\android\findupto_pos_mobile.apk
 echo Android server URL embedded in APK: http://%POS_ANDROID_SERVER_IP%:5000
 echo.
echo Run CHECK_PC.bat before installing anything.
exit /b 0

:DotnetMissing
echo ERROR: .NET 8 SDK is required.
exit /b 1
:FlutterMissing
echo ERROR: Flutter SDK is required.
exit /b 1
:ServerPublishFailed
echo ERROR: Server publish did not create the EXE.
exit /b 1
:WindowsBuildFailed
echo ERROR: Windows POS EXE was not created.
exit /b 1
:ApkBuildFailed
echo ERROR: Android APK was not created.
exit /b 1
