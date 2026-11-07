@echo off
rem MovieShelf native - builds one setup file, ..\MovieShelf-Setup.exe, as small as it goes:
rem   1. publishes the self-contained app, 2. deletes the parts of the Windows App SDK that MovieShelf never loads,
rem   3. packs it with 7-Zip (LZMA2, solid), 4. puts it behind the setup wizard (src\*.cs, compiled with the C# compiler
rem      that comes with Windows, so the wizard needs nothing installed on the target PC).
rem Needs the .NET 10 SDK (winget install Microsoft.DotNet.SDK.10) and 7-Zip (winget install 7zip.7zip).
rem Options:
rem   --from <folder>   pack this finished publish folder instead of publishing and pruning again
rem   --no-pause        do not wait for a key at the end
setlocal
set "HERE=%~dp0"
cd /d "%HERE%"

set "FROM="
set "NOPAUSE="
:args
if "%~1"=="" goto :argsdone
if /i "%~1"=="--no-pause" (
  set "NOPAUSE=1"
  shift
  goto :args
)
if /i "%~1"=="--from" (
  set "FROM=%~f2"
  shift
  shift
  goto :args
)
echo Unknown option: %~1
goto :fail
:argsdone

set "SZ=%ProgramFiles%\7-Zip"
if not exist "%SZ%\7z.exe" set "SZ=%ProgramFiles(x86)%\7-Zip"
if not exist "%SZ%\7z.exe" (
  echo 7-Zip was not found. Install it with: winget install 7zip.7zip
  goto :fail
)
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo The C# compiler of .NET Framework 4 was not found at "%CSC%".
  goto :fail
)

set "WORK=%TEMP%\movieshelf-setup-build"
if exist "%WORK%" rmdir /s /q "%WORK%"
mkdir "%WORK%"

if defined FROM (
  if not exist "%FROM%\MovieShelf.exe" (
    echo There is no MovieShelf.exe in "%FROM%".
    goto :fail
  )
  set "APP=%FROM%"
  goto :pack
)

where dotnet >nul 2>&1
if errorlevel 1 (
  if exist "%ProgramFiles%\dotnet\dotnet.exe" (set "PATH=%PATH%;%ProgramFiles%\dotnet") else (
    echo The .NET 10 SDK was not found. Install it with: winget install Microsoft.DotNet.SDK.10
    goto :fail
  )
)
set "APP=%WORK%\app"
mkdir "%APP%"

echo Publishing the app (the first build downloads packages)...
dotnet publish "..\MovieShelf.App\MovieShelf.App.csproj" -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o "%APP%" -nologo -v q
if errorlevel 1 goto :fail

echo Removing what the app does not use...
powershell -NoProfile -ExecutionPolicy Bypass -File prune.ps1 -Dir "%APP%"
if errorlevel 1 goto :fail

:pack
echo Packing...
"%SZ%\7z.exe" a -t7z -mx=9 -md=128m -mfb=273 -ms=on -bso0 -bsp0 "%WORK%\payload.7z" "%APP%\*"
if errorlevel 1 goto :fail
"%SZ%\7z.exe" a -tzip -mx=9 -bso0 -bsp0 "%WORK%\tools.zip" "%SZ%\7z.exe" "%SZ%\7z.dll" "%SZ%\License.txt"
if errorlevel 1 goto :fail

echo Building the setup program...
set "VERSION="
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "([xml](Get-Content '..\MovieShelf.App\MovieShelf.App.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1"`) do set "VERSION=%%V"
if not defined VERSION (
  echo The version was not found in MovieShelf.App.csproj.
  goto :fail
)
 >"%WORK%\BuildInfo.g.cs" echo [assembly: System.Reflection.AssemblyVersion("%VERSION%.0")]
>>"%WORK%\BuildInfo.g.cs" echo [assembly: System.Reflection.AssemblyFileVersion("%VERSION%.0")]
>>"%WORK%\BuildInfo.g.cs" echo static class BuildInfo { public const string Version = "%VERSION%"; }
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /out:"%WORK%\stub.exe" /win32icon:"..\..\tools\icon\MovieShelf.ico" /win32manifest:setup.manifest /res:"..\..\tools\icon\icon-256.png",logo.png /res:res\license.en.txt,license.en.txt /res:res\license.fa.txt,license.fa.txt /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.IO.Compression.dll "%WORK%\BuildInfo.g.cs" src\*.cs
if errorlevel 1 goto :fail
powershell -NoProfile -ExecutionPolicy Bypass -File Pack-Setup.ps1 -Stub "%WORK%\stub.exe" -Tools "%WORK%\tools.zip" -Payload "%WORK%\payload.7z" -App "%APP%" -Out "%HERE%..\MovieShelf-Setup.exe"
if errorlevel 1 goto :fail

for %%F in ("%WORK%\stub.exe") do echo Setup program: %%~zF bytes
rmdir /s /q "%WORK%"
for %%F in ("%HERE%..\MovieShelf-Setup.exe") do echo.& echo Done: %%~fF  (%%~zF bytes)
if not defined NOPAUSE pause
exit /b 0

:fail
echo.
echo The installer was not built.
if not defined NOPAUSE pause
exit /b 1
