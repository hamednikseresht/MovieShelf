@echo off
rem MovieShelf native - double-click to open the Windows app. Builds it first if it has not been built yet.
cd /d "%~dp0"
if not exist "dist\MovieShelf.exe" call "%~dp0Build.bat" --no-pause
if not exist "dist\MovieShelf.exe" exit /b 1
start "" "dist\MovieShelf.exe"
exit /b 0
