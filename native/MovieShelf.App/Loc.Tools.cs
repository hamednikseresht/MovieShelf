namespace MovieShelf.App;

public static partial class Loc
{
    /// <summary>The text in the given language, whatever the app shows now: the printable help page is written in both.</summary>
    internal static string TIn(bool fa, string en)
    {
        if (fa)
            foreach (var d in Dictionaries)
                if (d.TryGetValue(en, out var v)) return v;
        return en;
    }

    // Persian for Settings, the welcome dialog and the tool windows.
    static readonly Dictionary<string, string> ExtraTools = new()
    {
        ["Poster size"] = "اندازه‌ی پوستر", ["Small"] = "کوچک", ["Medium"] = "متوسط", ["Large"] = "بزرگ",
        // ---- settings: general ----
        ["Setting saved"] = "تنظیم ذخیره شد",
        ["{0}: started"] = "{0}: شروع شد",
        ["{0}: queued, runs after the current job"] = "{0}: در صف است و بعد از کار فعلی اجرا می‌شود",
        ["list entries"] = "مورد در فهرست", ["matched to IMDb"] = "تطبیق‌یافته با IMDb", ["unique titles"] = "عنوان یکتا", ["posters"] = "پوستر",
        ["with cast & crew"] = "با بازیگران و عوامل", ["with RT / Metacritic"] = "با امتیاز RT / Metacritic",
        ["Add movie lists"] = "افزودن فهرست فیلم",
        ["A .txt list or a folder of lists, one movie per line. They are added to this library; nothing in it is replaced. Lists are kept in {0}."] =
            "یک فایل فهرست .txt یا پوشه‌ای از فهرست‌ها، هر خط یک فیلم. به همین کتابخانه افزوده می‌شوند و چیزی جایگزین نمی‌شود. فهرست‌ها در {0} نگه داشته می‌شوند.",
        [@"D:\Lists\more.txt or a folder"] = @"D:\Lists\more.txt یا یک پوشه",
        ["Choose a list…"] = "انتخاب فهرست…", ["Choose a folder…"] = "انتخاب پوشه…",
        ["{0} list added to your library. Updating…"] = "{0} فهرست به کتابخانه شما افزوده شد. در حال به‌روزرسانی…",
        ["{0} lists added to your library. Updating…"] = "{0} فهرست به کتابخانه شما افزوده شد. در حال به‌روزرسانی…",
        ["Those lists are already in your library"] = "این فهرست‌ها از قبل در کتابخانه شما هستند",
        ["Movie folders (optional)"] = "پوشه‌های فیلم (اختیاری)",
        ["One folder per line. Lets the app find each movie on disk: sizes, locations, wasted space in Problems, “Show in folder”, and the rename plan."] =
            "هر خط یک پوشه. برنامه با آن هر فیلم را روی دیسک پیدا می‌کند: حجم، محل، فضای هدررفته در مشکلات، «نمایش در پوشه» و طرح تغییر نام.",
        ["Add a folder…"] = "افزودن پوشه…", ["Save and scan"] = "ذخیره و اسکن",
        ["Reading your movie folders…"] = "در حال خواندن پوشه‌های فیلم شما…", ["Movie folders cleared"] = "پوشه‌های فیلم پاک شدند",
        ["Read audio languages, subtitles, length and real picture size with ffprobe"] = "خواندن زبان صدا، زیرنویس، مدت و اندازه واقعی تصویر با ffprobe",
        ["Read files now"] = "خواندن فایل‌ها اکنون", ["Reading the files with ffprobe…"] = "در حال خواندن فایل‌ها با ffprobe…",
        ["Only new or changed files are read on later updates."] = "در به‌روزرسانی‌های بعدی فقط فایل‌های تازه یا تغییریافته خوانده می‌شوند.",
        ["ffprobe not found. Install ffmpeg (winget install Gyan.FFmpeg), then restart the app."] = "برنامه ffprobe پیدا نشد. ffmpeg را نصب کنید (winget install Gyan.FFmpeg) و برنامه را دوباره باز کنید.",
        ["Automatic updates"] = "به‌روزرسانی‌های خودکار", ["What the app does on its own in the background."] = "کارهایی که برنامه خودش در پس‌زمینه انجام می‌دهد.",
        ["Download posters from IMDb"] = "دانلود پوستر از IMDb",
        ["Search IMDb online for titles not found offline"] = "جستجوی آنلاین در IMDb برای عنوان‌هایی که آفلاین پیدا نمی‌شوند",
        ["Keep IMDb ratings fresh (weekly) and critic scores (monthly)"] = "تازه نگه داشتن امتیازهای IMDb (هفتگی) و امتیاز منتقدان (ماهانه)",
        ["IMDb ratings are not downloaded yet."] = "امتیازهای IMDb هنوز دانلود نشده‌اند.",
        ["IMDb ratings are up to date today."] = "امتیازهای IMDb امروز به‌روز شده‌اند.",
        ["IMDb ratings are {0} days old."] = "امتیازهای IMDb مربوط به {0} روز پیش است.",
        ["The IMDb data (about 2 GB) is downloaded to {0}; the search index built from it is imdb_index.db in the folder above it."] =
            "داده‌های IMDb (حدود 2 گیگابایت) در {0} دانلود می‌شوند؛ نمایه جستجو که از آن‌ها ساخته می‌شود فایل imdb_index.db در پوشه بالاتر است.",
        ["The index is {0}."] = "حجم نمایه {0} است.",

        // ---- settings: appearance ----
        ["Saved on this computer."] = "روی همین رایانه ذخیره می‌شود.", ["Light, dark, or follow Windows."] = "روشن، تیره یا مطابق ویندوز.",
        ["Language / زبان"] = "زبان / Language",
        ["Menus and Help can be shown in English or Persian (فارسی)."] = "منوها و راهنما می‌توانند به انگلیسی یا فارسی نمایش داده شوند.",

        // ---- settings: online data ----
        ["Both keys are optional and free. Without them the app still works from the IMDb data."] = "هر دو کلید اختیاری و رایگان‌اند. بدون آن‌ها هم برنامه با داده‌های IMDb کار می‌کند.",
        ["TMDB API key"] = "کلید API برای TMDB", ["OMDb API key"] = "کلید API برای OMDb", ["Test & save"] = "آزمایش و ذخیره", ["Key saved"] = "کلید ذخیره شد",
        ["Key saved – paste a new one to replace"] = "کلید ذخیره شده است؛ برای جایگزینی، کلید تازه را بچسبانید",
        ["API key (v3) or Read Access Token (v4)"] = "کلید API (نسخه 3) یا Read Access Token (نسخه 4)",
        ["Adds plot, tagline, countries, languages, Persian titles, cast photos and backdrops."] = "خلاصه داستان، شعار فیلم، کشورها، زبان‌ها، عنوان فارسی، عکس بازیگران و تصویر پس‌زمینه را اضافه می‌کند.",
        ["Get one at themoviedb.org → Settings → API."] = "از themoviedb.org، بخش Settings و سپس API بگیرید.",
        ["Fills Rotten Tomatoes and Metacritic scores that Wikidata does not have."] = "امتیازهای Rotten Tomatoes و Metacritic را که در Wikidata نیست کامل می‌کند.",
        ["Get one at omdbapi.com/apikey.aspx (free: 1,000 titles a day; the rest continue on the next update)."] = "از omdbapi.com/apikey.aspx بگیرید (رایگان: روزی 1,000 عنوان؛ بقیه در به‌روزرسانی بعدی ادامه می‌یابد).",
        ["Key works and is saved. Click “Scan list & update” to fetch details."] = "کلید کار می‌کند و ذخیره شد. برای دریافت جزئیات، «اسکن فهرست و به‌روزرسانی» را بزنید.",
        ["Key works and is saved. Click “Update RT & Metacritic now”."] = "کلید کار می‌کند و ذخیره شد. «به‌روزرسانی RT و Metacritic» را بزنید.",

        // ---- settings: maintenance ----
        ["“Scan list & update” adds new lines from your list files. Manual matches are never overwritten."] = "«اسکن فهرست و به‌روزرسانی» خط‌های تازه فایل‌های فهرست را اضافه می‌کند. تطبیق‌های دستی هرگز بازنویسی نمی‌شوند.",
        ["Scan list & update"] = "اسکن فهرست و به‌روزرسانی", ["Update IMDb ratings"] = "به‌روزرسانی امتیازهای IMDb", ["Update RT & Metacritic"] = "به‌روزرسانی RT و Metacritic",
        ["Retry missing posters"] = "تلاش دوباره برای پوسترهای ناموجود",
        ["“Update IMDb ratings” downloads only the ratings file (about 8 MB)."] = "«به‌روزرسانی امتیازهای IMDb» فقط فایل امتیازها را دانلود می‌کند (حدود 8 مگابایت).",
        ["Pick the movies and the columns, then save an Excel or CSV file."] = "فیلم‌ها و ستون‌ها را انتخاب کنید و یک فایل Excel یا CSV ذخیره کنید.",
        ["Export to Excel…"] = "خروجی به Excel…",
        ["Add a free TMDB key in Online data first."] = "ابتدا در بخش داده‌های آنلاین یک کلید رایگان TMDB وارد کنید.",
        ["Whom to scan"] = "چه کسانی اسکن شوند", ["Directors and writers"] = "کارگردانان و نویسندگان",
        ["Directors, writers and the first five actors"] = "کارگردانان، نویسندگان و پنج بازیگر اول",
        ["Everyone with a credit (slow: tens of thousands)"] = "همه افراد ذکرشده (کند: ده‌ها هزار نفر)",
        ["Scan people now"] = "اسکن افراد اکنون", ["Scan people automatically after each update"] = "اسکن خودکار افراد بعد از هر به‌روزرسانی",
        ["Scanning people… progress shows below."] = "در حال اسکن افراد… پیشرفت در پایین دیده می‌شود.",
        ["Queued: it runs after the current job."] = "در صف است: بعد از کار فعلی اجرا می‌شود.",
        ["Photos, biographies and full filmographies of directors, writers and actors come from TMDB. Profiles are fetched when you open them; scanning fetches them in advance, so they open at once and work offline."] =
            "عکس، زندگینامه و فیلم‌شناسی کامل کارگردانان، نویسندگان و بازیگران از TMDB می‌آید. نمایه‌ها هنگام باز کردن دریافت می‌شوند؛ اسکن آن‌ها را از پیش دریافت می‌کند تا فوری باز شوند و آفلاین هم کار کنند.",
        ["{0} profiles fetched so far ({1} with a photo)."] = "تاکنون {0} نمایه دریافت شده است ({1} با عکس).",
        ["({0} to fetch)"] = "({0} برای دریافت)",
        ["Folders"] = "پوشه‌ها", ["Needs your movie folders (General)."] = "به پوشه‌های فیلم شما نیاز دارد (بخش عمومی).",
        ["Scan movie folders now"] = "اسکن پوشه‌های فیلم اکنون", ["Rename plan…"] = "طرح تغییر نام…",
        ["Everything in one .zip file: the whole library, all settings and API keys, your list files, the posters and, if you want, the IMDb data. Use it for a complete safety copy or to move to another PC."] =
            "همه‌چیز در یک فایل zip: کل کتابخانه، همه تنظیمات و کلیدهای API، فایل‌های فهرست، پوسترها و در صورت تمایل داده‌های IMDb. برای یک نسخه ایمن کامل یا انتقال به رایانه‌ای دیگر.",
        ["Save full backup…"] = "ذخیره پشتیبان کامل…", ["Load full backup…"] = "بازیابی پشتیبان کامل…",
        ["Quick backup (your own data only)"] = "پشتیبان سریع (فقط داده‌های خودتان)",
        ["Rebuild"] = "بازسازی", ["Only needed if matches look wrong or the IMDb data is damaged."] = "فقط وقتی لازم است که تطبیق‌ها نادرست به نظر برسند یا داده‌های IMDb آسیب دیده باشند.",
        ["Re-match everything"] = "تطبیق دوباره همه", ["Re-match everything?"] = "همه دوباره تطبیق داده شوند؟", ["Re-match"] = "تطبیق دوباره",
        ["Every automatically matched file is matched again. Matches you set yourself are kept. This takes a few minutes."] =
            "همه فایل‌هایی که خودکار تطبیق داده شده‌اند دوباره تطبیق داده می‌شوند. تطبیق‌هایی که خودتان انجام داده‌اید می‌مانند. این کار چند دقیقه طول می‌کشد.",
        ["Re-download IMDb data"] = "دانلود دوباره داده‌های IMDb", ["Download all IMDb data again?"] = "همه داده‌های IMDb دوباره دانلود شود؟", ["Download"] = "دانلود",
        ["About 1.9 GB is downloaded and the index is rebuilt (10–20 minutes). Your matches and notes are kept."] =
            "حدود 1.9 گیگابایت دانلود و نمایه دوباره ساخته می‌شود (10 تا 20 دقیقه). تطبیق‌ها و یادداشت‌های شما می‌مانند.",
        ["“Re-download IMDb data” refreshes everything (about 1.9 GB)."] = "«دانلود دوباره داده‌های IMDb» همه‌چیز را تازه می‌کند (حدود 1.9 گیگابایت).",
        ["What the last updates did."] = "کارهایی که آخرین به‌روزرسانی‌ها انجام دادند.", ["No activity yet."] = "هنوز فعالیتی نیست.",

        // ---- welcome ----
        ["How do you want to start?"] = "چطور می‌خواهید شروع کنید؟", ["Start with the defaults"] = "شروع با تنظیمات پیش‌فرض", ["Load a backup"] = "بارگذاری پشتیبان",
        ["Movie list folder"] = "پوشه فهرست فیلم‌ها",
        ["Read your movie list, download the IMDb data (about 2 GB, 10 to 20 minutes) and build a new library."] =
            "فهرست فیلم شما خوانده می‌شود، داده‌های IMDb دانلود می‌شود (حدود 2 گیگابایت، 10 تا 20 دقیقه) و کتابخانه‌ای تازه ساخته می‌شود.",
        ["Every .txt file in it is read, one movie name per line. More lists can be added later in Settings."] =
            "همه فایل‌های .txt آن خوانده می‌شوند، هر خط یک نام فیلم. بعداً می‌توانید در تنظیمات فهرست‌های دیگری اضافه کنید.",
        ["Bring back a full backup made on this or another PC: library, settings, API keys, list files and posters."] =
            "یک پشتیبان کامل ساخته‌شده روی این رایانه یا رایانه‌ای دیگر را برمی‌گرداند: کتابخانه، تنظیمات، کلیدهای API، فایل‌های فهرست و پوسترها.",

        // ---- what a running job is doing ----
        ["Fetching people profiles from TMDB"] = "دریافت نمایه افراد از TMDB", ["Fetching plots & details from TMDB"] = "دریافت خلاصه داستان و جزئیات از TMDB",
        ["Getting Rotten Tomatoes & Metacritic scores (Wikidata)"] = "دریافت امتیازهای Rotten Tomatoes و Metacritic از Wikidata", ["Getting missing scores from OMDb"] = "دریافت امتیازهای ناموجود از OMDb",
        ["Searching IMDb online for unmatched titles"] = "جستجوی آنلاین IMDb برای عنوان‌های تطبیق‌نیافته", ["Updating ratings"] = "به‌روزرسانی امتیازها",
        ["Downloading posters ({0} saved)"] = "دانلود پوسترها ({0} ذخیره شد)", ["Downloading {0}"] = "دانلود {0}",
        ["Reading ratings"] = "خواندن امتیازها", ["Indexing titles"] = "نمایه‌سازی عنوان‌ها", ["Indexing alternative titles"] = "نمایه‌سازی عنوان‌های دیگر",
        ["Creating search indexes"] = "ساخت نمایه‌های جستجو", ["Reading directors/writers"] = "خواندن کارگردانان و نویسندگان", ["Reading cast"] = "خواندن بازیگران",
        ["Reading names"] = "خواندن نام‌ها", ["Reading people details"] = "خواندن مشخصات افراد",

        // ---- tool windows ----
        ["Video files in your movie folders that are in none of your list files, so they are not in the library yet. Tick the ones you want and add them: their names go to added.txt in your list folder and are matched like the rest. Samples, trailers, episodes and tiny files are left out."] =
            "فایل‌های ویدیویی پوشه‌های فیلم شما که در هیچ فایل فهرستی نیستند و بنابراین هنوز در کتابخانه هم نیستند. آن‌هایی را که می‌خواهید علامت بزنید و اضافه کنید: نامشان به added.txt در پوشه فهرست‌ها افزوده و مانند بقیه تطبیق داده می‌شود. نمونه‌ها، تریلرها، قسمت‌های سریال و فایل‌های خیلی کوچک کنار گذاشته شده‌اند.",
        ["Archive footage"] = "تصاویر آرشیوی", ["On the wishlist"] = "در فهرست آرزو", ["Batch file"] = "فایل دسته‌ای",
        ["They are being matched now; progress shows below."] = "هم‌اکنون در حال تطبیق‌اند؛ پیشرفت در پایین دیده می‌شود.",
    };
}
