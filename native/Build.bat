@echo off
rem MovieShelf native - builds the Windows app into the "dist" folder next to this file.
rem Needs the .NET 10 SDK (winget install Microsoft.DotNet.SDK.10). The result runs on any Windows 10/11 x64 PC without .NET installed.
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
  if exist "%ProgramFiles%\dotnet\dotnet.exe" (set "PATH=%PATH%;%ProgramFiles%\dotnet") else goto :nodotnet
)
echo Building MovieShelf (the first build downloads packages and takes a minute)...
dotnet publish MovieShelf.App\MovieShelf.App.csproj -c Release -r win-x64 --self-contained true -o "%~dp0dist" -nologo -v q
if errorlevel 1 (
  echo.
  echo The build failed. Scroll up for the first error.
  exit /b 1
)
echo.
echo Done. Start the app with MovieShelf-Native.bat or dist\MovieShelf.exe
if /i not "%~1"=="--no-pause" pause
exit /b 0
:nodotnet
echo The .NET 10 SDK was not found. Install it with:
echo     winget install Microsoft.DotNet.SDK.10
echo then run Build.bat again.
if /i not "%~1"=="--no-pause" pause
exit /b 1
