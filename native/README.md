# MovieShelf native

The Windows 11 version of MovieShelf, written in C# on .NET 10 and WinUI 3 (Windows App SDK). It is a real desktop app: Mica backdrop, native title bar with the search box, light and dark theme that follow Windows, per-monitor DPI, and no web server or browser window behind it.

It works on **the same data folder as the Python version** (`%LOCALAPPDATA%\MovieShelf\data`: `library.db`, `imdb_index.db`, `posters\`, `settings.json`). You can keep both apps and switch between them; favourites, watched marks, ratings, notes, fixed matches and ignored problems are shared. The Python version stays in `..\app` and is not changed.

## Build and run

1. Install the .NET 10 SDK once: `winget install Microsoft.DotNet.SDK.10`
2. Double-click **`MovieShelf-Native.bat`**. It runs `Build.bat` the first time (about a minute, mostly package download) and then starts `dist\MovieShelf.exe`.

`Build.bat` publishes a self-contained build (about 240 MB), so the finished `dist` folder runs on any Windows 10 or 11 x64 PC without .NET installed. You can copy it anywhere. No Visual Studio is needed; plain `dotnet` is enough.

## One-file installer

Run **`Installer\Build-Installer.bat`** to get a single file, `MovieShelf-Setup.exe` (about 39 MB), to hand to other people. It needs the .NET 10 SDK and 7-Zip (`winget install 7zip.7zip`) on the build machine only; `--from <folder>` packs a finished publish folder instead of publishing again, and `--no-pause` skips the final key press.

The setup file is a wizard in English and Persian (right to left), with nothing to install first. It works on Windows 10 1809 and later and on Windows 11:

- **Pages:** language and welcome, system check, license, install location, components, shortcuts, a summary, progress, and a last page. If MovieShelf is already installed, the first page offers *Upgrade or repair* (which keeps the location and choices) or *Uninstall*.
- **Just for me** installs into `%LOCALAPPDATA%\Programs\MovieShelf` with no administrator rights. **For all users** installs into `%ProgramFiles%\MovieShelf`: the wizard stays unelevated and starts an elevated copy of itself for the file and HKLM work. **Portable** writes `portable.txt` so the library lives in the program folder.
- **Components:** the IMDb datasets (about 1.9 GB, to `<data>\imdb`, skipping files under 7 days old) and ffprobe (from gyan.dev, checked against its `.sha256`, to `<install>\tools\ffprobe.exe`). Downloads resume after a failure, and a failed optional download does not fail the install.
- **Safe to stop:** the new version is unpacked beside the target, then swapped in by renaming. Cancelling or a failure restores what was there; a new install removes what it wrote.
- **What it leaves:** the files, `uninstall.exe`, `install-manifest.txt` (every file and folder setup created), `install.log`, shortcuts and an entry in *Installed apps*. If a `MovieShelf.lnk` that opens something else already exists, it stays and the new shortcut is named `MovieShelf (native).lnk`.

`uninstall.exe` deletes only what the manifest lists and keeps the library unless you untick something on its *What to keep* page. It reports what was left behind.

Silent use:

```
MovieShelf-Setup.exe /S [/D=path] [/ALLUSERS] [/COMPONENTS=imdb,ffprobe] [/DESKTOP] [/NODESKTOP] [/NOSTARTMENU] [/PORTABLE] [/RUN] [/LANG=fa] [/LOG=path]
uninstall.exe /S [/DELETEDATA=library,backups,settings,posters,imdb,all] [/LOG=path]
```

`-y` is the same as `/S`. Exit codes: 0 done, 1 failed, 2 bad arguments, 3 cancelled, 4 system too old, 5 damaged setup file, 6 done but an optional download failed, 1223 administrator prompt declined.

For testing, `MOVIESHELF_SETUP_TEST_RENDER=<folder>` draws every page to PNG in both languages at four scales and writes any text that does not fit to `fit.txt`, and `MOVIESHELF_SETUP_TEST_DRIVE=<script>` walks the wizard through its real buttons (`src\TestMode.cs` lists the commands). `MOVIESHELF_HOME` is honoured as in the app.

How it stays small: the app is published self-contained, then `Installer\prune.ps1` deletes the Windows App SDK and .NET parts MovieShelf never loads (on-device AI with ONNX Runtime and DirectML, search, widgets, XML, debugger helpers, about 90 translations of the framework's own strings). The rest is packed with 7-Zip (LZMA2, solid) behind a small setup program (`Installer\src`, under 200 KB, compiled with the C# compiler that comes with Windows). Trimming the .NET assemblies (`PublishTrimmed`) was tried and crashes the app, so it is not used. If a future change makes the app load something `prune.ps1` removed, the app fails to start; run the app from the pruned build to check.

To work on it: open `MovieShelf.Native.slnx` in Visual Studio 2022 17.14 or newer, or use `dotnet build native\MovieShelf.App -c Release` and `dotnet test native\MovieShelf.Core.Tests`.

## What it does

Everything the Python version does, in a native window.

- **Library:** all movies and series in a poster grid or a table, sorted by title, year, IMDb rating, votes, Rotten Tomatoes, Metacritic, runtime, date added or your own rating, optionally grouped by decade, year, genre, rating, quality, type, first letter or watched state, with a **Movies / Series** switch and counts. Loads about 7,000 titles in under two seconds.
- **Search:** the full search language of the Python app (`year:1990-1999 rating:>8 genre:comedy dir:nolan actor:pitt res:4k is:unwatched has:poster -horror "exact phrase"` and more). The Help button lists them.
- **Filters and saved views:** status, genre, decade and year, IMDb rating, votes, Rotten Tomatoes, Metacritic, resolution, source, type, runtime, audio and video details, country and language, each with live counts. **Views** saves the current search, filters, sort and grouping under a name.
- **Movie pane:** poster, titles, scores, tagline, plot, directors and cast (click a name to open the person's profile), every file with its quality, Favourite, Watched, your own star rating and notes, and **Fix match** to pick another IMDb title.
- **Update:** one button runs the whole pipeline: downloads the IMDb data files and builds the offline index the first time (about 1 GB, a few minutes), refreshes ratings weekly, reads your list folder, scans your movie folders, matches names, fills in details, downloads posters, fetches plot and cast from TMDB, Rotten Tomatoes and Metacritic scores from Wikidata and OMDb, and optionally people profiles. It shows progress, has a Stop button, and queues a second request behind the running one. A welcome dialog sets up a fresh install or restores a backup.
- **Problems window:** Matching (with suggestions and the misspelling search), Duplicates, Name differs, Year differs, Low quality, No resolution, Dubbed only, TV that is not a film and No poster. Ignore and Stop ignoring work as in the Python app, and each list exports to CSV.
- **Wishlist:** movies you want, added by hand, from a collection, from the list check or from the selected movies. Copy, save as text or CSV, mark found.
- **Collections, Statistics, Person profiles:** missing parts of the franchises you own, charts of your library (click a bar to filter), and TMDB biography, photo and filmography for any person.
- **Check a list:** compare a list file, a folder or pasted text with your library and see what is new, better, a different version, worse or already there. Add to the library or the wishlist, export the report.
- **Movie folders:** every movie found in them is listed (with file size and place) and goes away when its video does; read video details with ffprobe (resolution, codec, audio and subtitle languages).
- **Rename plan:** a `.bat`, `.csv` or name list that renames your video files, their subtitles and the movie folder by a pattern you choose (`{title} {year} {res} {src} {codec} {dub} {edition} {group} {imdb}`). It never renames over an existing name. The app never renames anything itself.
- **Two places for movies:** your movie folders (every movie in them is listed, and goes away when its video does) and the **Load list** button (a `.txt` or `.csv` list, a folder of lists or a folder of movies, with a preview of repeated and existing names; add to the current list or start a new one, the old lists go to `previous lists`).
- **Backups:** personal backup (favourites, ratings, notes, fixed matches, views, wishlist) automatically once a day, and full backups as a zip with a restore.
- **Export:** Excel (`.xlsx`) or CSV (Excel-friendly UTF-8), one row per movie or per file, with the columns you tick.
- **Language and theme:** English or Persian (فارسی); Persian switches the whole window to right-to-left. Light, dark or the Windows setting.

The TMDB and OMDb keys in Settings are optional: without them you still get IMDb data, posters and Wikidata scores.

## How it is built

```
native\
  MovieShelf.Core\        no UI: name parser, matcher, IMDb download and index, online services, search language, problem checks, backups, export, SQLite library
  MovieShelf.Core.Tests\  xUnit tests, including parity checks against the Python code
  MovieShelf.App\         the WinUI 3 app (main window, dialogs, secondary windows, Persian translation)
```

`MovieShelf.Core` is a port of the Python modules. `parity.json` (3,560 real release names) and `match_parity.json` (395 matches against the real IMDb index) were produced by the Python code, and the tests check that the C# parser and matcher give the same results. Run `dotnet test native\MovieShelf.Core.Tests -c Release`; the matcher parity test is skipped when no IMDb index is installed.

Errors that are not caught are written to `%LOCALAPPDATA%\MovieShelf\data\native.log`. Window layout choices are kept in `native_ui.json` and the language in `settings.json` (`native_lang`) in the same folder.

For checking the interface without a screen, set the environment variable `MOVIESHELF_SHOT` to a folder before starting the app: it renders the main window in several states and every secondary window to PNG files there, writes `shots.log` and exits.
