namespace MovieShelf.App;

public static partial class Loc
{
    static readonly Dictionary<string, string> ExtraExport = new()
    {
        // the dialog
        ["Which movies"] = "کدام فیلم‌ها", ["Selected movies"] = "فیلم‌های انتخاب‌شده", ["{0} movies you selected"] = "{0} فیلم که انتخاب کرده‌اید",
        ["Current results"] = "نتایج فعلی", ["{0} movies, with your filters and search, in the order shown"] = "{0} فیلم، با فیلترها و جستجوی شما، به همان ترتیب نمایش",
        ["{0} movies, no filters applied"] = "{0} فیلم، بدون فیلتر", ["Whole library"] = "کل کتابخانه", ["{0} movies"] = "{0} فیلم",
        ["Narrow down further"] = "محدودتر کردن", ["Same as the search box:"] = "مانند کادر جستجو:", ["IMDb rating at least"] = "حداقل امتیاز IMDb",
        ["Show"] = "نمایش", ["One row per movie"] = "یک ردیف برای هر فیلم", ["One row per file"] = "یک ردیف برای هر فایل",
        ["Files of the same movie are joined in one cell"] = "فایل‌های یک فیلم در یک خانه کنار هم می‌آیند",
        ["Movies with several files get several rows"] = "فیلم‌هایی که چند فایل دارند چند ردیف می‌گیرند",
        ["One Excel sheet per group"] = "یک برگه Excel برای هر گروه", ["Using your current grouping ({0})"] = "بر اساس گروه‌بندی فعلی شما ({0})",
        ["File type"] = "نوع فایل", ["CSV"] = "CSV", ["{0} selected"] = "{0} انتخاب‌شده", ["Essentials"] = "ضروری‌ها", ["Everything"] = "همه ستون‌ها",
        ["All / none"] = "همه / هیچ‌کدام", ["{0} movies · {1} rows × {2} columns"] = "{0} فیلم · {1} ردیف × {2} ستون",
        ["Preparing…"] = "در حال آماده‌سازی…", ["Preparing… {0} of {1}"] = "در حال آماده‌سازی… {0} از {1}", ["Cancelled"] = "لغو شد",
        ["Saved to {0}"] = "ذخیره شد در {0}", ["Excel workbook"] = "کارپوشه Excel", ["CSV file"] = "فایل CSV", ["Title A–Z"] = "حرف اول عنوان",

        // column groups
        ["Basic"] = "پایه", ["People"] = "افراد", ["Files and quality"] = "فایل‌ها و کیفیت", ["Personal"] = "شخصی",

        // columns
        ["Original title"] = "عنوان اصلی", ["Persian title"] = "عنوان فارسی", ["IMDb title"] = "عنوان IMDb", ["IMDb id (link)"] = "شناسه IMDb (پیوند)",
        ["Runtime (min)"] = "مدت فیلم (دقیقه)", ["Genres"] = "ژانرها", ["Countries"] = "کشورها", ["Languages"] = "زبان‌ها", ["Collection"] = "مجموعه",
        ["Tagline"] = "شعار فیلم", ["IMDb votes"] = "رأی‌های IMDb", ["Rotten Tomatoes %"] = "Rotten Tomatoes %", ["Writers"] = "نویسندگان",
        ["Cast (top 10)"] = "بازیگران (10 نفر اول)", ["Quality details"] = "جزئیات کیفیت", ["Codec"] = "کدک", ["HDR"] = "HDR", ["Edition"] = "نسخه",
        ["Release group"] = "گروه انتشار", ["File name"] = "نام فایل", ["Size (GB)"] = "حجم (GB)", ["Folder path"] = "مسیر پوشه", ["Length (min)"] = "طول (دقیقه)",
        ["Picture size"] = "اندازه تصویر", ["Audio languages"] = "زبان‌های صدا", ["Subtitles"] = "زیرنویس‌ها", ["List file"] = "فایل فهرست", ["Match"] = "تطبیق",
    };
}
