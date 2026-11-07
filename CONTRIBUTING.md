# 🤝 Contributing to MovieShelf

Thank you for helping! Small fixes and big ideas are both welcome.

## 🐛 Found a bug?
[Open an issue](https://github.com/hamednikseresht/MovieShelf/issues/new/choose). Please include your Windows or OS version, the app version (Settings → About, or the setup file name), what you did, what you expected and what happened. A log from `%LOCALAPPDATA%\MovieShelf\data\` helps. Remove private paths first.

## 💡 Have an idea?
Open a *Feature request*. Say what you want to do, not only how.

## 🌍 Translations
The Persian text is in `app/web/i18n.js` and `native/MovieShelf.App/Loc*.cs`; the help is in `app/web/help/` and `native/MovieShelf.App/Help/`. Other languages: add a help file and a string list the same way.

## 🔧 Code
1. Fork and create a branch.
2. Build and test: see [docs/BUILD.md](docs/BUILD.md). Run **both** test suites:
   `python -m unittest discover -s tests` and `dotnet test native/MovieShelf.Core.Tests -c Release`.
3. The two apps share one library format, and the C# engine is a port of the Python modules. A behaviour change in one needs the same change in the other, with a test in each.
4. Keep pull requests small and describe what changed and why.

By contributing you agree your work is released under the [MIT license](LICENSE).
