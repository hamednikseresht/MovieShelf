namespace MovieShelf.App;

public static partial class Loc
{
    // Persian for the main window: the movie pane, filters and search, selection, Fix match, Watch next and the short messages.
    static readonly Dictionary<string, string> ExtraMovie = new()
    {
        // the movie pane
        ["Remove from list"] = "حذف از فهرست", ["Original"] = "عنوان اصلی", ["Director"] = "کارگردان", ["Writers"] = "نویسندگان", ["Crew"] = "عوامل",
        ["Also known as"] = "نام‌های دیگر", ["Details"] = "جزئیات", ["Match"] = "تطبیق", ["Collection"] = "مجموعه", ["Trailer"] = "تریلر",
        ["Your files ({0})"] = "فایل‌های شما ({0})", ["Your notes… (saved automatically)"] = "یادداشت‌های شما… (خودکار ذخیره می‌شود)",
        ["Add a free TMDB key in Settings to get plots, countries and cast photos."] = "برای دریافت خلاصه داستان، کشور و عکس بازیگران، یک کلید رایگان TMDB در تنظیمات وارد کنید.",
        ["Take this movie out of your library. Your list files and movie files are not changed."] = "این فیلم را از کتابخانه خارج می‌کند. فایل‌های فهرست و فایل‌های فیلم شما تغییر نمی‌کنند.",
        ["Open the profile of {0}"] = "باز کردن پروفایل {0}", ["Show all {0} movies"] = "نمایش همه فیلم‌های {0}",
        ["Go back to the title from TMDB"] = "بازگشت به عنوان TMDB", ["Persian title saved"] = "عنوان فارسی ذخیره شد", ["none"] = "ندارد", ["on"] = "در تاریخ",
        ["fresh"] = "تازه", ["rotten"] = "گندیده", ["votes"] = "رأی", ["Subs"] = "زیرنویس", ["Audio languages"] = "زبان‌های صدا",
        ["Wrong movie? Fix match"] = "فیلم اشتباه است؟ اصلاح تطبیق", ["Find on IMDb"] = "یافتن در IMDb", ["Show in folder"] = "نمایش در پوشه",
        ["Exact match"] = "تطبیق دقیق", ["Good match"] = "تطبیق خوب", ["Set by you"] = "تعیین‌شده توسط شما",
        ["Online match – check"] = "تطبیق آنلاین – بررسی کنید", ["No year in name – check"] = "نام بدون سال – بررسی کنید",
        ["Ambiguous – check"] = "مبهم – بررسی کنید", ["Fuzzy match – check"] = "تطبیق تقریبی – بررسی کنید",
        ["That file is not where it was. Scan the movie folders again."] = "این فایل دیگر در جای قبلی نیست. پوشه‌های فیلم را دوباره بررسی کنید.",

        // poster by hand
        ["Add poster"] = "افزودن پوستر", ["Change poster"] = "تغییر پوستر", ["Remove poster"] = "حذف پوستر", ["Use"] = "استفاده",
        ["Image address (https://…) or a file path"] = "نشانی تصویر (https://…) یا مسیر فایل", ["Choose an image file…"] = "انتخاب فایل تصویر…",
        ["JPEG, PNG, WebP or GIF, up to 15 MB. A poster you add is kept: updates never replace it."] = "JPEG، PNG، WebP یا GIF تا ۱۵ مگابایت. پوستری که خودتان اضافه کنید می‌ماند و به‌روزرسانی‌ها جایگزینش نمی‌کنند.",
        ["Paste an image address or a file path first."] = "ابتدا نشانی تصویر یا مسیر فایل را وارد کنید.", ["Saving…"] = "در حال ذخیره…",
        ["Poster saved"] = "پوستر ذخیره شد", ["Poster removed"] = "پوستر حذف شد", ["The image is larger than 15 MB."] = "حجم تصویر بیشتر از ۱۵ مگابایت است.",
        ["The image is empty."] = "تصویر خالی است.", ["That is not an image (JPEG, PNG, WebP or GIF)."] = "این فایل تصویر نیست (JPEG، PNG، WebP یا GIF).",
        ["That is not a web address (http...) or a file on this PC."] = "این نه نشانی وب (http...) است و نه فایلی روی این رایانه.",
        ["That is not an image (JPEG, PNG, WebP or GIF). For a web picture, copy the image address, not the page address."] = "این فایل تصویر نیست (JPEG، PNG، WebP یا GIF). برای تصویر وب، نشانی خود تصویر را کپی کنید، نه نشانی صفحه را.",
        ["Choose an image file, or paste a web address or a file path."] = "یک فایل تصویر انتخاب کنید، یا نشانی وب یا مسیر فایل را وارد کنید.",
        ["This movie has no IMDb match yet, so it cannot have a poster."] = "این فیلم هنوز با IMDb تطبیق داده نشده، پس نمی‌تواند پوستر داشته باشد.",

        // fix match
        ["Title and year, or IMDb link / tt-number"] = "عنوان و سال، یا پیوند IMDb / شماره tt", ["Searching IMDb…"] = "در حال جستجو در IMDb…",
        ["Nothing found. Try another spelling or paste the IMDb link."] = "چیزی پیدا نشد. املای دیگری را امتحان کنید یا پیوند IMDb را وارد کنید.",
        ["Mark as not on IMDb"] = "علامت‌گذاری: در IMDb نیست", ["Mark as not on IMDb?"] = "به‌عنوان «در IMDb نیست» علامت‌گذاری شود؟",
        ["This file keeps no IMDb data (no poster, cast or ratings). You can fix it again later."] = "این فایل هیچ داده‌ای از IMDb نخواهد داشت (پوستر، بازیگران و امتیاز). بعداً می‌توانید دوباره آن را اصلاح کنید.",
        ["Marked as not on IMDb"] = "به‌عنوان «در IMDb نیست» علامت‌گذاری شد", ["Match saved"] = "تطبیق ذخیره شد", ["Use this"] = "استفاده از این", ["View"] = "مشاهده",

        // remove from list
        ["Remove this title from your list?"] = "این عنوان از فهرست شما حذف شود؟", ["Remove {0} titles from your list?"] = "{0} عنوان از فهرست شما حذف شود؟",
        ["They are hidden from the library and no update brings them back. Your list files and your movie files are not changed."] = "این موارد از کتابخانه پنهان می‌شوند و هیچ به‌روزرسانی‌ای آن‌ها را برنمی‌گرداند. فایل‌های فهرست و فایل‌های فیلم شما تغییر نمی‌کنند.",
        ["{0} removed from your list"] = "{0} مورد از فهرست شما حذف شد",

        // saved views
        ["Apply this view"] = "اعمال این نما", ["Delete the view {0}"] = "حذف نمای {0}", ["Delete the view “{0}”?"] = "نمای «{0}» حذف شود؟",
        ["Only the saved view goes; your movies are not touched."] = "فقط نمای ذخیره‌شده حذف می‌شود و فیلم‌های شما دست‌نخورده می‌مانند.",
        ["Saved view “{0}”"] = "نمای «{0}» ذخیره شد", ["View: {0}"] = "نما: {0}", ["Delete"] = "حذف",

        // search and filters
        ["All words must match. Numbers take >8, <=1980 or 1990-1999. More fields: writer: country: lang: type: src: codec: audio: sub: file: votes: runtime: has:poster is:fav"] = "همه کلمه‌ها باید پیدا شوند. عددها می‌توانند به شکل >8، <=1980 یا 1990-1999 باشند. فیلدهای دیگر: writer: country: lang: type: src: codec: audio: sub: file: votes: runtime: has:poster is:fav",
        ["Remove filter {0}"] = "حذف فیلتر {0}", ["Audio and picture"] = "صدا و تصویر", ["Poster size"] = "اندازه پوستر", ["Small"] = "کوچک", ["Medium"] = "متوسط", ["Large"] = "بزرگ",
        ["Min"] = "دقیقه", ["Unknown year"] = "سال نامعلوم", ["No genre"] = "بدون ژانر", ["Unrated"] = "بدون امتیاز", ["and up"] = "و بالاتر", ["Below"] = "کمتر از",
        ["Unknown quality"] = "کیفیت نامعلوم", ["Reset filters"] = "بازنشانی فیلترها",

        // empty screens
        ["No movie list found yet"] = "هنوز فهرست فیلمی پیدا نشده",
        ["MovieShelf builds your library from .txt files with one movie name per line. Put them in your list folder, or choose the folder where they are."] = "MovieShelf کتابخانه شما را از فایل‌های .txt می‌سازد که در هر خط آن‌ها نام یک فیلم است. آن‌ها را در پوشه فهرست‌ها بگذارید یا پوشه‌ای را که در آن هستند انتخاب کنید.",
        ["Choose the list folder"] = "انتخاب پوشه فهرست‌ها", ["How to prepare a list"] = "طرز آماده کردن فهرست", ["Building your library…"] = "در حال ساختن کتابخانه شما…",
        ["The first run downloads IMDb data (about 1.9 GB) and matches your list. This takes around 10 minutes; progress shows at the top."] = "اجرای نخست داده‌های IMDb را دانلود می‌کند (حدود ۱٫۹ گیگابایت) و فهرست شما را تطبیق می‌دهد. این کار حدود ۱۰ دقیقه طول می‌کشد و پیشرفت آن در بالا نمایش داده می‌شود.",
        ["No series in your library yet."] = "هنوز سریالی در کتابخانه شما نیست.",
        ["A series shows up here when a name in your list matches an IMDb TV series or mini-series, for example Breaking.Bad.2008."] = "وقتی نامی در فهرست شما با یک سریال یا مینی‌سریال IMDb تطبیق پیدا کند، اینجا نمایش داده می‌شود؛ مثلاً Breaking.Bad.2008.",
        ["No movies match these filters."] = "هیچ فیلمی با این فیلترها پیدا نشد.", ["No series match these filters."] = "هیچ سریالی با این فیلترها پیدا نشد.",

        // selection
        ["Not favorite"] = "حذف از علاقه‌مندی‌ها", ["Added to favorites"] = "به علاقه‌مندی‌ها افزوده شد", ["Removed from favorites"] = "از علاقه‌مندی‌ها حذف شد",
        ["Marked watched"] = "دیده‌شده علامت خورد", ["Marked not watched"] = "ندیده علامت خورد", ["Rated {0}/10"] = "امتیاز {0}/10 داده شد",
        ["Already on the wishlist"] = "از قبل در فهرست آرزو هست", ["({0} already there)"] = "({0} مورد از قبل بود)",

        // job bar and messages
        ["The last update stopped:"] = "آخرین به‌روزرسانی متوقف شد:", ["Update finished"] = "به‌روزرسانی تمام شد", ["Update stopped"] = "به‌روزرسانی متوقف شد",
        ["Update stopped with an error. See the log in Settings."] = "به‌روزرسانی با خطا متوقف شد. گزارش را در تنظیمات ببینید.",
        ["Problems: {0} to look at"] = "مشکلات: {0} مورد برای بررسی", ["Could not read the library:"] = "خواندن کتابخانه ممکن نشد:",

        // watch next
        ["Current results ({0})"] = "نتایج فعلی ({0})", ["Whole library ({0})"] = "کل کتابخانه ({0})", ["Choose from"] = "انتخاب از", ["Kind of pick"] = "نوع پیشنهاد",
        ["Best rated"] = "بهترین امتیاز", ["Surprise me"] = "غافلگیرم کن", ["Hidden gems"] = "گوهرهای پنهان", ["Length"] = "مدت", ["Up to 100 min"] = "تا ۱۰۰ دقیقه",
        ["Up to 2 h"] = "تا ۲ ساعت", ["Up to 2.5 h"] = "تا ۲٫۵ ساعت", ["6 and up"] = "۶ و بالاتر", ["7 and up"] = "۷ و بالاتر", ["8 and up"] = "۸ و بالاتر",
        ["Only movies I have not watched"] = "فقط فیلم‌هایی که ندیده‌ام", ["movies match."] = "فیلم پیدا شد.",
        ["Hidden gems: rated 7.2 or more with fewer than 60,000 votes."] = "گوهرهای پنهان: امتیاز ۷٫۲ یا بیشتر با کمتر از ۶۰٬۰۰۰ رأی.",
        ["Nothing matches. Loosen a setting above."] = "چیزی پیدا نشد. یکی از گزینه‌های بالا را بازتر کنید.", ["Pick again"] = "انتخاب دوباره",
        ["Open {0}"] = "باز کردن {0}", ["Marked watched: {0}"] = "دیده‌شده علامت خورد: {0}",
    };
}
