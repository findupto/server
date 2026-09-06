@echo off
setlocal EnableExtensions
cd /d "%~dp0"
if not exist "artifacts\windows-server\FindUpTo.Pos.Server.exe" (
  echo ERROR: Server executable not found.
  echo Run BUILD.bat first.
  exit /b 1
)
if not defined POS_JWT_KEY (
  echo ERROR: POS_JWT_KEY is not set in this shell.
  echo Set POS_JWT_KEY and the required INITIAL_*_PASSWORD values before starting the server.
  exit /b 1
)
if not defined ASPNETCORE_URLS set "ASPNETCORE_URLS=http://0.0.0.0:5000"
echo Starting FindUpTo POS server on %ASPNETCORE_URLS% ...
artifacts\windows-server\FindUpTo.Pos.Server.exe
exit /b %ERRORLEVEL%
