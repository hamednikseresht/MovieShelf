// Language: English (the app) and Persian (menus and Help only). Plain script, loaded before app.js.
// Static page text carries data-i18n (text) or data-i18n-attr="title aria-label placeholder" (attributes); the English
// text in the page is the key. Text built by app.js goes through t('English text').
const FA = {
  // header
  'Check list': 'بررسی فهرست',
  'Load list': 'بارگذاری فهرست',
  'Load a list or folder of movie names into the library': 'بارگذاری فهرست یا پوشه‌ی نام فیلم‌ها در کتابخانه',
  'Check a list or folder against the library': 'مقایسه یک فهرست یا پوشه با کتابخانه',
  'Problems': 'مشکلات',
  'Remove from list': 'حذف از فهرست',
  'Problems: titles that need a check': 'مشکلات: فیلم‌هایی که نیاز به بررسی دارند',
  'Wishlist': 'فهرست آرزو',
  'Collections': 'مجموعه‌ها',
  'Collections: franchises and what is missing': 'مجموعه‌ها: سری فیلم‌ها و آنچه کم دارید',
  'Statistics': 'آمار',
  'Settings and update': 'تنظیمات و به‌روزرسانی',
  'Theme': 'پوسته',
  'Light': 'روشن', 'Dark': 'تیره', 'System': 'مطابق سیستم',
  'Help': 'راهنما',
  'Help and guides': 'راهنما و آموزش',
  'Language': 'زبان',
  'Language: English / فارسی': 'زبان: فارسی / English',
  'Show filters': 'نمایش فیلترها',
  'Search tips': 'راهنمای جستجو',
  'Search movies': 'جستجوی فیلم',
  'Search titles, people, files…  try  year:1999  rating:>8': 'جستجوی فیلم، افراد، فایل‌ها…  مثلاً  year:1999  rating:>8',
  'View log': 'مشاهده گزارش',
  'Stop': 'توقف',  'Dismiss this message': 'بستن این پیام',
  // sidebar
  'Filters': 'فیلترها', 'Reset all': 'بازنشانی همه',
  'Saved views': 'نماهای ذخیره‌شده',
  'Save the current view…': 'ذخیره نمای فعلی…',
  'Name this view': 'نامی برای این نما',
  'Name of the view': 'نام نما',
  'Save': 'ذخیره',
  'Library': 'کتابخانه', 'Genre': 'ژانر', 'Year': 'سال', 'Ratings': 'امتیازها', 'Runtime': 'مدت فیلم', 'Quality': 'کیفیت',
  'Type': 'نوع', 'Country & language': 'کشور و زبان', 'Country': 'کشور',
  'Any': 'همه', 'any': 'همه', 'From': 'از', 'To': 'تا', 'Year from': 'سال از', 'Year to': 'سال تا',
  'IMDb rating': 'امتیاز IMDb', 'Votes': 'تعداد رأی',
  'Favorites': 'علاقه‌مندی‌ها', 'Watched': 'دیده‌شده', 'Unwatched': 'ندیده', 'Needs review': 'نیازمند بررسی',
  'Multiple files': 'چند فایل', 'No poster': 'بدون پوستر', 'Top rated 8+': 'امتیاز بالای 8',
  'Farsi dubbed': 'دوبله فارسی', 'Dual audio': 'دو زبانه', 'Softsub': 'زیرنویس نرم', '10-bit': '10 بیت',
  '5.1+ audio': 'صدای 5.1+', 'Special edition': 'نسخه ویژه',
  'Movie': 'فیلم', 'TV movie': 'فیلم تلویزیونی', 'Video': 'ویدیو', 'Short': 'کوتاه', 'TV short': 'کوتاه تلویزیونی',
  'TV special': 'ویژه تلویزیون', 'Mini-series': 'مینی‌سریال', 'TV series': 'سریال', 'Unmatched': 'تطبیق‌نشده',
  // results toolbar
  'movies': 'فیلم', 'movie': 'فیلم', 'of': 'از',
  'All': 'همه', 'Movies': 'فیلم‌ها', 'Series': 'سریال‌ها', 'Section': 'بخش', 'series': 'سریال',
  'No grouping': 'بدون گروه‌بندی', 'Group the results': 'گروه‌بندی نتایج', 'Group by': 'گروه‌بندی بر اساس',
  'Group: Decade': 'گروه: دهه', 'Group: Year': 'گروه: سال', 'Group: Genre': 'گروه: ژانر', 'Group: IMDb rating': 'گروه: امتیاز IMDb',
  'Group: Quality': 'گروه: کیفیت', 'Group: Type': 'گروه: نوع', 'Group: Title A–Z': 'گروه: حرف اول عنوان', 'Group: Watched': 'گروه: دیده‌شده',
  'Sort the results': 'مرتب‌سازی نتایج', 'Sort by': 'مرتب‌سازی بر اساس',
  'Sort: Title': 'ترتیب: عنوان', 'Sort: Year': 'ترتیب: سال', 'Sort: IMDb rating': 'ترتیب: امتیاز IMDb',
  'Sort: Rotten Tomatoes': 'ترتیب: Rotten Tomatoes', 'Sort: Metacritic': 'ترتیب: Metacritic', 'Sort: Popularity': 'ترتیب: محبوبیت',
  'Sort: Runtime': 'ترتیب: مدت', 'Sort: Recently added': 'ترتیب: تازه‌ترین‌ها', 'Sort: My rating': 'ترتیب: امتیاز من',
  'Reverse the sort order': 'معکوس کردن ترتیب',
  'Select': 'انتخاب', 'Select several movies to change them together': 'چند فیلم را انتخاب کنید و با هم تغییر دهید',
  'Export': 'خروجی', 'Export the movies shown to Excel or CSV': 'خروجی فیلم‌های نمایش‌داده‌شده به Excel یا CSV',
  'Poster grid': 'نمای پوستر', 'Table': 'جدول', 'Layout': 'نمایش',
  'Watch next: picks from the results': 'چه ببینیم؟ پیشنهاد از میان نتایج',
  'Back to top': 'بازگشت به بالا',
  // bulk bar
  'Click movies to select them': 'روی فیلم‌ها کلیک کنید تا انتخاب شوند', 'selected': 'انتخاب‌شده',
  'Favorite': 'علاقه‌مندی', 'Not watched': 'ندیده', 'Rate…': 'امتیاز…', 'Select all results': 'انتخاب همه نتایج',
  'Clear': 'پاک‌کردن', 'Done': 'پایان', 'Cancel': 'انصراف', 'Close': 'بستن',
  // dialogs: titles and tabs
  'Settings': 'تنظیمات', 'General': 'عمومی', 'Appearance': 'ظاهر', 'Online data': 'داده‌های آنلاین',
  'Maintenance': 'نگهداری', 'Activity log': 'گزارش فعالیت',
  'Matching': 'تطبیق', 'Duplicates': 'تکراری‌ها', 'Other': 'سایر',
  'Collection statistics': 'آمار مجموعه', 'Export movies': 'خروجی فیلم‌ها', 'Watch next': 'چه ببینیم؟',
  'Rename plan': 'طرح تغییر نام', 'Check a list against your library': 'مقایسه فهرست با کتابخانه',
  'Recently watched': 'اخیراً دیده‌شده',
  // help window
  'Contents': 'فهرست مطالب', 'Search the help': 'جستجو در راهنما', 'Open as a page': 'باز کردن به‌صورت صفحه',
  'Print': 'چاپ', 'Nothing found in the help.': 'چیزی در راهنما پیدا نشد.', 'Back to MovieShelf': 'بازگشت به MovieShelf',
  'The help could not be loaded.': 'راهنما بارگذاری نشد.',
  // search tips panel
  'Recent searches': 'جستجوهای اخیر', 'Narrow a search (click to add)': 'محدودتر کردن جستجو (برای افزودن کلیک کنید)',
  'Released in the 90s': 'ساخت دهه‌ی 90', 'IMDb rating above 8': 'امتیاز IMDb بالاتر از 8', 'One genre': 'یک ژانر',
  'A director': 'یک کارگردان', 'A cast member': 'یک بازیگر', 'A quality': 'یک کیفیت', 'Critics agree': 'مورد تأیید منتقدان',
  'Not watched yet': 'هنوز ندیده‌شده', 'Leave a word out': 'کنار گذاشتن یک کلمه', 'Exact phrase': 'عبارت دقیق',
  'Rotten Tomatoes': 'Rotten Tomatoes', 'Metacritic': 'Metacritic',
  'Search and filter, then save the combination here.': 'جستجو و فیلتر کنید، سپس ترکیب را اینجا ذخیره کنید.',
  'Edit': 'ویرایش',
  'Poster': 'پوستر', 'English': 'English', 'Persian': 'فارسی',
};

let LANG = 'en';
try { LANG = localStorage.getItem('lang') === 'fa' ? 'fa' : 'en'; } catch { }
const tr = s => (LANG === 'fa' && FA[s]) || s;
const isFa = () => LANG === 'fa';
const _orig = new WeakMap();

// translate the marked elements under root (call again after the language changes)
function applyI18n(root = document) {
  root.querySelectorAll('[data-i18n]').forEach(el => {
    for (const n of el.childNodes) {
      if (n.nodeType !== 3 || !n.textContent.trim()) continue;
      if (!_orig.has(n)) _orig.set(n, n.textContent);
      const en = _orig.get(n).trim();
      n.textContent = _orig.get(n).replace(en, tr(en));
    }
  });
  root.querySelectorAll('[data-i18n-attr]').forEach(el => {
    for (const a of el.dataset.i18nAttr.split(' ')) {
      const k = 'data-en-' + a;
      if (!el.hasAttribute(k)) el.setAttribute(k, el.getAttribute(a) || '');
      el.setAttribute(a, tr(el.getAttribute(k)));
    }
  });
  document.documentElement.lang = LANG;
  // menus, sidebar, toolbar and the help read right to left; the movies themselves stay as they are
  document.querySelectorAll('[data-rtl]').forEach(el => { el.dir = LANG === 'fa' ? 'rtl' : 'ltr'; });
}
