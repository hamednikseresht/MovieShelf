using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MovieShelf.App;

/// <summary>English and Persian text. The English text is the key; text without a Persian entry stays English.</summary>
public static partial class Loc
{
    public static bool IsFa;

    static readonly Dictionary<string, string> Extra = new()
    {
        ["Update"] = "به‌روزرسانی", ["Update the library from your lists"] = "به‌روزرسانی کتابخانه از فهرست‌های شما",
        ["Search titles, people, files…"] = "جستجوی فیلم، افراد، فایل‌ها…",
        ["Sort"] = "ترتیب", ["Title"] = "عنوان", ["Year ↑"] = "سال ↑", ["Year ↓"] = "سال ↓", ["IMDb rating"] = "امتیاز IMDb",
        ["Runtime"] = "مدت فیلم", ["Recently added"] = "تازه‌ترین‌ها", ["My rating"] = "امتیاز من", ["Votes"] = "تعداد رأی",
        ["Grid"] = "پوستر", ["List"] = "فهرست", ["Reset"] = "بازنشانی", ["Status"] = "وضعیت", ["Genre"] = "ژانر", ["Decade"] = "دهه",
        ["Resolution"] = "وضوح", ["Source"] = "منبع", ["Minimum IMDb rating"] = "حداقل امتیاز IMDb",
        ["No movies match"] = "فیلمی پیدا نشد", ["Change or reset the filters."] = "فیلترها را تغییر دهید یا بازنشانی کنید.",
        ["Open on IMDb"] = "باز کردن در IMDb", ["Plot"] = "خلاصه داستان", ["Directors"] = "کارگردانان", ["Cast"] = "بازیگران",
        ["Files"] = "فایل‌ها", ["Notes"] = "یادداشت", ["Fix match"] = "اصلاح تطبیق", ["Ignore"] = "نادیده گرفتن",
        ["Stop ignoring"] = "بازگرداندن", ["Name differs"] = "نام متفاوت", ["Year differs"] = "سال متفاوت", ["Low quality"] = "کیفیت پایین",
        ["No resolution"] = "بدون وضوح", ["Dubbed only"] = "فقط دوبله", ["TV, not a film"] = "تلویزیونی، نه فیلم",
        ["Show ignored"] = "نمایش نادیده‌ها", ["Use this title"] = "استفاده از این عنوان", ["Not on IMDb"] = "در IMDb نیست",
        ["This match is right"] = "این تطبیق درست است", ["Search IMDb"] = "جستجو در IMDb", ["Search"] = "جستجو",
        ["List folder"] = "پوشه فهرست‌ها", ["Browse"] = "انتخاب", ["Add lists"] = "افزودن فهرست", ["Language"] = "زبان",
        ["TMDB key"] = "کلید TMDB", ["OMDb key"] = "کلید OMDb", ["Format"] = "قالب", ["One row per file"] = "یک ردیف برای هر فایل",
        ["Columns"] = "ستون‌ها", ["Excel (.xlsx)"] = "Excel (.xlsx)", ["CSV (.csv)"] = "CSV (.csv)", ["Export movies shown"] = "خروجی فیلم‌های نمایش‌داده‌شده",
        ["Saved"] = "ذخیره شد", ["Stopping…"] = "در حال توقف…", ["Matching titles with IMDb"] = "تطبیق عنوان‌ها با IMDb",
        ["Favorite"] = "علاقه‌مندی", ["Watched"] = "دیده‌شده", ["files"] = "فایل", ["Done"] = "پایان",
        ["Search tips"] = "راهنمای جستجو", ["About"] = "درباره", ["Open the Persian help"] = "راهنمای فارسی",
    };

    static Dictionary<string, string>[]? dictionaries;

    // Built on first use: the dictionaries live in several files and their initialisers run in no guaranteed order.
    // The first dictionary holding a key wins.
    static Dictionary<string, string>[] Dictionaries => dictionaries ??= new[] { Extra, FromWeb, ExtraU1, ExtraU2, ExtraMain, ExtraCore, ExtraIssues, ExtraExport, ExtraMovie, ExtraTools };

    public static string T(string en)
    {
        if (!IsFa) return en;
        foreach (var d in Dictionaries)
            if (d.TryGetValue(en, out var v)) return v;
        return en;
    }

    static readonly ConditionalWeakTable<object, Dictionary<string, string>> Originals = new();

    static string Pick(object o, string prop, string current)
    {
        var d = Originals.GetOrCreateValue(o);
        // controls built while Persian is on already carry Persian text, so the English key comes back from the reverse map
        if (!d.TryGetValue(prop, out var en)) d[prop] = en = Reverse.TryGetValue(current, out var key) ? key : current;
        return T(en);
    }

    static Dictionary<string, string>? reverse;

    static Dictionary<string, string> Reverse
    {
        get
        {
            if (reverse != null) return reverse;
            var r = new Dictionary<string, string>();
            foreach (var d in Dictionaries)
                foreach (var kv in d) r.TryAdd(kv.Value, kv.Key);
            return reverse = r;
        }
    }

    /// <summary>Translates the text of the controls under <paramref name="root"/>; call again after the language changes.</summary>
    public static void Apply(DependencyObject? root)
    {
        if (root == null) return;
        switch (root)
        {
            case TextBlock tb when !string.IsNullOrEmpty(tb.Text): tb.Text = Pick(tb, "Text", tb.Text); break;
            case AutoSuggestBox a when !string.IsNullOrEmpty(a.PlaceholderText): a.PlaceholderText = Pick(a, "Placeholder", a.PlaceholderText); break;
            case TextBox t when !string.IsNullOrEmpty(t.PlaceholderText): t.PlaceholderText = Pick(t, "Placeholder", t.PlaceholderText); break;
            case SelectorBarItem sb when !string.IsNullOrEmpty(sb.Text): sb.Text = Pick(sb, "Text", sb.Text); break;
            case AppBarButton ab when !string.IsNullOrEmpty(ab.Label): ab.Label = Pick(ab, "Label", ab.Label); break;
            case ComboBoxItem cbi when cbi.Content is string s: cbi.Content = Pick(cbi, "Content", s); break;
            case Expander ex when ex.Header is string h: ex.Header = Pick(ex, "Header", h); break;
            case ContentControl cc when cc.Content is string s: cc.Content = Pick(cc, "Content", s); break;
        }
        if (root is FrameworkElement fe)
        {
            var tip = ToolTipService.GetToolTip(fe);
            if (tip is string ts && ts.Length > 0) ToolTipService.SetToolTip(fe, Pick(fe, "Tip", ts));
        }
        switch (root)
        {
            case Panel p: foreach (var c in p.Children) Apply(c); break;
            case Border b: Apply(b.Child); break;
            case ScrollViewer sv: Apply(sv.Content as DependencyObject); break;
            case SplitView spl: Apply(spl.Pane); Apply(spl.Content); break;
            case Expander ex: Apply(ex.Content as DependencyObject); break;
            case SelectorBar bar: foreach (var i in bar.Items) Apply(i); break;
            case ComboBox cb: foreach (var i in cb.Items) Apply(i as DependencyObject); break;
            case CommandBar cmd: foreach (var i in cmd.PrimaryCommands) Apply(i as DependencyObject); foreach (var i in cmd.SecondaryCommands) Apply(i as DependencyObject); break;
            case UserControl uc: Apply(uc.Content); break;
            case ContentControl cc: Apply(cc.Content as DependencyObject); break;
        }
    }
}
