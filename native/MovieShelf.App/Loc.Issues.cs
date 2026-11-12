namespace MovieShelf.App;

/// <summary>Persian text of the Problems window. Keys with {0}, {1} are format strings.</summary>
public static partial class Loc
{
    static readonly Dictionary<string, string> ExtraIssues = new()
    {
        // ---- tabs ----
        ["Not found"] = "پیدا نشد", ["Ambiguous"] = "مبهم", ["Spelling differs"] = "املای متفاوت", ["No year"] = "بدون سال", ["Online match"] = "تطبیق آنلاین",
        ["Same quality"] = "کیفیت یکسان", ["Lower copy"] = "نسخه ضعیف‌تر", ["Dubbed + original"] = "دوبله + اصلی", ["Different movies?"] = "فیلم‌های متفاوت؟",
        ["Same file twice"] = "یک فایل، دو بار", ["Unknown quality"] = "کیفیت نامشخص", ["Name vs picture"] = "نام در برابر تصویر",
        ["No video file"] = "بدون فایل ویدیویی", ["Length differs"] = "مدت متفاوت", ["Persian title"] = "عنوان فارسی",

        // ---- help of each tab ----
        ["Movies you have more than once. Each copy is marked Keep, Lower quality or Same quality. The app never deletes files: copy the names of the extra copies and remove them yourself, or ignore the movie if you want to keep both. If the copies are really different movies (Vol. 1 and Vol. 2, for example), use Fix match on the extra one."] =
            "فیلم‌هایی که بیش از یک نسخه از آن‌ها دارید. هر نسخه با «نگه دارید»، «کیفیت پایین‌تر» یا «کیفیت یکسان» مشخص شده است. برنامه هیچ فایلی را پاک نمی‌کند: نام نسخه‌های اضافی را کپی کنید و خودتان حذفشان کنید، یا اگر می‌خواهید هر دو را نگه دارید، فیلم را نادیده بگیرید. اگر نسخه‌ها در واقع فیلم‌های متفاوتی هستند (مثلاً Vol. 1 و Vol. 2)، روی نسخه اضافی «اصلاح تطبیق» را بزنید.",
        ["Two or more copies of the same quality, so one is redundant. Often the same movie listed under two different names."] =
            "دو یا چند نسخه با کیفیت یکسان، پس یکی اضافی است. اغلب همان فیلم با دو نام متفاوت در فهرست آمده است.",
        ["A better copy is already in your library next to a lower-quality one. The lower one can usually go."] =
            "نسخه بهتری کنار نسخه‌ای با کیفیت پایین‌تر در کتابخانه شما هست. معمولاً می‌توان نسخه ضعیف‌تر را کنار گذاشت.",
        ["One dubbed and one original-audio copy. Not redundant, so nothing to remove; ignore them once you have seen them."] =
            "یک نسخه دوبله و یک نسخه با صدای اصلی. این‌ها اضافی نیستند و چیزی برای حذف وجود ندارد؛ پس از دیدن، نادیده‌شان بگیرید.",
        ["Files matched to one IMDb title whose names differ by a number (Vol. 1 / Vol. 2, Star Wars 4 / 6) or that look like bonus material. These are probably different movies: use Fix match on the wrong one."] =
            "فایل‌هایی که به یک عنوان IMDb تطبیق داده شده‌اند اما نامشان در یک عدد فرق دارد (Vol. 1 / Vol. 2، Star Wars 4 / 6) یا به محتوای جانبی می‌مانند. این‌ها احتمالاً فیلم‌های متفاوتی هستند: روی فایل نادرست «اصلاح تطبیق» را بزنید.",
        ["Episodes of a TV series (file names with S01E02, or IMDb says it is a series). They are not copies and are not counted as problems. If one of them is a movie after all, press Not a series."] =
            "قسمت‌های یک سریال (نام فایل‌ها S01E02 دارد، یا IMDb آن را سریال می‌داند). این‌ها نسخه تکراری نیستند و مشکل به شمار نمی‌آیند. اگر یکی از آن‌ها در واقع فیلم است، «سریال نیست» را بزنید.",
        ["The same file name found in more than one movie folder (needs your movie folders in Settings). Keep one, remove the others."] =
            "یک نام فایل در بیش از یک پوشه فیلم پیدا شده است (پوشه‌های فیلم باید در تنظیمات تعیین شده باشند). یکی را نگه دارید و بقیه را حذف کنید.",
        ["Movies where your best copy is SD, 480p or 576p. The most popular come first: the best candidates for an upgrade."] =
            "فیلم‌هایی که بهترین نسخه شما از آن‌ها SD، 480p یا 576p است. پرطرفدارترها اول می‌آیند: بهترین گزینه‌ها برای ارتقا.",
        ["No resolution appears in the file name, so quality cannot be compared or checked."] =
            "در نام فایل وضوحی نیامده است، پس کیفیت را نمی‌توان مقایسه یا بررسی کرد.",
        ["Movies where every copy is Farsi-dubbed. Useful if you also want the original audio."] =
            "فیلم‌هایی که همه نسخه‌هایشان دوبله فارسی است. اگر صدای اصلی را هم می‌خواهید، به کارتان می‌آید.",
        ["The year in the file name is two or more years from IMDb’s year. The match may be a different movie."] =
            "سال در نام فایل دو سال یا بیشتر با سال IMDb فاصله دارد. شاید تطبیق با فیلم دیگری انجام شده باشد.",
        ["The name of the file is not the real name of the film on IMDb: a typo, a missing word, a word that is not part of the name, or another language. The most different names come first. Fix the file name, or use Fix match if it is the wrong film. Files already in Matching (unsure matches) are not repeated here."] =
            "نام فایل با نام واقعی فیلم در IMDb یکی نیست: غلط املایی، واژه جاافتاده، واژه‌ای که جزو نام نیست، یا زبانی دیگر. متفاوت‌ترین نام‌ها اول می‌آیند. نام فایل را اصلاح کنید، یا اگر فیلم اشتباه است «اصلاح تطبیق» را بزنید. فایل‌هایی که در بخش تطبیق هستند (تطبیق‌های نامطمئن) اینجا تکرار نمی‌شوند.",
        ["IMDb lists these as TV specials or TV shorts, not films. TV series and mini-series are not problems: they are in the Series section of the main list."] =
            "IMDb این‌ها را ویژه تلویزیون یا کوتاه تلویزیونی می‌داند، نه فیلم. سریال و مینی‌سریال مشکل نیستند: در بخش سریال‌های فهرست اصلی قرار دارند.",
        ["The file name says one resolution but ffprobe sees a smaller picture: a mislabelled or upscaled file. Needs ffprobe (Settings → General → Movie folders)."] =
            "نام فایل یک وضوح را نشان می‌دهد اما ffprobe تصویر کوچک‌تری می‌بیند: فایلی با برچسب نادرست یا بزرگ‌نمایی‌شده. به ffprobe نیاز دارد (تنظیمات ← عمومی ← پوشه‌های فیلم).",
        ["The length of the file is far from IMDb’s runtime: a cut-short download, a different cut, or the wrong movie. Needs ffprobe."] =
            "مدت فایل با مدت ثبت‌شده در IMDb فاصله زیادی دارد: دانلود ناقص، تدوینی دیگر، یا فیلم اشتباه. به ffprobe نیاز دارد.",
        ["The Persian title from TMDB is written in Latin letters. Open the movie and use Edit next to Persian to type the right one; your edit is kept."] =
            "عنوان فارسی TMDB با حروف لاتین نوشته شده است. فیلم را باز کنید و با «ویرایش» کنار عنوان فارسی، عنوان درست را بنویسید؛ ویرایش شما حفظ می‌شود.",
        ["No poster was found for these. Retry downloads the missing ones from IMDb, or use Add a poster to choose an image file, or paste an image address."] =
            "برای این‌ها پوستری پیدا نشد. «تلاش دوباره» پوسترهای ناموجود را از IMDb دانلود می‌کند، یا با «افزودن پوستر» یک فایل تصویر انتخاب کنید یا نشانی تصویری را بچسبانید.",
        ["The folder of this title (in your movie folders) has no video file: it is empty, or holds only junk such as .nfo, .jpg or .url files, or only a sample. Remove the title from your list. This hides it in the library; your list files and folders are not changed, and it stays out of every update. Needs your movie folders in Settings."] =
            "پوشه این عنوان (در پوشه‌های فیلم شما) فایل ویدیویی ندارد: خالی است، یا فقط فایل‌های بی‌مصرف مانند .nfo، .jpg یا .url یا فقط یک نمونه کوتاه دارد. عنوان را از فهرست حذف کنید. این کار آن را در کتابخانه پنهان می‌کند؛ فایل‌های فهرست و پوشه‌های شما تغییر نمی‌کنند و در هیچ به‌روزرسانی‌ای برنمی‌گردد. پوشه‌های فیلم باید در تنظیمات تعیین شده باشند.",
        ["These names could not be matched for sure: a different name on IMDb, a wrong year, or several movies with the same title. Confirm the current match, pick another, or search."] =
            "این نام‌ها با اطمینان تطبیق داده نشدند: نامی دیگر در IMDb، سال اشتباه، یا چند فیلم با یک عنوان. تطبیق فعلی را تأیید کنید، گزینه دیگری انتخاب کنید یا جستجو کنید.",
        ["Video files in your movie folders that are in none of your list files, so they are not in the library yet. Tick the ones you want and add them: their names go to added.txt in your list folder and are matched like the rest. Samples, trailers, episodes and tiny files are left out."] =
            "فایل‌های ویدیویی پوشه‌های فیلم شما که در هیچ فایل فهرستی نیستند و بنابراین هنوز در کتابخانه قرار ندارند. موارد دلخواه را علامت بزنید و اضافه کنید: نامشان در added.txt در پوشه فهرست‌ها نوشته می‌شود و مانند بقیه تطبیق داده می‌شود. نمونه‌ها، تریلرها، قسمت‌های سریال و فایل‌های خیلی کوچک کنار گذاشته شده‌اند.",
        ["Your movie folders are not set, so only your list is checked. Set them in Settings → General to also find folders with no movie and movies that are in no list."] =
            "پوشه‌های فیلم شما تعیین نشده‌اند، پس فقط فهرست شما بررسی می‌شود. آن‌ها را در تنظیمات ← عمومی تعیین کنید تا پوشه‌های بدون فیلم و فیلم‌هایی که در هیچ فهرستی نیستند هم پیدا شوند.",

        // ---- what a row says ----
        ["The folder is empty: no video file"] = "پوشه خالی است: فایل ویدیویی ندارد",
        ["The folder has no video file, only other files ({0})"] = "پوشه فایل ویدیویی ندارد و فقط فایل‌های دیگر در آن است ({0})",
        ["You marked this as a series"] = "شما این را سریال مشخص کرده‌اید",
        ["The file names have episode numbers (like S01E02): a series, not copies"] = "نام فایل‌ها شماره قسمت دارد (مانند S01E02): سریال است، نه نسخه تکراری",
        ["IMDb lists this as a TV series: the files are episodes, not copies"] = "IMDb این را سریال می‌داند: فایل‌ها قسمت‌های آن هستند، نه نسخه تکراری",
        ["The titles differ by a number ({0}): probably different movies matched to one IMDb title, not duplicates"] =
            "عنوان‌ها در یک عدد فرق دارند ({0}): احتمالاً فیلم‌های متفاوتی هستند که به یک عنوان IMDb تطبیق داده شده‌اند، نه نسخه تکراری",
        ["One file looks like bonus material, not the movie: probably different movies matched to one IMDb title, not duplicates"] =
            "یک فایل به محتوای جانبی می‌ماند، نه خود فیلم: احتمالاً فیلم‌های متفاوتی هستند که به یک عنوان IMDb تطبیق داده شده‌اند، نه نسخه تکراری",
        ["Two copies of the same quality"] = "دو نسخه با کیفیت یکسان",
        ["A lower-quality copy sits beside a better one"] = "نسخه‌ای با کیفیت پایین‌تر کنار نسخه‌ای بهتر است",
        ["Dubbed and original: not redundant, probably on purpose"] = "دوبله و اصلی: اضافی نیست، احتمالاً عمدی است",
        ["Several copies; at least one has no resolution in its name, so they cannot be compared"] = "چند نسخه؛ دست‌کم یکی در نامش وضوح ندارد، پس نمی‌توان آن‌ها را مقایسه کرد",
        ["The same file is in {0} folders"] = "این فایل در {0} پوشه است",
        ["Best copy is {0}"] = "بهترین نسخه: {0}",
        ["No resolution in the file name"] = "وضوح در نام فایل نیامده است",
        ["Only a Farsi-dubbed copy"] = "فقط نسخه دوبله فارسی",
        ["Named {0}, but the picture is {1}x{2}"] = "در نام فایل {0} آمده، اما تصویر {1}x{2} است",
        ["The file is {0} min, IMDb says {1} min: cut short, or a different movie"] = "فایل {0} دقیقه است و IMDb می‌گوید {1} دقیقه: ناقص است یا فیلمی دیگر",
        ["The file is {0} min, IMDb says {1} min: a different cut, or a different movie"] = "فایل {0} دقیقه است و IMDb می‌گوید {1} دقیقه: تدوینی دیگر یا فیلمی دیگر",
        ["The file name says {0}, IMDb says {1}"] = "در نام فایل {0} آمده و IMDb می‌گوید {1}",
        ["The file name says “{0}”, IMDb calls it “{1}”"] = "نام فایل «{0}» است و IMDb آن را «{1}» می‌نامد",
        ["IMDb lists this as a TV episode"] = "IMDb این را یک قسمت از سریال می‌داند",
        ["IMDb lists this as a TV special"] = "IMDb این را ویژه تلویزیون می‌داند",
        ["IMDb lists this as a TV short"] = "IMDb این را کوتاه تلویزیونی می‌داند",
        ["No poster found"] = "پوستری پیدا نشد",
        ["The Persian title is written in Latin letters: {0}"] = "عنوان فارسی با حروف لاتین نوشته شده است: {0}",
        ["Not found on IMDb: the name may be misspelled, or the year in the name is wrong"] = "در IMDb پیدا نشد: شاید نام غلط نوشته شده یا سال در نام اشتباه است",
        ["Not found on IMDb — the name may be misspelled, or the year in the name is wrong"] = "در IMDb پیدا نشد: شاید نام غلط نوشته شده یا سال در نام اشتباه است",
        ["Name is spelled differently on IMDb"] = "نام در IMDb طور دیگری نوشته شده است",
        ["No year in the name: several movies may share this title"] = "سال در نام نیامده است: شاید چند فیلم همین عنوان را داشته باشند",
        ["No year in the name — several movies may share this title"] = "سال در نام نیامده است: شاید چند فیلم همین عنوان را داشته باشند",
        ["Only found by online search"] = "فقط با جستجوی آنلاین پیدا شد",
        ["Year in the name ({0}) differs from IMDb ({1})"] = "سال در نام ({0}) با IMDb ({1}) فرق دارد",
        ["Several titles share this name and year"] = "چند عنوان همین نام و سال را دارند",

        // ---- rows ----
        ["{0} votes"] = "{0} رأی", ["Now matched to:"] = "تطبیق فعلی:", ["Looks right"] = "درست است", ["Or maybe:"] = "یا شاید:", ["Possible matches:"] = "تطبیق‌های احتمالی:",
        ["View"] = "مشاهده", ["Use this"] = "استفاده از این", ["Search IMDb…"] = "جستجو در IMDb…",
        ["No year in name"] = "بدون سال در نام", ["Fuzzy match"] = "تطبیق تقریبی", ["Not found on IMDb"] = "در IMDb پیدا نشد",
        ["Keep"] = "نگه دارید", ["Lower quality"] = "کیفیت پایین‌تر", ["Cannot compare"] = "مقایسه‌پذیر نیست",
        ["Empty folder"] = "پوشه خالی", ["{0} file, no video"] = "{0} فایل، بدون ویدیو", ["{0} files, no video"] = "{0} فایل، بدون ویدیو", ["Open folder"] = "باز کردن پوشه",
        ["Not a series"] = "سریال نیست", ["This is a series"] = "این سریال است", ["Remove all from list"] = "حذف همه از فهرست", ["Add a poster…"] = "افزودن پوستر…",
        ["Open movie"] = "باز کردن فیلم", ["Copy extra paths"] = "کپی مسیرهای اضافی", ["Copy paths to remove"] = "کپی مسیرهای قابل حذف", ["Copy names to remove"] = "کپی نام‌های قابل حذف",
        ["Add to wishlist"] = "افزودن به فهرست آرزو", ["Not the same movie? Fix match"] = "فیلم دیگری است؟ اصلاح تطبیق", ["Wrong movie? Fix match"] = "فیلم اشتباه است؟ اصلاح تطبیق",
        ["Restore"] = "بازگرداندن", ["Remove from list"] = "حذف از فهرست",

        // ---- bars and empty lists ----
        ["Check again"] = "بررسی دوباره", ["Checking…"] = "در حال بررسی…",
        ["Read your list files and movie folders again and rebuild this list"] = "خواندن دوباره فایل‌های فهرست و پوشه‌های فیلم و ساختن دوباره این فهرست",
        ["Export list (CSV)"] = "خروجی فهرست (CSV)", ["Retry missing posters"] = "تلاش دوباره برای پوسترهای ناموجود", ["Select none"] = "لغو انتخاب همه", ["Select all {0}"] = "انتخاب هر {0} مورد",
        ["Looking for problems…"] = "در حال یافتن مشکلات…",
        ["Fixes are saved right away and never overwritten by updates."] = "اصلاح‌ها فوراً ذخیره می‌شوند و به‌روزرسانی‌ها هرگز آن‌ها را بازنویسی نمی‌کنند.",
        ["Adding writes only to added.txt in your list folder."] = "افزودن فقط در added.txt در پوشه فهرست‌ها می‌نویسد.",
        ["Removing only hides a title in the library. Your list files and folders are not changed."] = "حذف فقط عنوان را در کتابخانه پنهان می‌کند. فایل‌های فهرست و پوشه‌های شما تغییر نمی‌کنند.",
        ["Nothing here changes your files. Ignored items stay ignored until their files change."] = "هیچ‌چیز در اینجا فایل‌های شما را تغییر نمی‌دهد. موارد نادیده‌گرفته‌شده تا زمانی که فایل‌هایشان تغییر نکند نادیده می‌مانند.",
        ["Nothing to check. Every title is matched."] = "چیزی برای بررسی نیست. همه عنوان‌ها تطبیق داده شده‌اند.",
        ["Every movie file in your folders is in your list."] = "همه فایل‌های فیلم پوشه‌های شما در فهرست هستند.",
        ["Set your movie folders in Settings → General first."] = "ابتدا پوشه‌های فیلم را در تنظیمات ← عمومی تعیین کنید.",
        ["Everything here is ignored."] = "همه موارد اینجا نادیده گرفته شده‌اند.", ["Nothing here. All good."] = "اینجا چیزی نیست. همه‌چیز درست است.",
        ["Remove all {0} from list"] = "حذف هر {0} مورد از فهرست", ["Show ignored ({0})"] = "نمایش نادیده‌ها ({0})",
        ["{0} of disk space is taken by redundant copies (from your movie folders)."] = "{0} از فضای دیسک را نسخه‌های اضافی گرفته‌اند (بر اساس پوشه‌های فیلم شما).",

        // ---- messages and confirmations ----
        ["Fixed"] = "اصلاح شد", ["Confirmed"] = "تأیید شد", ["Marked as not on IMDb"] = "به‌عنوان «در IMDb نیست» علامت خورد",
        ["Mark as not on IMDb?"] = "به‌عنوان «در IMDb نیست» علامت بخورد؟", ["Mark as not on IMDb"] = "علامت‌گذاری به‌عنوان «در IMDb نیست»",
        ["This file keeps no IMDb data (no poster, cast or ratings). You can fix it again later."] = "این فایل هیچ داده‌ای از IMDb نخواهد داشت (نه پوستر، نه بازیگران، نه امتیاز). بعداً می‌توانید دوباره اصلاحش کنید.",
        ["Remove this title from your list?"] = "این عنوان از فهرست شما حذف شود؟", ["Remove {0} titles from your list?"] = "{0} عنوان از فهرست شما حذف شود؟",
        ["They are hidden from the library and no update brings them back. Your list files and your movie files are not changed."] =
            "این عنوان‌ها از کتابخانه پنهان می‌شوند و هیچ به‌روزرسانی‌ای آن‌ها را برنمی‌گرداند. فایل‌های فهرست و فایل‌های فیلم شما تغییر نمی‌کنند.",
        ["{0} removed from your list"] = "{0} مورد از فهرست شما حذف شد", ["Restored"] = "بازگردانده شد",
        ["Ignored. Show it again with \"Show ignored\"."] = "نادیده گرفته شد. با «نمایش نادیده‌ها» دوباره نشانش دهید.",
        ["Marked as a series: it is not a duplicate any more"] = "سریال علامت خورد: دیگر تکراری به حساب نمی‌آید",
        ["Not a series: it is checked for duplicates again"] = "سریال نیست: دوباره از نظر تکراری بودن بررسی می‌شود",
        ["Copied {0} file name"] = "نام {0} فایل کپی شد", ["Copied {0} file names"] = "نام {0} فایل کپی شد", ["Could not copy"] = "کپی انجام نشد",
        ["Already on the wishlist"] = "از قبل در فهرست آرزو هست", ["Match updated"] = "تطبیق به‌روز شد",
        ["Looking for the missing posters…"] = "در حال جستجوی پوسترهای ناموجود…",
        ["Image address (https://…) or a file path"] = "نشانی تصویر (https://…) یا مسیر فایل", ["Choose an image file…"] = "انتخاب فایل تصویر…",
        ["JPEG, PNG, WebP or GIF, up to 15 MB. A poster you add is kept: updates never replace it."] = "JPEG، PNG، WebP یا GIF، تا 15 مگابایت. پوستری که اضافه کنید حفظ می‌شود: به‌روزرسانی‌ها هرگز آن را جایگزین نمی‌کنند.",
        ["Use"] = "استفاده", ["Paste an image address or a file path first."] = "ابتدا نشانی تصویر یا مسیر فایل را بچسبانید.", ["The image is larger than 15 MB."] = "تصویر بزرگ‌تر از 15 مگابایت است.",
        ["Saving…"] = "در حال ذخیره…", ["Poster saved"] = "پوستر ذخیره شد",
        ["Add {0} to my list"] = "افزودن {0} مورد به فهرست من",
        ["{0} added to your list (added.txt). They are being matched now."] = "{0} مورد به فهرست شما (added.txt) اضافه شد. هم‌اکنون در حال تطبیق هستند.",
        ["Those names are already in your list"] = "این نام‌ها از قبل در فهرست شما هستند",
        ["Checking your lists and movie folders again…"] = "در حال بررسی دوباره فهرست‌ها و پوشه‌های فیلم…",
        // ---- add movie, revise, suspicious lists ----
        ["Revise IMDb match"] = "اصلاح عنوان IMDb",
        ["This is the wrong IMDb title, for example one of two movies with the same name and year. Give it another IMDb link or number."] = "عنوان IMDb اشتباه است، مثلاً یکی از دو فیلمی که نام و سال یکسان دارند. پیوند یا شماره IMDb دیگری بدهید.",
        ["Add movie"] = "افزودن فیلم",
        ["Add one movie by its IMDb link or number"] = "یک فیلم را با پیوند یا شماره IMDb اضافه کنید",
        ["Paste the IMDb link or the tt-number of the movie. MovieShelf reads its name and year from IMDb, adds it to your library, and fetches the cast, rating and poster."] = "پیوند IMDb یا شماره tt فیلم را بچسبانید. MovieShelf نام و سال آن را از IMDb می‌خواند، به کتابخانه اضافه می‌کند و بازیگران، امتیاز و پوستر را می‌گیرد.",
        ["IMDb link or tt-number"] = "پیوند یا شماره tt در IMDb",
        ["That is not an IMDb link or number."] = "این پیوند یا شماره IMDb نیست.",
        ["Looking it up on IMDb…"] = "در حال جستجو در IMDb…",
        ["{0} is already in your library."] = "{0} از قبل در کتابخانه شما هست.",
        ["{0} added. Fetching its details…"] = "{0} اضافه شد. در حال گرفتن اطلاعات…",
        ["Missing data"] = "اطلاعات ناقص", ["Same name and year"] = "نام و سال یکسان",
        ["Missing: {0}"] = "کم است: {0}",
        ["IMDb has other titles with this name and year: {0}"] = "در IMDb عنوان‌های دیگری با همین نام و سال هست: {0}",
        ["Movies with something unusual missing: no poster, no cast, no IMDb rating, no genres or no length. A new film may simply not have them yet; for an old one the match is often wrong, so open the movie or use Revise to check it is the right IMDb title."] = "فیلم‌هایی که چیز مهمی ندارند: بدون پوستر، بازیگر، امتیاز IMDb، ژانر یا مدت. فیلم تازه شاید هنوز این‌ها را نداشته باشد؛ در فیلم قدیمی معمولاً تطبیق اشتباه است، پس فیلم را باز کنید یا با «اصلاح عنوان IMDb» درستی عنوان را بررسی کنید.",
        ["IMDb has more than one title with the same name and the same year as these movies (a film and its remake or short, two films with one name). The file may belong to the other one: open the movie, compare, and use Fix match or Revise if it is wrong. Ignore the row when the match is right."] = "در IMDb بیش از یک عنوان با همین نام و همین سال برای این فیلم‌ها هست (فیلم و بازسازی یا کوتاهش، دو فیلم با یک نام). شاید فایل مال دیگری باشد: فیلم را باز کنید، مقایسه کنید و اگر اشتباه بود از اصلاح تطبیق یا اصلاح عنوان IMDb استفاده کنید. اگر تطبیق درست است ردیف را نادیده بگیرید.",
        ["Queued: runs after the current job"] = "در صف: پس از کار فعلی اجرا می‌شود",
    };
}
