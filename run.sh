#!/usr/bin/env bash
# MovieShelf for macOS and Linux (the Python app).
# First run: creates a private Python environment and installs what MovieShelf needs. After that it just starts the app.
#
#   ./run.sh              start MovieShelf (own window on macOS, your web browser on Linux)
#   ./run.sh --browser    always open it in your web browser
#   ./run.sh --server     run the server only and print the address
#   ./run.sh --reinstall  rebuild the private Python environment, then start
#
# Your library, posters, IMDb data and settings live in MOVIESHELF_HOME. Default:
#   macOS  ~/Library/Application Support/MovieShelf
#   Linux  ${XDG_DATA_HOME:-~/.local/share}/movieshelf
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

if [ -z "${MOVIESHELF_HOME:-}" ]; then
  case "$(uname -s)" in
    Darwin) MOVIESHELF_HOME="$HOME/Library/Application Support/MovieShelf" ;;
    *)      MOVIESHELF_HOME="${XDG_DATA_HOME:-$HOME/.local/share}/movieshelf" ;;
  esac
fi
export MOVIESHELF_HOME
VENV="$MOVIESHELF_HOME/venv"
mkdir -p "$MOVIESHELF_HOME"

args=()
reinstall=0
for a in "$@"; do
  if [ "$a" = "--reinstall" ]; then reinstall=1; else args+=("$a"); fi
done

find_python() {
  for c in python3.13 python3.12 python3.11 python3.10 python3.9 python3 python; do
    if command -v "$c" >/dev/null 2>&1 && "$c" -c 'import sys; sys.exit(0 if sys.version_info >= (3, 9) else 1)' 2>/dev/null; then
      echo "$c"; return 0
    fi
  done
  return 1
}

if [ "$reinstall" = 1 ] && [ -d "$VENV" ]; then rm -rf "$VENV"; fi

if [ ! -x "$VENV/bin/python" ]; then
  PY="$(find_python)" || {
    echo "MovieShelf needs Python 3.9 or newer, and none was found."
    echo "  macOS:   brew install python        (or https://www.python.org/downloads/)"
    echo "  Debian:  sudo apt install python3 python3-venv python3-pip"
    echo "  Fedora:  sudo dnf install python3"
    exit 1
  }
  echo "Setting up MovieShelf (first run only) with $($PY --version)..."
  "$PY" -m venv "$VENV" || { echo "Could not create the Python environment. On Debian/Ubuntu: sudo apt install python3-venv"; exit 1; }
  "$VENV/bin/python" -m pip install --quiet --upgrade pip
  "$VENV/bin/python" -m pip install --quiet "rapidfuzz>=3.0"
fi

# The own-window mode needs pywebview, which needs system libraries on Linux. Try it once on macOS; on Linux the browser is the default.
want_window=0
case "$(uname -s)" in Darwin) want_window=1 ;; esac
[ "${MOVIESHELF_WINDOW:-0}" = 1 ] && want_window=1
if [ "$want_window" = 1 ] && ! "$VENV/bin/python" -c 'import webview' 2>/dev/null; then
  "$VENV/bin/python" -m pip install --quiet "pywebview>=5.0" || echo "pywebview could not be installed: MovieShelf opens in your web browser instead."
fi

has_mode=0
for a in ${args[@]+"${args[@]}"}; do case "$a" in --browser|--server) has_mode=1 ;; esac; done
if [ "$has_mode" = 0 ] && [ "$want_window" = 0 ]; then args+=(--browser); fi

exec "$VENV/bin/python" app/run.py ${args[@]+"${args[@]}"}
