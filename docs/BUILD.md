# 🛠️ Build MovieShelf from source

## 🪟 Windows app (C#, .NET 10, WinUI 3)

You need Windows 10 or 11 (64-bit) and the **.NET 10 SDK**:

```powershell
winget install Microsoft.DotNet.SDK.10
```

### Run it

```bat
cd native
MovieShelf-Native.bat
```

The first run builds (about a minute, mostly package download) into `native\dist\` and starts `MovieShelf.exe`. The result is self-contained (about 240 MB) and runs on any Windows 10 or 11 x64 PC without .NET.

Or by hand:

```bat
dotnet publish native\MovieShelf.App\MovieShelf.App.csproj -c Release -r win-x64 --self-contained true -o dist
```

> 💡 Use `-r win-x64` with `dotnet build`, too, or the XAML compiler reports confusing "Unknown type" errors.

### One-file setup (`MovieShelf-Setup.exe`)

Needs [7-Zip](https://www.7-zip.org/) (`winget install 7zip.7zip`) on the build PC only:

```bat
native\Installer\Build-Installer.bat --no-pause
```

It publishes, removes the parts of the Windows App SDK the app never loads, packs everything with 7-Zip, and wraps it in the wizard (`native\Installer\src`, compiled with the C# compiler that ships with Windows). The result is `native\MovieShelf-Setup.exe` (about 39 MB). The version comes from `<Version>` in `native\MovieShelf.App\MovieShelf.App.csproj`.

`--from <folder>` packs a finished publish folder instead of publishing again.

### Tests

```bat
dotnet test native\MovieShelf.Core.Tests -c Release
```

256 xUnit tests, including **parity checks**: fixtures produced by the Python app (3,560 release names, 395 IMDb matches, backups, exports) must give the same answers in C#.

Useful environment variables while testing: `MOVIESHELF_HOME` (use another data folder), `MOVIESHELF_SHOT=<folder>` (render windows to PNG), `MOVIESHELF_SETUP_TEST_RENDER=<folder>` (draw every setup page).

## 🐍 Python app

```bash
python -m venv .venv && .venv/bin/pip install -r requirements.txt
.venv/bin/python app/run.py --browser
python -m unittest discover -s tests
```

See [PYTHON-OTHER-PLATFORMS.md](PYTHON-OTHER-PLATFORMS.md).

## 📦 Make a release

1. Set the version in `VERSION` and `native/MovieShelf.App/MovieShelf.App.csproj`, update `CHANGELOG.md`.
2. Run all tests (Python and C#).
3. Build the setup file, rename it `MovieShelf-Setup-<version>.exe`.
4. Zip the Python app: `app/`, `tests/`, `tools/`, `docs/`, `run.sh`, `Setup.bat`, `MovieShelf.bat`, `requirements.txt`, `VERSION`, `LICENSE`, `README.md`.
5. Write `SHA256SUMS.txt` and attach all three files to a new GitHub release tagged `v<version>`.
