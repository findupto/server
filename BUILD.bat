@echo off
setlocal EnableExtensions
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
  echo Server-only publish is still available with deploy\windows\publish.ps1.
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

 echo [5/7] Prepare Flutter platforms...
cd mobile\flutter_app
call flutter create . --platforms=android,windows
if errorlevel 1 exit /b 1
rem The Android project is regenerated on each clean checkout. Disable Kotlin
rem incremental caches and the Kotlin daemon because Windows mapped-file cache
rem locking can fail when the project and Pub cache live on different drives.
if not exist "android\gradle.properties" type nul > "android\gradle.properties"
>>"android\gradle.properties" echo kotlin.incremental=false
>>"android\gradle.properties" echo kotlin.compiler.execution.strategy=in-process
call flutter clean
if errorlevel 1 exit /b 1
call flutter pub get
if errorlevel 1 exit /b 1
call flutter analyze
if errorlevel 1 exit /b 1
call flutter test
if errorlevel 1 exit /b 1

 echo [6/7] Build Windows desktop app...
call flutter build windows --release
if errorlevel 1 exit /b 1

 echo [7/7] Build Android APK...
rem Remove only incomplete Android NDK installations. A missing source.properties
rem means the SDK download was interrupted/corrupted; Gradle can then provision it again.
if exist "%LOCALAPPDATA%\Android\sdk\ndk" (
  for /d %%D in ("%LOCALAPPDATA%\Android\sdk\ndk\*") do (
    if exist "%%~fD" if not exist "%%~fD\source.properties" (
      echo Removing incomplete Android NDK: %%~fD
      rmdir /s /q "%%~fD"
    )
  )
)
rem Stop any stale Kotlin/Gradle daemon before the release APK compilation.
if exist "android\gradlew.bat" call android\gradlew.bat --stop >nul 2>nul
call flutter clean
if errorlevel 1 exit /b 1
call flutter pub get
if errorlevel 1 exit /b 1
call flutter build apk --release
if errorlevel 1 exit /b 1

cd ..\..

echo.
echo BUILD COMPLETE
echo Server EXE: artifacts\windows-server\FindUpTo.Pos.Server.exe
echo Windows app: mobile\flutter_app\build\windows\x64\runner\Release\findupto_pos_mobile.exe
echo Android APK: mobile\flutter_app\build\app\outputs\flutter-apk\app-release.apk
exit /b 0
