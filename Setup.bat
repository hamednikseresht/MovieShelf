@echo off
rem MovieShelf setup - installs everything the program needs, then starts MovieShelf. No questions, no database.
rem Safe to run again any time (repairs and updates; your data is kept).
setlocal EnableExtensions
title MovieShelf setup
cd /d "%~dp0"
chcp 65001 >nul

echo.
echo  =============================================
echo     MovieShelf setup
echo  =============================================
echo.

rem ---- find Python 3.9 or newer --------------------------------------------
rem Everything setup makes (private Python environment, database, posters, settings) goes to this folder.
set "HOME_DIR=%MOVIESHELF_HOME%"
if not defined HOME_DIR set "HOME_DIR=%LocalAppData%\MovieShelf"
set "PY="
if exist "%HOME_DIR%\venv\Scripts\python.exe" (
  "%HOME_DIR%\venv\Scripts\python.exe" -c "import sys" >nul 2>&1 && set "PY=%HOME_DIR%\venv\Scripts\python.exe"
)
if not defined PY (
  py -3 -c "import sys; sys.exit(0 if sys.version_info >= (3, 9) else 1)" >nul 2>&1 && set "PY=py -3"
)
if not defined PY (
  python -c "import sys; sys.exit(0 if sys.version_info >= (3, 9) else 1)" >nul 2>&1 && set "PY=python"
)
if not defined PY (
  for %%V in (313 312 311 310) do (
    if not defined PY if exist "%LocalAppData%\Programs\Python\Python%%V\python.exe" set "PY=%LocalAppData%\Programs\Python\Python%%V\python.exe"
  )
)
if defined PY goto :have_python

echo  [FAIL] Python 3.9 or newer was not found.
echo.
where winget >nul 2>&1
if errorlevel 1 goto :manual_python
echo  Installing Python 3.12 for your user ... (a few minutes)
winget install -e --id Python.Python.3.12 --scope user --accept-package-agreements --accept-source-agreements
if exist "%LocalAppData%\Programs\Python\Python312\python.exe" (
  set "PY=%LocalAppData%\Programs\Python\Python312\python.exe"
  goto :have_python
)
echo  [FAIL] Python install did not finish.

:manual_python
echo.
echo  How to fix:
echo    1. Download Python 3.12 from https://www.python.org/downloads/
echo    2. In the installer, tick "Add python.exe to PATH", then Install.
echo    3. Run Setup.bat again.
echo.
pause
exit /b 1

:have_python
if "%PY%"=="py -3" (
  %PY% app\setup.py %*
) else (
  "%PY%" app\setup.py %*
)
set "RC=%ERRORLEVEL%"
if "%RC%"=="0" (
  ping -n 5 127.0.0.1 >nul
  exit /b 0
)
echo.
echo  Setup stopped. Details are in "%HOME_DIR%\data\setup.log"
pause
exit /b %RC%
