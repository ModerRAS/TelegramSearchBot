@echo off
setlocal

set SCRIPT_DIR=%~dp0
set ROOT_DIR=%SCRIPT_DIR%..
set OUTPUT_DIR=%ROOT_DIR%\nupkg

echo Building solution...
dotnet build "%ROOT_DIR%\src\Moder.Update.sln" --configuration Release
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%

echo Packing Moder.Update...
dotnet pack "%ROOT_DIR%\src\Moder.Update\Moder.Update.csproj" --configuration Release --output "%OUTPUT_DIR%" --no-build
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%

echo.
echo Package created in: %OUTPUT_DIR%
dir /b "%OUTPUT_DIR%\*.nupkg" 2>nul || echo No .nupkg files found!

endlocal
