cls

@echo off

echo ===============================
echo Building SignalStack locally
echo ===============================

echo.
echo === .NET: Restore and Build ===

dotnet.exe restore SignalStack.sln
IF ERRORLEVEL 1 exit /b 1

dotnet.exe build SignalStack.sln -c Release
IF ERRORLEVEL 1 exit /b 1

echo.
echo === .NET: Test ===
dotnet.exe test SignalStack.sln -c Release --no-build
IF ERRORLEVEL 1 exit /b 1

echo.
echo === Node.js: Web Build ===

where node >nul 2>&1
IF ERRORLEVEL 1 (
  echo ERROR: Node.js not found in PATH
  exit /b 1
)

cd apps\web || exit /b 1

echo.
echo Installing dependencies
call npm ci
IF ERRORLEVEL 1 exit /b 1

echo.
echo Running lint
call npm run lint
IF ERRORLEVEL 1 exit /b 1

echo.
echo Building web app
call npm run build
IF ERRORLEVEL 1 exit /b 1

cd ..\..

echo.
echo ===============================
echo BUILD SUCCESSFUL
echo ===============================
exit /b 0