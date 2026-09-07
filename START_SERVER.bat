@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

if not exist "artifacts\windows-server\FindUpTo.Pos.Server.exe" (
  echo ERROR: Server executable not found.
  echo Run BUILD.bat first, or run deploy\windows\publish.ps1.
  exit /b 1
)

if not defined POS_JWT_KEY (
  echo POS_JWT_KEY is not set. Enter a secret with at least 32 characters.
  set /p "POS_JWT_KEY=JWT key: "
)
if not defined POS_JWT_KEY goto ConfigError
if "!POS_JWT_KEY:~31,1!"=="" goto ConfigError

if not defined INITIAL_OWNER_PASSWORD set /p "INITIAL_OWNER_PASSWORD=Owner password: "
if not defined INITIAL_ADMIN_PASSWORD set /p "INITIAL_ADMIN_PASSWORD=Admin password: "
if not defined INITIAL_WAITER_PASSWORD set /p "INITIAL_WAITER_PASSWORD=Waiter password: "
if not defined INITIAL_COUNTER_PASSWORD set /p "INITIAL_COUNTER_PASSWORD=Counter password: "

if not defined INITIAL_OWNER_PASSWORD goto ConfigError
if not defined INITIAL_ADMIN_PASSWORD goto ConfigError
if not defined INITIAL_WAITER_PASSWORD goto ConfigError
if not defined INITIAL_COUNTER_PASSWORD goto ConfigError

if not defined ASPNETCORE_URLS set "ASPNETCORE_URLS=http://0.0.0.0:5000"
echo Starting FindUpTo POS server on %ASPNETCORE_URLS% ...
artifacts\windows-server\FindUpTo.Pos.Server.exe
exit /b %ERRORLEVEL%

:ConfigError
echo ERROR: Required startup configuration is missing or invalid.
echo Set POS_JWT_KEY to at least 32 characters and provide all four initial passwords.
exit /b 1
