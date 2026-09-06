@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
echo ========================================
echo FindUpTo POS - build and package
echo ========================================
where dotnet >nul 2>nul
if errorlevel 1 (echo ERROR: .NET 8 SDK is not installed or not on PATH.&exit /b 1)
where flutter >nul 2>nul
if errorlevel 1 (echo ERROR: Flutter SDK is not installed or not on PATH.&exit /b 1)
if not defined POS_JWT_KEY set "POS_JWT_KEY=CHANGE_THIS_BEFORE_RUNNING"
if "%POS_JWT_KEY%"=="CHANGE_THIS_BEFORE_RUNNING" (echo ERROR: Set POS_JWT_KEY to a random secret of at least 32 characters before building/running production.&exit /b 1)
if not defined INITIAL_OWNER_PASSWORD set "INITIAL_OWNER_PASSWORD=CHANGE_THIS_OWNER_PASSWORD"
if not defined INITIAL_ADMIN_PASSWORD set "INITIAL_ADMIN_PASSWORD=CHANGE_THIS_ADMIN_PASSWORD"
if not defined INITIAL_WAITER_PASSWORD set "INITIAL_WAITER_PASSWORD=CHANGE_THIS_WAITER_PASSWORD"
if not defined INITIAL_COUNTER_PASSWORD set "INITIAL_COUNTER_PASSWORD=CHANGE_THIS_COUNTER_PASSWORD"
if "%INITIAL_OWNER_PASSWORD%"=="CHANGE_THIS_OWNER_PASSWORD" goto PasswordError
if "%INITIAL_ADMIN_PASSWORD%"=="CHANGE_THIS_ADMIN_PASSWORD" goto PasswordError
if "%INITIAL_WAITER_PASSWORD%"=="CHANGE_THIS_WAITER_PASSWORD" goto PasswordError
if "%INITIAL_COUNTER_PASSWORD%"=="CHANGE_THIS_COUNTER_PASSWORD" goto PasswordError

echo [1/8] Restoring .NET solution...
dotnet restore FindUpTo.Pos.Server.sln
if errorlevel 1 exit /b 1
echo [2/8] Building .NET solution...
dotnet build FindUpTo.Pos.Server.sln --configuration Release --no-restore
if errorlevel 1 exit /b 1
echo [3/8] Running server tests...
if exist "tests\FindUpTo.Pos.Server.Tests\FindUpTo.Pos.Server.Tests.csproj" (dotnet test "tests\FindUpTo.Pos.Server.Tests\FindUpTo.Pos.Server.Tests.csproj" --configuration Release --no-restore & if errorlevel 1 exit /b 1) else echo WARNING: test project not found; build continues.
echo [4/8] Publishing Windows server...
if exist "artifacts\windows-server" rmdir /s /q "artifacts\windows-server"
dotnet publish "src\FindUpTo.Pos.Server\FindUpTo.Pos.Server.csproj" --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "artifacts\windows-server"
if errorlevel 1 exit /b 1
cd mobile\flutter_app
if not exist "android" goto FlutterScaffold
if not exist "windows" goto FlutterScaffold
goto FlutterReady
:FlutterScaffold
echo [5/8] Flutter platform scaffolding is missing; generating Android and Windows runners...
call flutter create . --platforms=android,windows
if errorlevel 1 exit /b 1
:FlutterReady
echo [6/8] Restoring Flutter packages...
call flutter pub get
if errorlevel 1 exit /b 1
echo [7/8] Analyzing and testing Flutter app...
call flutter analyze
if errorlevel 1 exit /b 1
call flutter test
if errorlevel 1 exit /b 1
echo [8/8] Building Android APK and Windows desktop app...
call flutter build apk --release
if errorlevel 1 exit /b 1
call flutter build windows --release
if errorlevel 1 exit /b 1
cd ..\..
echo.
echo BUILD COMPLETE.
echo Server:  artifacts\windows-server
echo Android: mobile\flutter_app\build\app\outputs\flutter-apk\app-release.apk
echo Windows: mobile\flutter_app\build\windows\x64\runner\Release
exit /b 0
:PasswordError
echo ERROR: Replace the CHANGE_THIS_* password placeholders with real deployment passwords.
exit /b 1
