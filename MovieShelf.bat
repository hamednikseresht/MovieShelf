@echo off
rem MovieShelf - double-click to open the app. (Run Setup.bat once first.)
cd /d "%~dp0"
set "HOME_DIR=%MOVIESHELF_HOME%"
if not defined HOME_DIR set "HOME_DIR=%LocalAppData%\MovieShelf"
set "PYW=%HOME_DIR%\venv\Scripts\pythonw.exe"
if not exist "%PYW%" goto :setup
"%HOME_DIR%\venv\Scripts\python.exe" -c "import rapidfuzz" >nul 2>&1
if errorlevel 1 goto :setup
start "" "%PYW%" "%~dp0app\run.py"
exit /b 0
:setup
echo MovieShelf is not set up on this PC yet. Starting Setup...
call "%~dp0Setup.bat"
exit /b
