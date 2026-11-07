# Changelog

## 1.0.0 (first public release)

The first public release of **MovieShelf** (the new name of MovieDB).

### 🪟 Windows app
- Native WinUI 3 app for Windows 10 (1809+) and Windows 11, no other software needed
- One-file setup and uninstall wizard in English and Persian (just me, all users, or portable; silent install with `/S`)
- Poster grid and table, search language, filters, saved views, movie and person windows
- **Load list** (`.txt`, `.csv`, a folder of lists or a folder of movies) with a preview of repeated, existing and similar names; add to the current list or start a new one (old lists go to `previous lists`, nothing is deleted)
- Movie folders are a source of movies: every video found is listed and goes away with its file
- **Problems** window: matching, duplicates, quality and more, with Fix match per file, a Google search link and *Show in folder*
- **Rename plan** with your own pattern (`{title} {year} {res} {src} {codec} {dub} {edition} {group} {imdb}`) for videos, subtitles and folders; never renames over an existing name
- Check a list, wishlist, collections, statistics, Watch next, Excel and CSV export
- Quick backup (now with removed titles and your lists) and full backup with restore; backups made by MovieDB restore fine

### 🐍 Python app
- The same library on Windows, macOS and Linux (`run.sh`, `Setup.bat`), in its own window or your browser

### 📝 Docs
- Full guide, Windows install guide, Python guide, build guide
