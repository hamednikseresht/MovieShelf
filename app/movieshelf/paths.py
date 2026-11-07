"""Where MovieShelf keeps the files it makes after setup."""
import os


def home(appdir=None):
    r"""The folder for the database, posters, IMDb data, settings, backups, logs and the Python environment:
    MOVIESHELF_HOME if set, the program's own folder if it has a portable.txt, else %LOCALAPPDATA%\MovieShelf."""
    env = os.environ.get('MOVIESHELF_HOME')
    if env:
        return os.path.abspath(env)
    if appdir and os.path.exists(os.path.join(appdir, 'portable.txt')):
        return os.path.abspath(appdir)
    base = os.environ.get('LOCALAPPDATA') or os.path.join(os.path.expanduser('~'), 'AppData', 'Local')
    return os.path.join(base, 'MovieShelf')


def default_list_dir():
    """Where the movie-name .txt lists go until the user picks another folder."""
    return os.path.join(os.path.expanduser('~'), 'Documents', 'MovieShelf', 'lists')
