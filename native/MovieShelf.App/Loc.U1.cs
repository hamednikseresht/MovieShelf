namespace MovieShelf.App;

/// <summary>Persian text of the wishlist, statistics, collections and backups windows (package U1). Keys with {0}, {1} are
/// format strings: the code calls <c>string.Format(Loc.T(key), ...)</c>. The main Loc class merges this table.</summary>
public static partial class Loc
{
    internal static readonly Dictionary<string, string> ExtraU1 = new()
    {
        // ---- shared ----
        ["Wishlist"] = "فهرست آرزو", ["Collections"] = "مجموعه‌ها", ["Statistics"] = "آمار", ["Backups"] = "پشتیبان‌گیری",
        ["Stop"] = "توقف", ["Cancel"] = "انصراف", ["Close"] = "بستن", ["Saved"] = "ذخیره شد", ["Add"] = "افزودن",
        ["Restore"] = "بازیابی", ["Starting"] = "در حال شروع", ["Finished"] = "پایان یافت", ["Stopping…"] = "در حال توقف…",
        ["An update is already running."] = "یک به‌روزرسانی در حال اجراست.",
        ["Another update is running. Try again when it has finished."] = "یک به‌روزرسانی در حال اجراست. پس از پایان دوباره تلاش کنید.",

        // ---- wishlist ----
        ["Movies you want to get or replace with a better copy. Add them from the list check, from Problems, from Collections, or here."] =
            "فیلم‌هایی که می‌خواهید تهیه کنید یا با نسخه بهتری جایگزین شوند. آن‌ها را از بررسی فهرست، مشکلات، مجموعه‌ها یا همین‌جا اضافه کنید.",
        ["Title (Year), for example Heat (1995)"] = "عنوان (سال)، مثلاً Heat (1995)",
        ["Wanted"] = "مورد نظر", ["Found"] = "پیدا شده", ["Wanted again"] = "دوباره می‌خواهم", ["Mark found"] = "پیدا کردم",
        ["Remove"] = "حذف", ["Open on IMDb"] = "باز کردن در IMDb",
        ["Added {0}"] = "افزوده‌شده در {0}",
        ["Added by hand"] = "افزوده‌شده دستی", ["Collection"] = "مجموعه",
        ["The wishlist is empty."] = "فهرست آرزو خالی است.", ["Nothing here."] = "اینجا چیزی نیست.",
        ["{0} wanted, {1} found"] = "{0} مورد نظر، {1} پیدا شده",
        ["Copy wanted"] = "کپی موارد مورد نظر", ["Save as text"] = "ذخیره به‌صورت متن", ["Save as CSV"] = "ذخیره به‌صورت CSV",
        ["Nothing to copy."] = "چیزی برای کپی نیست.", ["Copied {0} titles"] = "{0} عنوان کپی شد",
        ["Text file"] = "فایل متنی", ["CSV file"] = "فایل CSV",
        ["Added to the wishlist."] = "به فهرست آرزو اضافه شد.", ["Already on the wishlist."] = "از قبل در فهرست آرزو است.",

        // ---- statistics ----
        ["movies"] = "فیلم", ["files"] = "فایل", ["watched"] = "دیده‌شده", ["need review"] = "نیازمند بررسی",
        ["average IMDb rating"] = "میانگین امتیاز IMDb", ["total runtime"] = "مجموع زمان فیلم‌ها", ["{0} h"] = "{0} ساعت",
        ["total runtime ({0} days)"] = "مجموع زمان فیلم‌ها ({0} روز)",
        ["By decade"] = "بر اساس دهه", ["Genres"] = "ژانرها", ["Top directors"] = "پرکارترین کارگردانان", ["Top actors"] = "پرکارترین بازیگران",
        ["Quality"] = "کیفیت", ["IMDb rating"] = "امتیاز IMDb", ["Countries"] = "کشورها", ["Nothing to show."] = "چیزی برای نمایش نیست.",

        // ---- collections ----
        ["Collections need a TMDB key"] = "مجموعه‌ها به کلید TMDB نیاز دارند",
        ["Add a free TMDB key in Settings, Online data. Then press Fetch collection details."] =
            "در تنظیمات، بخش داده‌های آنلاین، یک کلید رایگان TMDB وارد کنید. سپس «دریافت جزئیات مجموعه‌ها» را بزنید.",
        ["Franchises in your library, and the movies of each that you do not have yet. The list of parts comes from TMDB."] =
            "سری فیلم‌های کتابخانه شما و فیلم‌هایی از هر سری که هنوز ندارید. فهرست قسمت‌ها از TMDB می‌آید.",
        ["Fetch collection details"] = "دریافت جزئیات مجموعه‌ها",
        ["Missing movies"] = "فیلم‌های ناقص", ["Complete"] = "کامل", ["All"] = "همه",
        ["Have"] = "دارید", ["Missing"] = "ندارید",
        ["you have {0} of {1}"] = "{0} از {1} فیلم را دارید",
        ["{0} in your library, parts not checked"] = "{0} فیلم در کتابخانه، قسمت‌ها بررسی نشده",
        ["Add {0} missing to wishlist"] = "افزودن {0} فیلم ناقص به فهرست آرزو",
        ["{0} of {1} collections not checked yet"] = "{0} مجموعه از {1} هنوز بررسی نشده",
        ["All collections are checked."] = "همه مجموعه‌ها بررسی شده‌اند.",
        ["No collections known yet."] = "هنوز مجموعه‌ای شناخته نشده است.", ["Nothing in this list."] = "در این فهرست چیزی نیست.",
        ["Stopped. What was checked is kept."] = "متوقف شد. آنچه بررسی شده بود حفظ شد.",
        ["Collections checked."] = "مجموعه‌ها بررسی شدند.",
        ["{0} added to the wishlist"] = "{0} مورد به فهرست آرزو اضافه شد",
        ["{0} added to the wishlist ({1} already there)"] = "{0} مورد به فهرست آرزو اضافه شد ({1} مورد از قبل بود)",
        ["Fetching collections from TMDB"] = "دریافت مجموعه‌ها از TMDB",

        // ---- backups ----
        ["Quick backup"] = "پشتیبان سریع", ["Full backup"] = "پشتیبان کامل",
        ["Your favorites, watched flags, ratings, notes, manual matches, ignored problems, saved views and wishlist. A copy is also saved automatically once a day in the data folder (the last 14 are kept)."] =
            "علاقه‌مندی‌ها، نشانه دیده‌شده، امتیازها، یادداشت‌ها، تطبیق‌های دستی، مشکلات نادیده‌گرفته‌شده، نماهای ذخیره‌شده و فهرست آرزوی شما. روزی یک نسخه هم به‌طور خودکار در پوشه داده‌ها ذخیره می‌شود (14 نسخه آخر نگه داشته می‌شود).",
        ["Export backup…"] = "ذخیره پشتیبان…", ["Import backup…"] = "بازیابی از پشتیبان…", ["Update now"] = "به‌روزرسانی",
        ["Automatic backups"] = "پشتیبان‌های خودکار",
        ["Restoring one merges it into your library. Nothing is deleted."] = "بازیابی، پشتیبان را با کتابخانه شما ادغام می‌کند. چیزی حذف نمی‌شود.",
        ["No automatic backups yet."] = "هنوز پشتیبان خودکاری وجود ندارد.",
        ["MovieShelf backup"] = "پشتیبان MovieShelf",
        ["Restore this backup?"] = "این پشتیبان بازیابی شود؟",
        ["Favorites, ratings, notes and views from the backup are merged into your library. Nothing is deleted."] =
            "علاقه‌مندی‌ها، امتیازها، یادداشت‌ها و نماهای پشتیبان با کتابخانه شما ادغام می‌شود. چیزی حذف نمی‌شود.",
        ["Restored {0} movie notes and flags, {1} manual matches, {2} ignored problems, {3} views and {4} wishlist items."] =
            "بازیابی شد: {0} یادداشت و نشانه فیلم، {1} تطبیق دستی، {2} مشکل نادیده‌گرفته‌شده، {3} نما و {4} مورد فهرست آرزو.",
        ["An update is needed"] = "به‌روزرسانی لازم است",
        ["The manual matches are applied by an update."] = "تطبیق‌های دستی با یک به‌روزرسانی اعمال می‌شوند.",
        ["Save a full backup"] = "ذخیره پشتیبان کامل",
        ["One .zip file with what you tick below. Restoring it on this or another PC brings everything back."] =
            "یک فایل zip با مواردی که در زیر انتخاب می‌کنید. بازیابی آن روی این رایانه یا رایانه‌ای دیگر همه‌چیز را برمی‌گرداند.",
        ["The library: every movie, match, detail, score, your favorites, watched flags, ratings, notes, saved views and wishlist ({0})"] =
            "کتابخانه: همه فیلم‌ها، تطبیق‌ها، جزئیات، امتیازها، علاقه‌مندی‌ها، نشانه دیده‌شده، امتیازهای شما، یادداشت‌ها، نماهای ذخیره‌شده و فهرست آرزو ({0})",
        ["All settings (list folder, movie folders, update options)"] = "همه تنظیمات (پوشه فهرست‌ها، پوشه فیلم‌ها، گزینه‌های به‌روزرسانی)",
        ["API keys (TMDB, OMDb)"] = "کلیدهای API برای TMDB و OMDb",
        ["Stored in the file as plain text. Keep the backup private."] = "در فایل به‌صورت متن ساده ذخیره می‌شوند. پشتیبان را نزد خود نگه دارید.",
        ["No API keys are set."] = "کلید API ثبت نشده است.",
        ["Your list files ({0}, {1})"] = "فایل‌های فهرست شما ({0}، {1})",
        ["Posters and person photos ({0}, {1})"] = "پوسترها و عکس افراد ({0}، {1})",
        ["{0} file"] = "{0} فایل", ["{0} files"] = "{0} فایل",
        ["IMDb data ({0})"] = "داده‌های IMDb ({0})",
        ["Can be downloaded again, so it is off by default. Tick it to restore without downloading it."] =
            "دوباره قابل دانلود است، پس به‌طور پیش‌فرض خاموش است. برای بازیابی بدون دانلود دوباره، آن را علامت بزنید.",
        ["About {0} before compression"] = "حدود {0} پیش از فشرده‌سازی",
        ["Save backup…"] = "ذخیره پشتیبان…", ["Full backup (.zip)"] = "پشتیبان کامل (zip)",
        ["Load a full backup"] = "بازیابی پشتیبان کامل",
        ["Pick a backup .zip made by Save a full backup. What you choose below replaces what you have now. A copy of your current library, settings and list files is saved first in the backups folder (before-restore-…)."] =
            "یک فایل zip ساخته‌شده با «ذخیره پشتیبان کامل» را انتخاب کنید. آنچه در زیر انتخاب می‌کنید جای داده‌های فعلی را می‌گیرد. ابتدا از کتابخانه، تنظیمات و فایل‌های فهرست فعلی شما یک نسخه در پوشه پشتیبان‌ها، با نام ‪before-restore‬، ذخیره می‌شود.",
        ["Choose backup file…"] = "انتخاب فایل پشتیبان…", ["Restore…"] = "بازیابی…",
        ["Made {0}"] = "ساخته‌شده در {0}",
        ["{0} list file"] = "{0} فایل فهرست", ["{0} list files"] = "{0} فایل فهرست",
        ["{0} poster"] = "{0} پوستر", ["{0} posters"] = "{0} پوستر",
        ["IMDb data included"] = "شامل داده‌های IMDb", ["no IMDb data"] = "بدون داده‌های IMDb",
        ["API keys included"] = "شامل کلیدهای API", ["API keys not included"] = "بدون کلیدهای API",
        ["The library (replaces your current one)"] = "کتابخانه (جایگزین کتابخانه فعلی می‌شود)",
        ["Settings"] = "تنظیمات", ["API keys"] = "کلیدهای API", ["List files"] = "فایل‌های فهرست", ["Posters"] = "پوسترها",
        ["IMDb data (large; replaces the downloaded data)"] = "داده‌های IMDb (حجیم؛ جایگزین داده‌های دانلودشده می‌شود)",
        ["Replace your library with this backup?"] = "کتابخانه شما با این پشتیبان جایگزین شود؟",
        ["Your current library and the parts you ticked are replaced. A safety copy is saved first, so this can be undone."] =
            "کتابخانه فعلی و بخش‌هایی که علامت زده‌اید جایگزین می‌شوند. ابتدا یک نسخه ایمنی ذخیره می‌شود، پس می‌توان آن را برگرداند.",
        ["Stopped. No backup file was written."] = "متوقف شد. فایل پشتیبانی نوشته نشد.",
        ["Full backup saved: {0}"] = "پشتیبان کامل ذخیره شد: {0}",
        ["A copy of the previous state is in {0}"] = "نسخه‌ای از وضعیت قبلی در {0} است",
        ["Restored, with notes"] = "بازیابی شد، با چند توضیح", ["Backup restored"] = "پشتیبان بازیابی شد",
        ["Backup: copying the database"] = "پشتیبان: کپی پایگاه داده",
        ["Backup: writing files"] = "پشتیبان: نوشتن فایل‌ها",
        ["Restore: saving what you have now"] = "بازیابی: ذخیره وضعیت فعلی",
        ["Restore: reading the database"] = "بازیابی: خواندن پایگاه داده",
        ["Restore: posters"] = "بازیابی: پوسترها", ["Restore: IMDb data"] = "بازیابی: داده‌های IMDb",
    };
}
