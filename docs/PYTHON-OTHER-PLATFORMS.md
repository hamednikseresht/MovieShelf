# 🐍 MovieShelf with Python (macOS, Linux, Windows)

The Python app is the original MovieShelf: a small local server plus web pages. It runs anywhere Python 3.9 or newer runs, in its own window or in your browser. It reads the same library format as the Windows app.

> ⚠️ **Honest note:** the Python app is developed and tested on **Windows**. The macOS and Linux launcher (`run.sh`) is written carefully but has had less testing. If something fails, please [open an issue](https://github.com/hamednikseresht/MovieShelf/issues).

## ⚡ Quick start

### 🍎🐧 macOS and Linux

```bash
git clone https://github.com/hamednikseresht/MovieShelf.git
cd MovieShelf
chmod +x run.sh
./run.sh
```

Or download **`MovieShelf-Python-1.0.0.zip`** from the [latest release](https://github.com/hamednikseresht/MovieShelf/releases/latest), unzip it, and run `./run.sh` inside.

What `run.sh` does the first time:
1. finds Python 3.9 or newer,
2. creates a **private environment** in your MovieShelf data folder (your system Python is not changed),
3. installs `rapidfuzz` (and `pywebview` on macOS),
4. starts MovieShelf: **own window on macOS**, **your web browser on Linux**.

Later runs start straight away.

| Command | What it does |
|---|---|
| `./run.sh` | Start (window on macOS, browser on Linux) |
| `./run.sh --browser` | Always open in your web browser |
| `./run.sh --server` | Server only, prints the address (for a home server or NAS) |
| `./run.sh --reinstall` | Rebuild the private environment, then start |
| `MOVIESHELF_WINDOW=1 ./run.sh` | Try the own window on Linux (needs GTK or Qt, see below) |
| `MOVIESHELF_PORT=8765 ./run.sh` | Use a fixed port |

### 🪟 Windows (Python version)

Double-click **`Setup.bat`**. It finds or installs Python 3.12 (with winget), sets up a private environment, adds a desktop shortcut and starts the app. Start it later with **`MovieShelf.bat`**. Most Windows users prefer the [native app](INSTALL-WINDOWS.md).

### 🧑‍💻 By hand, on any system

```bash
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt     # Windows: .venv\Scripts\pip
.venv/bin/python app/run.py --browser          # or: python app/run.py  (own window, needs pywebview)
```

`requirements.txt` has `rapidfuzz` and `pywebview`. If you only use the browser mode, `rapidfuzz` alone is enough.

## 🚀 First start

The first time, MovieShelf asks how to begin:

- 📁 **Movie folder** and/or 📝 **Movie list** (a `.txt` or `.csv`, one name per line). One is enough.
- Or **Load a backup** (a full backup `.zip`).

It then downloads the IMDb data (about 2 GB, once) and builds your library. Plan for 10 to 20 minutes. Later, add movies with **Load list** or **Settings → Movie folders**. The complete walkthrough is in the [guide](GUIDE.md).

## 📂 Where your data lives

| System | Default folder |
|---|---|
| macOS | `~/Library/Application Support/MovieShelf` |
| Linux | `${XDG_DATA_HOME:-~/.local/share}/movieshelf` |
| Windows | `%LOCALAPPDATA%\MovieShelf` |

Change it with the environment variable **`MOVIESHELF_HOME`**. A file named `portable.txt` next to `app/run.py` makes the program folder hold the data instead.

Inside: `data/library.db` (your library), `data/imdb/` (IMDb files), `data/posters/`, `data/settings.json`, `data/backups/`, `data/movieshelf.log`, and `venv/` (the private Python).

## 🧯 Troubleshooting

| Problem | Fix |
|---|---|
| `python3: command not found` | macOS: `brew install python`. Debian / Ubuntu: `sudo apt install python3 python3-venv python3-pip`. Fedora: `sudo dnf install python3` |
| `Could not create the Python environment` | Debian / Ubuntu: `sudo apt install python3-venv` |
| `pip` cannot download packages | Check your internet, proxy or VPN. Run `./run.sh --reinstall` after fixing it |
| The window does not open on Linux | That is the default: it opens in your browser. For a window install GTK (`sudo apt install python3-gi gir1.2-webkit2-4.1`) or Qt, then `MOVIESHELF_WINDOW=1 ./run.sh` |
| "MovieShelf is already running" | It is open already; the address was opened in your browser. Close the other instance to start a new one |
| **Show in folder** does nothing | That button uses Windows Explorer and is **Windows-only** in the Python app |
| `ffprobe` not found | Install ffmpeg: `brew install ffmpeg` or `sudo apt install ffmpeg`. MovieShelf finds it on your `PATH` |
| Port in use | Set `MOVIESHELF_PORT` to a free port |

## 🧪 Run the tests

```bash
python -m unittest discover -s tests        # 84 Python tests
node --test tests/query.test.mjs tests/i18n.test.mjs   # search and translation tests (needs Node.js)
```

## 🔁 Python app and Windows app together

On Windows you can use both apps on the **same library**: favorites, watched marks, ratings, notes and fixed matches are shared. Do not run both at the same moment.
