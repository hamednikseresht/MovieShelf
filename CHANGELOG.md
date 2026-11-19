# Changelog

## 1.0.3 (2026-11-19)

### ⚡ Faster
- **Problems** opens much faster: the *Same name and year* check now uses the IMDb index's name keys and remembers its answers (on a 7,000-movie library the problem list went from about 7 s to under 1 s; the Windows Problems window shows its list in 0.5 s instead of 5.4 s)
- **Remove from list** in Problems: the row disappears at once, and the lists refresh in the background (both apps)
- Python app: the problem list no longer waits for a running update, and the spelling suggestions are prepared in the background when the app starts
- Windows app: the numbers in **Settings** and on the toolbar badges no longer wait for a running update

## 1.0.2 (2026-11-15)

### 🐍 Python app
- Everything new in 1.0.1 is now in the Python app too: **Add movie** (top bar), **Revise IMDb match** (in a movie's details), and the **Missing data** and **Same name and year** lists under Problems → Other

### 🪟 Windows app
- Problem lists sort exactly like the Python app (titles starting with İ)
- Python and Windows apps are checked against each other on the new lists (275 Windows tests, 92 Python tests)

## 1.0.1 (2026-11-12)

### 🪟 Windows app
- **Add movie**: add one movie by its IMDb link or `tt` number
- **Revise IMDb match** button in a movie's details: give the movie (all its files) another IMDb title by link, number or search
- Problems → Other: new **Missing data** list (no poster, cast, rating, genres or length) and **Same name and year** list (IMDb has other titles with the same name and year)

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
