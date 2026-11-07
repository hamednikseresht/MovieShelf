# MovieShelf: the complete guide

> This is the full user guide. New here? Start with the [README](../README.md), [Windows install](INSTALL-WINDOWS.md) or [Python on macOS / Linux](PYTHON-OTHER-PLATFORMS.md).

A fast movie database app for Windows, macOS and Linux. It builds the database from your movie list (`.txt` files, one release name per line) and IMDb data, or loads a full backup made earlier.

## What is in the folder

```
MovieShelf\
  Setup.bat     the one setup: run it once (and again to repair or update)
  MovieShelf.bat   the one file that starts the app
  app\          the program (Python code and the web pages)
  tests\        self-checks
  tools\        the icon and the test runner (not needed to use the app)
  native\       the Windows 11 native version (C#, .NET 10, WinUI 3); see native\README.md
```

The `native` app is a second front end for the same library: it reads and writes the same data folder, so you can use either one. Start it with `native\MovieShelf-Native.bat` (the first run builds it and needs the .NET 10 SDK).

Everything the app makes after setup (the Python environment, database, posters, IMDb data, settings, logs) goes to **`%LOCALAPPDATA%\MovieShelf`**, so the MovieShelf folder stays clean and can be moved, copied or deleted without touching your library. If an older version kept its `data` folder inside the MovieShelf folder, Setup moves it there for you.

## Help and language

The full guide is inside the app: the **?** button in the top bar opens **Help** with a contents list, a search box and fourteen topics (welcome, setup, the main window, finding movies, the movie window, collection tools, list check, problems, folders and ffprobe, export, settings, shortcuts, troubleshooting, files and licences). Windows such as Settings, Problems and Export have a small **?** in the corner that jumps to their topic. *Open as a page* shows the same guide as a normal web page that can be printed: `http://127.0.0.1:<port>/static/help.html`.

The **FA / EN** button in the top bar (or Settings → Appearance → Language) switches the menus and the Help between English and Persian (فارسی). The menus, filters, toolbar, window titles and tabs and the whole Help are translated and read right to left; movie titles, cast and the text inside dialogs stay as they are. The choice is remembered.

The help text lives in `app\web\help\en.html` and `app\web\help\fa.html` (one `<section>` per topic, the same topics in both files); the menu translations are the `FA` list in `app\web\i18n.js`. To translate to another language, add a file in `app\web\help` and a list like `FA`.

## Setup guide

1. **Run `Setup.bat`** (double-click it in the MovieShelf folder). It asks nothing: it installs what the program needs, adds a desktop shortcut and starts MovieShelf. It does not build the database.

   | Step | What it does | If it fails |
   |---|---|---|
   | Python | Finds Python 3.9 or newer. If there is none, it installs Python 3.12 for your user with winget. | Install Python 3.12 from python.org and tick "Add python.exe to PATH". Then run Setup.bat again. |
   | Python packages | Creates a private environment (`%LOCALAPPDATA%\MovieShelf\venv`) and downloads and installs `rapidfuzz` and `pywebview`. It retries once. | The hint names the cause: no internet, proxy or VPN, SSL inspection by an antivirus, or a file that is in use. |
   | WebView2 | Checks the Edge runtime that draws the app window and installs it if missing. | Without it, the app opens in your web browser instead. |
   | Shortcut | Puts a MovieShelf shortcut on the desktop. | Use `MovieShelf.bat` instead. |

   Every step is written to `%LOCALAPPDATA%\MovieShelf\data\setup.log`. Setup is safe to run again at any time to repair or update; your data is kept. `Setup.bat --no-run` installs without starting MovieShelf.

2. **Choose how to start.** The first time MovieShelf opens it asks:

   * **Load a backup**: pick a full backup (.zip) made on this or another PC. Library, settings, API keys, list files and posters come back; whatever the backup does not hold (such as the IMDb data) is downloaded afterwards.
   * **Start with the defaults**: choose the folder with your `.txt` list. MovieShelf downloads the IMDb data (about 1.9 GB, the first time only), matches your list and gets cast, posters, ratings and Rotten Tomatoes / Metacritic scores. Plan for 10 to 20 minutes; finished parts are kept if you close the app.

3. **Open the app** later with the desktop shortcut or `MovieShelf.bat`. If the app is not set up yet, `MovieShelf.bat` starts Setup for you. If MovieShelf is already open, a second start does not run it twice. Errors at start-up are shown in a message box and logged to `%LOCALAPPDATA%\MovieShelf\data\movieshelf.log`.

* `python run.py --browser` opens the app in your web browser instead of its own window.
* `python run.py --server` runs the server only and prints the address.

Each time the app starts it rescans the list. It adds new lines and fills in anything still missing (cast, posters, scores). A progress bar under the top bar shows the progress; its **Stop** button ends the update at once (a half-downloaded file is removed, finished files and results are kept, and the next update continues). A restore cannot be stopped. The IMDb files are downloaded to `%LOCALAPPDATA%\MovieShelf\data\imdb`, and the search index built from them is `%LOCALAPPDATA%\MovieShelf\data\imdb_index.db`.

## Finding movies

The search box understands a few operators; type them next to normal words (all words must match). Click the question mark next to the box for examples.

| Type | Finds |
| --- | --- |
| `year:1990-1999`, `year:>2005` | released in a range, or after / before a year |
| `rating:>8`, `rt:>=90`, `mc:>=75`, `votes:>100k` | IMDb rating, Rotten Tomatoes, Metacritic, votes (a bare number means "at least") |
| `genre:horror`, `dir:nolan`, `actor:"tom hanks"`, `writer:`, `country:`, `lang:` | people, genres, places |
| `res:1080p`, `res:4k`, `src:bluray`, `codec:x265`, `file:farsi` | your files |
| `is:fav`, `is:unwatched`, `is:review`, `has:poster`, `has:plot` | flags |
| `-zombie`, `"love story"` | leave a word out, exact phrase |

Above the results, **Group** splits the list by decade, year, genre, rating, quality, type, first letter or watched (click a group name to fold it), and **Sort** orders it (the arrow button reverses; clicking a table column header does the same). Settings → Appearance switches between light, dark and Windows' own theme and sets the poster size.

## Export to Excel

Click **Export** above the results (or Settings → Maintenance). Choose:

- **Which movies**: the current results (your filters and search, in the order shown) or the whole library, narrowed further by search text, year, rating, genre, quality or status.
- **Columns**: tick what you need (title, year, ratings, people, file quality, file names, your notes, and more). *Essentials*, *Everything* and *Clear* are shortcuts, and your choice is remembered.
- **Layout**: one row per movie, or one row per file; optionally one Excel sheet per group (uses the Group setting).
- **File type**: Excel (.xlsx, with a frozen header row, filter buttons and clickable IMDb links) or CSV.

A last sheet, *Export info*, records when the file was made and which filters were used. Excel does not need to be installed to create the file.

## Watch next

The shuffle button above the results opens **Watch next**: six picks from the current results (or the whole library), by default only movies you have not watched. Choose *Best rated*, *Surprise me* or *Hidden gems* (rated 7.2+ with fewer than 60,000 votes), limit the length and rating, and press *Pick again*. *Watched* on a pick marks it and brings another. The dialog also lists what you watched recently.

## Select several movies

**Select** above the results turns on selection: click movies (Shift-click for a range), then use the bar at the bottom: favorite, watched, not watched, rate 1 to 10, add to the wishlist, or export just those movies. *Select all results* picks everything the filters show. Esc or *Done* leaves selection mode.

## People profiles

Click any cast, director, writer or crew name (in a movie window or the statistics) to open the person's **profile**: photo, birth and death (with age), birthplace, professions, biography and their movies grouped by job: Actor, Director, Writer, Producer, Composer, Cinematographer, Editor and every other crew job. The movies in your library come first as posters (with the character or job); the person's other movies follow, each with a *Wishlist* button. *Show all in the library* filters the library by that person.

IMDb's offline data gives birth and death years and professions for everyone. Photo, biography, birthplace and the full filmography need a free TMDB key (Settings → Online data). A profile is fetched the first time it is opened and kept for 90 days (*Refresh* fetches it again). **Settings → General → People → Scan people now** fetches profiles in advance for directors and writers, directors/writers plus the first five actors, or everyone (tens of thousands: slow), so profiles open at once and work offline; *Scan people automatically after each update* repeats it. Photos live in `data\people` and are part of the full backup (with the posters).

## Saved views

Set up a search and filters, then **Saved views → Save the current view** in the sidebar (the search text, filters, sort and grouping are all kept). Click a view to apply it, the cross to delete it.

## Wishlist

The bookmark button in the header opens the wishlist: movies you want, or want a better copy of. Add to it by hand, from the list check (*Add selected to wishlist*), from Problems → Quality → Low quality, or from Collections. Mark items found, copy the list, or save it as text or CSV.

## Collections

The layers button in the header lists franchises in your library and, with a TMDB key, the movies of each that you do not have yet (*Check for missing movies*). *Add missing to wishlist* puts them on the wishlist. Without a TMDB key the list stays empty.

## Where movies come from: two places only

1. **Movie folders** (Settings → General): one folder per line. The app goes through them by itself, finds every movie and lists it. A movie found this way goes away when its video does (a folder on a drive that is not connected is left as it is). With the folders it also knows each movie's path and size, so:

2. **Load list** (button at the top, also in Settings → General): a `.txt` or `.csv` list of movie names, a folder of lists, or a folder of movies (the names of its video files). Before anything is added you see the duplicates: names repeated inside the list, names already in your library, and names that look like a movie you already have under another file name. Then you choose **Add to current list** (the names join your lists) or **Start a new list** (your current list files are moved to a `previous lists` folder inside the list folder, never deleted; movies found in your movie folders stay).

Lists are kept as `.txt` files in your list folder (shown in Settings → General). One of the two places is enough.

- Problems → Duplicates shows sizes and locations, keeps the bigger file when quality is equal, adds up the disk space of redundant copies, and reports the same file in two folders.
- Each file in the movie window has *Show in folder*.
- Excel export gets *Size (GB)* and *Folder path* columns.
- Settings → Maintenance → **Rename plan** writes a `.bat` script (and a CSV and a list of new names) with tidy names for each movie's file, subtitles and folder, from a pattern you choose (default `{title} ({year}) [{res} {src}]`). The app never renames anything itself: read the script, then run it. Afterwards bring the new names in with Load list.

### Audio, subtitles and real quality (ffprobe)

With movie folders set, tick **Read audio languages, subtitles, length and real picture size with ffprobe** (Settings → General). The app needs `ffprobe`, which comes with ffmpeg: `winget install Gyan.FFmpeg`. It reads each file once (and again only if it changes), then:

- the movie window shows length, picture size, audio languages and subtitles of each file;
- search understands `audio:persian` and `sub:english`; Excel export gets *Length*, *Picture size*, *Audio languages* and *Subtitles* columns;
- Problems → Quality → **Name vs picture** lists files whose name claims a higher resolution than the picture has, and Problems → Other → **Length differs** lists files far shorter or longer than IMDb's runtime (cut short, another cut, or the wrong movie).

## Updating to a new version

Your library, posters, IMDb data and settings are in `%LOCALAPPDATA%\MovieShelf`, not in the MovieShelf folder, so an update only swaps the program:

1. Optional safety copy: Settings → Maintenance → *Save full backup…*.
2. Close MovieShelf.
3. Unzip the new release over the old MovieShelf folder (replace everything), or delete the old folder and put the new one in its place.
4. Run `Setup.bat` (it updates the Python packages if the release needs newer ones, and starts the app).

The library is upgraded on the first start (new columns are added, nothing is lost, no download unless the release needs new data). The version number is in the `VERSION` file.

## Backup

**Full backup** (Settings → Maintenance → Full backup) writes everything into one `.zip`: the whole library (`library.db`: every movie, match, detail and score plus your favorites, watched flags, ratings, notes, ignored problems, saved views, wishlist and scanned folders), all settings **including the API keys**, your list `.txt` files and the posters. The IMDb data (about 3 GB) is optional because it can be downloaded again. Untick *API keys* to leave them out; the file holds them as plain text, so keep it private.

**Load full backup** restores it, on this PC or another: tick what to restore (library, settings, API keys, list files, posters, IMDb data) and press *Restore*. First a safety copy of your current library, settings and list files is saved in `data\backups\before-restore-<time>`, so a wrong restore can be undone. If the old list or movie folders are missing on the new PC, you are told, and the list folder setting points to the restored list files. Backups from older versions are upgraded automatically.

**Quick backup** (same place) keeps only your own data (favorites, watched, ratings, notes, manual matches, ignored problems, saved views, wishlist, and the names in your movie lists) in a small `.json`. A copy of that is also saved automatically once a day in `data\backups` (the last 14 are kept); *Restore an automatic backup* merges one back without deleting anything.

## Tests

```
tools\Tests.bat
```

or by hand:

```
python -m unittest discover -s tests
node --test tests/query.test.mjs tests/i18n.test.mjs
```

`tools\Tests.bat` runs the Python and search tests.

The first line tests the Python side (parsing, quality comparison, problems, export, backup, collections); the second tests the search language in `web\query.js`; the third (`tests\i18n.test.mjs`) checks that the English and Persian help have the same topics and that every menu text has a Persian translation.

## Check a list or folder

Use the clipboard icon in the top bar to compare another list with your library before adding it. The list can be:

* a **.txt** file (one name per line; lines ending in .srt, .nfo, .jpg and other side-file extensions are ignored, in lists and in the library);
* a **.csv** file (a name column, or title + year columns, or an IMDb id column; these are detected automatically);
* a **folder**: every video file in it and its sub-folders is read. Samples and extras are skipped. DVD and Blu-ray folders count as one movie. A generic file such as `movie.mkv` takes its folder's name;
* names pasted into the box.

Each entry gets one of these results:

| Result | Meaning |
|---|---|
| New | Not in your library yet. |
| Better quality | You have this movie, but in lower quality. The comparison uses resolution, then source, HDR, 10-bit and audio channels, and only facts known for both copies. |
| Other version | You have this movie, but the audio differs (Farsi dubbed or original). |
| Lower quality / Already have | You already have a copy that is better or the same. |
| Same file | This exact name is already in your list. |
| Not found | IMDb has no match. These entries also show a reason. |
| Needs a check | The name is spelled differently, the year differs, or several movies share the title. Pick the right one from the choices shown. |
| Twice in this list | The same movie appears more than once in the checked list. |
| Only in library | You have it, but the checked list does not. Useful when you check a whole drive. |

Tick the entries you want and click **Add selected**. They join your list (in `added.txt` in your list folder) and are matched like the rest. **Export report** saves everything as CSV.

## Titles with no movie file (remove from list)

When your movie folders are set, a title whose own folder is empty, or holds only junk (`.nfo`, `.jpg`, `.url`, a sample), is listed in **Problems → Other → No video file** (the header badge counts it, and Problems opens there when nothing needs matching). Press **Remove from list**, or **Remove all** for the whole tab. The title is hidden in the library and no update brings it back. Your list files and movie files are not changed. **Remove from list** is also in the movie window (next to each file) and in the selection bar. A title that has a real video in another folder is not reported.

## Posters by hand

If no poster can be found, open the movie and press **Add poster** (or use Problems → Other → **No poster**). Choose an image file (JPEG, PNG, WebP or GIF, up to 15 MB), or paste an image address or a file path, then press **Use**. The poster is kept: updates never replace it, and the full backup holds it. **Remove poster** clears it.

## Problems

The **Problems** button (with a count) has four tabs:

- **Matching**: names that could not be matched to IMDb for sure (different name, wrong year, several movies with one title). Confirm the match, pick another, or search.
- **Duplicates**: movies you have more than once. Every copy is marked *Keep*, *Lower quality* or *Same quality*; a dubbed + original pair is shown as *not redundant*. The app never deletes files: use *Copy names to remove*, or *Ignore* if you keep both on purpose. Files whose names differ by a number (Vol. 1 / Vol. 2, Star Wars 4 / 6) or look like bonus material are listed separately as *Different movies?*: they are probably wrong matches, so use *Fix match*. With movie folders set, *Same file twice* reports a file stored in two places and the list shows sizes and wasted space.
- **Quality**: movies whose best copy is SD, 480p or 576p (most popular first, the best upgrade candidates), movies with no resolution in the file name, and movies that only exist Farsi-dubbed.
- **Other**: *Name differs* (the file name is not the real name of the film on IMDb: a typo, a missing word, another language; the most different first, use *Fix match* if it is the wrong film), file-name year two or more years from IMDb's year (possible wrong match), TV specials and shorts in a movie list, and missing posters (with a *Retry* button).

## Series

The switch above the results, **All / Movies / Series**, picks a section. **Series** holds the TV series and mini-series that your list names matched on IMDb (for example `Breaking.Bad.2008`); the poster shows the years it ran and the length of an episode. Filters, search, sorting and counts work inside the chosen section, and the choice is remembered. TV series are not problems: Problems → Other lists only TV specials and shorts.

*Ignore* hides an item until its files change; *Show ignored* brings it back. Each list can be exported to CSV.

## Ratings and critic scores

* **IMDb rating and votes** refresh automatically once a week from IMDb's ratings file (about 8 MB). Settings has an **Update IMDb ratings now** button.
* **Rotten Tomatoes (Tomatometer) and Metacritic (Metascore)** come from Wikidata, which is free and needs no key. About 80% of the collection is covered. These scores refresh monthly, and Settings has an **Update RT & Metacritic now** button.
* **Optional OMDb key** (free at omdbapi.com) fills the scores that Wikidata lacks. The free tier allows 1,000 titles a day, and the rest continue on the next update.
* Scores appear on the cards, in the details view and in the table. You can sort by them and filter with minimum-score sliders.

## What it does

* **Matching.** Each name such as `13.Minutes.(Elser).2015.720p.Bluray.Ganool` is parsed into a title, an alternative title, the year and quality info. It is matched offline against the official **IMDb datasets**, using exact titles, all alternative titles (akas), English and original names, fuzzy matching within the year, and English and original names glued together. Anything still not found is searched on IMDb online.
* **Data per movie.** IMDb number, English, original and Persian titles, other titles, year, type, runtime, genres, IMDb rating and votes, Rotten Tomatoes and Metacritic scores, directors, writers, main cast with character names, and other crew.
* **Poster.** Downloaded once and cached in `data\posters`.
* **Quality** read from the file name: resolution, source, x265, 10-bit, HDR, 5.1 or 7.1 audio, Farsi dubbed, dual audio, softsub, editions and release group.
* **Search** works across every title variant, actor, character, director, writer, year, `tt` id and file name. It ignores accents. Press `/` to start searching.
* **Filters:** genre, decade or year range, minimum IMDb, Rotten Tomatoes and Metacritic scores, votes, runtime, quality, type, country and language (TMDB), favorites, watched, needs review and multiple files. Click any actor or director to see all their movies.
* **Your own data:** favorite, watched, your rating (1–10) and notes.
* **Fix match** in each movie's details, plus **statistics**, a **random pick** and **CSV export** (Excel-friendly).

## Optional: TMDB key

Settings → paste a free [TMDB](https://www.themoviedb.org/settings/api) API key. This adds plots, taglines, countries, languages, Persian titles, cast photos and backdrops.

## Files

* `data\imdb\*.tsv.gz`: the IMDb datasets (about 1.9 GB). New movies need them for their cast.
* `data\imdb_index.db`: the offline title index (about 1 GB).
* `data\library.db`: your collection, matches and personal data. A daily copy of the personal part is in `data\backups`.
* `data\posters\`: cached posters.
* `data\setup.log` and `data\movieshelf.log`: logs from Setup and from the app.
* `venv\`: the private Python environment that Setup creates.

All of these are inside `%LOCALAPPDATA%\MovieShelf`.

IMDb data is used under IMDb's [non-commercial licence](https://developer.imdb.com/non-commercial-datasets/), for personal use only. The critic scores come from [Wikidata](https://www.wikidata.org/) (CC0) and, optionally, [OMDb](https://www.omdbapi.com/).
