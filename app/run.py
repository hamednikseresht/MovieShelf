"""MovieShelf launcher: starts the local server and opens the app window."""
import json
import os
import socket
import sys
import time
import webbrowser

FROZEN = getattr(sys, 'frozen', False)  # a PyInstaller build
APPDIR = os.path.dirname(sys.executable) if FROZEN else os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, APPDIR)
from movieshelf import paths  # noqa: E402

# The program stays clean: the database, posters, IMDb data, settings and logs go to %LOCALAPPDATA%\MovieShelf
# (or beside the program if it has a portable.txt, or to MOVIESHELF_HOME).
ROOT = paths.home(APPDIR)
os.makedirs(os.path.join(ROOT, 'data'), exist_ok=True)
WEB = os.path.join(getattr(sys, '_MEIPASS', APPDIR), 'web')  # the page files travel inside the program
INSTANCE = os.path.join(ROOT, 'data', 'instance.json')

# pythonw has no console: keep output and errors in data/movieshelf.log
if sys.stdout is None or sys.stderr is None:
    os.makedirs(os.path.join(ROOT, 'data'), exist_ok=True)
    sys.stdout = sys.stderr = open(os.path.join(ROOT, 'data', 'movieshelf.log'), 'a', encoding='utf-8', buffering=1)


def message(text, error=False):
    """Visible even without a console (pythonw)."""
    print(text)
    if os.name == 'nt':
        import ctypes
        ctypes.windll.user32.MessageBoxW(None, text, 'MovieShelf', 0x10 if error else 0x40)


def running_port():
    """Port of an already running MovieShelf, or None."""
    try:
        with open(INSTANCE, encoding='utf-8') as f:
            port = json.load(f)['port']
        with socket.create_connection(('127.0.0.1', port), timeout=0.5):
            return port
    except Exception:
        return None


def main():
    port = running_port()
    if port:
        if '--server' not in sys.argv:
            webbrowser.open(f'http://127.0.0.1:{port}/')
        message('MovieShelf is already running. It was opened in your web browser too.\n'
                'Close the other MovieShelf window first if you want only one.')
        return
    from movieshelf.library import Library
    from movieshelf.server import serve
    lib = Library(ROOT)
    try:
        from movieshelf import backup
        backup.auto(lib)  # one copy of your favorites, notes and manual matches per day
    except Exception as e:
        print(f'Automatic backup skipped: {e}')
    httpd = serve(lib, WEB, int(os.environ.get('MOVIESHELF_PORT', '0')))
    url = f'http://127.0.0.1:{httpd.server_address[1]}/'
    with open(INSTANCE, 'w', encoding='utf-8') as f:
        json.dump({'port': httpd.server_address[1], 'pid': os.getpid()}, f)
    print(f'{time.strftime("%Y-%m-%d %H:%M:%S")} MovieShelf running at {url}')
    # every start: pick up new lines in the list and finish anything left undone. The very first start waits:
    # the page asks whether to load a backup or start fresh (which downloads IMDb data and builds everything)
    if not lib.first_run():
        lib.start(lib.full_update)
    try:
        if '--browser' in sys.argv or '--server' in sys.argv:
            if '--browser' in sys.argv:
                webbrowser.open(url)
            wait()
            return
        try:
            import webview
        except ImportError:
            webbrowser.open(url)
            print('Tip: run Setup.bat to get the native window. Press Ctrl+C to quit.')
            wait()
            return
        window = webview.create_window('MovieShelf', url, width=1500, height=950, min_size=(900, 600),
                                       background_color='#0f1115', js_api=PickerApi())
        PickerApi._window = window
        try:
            webview.start()
        except Exception as e:  # WebView2 missing or broken: the browser still works
            print(f'Native window failed ({e}); opening the browser')
            webbrowser.open(url)
            wait()
    finally:
        try:
            os.remove(INSTANCE)
        except OSError:
            pass


def wait():
    try:
        while True:
            time.sleep(3600)
    except KeyboardInterrupt:
        pass


class PickerApi:
    """Native Windows open dialogs for the list check (called from the page as window.pywebview.api).
    Only public methods are exposed to the page; '_window' stays private."""
    _window = None

    def _dialog(self, kind, **kw):
        import webview
        fd = getattr(webview, 'FileDialog', None)  # pywebview >= 5.3
        mode = getattr(fd, kind) if fd else getattr(webview, kind + '_DIALOG')
        res = self._window.create_file_dialog(mode, **kw)
        if isinstance(res, str):
            return res
        return res[0] if res else None

    def pick_file(self):
        return self._dialog('OPEN', file_types=('Movie lists (*.txt;*.csv;*.tsv)', 'All files (*.*)'))

    def pick_folder(self):
        return self._dialog('FOLDER')

    def save_backup(self, name):
        """Ask where to save a full backup (.zip). Returns the path, or None if cancelled."""
        path = self._dialog('SAVE', save_filename=name, file_types=('MovieShelf full backup (*.zip)',))
        if path and not path.lower().endswith('.zip'):
            path += '.zip'
        return path or None

    def pick_backup(self):
        return self._dialog('OPEN', file_types=('MovieShelf full backup (*.zip)', 'All files (*.*)'))

    def save_export(self, token, name):
        """Ask where to save an export built by the server and write it there. Returns the path, or None if cancelled."""
        from movieshelf import exporter
        item = exporter.STORE.get(token)
        if not item:
            raise ValueError('This export has expired. Export again.')
        ext = os.path.splitext(name)[1].lstrip('.') or 'xlsx'
        kind = 'Excel workbook' if ext == 'xlsx' else 'CSV file'
        path = self._dialog('SAVE', save_filename=name, file_types=(f'{kind} (*.{ext})', 'All files (*.*)'))
        if not path:
            return None
        if not path.lower().endswith('.' + ext):
            path += '.' + ext
        with open(path, 'wb') as f:
            f.write(item[2])
        return path


if __name__ == '__main__':
    try:
        main()
    except Exception as e:
        import traceback
        traceback.print_exc()
        message(f'MovieShelf could not start:\n\n{type(e).__name__}: {e}\n\n'
                f'Run Setup.bat to check and repair the installation.\n'
                f'Details: {os.path.join(ROOT, "data", "movieshelf.log")}', error=True)
