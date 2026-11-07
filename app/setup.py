r"""MovieShelf setup: installs everything the program needs (private Python environment with its packages, the
WebView2 window runtime, a desktop shortcut) and starts MovieShelf. Started by Setup.bat. It asks no questions.

It does not build the database. The first start of MovieShelf asks whether to load a backup or start fresh.

Everything the app makes (Python environment, database, posters, IMDb data, settings, logs) goes to one
folder, %LOCALAPPDATA%\MovieShelf, so this project folder stays clean and can be moved or copied freely.

  python app\setup.py            install, then start MovieShelf
  python app\setup.py --no-run   install only
"""
import json
import os
import shutil
import socket
import subprocess
import sys
import time

APP = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(APP)  # the project folder (Setup.bat, MovieShelf.bat, requirements.txt, tests)
sys.path.insert(0, APP)
from movieshelf import paths  # noqa: E402

HOME = paths.home()
DATA = os.path.join(HOME, 'data')
VENV = os.path.join(HOME, 'venv')
VPY = os.path.join(VENV, 'Scripts', 'python.exe')
LOG = os.path.join(DATA, 'setup.log')
os.makedirs(DATA, exist_ok=True)

if os.name == 'nt':
    os.system('')  # enable ANSI colours in the Windows console
G, Y, R, B, D, X = '\033[92m', '\033[93m', '\033[91m', '\033[1m', '\033[2m', '\033[0m'
problems = []


def log(msg):
    with open(LOG, 'a', encoding='utf-8') as f:
        f.write(f'{time.strftime("%Y-%m-%d %H:%M:%S")} {msg}\n')


def ok(msg):
    print(f'  {G}[ OK ]{X} {msg}')
    log('OK   ' + msg)


def warn(msg, hint=''):
    print(f'  {Y}[WARN]{X} {msg}')
    if hint:
        print(f'         {D}{hint}{X}')
    log(f'WARN {msg} | {hint}')
    problems.append(('warn', msg, hint))


def fail(msg, hint=''):
    print(f'  {R}[FAIL]{X} {msg}')
    if hint:
        print(f'         {hint}')
    log(f'FAIL {msg} | {hint}')
    problems.append(('fail', msg, hint))


def step(n, title):
    print(f'\n{B}{n}. {title}{X}')
    log(f'--- {n}. {title}')


def run(cmd, timeout=900):
    log('RUN  ' + ' '.join(cmd))
    p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, cwd=ROOT,
                       encoding='utf-8', errors='replace')
    log(p.stdout[-3000:] + p.stderr[-3000:])
    return p


def pip_hint(out):
    o = out.lower()
    if 'proxy' in o or 'connection' in o or 'timed out' in o or 'getaddrinfo' in o or 'network' in o:
        return 'No internet for pip. Check the connection / VPN / proxy, then run Setup.bat again.'
    if 'ssl' in o or 'certificate' in o:
        return 'SSL error: a firewall or antivirus is inspecting HTTPS. Allow python.exe, then run Setup.bat again.'
    if 'permission' in o or 'access is denied' in o:
        return 'Access denied: close MovieShelf (and any python window) and run Setup.bat again.'
    if 'no matching distribution' in o:
        return f'No package for Python {sys.version_info.major}.{sys.version_info.minor}. Install Python 3.12 from python.org and run Setup.bat again.'
    return f'See {LOG} for the full pip output.'


# ---------------------------------------------------------------------------
def check_python():
    step(1, 'Python')
    v = sys.version_info
    if v < (3, 9):
        fail(f'Python {v.major}.{v.minor} is too old', 'Install Python 3.12 from https://www.python.org/downloads/ (tick "Add python.exe to PATH").')
        return False
    ok(f'Python {v.major}.{v.minor}.{v.micro} ({sys.executable})')
    if v >= (3, 14):
        warn(f'Python {v.major}.{v.minor} is very new; the native window package may not support it yet',
             'If the app opens in your web browser instead of its own window, install Python 3.12 and run Setup.bat again.')
    return True


def app_running(data=None):
    try:
        with open(os.path.join(data or DATA, 'instance.json'), encoding='utf-8') as f:
            port = json.load(f)['port']
        with socket.create_connection(('127.0.0.1', port), timeout=0.5):
            return True
    except Exception:
        return False


def migrate():
    """Older versions kept data/ and .venv inside the project folder. Move them to the app-data folder
    (a rename when it is the same drive, so even gigabytes take a second)."""
    old = os.path.join(ROOT, 'data')
    if (os.path.abspath(old) != os.path.abspath(DATA) and os.path.isfile(os.path.join(old, 'library.db'))
            and not os.path.exists(os.path.join(DATA, 'library.db'))):
        step(0, 'Moving your data out of the project folder')
        if app_running(old):
            fail('MovieShelf is open', 'Close MovieShelf and run Setup.bat again so your data can be moved.')
            return False
        try:
            for name in os.listdir(old):
                if name in ('instance.json', 'setup.log', 'movieshelf.log', 'fa_latin.json'):
                    continue  # temporary or no longer used
                dest = os.path.join(DATA, name)
                if os.path.exists(dest):
                    if os.path.isdir(dest) and not os.listdir(dest):
                        os.rmdir(dest)  # an empty folder the app made already
                if not os.path.exists(dest):
                    shutil.move(os.path.join(old, name), dest)
            for name in ('instance.json', 'setup.log', 'movieshelf.log', 'fa_latin.json'):
                try:
                    os.remove(os.path.join(old, name))
                except OSError:
                    pass
            os.rmdir(old)
        except OSError as e:
            fail(f'Could not move {old} to {DATA}: {e}', 'Close MovieShelf and anything that has these files open, then run Setup.bat again.')
            return False
        ok(f'Your library, posters and settings are now in {DATA}')
    shutil.rmtree(os.path.join(ROOT, '.venv'), ignore_errors=True)  # the environment now lives in the app-data folder
    return True


def setup_env():
    step(2, 'Python packages')
    py = sys.executable
    if os.path.abspath(sys.prefix) != os.path.abspath(VENV):
        if not os.path.exists(VPY):
            print(f'  {D}Creating a private environment in {VENV} ...{X}')
            p = run([sys.executable, '-m', 'venv', VENV])
            if p.returncode or not os.path.exists(VPY):
                warn('Could not create the private environment; installing for your Windows user instead', (p.stderr or '').strip()[-200:])
            else:
                ok('Private environment created')
        if os.path.exists(VPY):
            py = VPY
    else:
        ok('Using the private environment')
    print(f'  {D}Downloading and installing the packages (rapidfuzz, pywebview) ...{X}')
    run([py, '-m', 'pip', 'install', '--upgrade', '--disable-pip-version-check', 'pip'], timeout=300)
    args = [py, '-m', 'pip', 'install', '--upgrade', '--disable-pip-version-check', '-r', os.path.join(ROOT, 'requirements.txt')]
    if py == sys.executable and not os.path.exists(VPY):
        args.append('--user')
    p = run(args)
    if p.returncode:
        time.sleep(2)
        p = run(args)  # one retry (flaky network)
    if p.returncode:
        fail('Package install failed', pip_hint(p.stdout + p.stderr))
        return None
    ok('Packages installed')
    # verify what the app needs
    p = run([py, '-c', 'import rapidfuzz; print(rapidfuzz.__version__)'])
    if p.returncode:
        fail('rapidfuzz does not load', pip_hint(p.stderr))
        return None
    ok(f'rapidfuzz {p.stdout.strip()}')
    p = run([py, '-c', 'import webview, clr; print(webview.__version__ if hasattr(webview, "__version__") else "")'])
    if p.returncode:
        warn('Native window package (pywebview / pythonnet) does not load',
             'MovieShelf will open in your web browser instead. That works fine; installing Python 3.12 fixes it.')
    else:
        ok('Native window support (pywebview)')
    return py


def check_webview2():
    step(3, 'Microsoft Edge WebView2 (draws the app window)')
    if os.name != 'nt':
        ok('Not Windows: the app opens in your browser')
        return
    import winreg
    guid = r'{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
    keys = [(winreg.HKEY_LOCAL_MACHINE, rf'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{guid}'),
            (winreg.HKEY_LOCAL_MACHINE, rf'SOFTWARE\Microsoft\EdgeUpdate\Clients\{guid}'),
            (winreg.HKEY_CURRENT_USER, rf'Software\Microsoft\EdgeUpdate\Clients\{guid}')]
    for hive, path in keys:
        try:
            with winreg.OpenKey(hive, path) as k:
                v = winreg.QueryValueEx(k, 'pv')[0]
                if v and v != '0.0.0.0':
                    ok(f'WebView2 runtime {v}')
                    return
        except OSError:
            pass
    if shutil.which('winget'):
        print(f'  {D}WebView2 runtime is missing. Installing it ...{X}')
        p = run(['winget', 'install', '-e', '--id', 'Microsoft.EdgeWebView2Runtime', '--silent',
                 '--accept-package-agreements', '--accept-source-agreements'], timeout=900)
        if p.returncode == 0:
            ok('WebView2 runtime installed')
            return
    warn('WebView2 runtime not found',
         'Get it from https://developer.microsoft.com/microsoft-edge/webview2/ (Evergreen). Without it MovieShelf opens in your browser.')


def shortcut():
    step(4, 'Desktop shortcut')
    if os.name != 'nt':
        return
    p = run(['powershell', '-NoProfile', '-Command', "[Environment]::GetFolderPath('Desktop')"], timeout=60)
    desktop = p.stdout.strip() or os.path.join(os.path.expanduser('~'), 'Desktop')  # OneDrive may move it
    lnk = os.path.join(desktop, 'MovieShelf.lnk')
    target = os.path.join(ROOT, 'MovieShelf.bat')
    icon = os.path.join(ROOT, 'tools', 'icon', 'MovieShelf.ico')
    ps = (f'$s=(New-Object -ComObject WScript.Shell).CreateShortcut("{lnk}");$s.TargetPath="{target}";'
          f'$s.WorkingDirectory="{ROOT}";$s.WindowStyle=7;$s.IconLocation="{icon}";'
          f'$s.Description="MovieShelf - your movie collection";$s.Save()')
    p = run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', ps], timeout=60)
    if p.returncode == 0 and os.path.exists(lnk):
        ok('Shortcut on the desktop')
    else:
        warn('Could not create the shortcut', 'Start MovieShelf with MovieShelf.bat in this folder instead.')


def main():
    log('==== setup started ====')
    print(f'{B}MovieShelf setup{X}  {D}(log: {LOG}){X}')
    if not check_python() or not migrate():
        return summary()
    py = setup_env()
    if not py:
        return summary()
    check_webview2()
    shortcut()
    return summary(start='--no-run' not in sys.argv)


def summary(start=False):
    fails = [p for p in problems if p[0] == 'fail']
    print()
    if fails:
        print(f'{R}{B}Setup did not finish.{X} Fix the item(s) marked FAIL and run Setup.bat again:')
        for _, m, h in fails:
            print(f'  - {m}\n    {h}')
        log('==== setup failed ====')
        return 1
    if problems:
        print(f'{Y}{B}Setup finished with warnings{X} (MovieShelf still works):')
        for _, m, h in problems:
            print(f'  - {m}' + (f'\n    {h}' if h else ''))
    else:
        print(f'{G}{B}Setup finished. Everything is installed.{X}')
    log('==== setup finished ====')
    if start:
        print(f'  {D}Starting MovieShelf: it asks whether to load a backup or start fresh.{X}')
        subprocess.Popen(['cmd', '/c', os.path.join(ROOT, 'MovieShelf.bat')], cwd=ROOT, stdin=subprocess.DEVNULL,
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                         creationflags=getattr(subprocess, 'DETACHED_PROCESS', 0) | getattr(subprocess, 'CREATE_NEW_PROCESS_GROUP', 0))
    return 0


if __name__ == '__main__':
    sys.exit(main())
