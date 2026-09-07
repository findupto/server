@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

if not exist "artifacts\windows-server\FindUpTo.Pos.Server.exe" (
  echo ERROR: Server executable not found.
  echo Run BUILD.bat first.
  exit /b 1
)

rem Safe local-development defaults. Override these before launching for production.
if not defined POS_JWT_KEY set "POS_JWT_KEY=findupto-local-development-jwt-key-please-change"
if not defined INITIAL_OWNER_PASSWORD set "INITIAL_OWNER_PASSWORD=Owner-Test-Password-123!"
if not defined INITIAL_ADMIN_PASSWORD set "INITIAL_ADMIN_PASSWORD=Admin-Test-Password-123!"
if not defined INITIAL_WAITER_PASSWORD set "INITIAL_WAITER_PASSWORD=Waiter-Test-Password-123!"
if not defined INITIAL_COUNTER_PASSWORD set "INITIAL_COUNTER_PASSWORD=Counter-Test-Password-123!"
if not defined ASPNETCORE_ENVIRONMENT set "ASPNETCORE_ENVIRONMENT=Development"
if not defined ASPNETCORE_URLS set "ASPNETCORE_URLS=http://0.0.0.0:5000"
rem A fresh local install has no SQLite schema yet. Allow the server to create it.
if not defined POS_ALLOW_SCHEMA_CREATE set "POS_ALLOW_SCHEMA_CREATE=true"

if "!POS_JWT_KEY:~31,1!"=="" goto ConfigError
if not defined INITIAL_OWNER_PASSWORD goto ConfigError
if not defined INITIAL_ADMIN_PASSWORD goto ConfigError
if not defined INITIAL_WAITER_PASSWORD goto ConfigError
if not defined INITIAL_COUNTER_PASSWORD goto ConfigError

echo Starting FindUpTo POS server on %ASPNETCORE_URLS% ...
echo Health check: http://127.0.0.1:5000/health
artifacts\windows-server\FindUpTo.Pos.Server.exe
exit /b %ERRORLEVEL%

:ConfigError
echo ERROR: Required startup configuration is missing or invalid.
echo POS_JWT_KEY must be at least 32 characters and all four initial passwords are required.
exit /b 1
