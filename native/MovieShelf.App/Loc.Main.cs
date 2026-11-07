namespace MovieShelf.App;

public static partial class Loc
{
    // Persian for the main window, Settings, the welcome dialog and the saved views.
    internal static readonly Dictionary<string, string> ExtraMain = new()
    {
        ["Wishlist"] = "فهرست آرزو", ["Views"] = "نماها", ["Saved searches and filters"] = "جستجوها و فیلترهای ذخیره‌شده",
        ["Check a list"] = "بررسی یک فهرست", ["Unlisted files"] = "فایل‌های خارج از فهرست", ["Rename plan"] = "طرح تغییر نام",
        ["Collections"] = "مجموعه‌ها", ["Statistics"] = "آمار", ["Backups and restore"] = "پشتیبان‌گیری و بازیابی",
        ["Save the current view…"] = "ذخیره نمای فعلی…", ["Save the current view"] = "ذخیره نمای فعلی", ["Delete a view"] = "حذف یک نما",
        ["Name"] = "نام", ["Save"] = "ذخیره", ["Cancel"] = "انصراف",
        ["Show in folder"] = "نمایش در پوشه", ["Audio"] = "صدا", ["Subtitles"] = "زیرنویس",
        ["Edit Persian title"] = "ویرایش عنوان فارسی", ["Use IMDb"] = "استفاده از IMDb",
        ["Leave it empty to hide the Persian title, or press Use IMDb to bring back the original."] = "برای پنهان کردن عنوان فارسی خالی بگذارید، یا برای بازگرداندن عنوان اصلی «استفاده از IMDb» را بزنید.",
        ["added to the wishlist"] = "به فهرست آرزو افزوده شد",
        ["An update is running. This one starts right after it."] = "یک به‌روزرسانی در حال اجراست. این یکی بلافاصله بعد از آن شروع می‌شود.",
        ["Settings"] = "تنظیمات", ["Movie lists"] = "فهرست فیلم‌ها", ["Language and look"] = "زبان و ظاهر", ["Theme"] = "پوسته",
        ["Same as Windows"] = "مطابق ویندوز", ["Light"] = "روشن", ["Dark"] = "تیره", ["IMDb data"] = "داده‌های IMDb",
        ["Refresh IMDb ratings about once a week"] = "تازه‌سازی امتیاز IMDb تقریباً هفته‌ای یک بار",
        ["Download IMDb data again"] = "دانلود دوباره داده‌های IMDb", ["IMDb index"] = "نمایه IMDb", ["ratings are"] = "امتیازها",
        ["days old"] = "روز قدمت دارند",
        ["IMDb data is not downloaded yet. Press Update on the main window to download and build it (about 1 GB, once)."] = "داده‌های IMDb هنوز دانلود نشده‌اند. در پنجره اصلی «به‌روزرسانی» را بزنید تا دانلود و ساخته شوند (حدود ۱ گیگابایت، یک بار).",
        ["Online"] = "آنلاین", ["Test"] = "آزمایش", ["Testing…"] = "در حال آزمایش…", ["Key works."] = "کلید درست کار می‌کند.", ["Key rejected"] = "کلید پذیرفته نشد",
        ["Download missing posters from IMDb"] = "دانلود پوسترهای ناموجود از IMDb",
        ["Look titles up on IMDb when the local index does not know them"] = "جستجوی عنوان‌ها در IMDb وقتی نمایه محلی آن‌ها را نمی‌شناسد",
        ["Movie folders"] = "پوشه‌های فیلم", ["Remove"] = "حذف", ["Add a movie folder"] = "افزودن پوشه فیلم",
        ["Folders with your movie files. Used to show file size and to find movies that are not in any list."] = "پوشه‌هایی که فایل فیلم‌هایتان در آن‌هاست. برای نمایش حجم فایل و یافتن فیلم‌هایی که در هیچ فهرستی نیستند.",
        ["Read video details (resolution, codec, audio and subtitle languages) with ffprobe"] = "خواندن مشخصات ویدیو (وضوح، کدک، زبان صدا و زیرنویس) با ffprobe",
        ["ffprobe location (leave empty to find it automatically)"] = "مسیر ffprobe (برای یافتن خودکار خالی بگذارید)",
        ["ffprobe found"] = "ffprobe پیدا شد",
        ["ffprobe was not found. Install ffmpeg (winget install Gyan.FFmpeg) or set its location."] = "ffprobe پیدا نشد. ffmpeg را نصب کنید (winget install Gyan.FFmpeg) یا مسیر آن را بدهید.",
        ["People"] = "افراد", ["Fetch people profiles and photos automatically after an update (needs a TMDB key)"] = "دریافت خودکار پروفایل و عکس افراد بعد از به‌روزرسانی (نیاز به کلید TMDB)",
        ["Which people"] = "کدام افراد", ["Directors only"] = "فقط کارگردانان", ["Directors and main cast"] = "کارگردانان و بازیگران اصلی", ["Everyone credited"] = "همه افراد ذکرشده",
        ["profiles saved"] = "پروفایل ذخیره شده", ["photos"] = "عکس", ["Fetch people now"] = "دریافت افراد اکنون", ["Add a TMDB key first."] = "ابتدا کلید TMDB را وارد کنید.",
        ["Backups"] = "پشتیبان‌گیری", ["Backups and restore…"] = "پشتیبان‌گیری و بازیابی…",
        ["added"] = "افزوده شد", ["already in the library."] = "از قبل در کتابخانه بود.", ["Press Update to read them."] = "برای خواندن آن‌ها «به‌روزرسانی» را بزنید.",
        ["An update is already running."] = "یک به‌روزرسانی در حال اجراست.",
        ["Welcome to MovieShelf"] = "به MovieShelf خوش آمدید", ["Start"] = "شروع", ["Restore a backup…"] = "بازیابی از پشتیبان…", ["Add list files…"] = "افزودن فایل‌های فهرست…",
        ["MovieShelf reads lists of movie names (.txt files, one name per line), matches them with IMDb and shows your collection. Choose the folder that has your lists, or add list files below. The first update downloads the IMDb data (about 1 GB, once) and builds an index, which takes a few minutes."] = "MovieShelf فهرست نام فیلم‌ها (فایل‌های .txt، هر خط یک نام) را می‌خواند، با IMDb تطبیق می‌دهد و مجموعه شما را نشان می‌دهد. پوشه‌ای که فهرست‌هایتان در آن است را انتخاب کنید یا فایل‌های فهرست را اضافه کنید. اولین به‌روزرسانی داده‌های IMDb را (حدود ۱ گیگابایت، یک بار) دانلود و یک نمایه می‌سازد که چند دقیقه طول می‌کشد.",
        ["list files copied to the folder."] = "فایل فهرست به پوشه کپی شد.",
    };
}
