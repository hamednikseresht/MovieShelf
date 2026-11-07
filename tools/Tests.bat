@echo off
rem Runs the MovieShelf self-checks (Python and search tests)
cd /d "%~dp0.."
set "PY=python"
set "HOME_DIR=%MOVIESHELF_HOME%"
if not defined HOME_DIR set "HOME_DIR=%LocalAppData%\MovieShelf"
if exist "%HOME_DIR%\venv\Scripts\python.exe" set "PY=%HOME_DIR%\venv\Scripts\python.exe"
echo Python tests:
"%PY%" -m unittest discover -s tests
set "RESULT=%errorlevel%"
where node >nul 2>&1
if errorlevel 1 (
  echo.
  echo Search tests skipped: Node.js is not installed.
) else (
  echo.
  echo Search tests:
  node --test tests\query.test.mjs tests\i18n.test.mjs
  if errorlevel 1 set "RESULT=1"
)
echo.
if "%RESULT%"=="0" (echo All tests passed.) else (echo Some tests failed.)
pause
exit /b %RESULT%
