// The look of the wizard: scaling, fonts, and the few custom-drawn controls. All coordinates in the pages are logical
// (96 DPI) and go through Ui.S. Fonts are sized in pixels so they follow the same scale, at any DPI and in the render test.
//
// Right to left: the form itself is mirrored (RightToLeftLayout), which puts the close button on the left. WinForms sets
// WS_EX_NOINHERITLAYOUT on it, so the single root panel inside is not mirrored and custom painting stays correct; everything
// inside the root panel is mirrored by Ui.X, and text controls carry RightToLeft.Yes.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class Theme
{
    public static readonly Color Side = Color.FromArgb(0x18, 0x18, 0x1B), SideLine = Color.FromArgb(0x3F, 0x3F, 0x46),
        Amber = Color.FromArgb(0xFB, 0xBF, 0x24), AmberHot = Color.FromArgb(0xF5, 0x9E, 0x0B), AmberSoft = Color.FromArgb(0xFF, 0xFB, 0xEB),
        Ink = Color.FromArgb(0x18, 0x18, 0x1B), Muted = Color.FromArgb(0x52, 0x52, 0x5B), Faint = Color.FromArgb(0xA1, 0xA1, 0xAA),
        Line = Color.FromArgb(0xE4, 0xE4, 0xE7), Wash = Color.FromArgb(0xFA, 0xFA, 0xFA), Hover = Color.FromArgb(0xF4, 0xF4, 0xF5),
        Pass = Color.FromArgb(0x16, 0xA3, 0x4A), Warn = Color.FromArgb(0xD9, 0x77, 0x06), Fail = Color.FromArgb(0xDC, 0x26, 0x26),
        Info = Color.FromArgb(0x25, 0x63, 0xEB), Link = Color.FromArgb(0xB4, 0x53, 0x09);
}

static class Ui
{
    public const int BaseW = 840, BaseH = 580;
    public static float Scale = 1f;
    public static bool Rtl;
    static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
    static Image logo;

    /// <summary>Scale from the system DPI, shrunk to fit a small screen.</summary>
    public static void Init()
    {
        float dpi;
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX;
        float s = dpi / 96f;
        string test = Env.Test("SCALE");
        if (test != null) s = float.Parse(test, System.Globalization.CultureInfo.InvariantCulture);
        else
        {
            Rectangle a = Screen.PrimaryScreen.WorkingArea;
            s = Math.Min(s, Math.Min(a.Width / (float)BaseW, (a.Height - 40) / (float)BaseH));
        }
        SetScale(Math.Max(0.75f, s));
    }

    public static void SetScale(float s)
    {
        Scale = s;
        foreach (Font f in fonts.Values) f.Dispose();
        fonts.Clear();
    }

    public static int S(double v) { return (int)Math.Round(v * Scale); }
    public static int Logical(int px) { return (int)Math.Ceiling(px / Scale); }

    /// <summary>Mirrors a left edge inside a parent of the given pixel width.</summary>
    public static int X(int parentWidth, int x, int w) { return Rtl ? parentWidth - x - w : x; }

    public static Font Font(float px, bool semibold)
    {
        string key = px + (semibold ? "s" : "r");
        Font f;
        if (!fonts.TryGetValue(key, out f))
            fonts[key] = f = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", px * Scale, FontStyle.Regular, GraphicsUnit.Pixel);
        return f;
    }

    public static Image Logo
    {
        get
        {
            if (logo == null)
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png")) logo = Image.FromStream(s);
            return logo;
        }
    }

    public static string Resource(string name)
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var r = new StreamReader(s, System.Text.Encoding.UTF8)) return r.ReadToEnd();
    }

    public static TextFormatFlags Flags(TextFormatFlags extra)
    {
        TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | extra;
        return Rtl ? f | TextFormatFlags.RightToLeft | TextFormatFlags.Right : f;
    }

    public static int TextHeight(string text, Font font, int width)
    {
        Size z = TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), Flags(TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl));
        return z.Height + 2;
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }

    public static void DrawShield(Graphics g, Rectangle r)
    {
        using (var i = new Icon(SystemIcons.Shield, r.Width, r.Height)) g.DrawIcon(i, r);
    }
}

enum BtnKind { Primary, Secondary }

/// <summary>A real Button (focus, keyboard, PerformClick, access keys), drawn flat.</summary>
class FlatBtn : Button
{
    public BtnKind Kind = BtnKind.Secondary;
    public bool Shield;
    bool hover, pressed;

    public FlatBtn()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Font = Ui.Font(13, false);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.Smooth(g);
        g.Clear(Parent != null ? Parent.BackColor : Color.White);
        bool prime = Kind == BtnKind.Primary;
        Color fill = !Enabled ? Theme.Hover : prime ? (pressed || hover ? Theme.AmberHot : Theme.Amber) : (pressed ? Theme.Line : hover ? Theme.Hover : Color.White);
        Color edge = !Enabled ? Theme.Line : prime ? fill : Focused ? Theme.Muted : Theme.Line;
        Color ink = !Enabled ? Theme.Faint : Theme.Ink;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (GraphicsPath p = Ui.Round(r, Ui.S(6)))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            using (var pen = new Pen(edge, Focused && prime ? 1f : 1f)) g.DrawPath(pen, p);
        }
        if (Focused && prime)
        {
            using (GraphicsPath p = Ui.Round(new RectangleF(2.5f, 2.5f, Width - 5.5f, Height - 5.5f), Ui.S(4)))
            using (var pen = new Pen(Color.FromArgb(0x92, 0x40, 0x0E))) g.DrawPath(pen, p);
        }
        Rectangle text = ClientRectangle;
        if (Shield)
        {
            int d = Ui.S(16), gap = Ui.S(6);
            int w = TextRenderer.MeasureText(Text.Replace("&", ""), Font).Width + d + gap;
            int x = (Width - w) / 2;
            Ui.DrawShield(g, new Rectangle(Ui.Rtl ? x + w - d : x, (Height - d) / 2, d, d));
            text = Ui.Rtl ? new Rectangle(x - gap, 0, w, Height) : new Rectangle(x + d + gap, 0, w - d - gap, Height);
        }
        TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        f |= Shield ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
        if (!ShowKeyboardCues) f |= TextFormatFlags.HidePrefix;
        if (Ui.Rtl) f |= TextFormatFlags.RightToLeft;
        TextRenderer.DrawText(g, Text, Font, text, ink, f);
    }
}

/// <summary>A selectable card that behaves as a radio button.</summary>
class Card : Control
{
    public string Title = "", Desc = "";
    public bool Shield, Compact;
    public List<Card> Group;
    public event EventHandler Picked;
    bool chk, hover;

    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AccessibleRole = AccessibleRole.RadioButton;
        Cursor = Cursors.Hand;
        TabStop = false; // as with radio buttons, only the chosen card of a group is a tab stop
    }

    public bool Checked
    {
        get { return chk; }
        set
        {
            chk = value;
            TabStop = value;
            AccessibleName = Title + (Desc.Length > 0 ? ". " + Desc : "");
            Invalidate();
        }
    }

    public void Pick()
    {
        if (!Enabled) return;
        if (Group != null) foreach (Card c in Group) c.Checked = c == this;
        else Checked = true;
        if (Picked != null) Picked(this, EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e) { Focus(); Pick(); base.OnClick(e); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override bool IsInputKey(Keys k)
    {
        return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space) { Pick(); e.Handled = true; return; }
        if (Group == null) return;
        int step = e.KeyCode == Keys.Down ? 1 : e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Right ? (Ui.Rtl ? -1 : 1) : e.KeyCode == Keys.Left ? (Ui.Rtl ? 1 : -1) : 0;
        if (step == 0) return;
        int i = Group.IndexOf(this);
        for (int n = 1; n < Group.Count; n++)
        {
            Card c = Group[(i + step * n + Group.Count * n) % Group.Count];
            if (!c.Enabled) continue;
            c.Focus();
            c.Pick();
            break;
        }
        e.Handled = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.Smooth(g);
        g.Clear(Parent != null ? Parent.BackColor : Color.White);
        Color edge = !Enabled ? Theme.Line : chk ? Theme.Amber : Focused ? Theme.Muted : hover ? Theme.Faint : Theme.Line;
        Color fill = chk ? Theme.AmberSoft : hover && Enabled ? Theme.Wash : Color.White;
        float pad = chk ? 1f : 0.5f;
        using (GraphicsPath p = Ui.Round(new RectangleF(pad, pad, Width - 2 * pad - 0.5f, Height - 2 * pad - 0.5f), Ui.S(8)))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            using (var pen = new Pen(edge, chk ? 2f : 1f)) g.DrawPath(pen, p);
        }
        int d = Ui.S(16), m = Ui.S(14);
        bool hasDesc = Desc.Length > 0;
        int top = Compact || !hasDesc ? (Height - d) / 2 : Ui.S(14);
        var dot = new Rectangle(Ui.Rtl ? Width - m - d : m, top, d, d);
        using (var pen = new Pen(chk ? Theme.AmberHot : Theme.Faint, Math.Max(1f, Ui.Scale * 1.5f))) g.DrawEllipse(pen, dot);
        if (chk)
        {
            Rectangle inner = dot;
            inner.Inflate(-Ui.S(4), -Ui.S(4));
            using (var b = new SolidBrush(Theme.AmberHot)) g.FillEllipse(b, inner);
        }
        int textX = m + d + Ui.S(10);
        int textW = Width - textX - m;
        if (Shield)
        {
            int s = Ui.S(20);
            Ui.DrawShield(g, new Rectangle(Ui.Rtl ? m : Width - m - s, Ui.S(11), s, s));
            textW -= s + Ui.S(4);
        }
        int left = Ui.X(Width, textX, textW);
        Font tf = Ui.Font(Compact ? 13 : 14, true), df = Ui.Font(12, false);
        Color ink = Enabled ? Theme.Ink : Theme.Faint;
        if (!hasDesc)
        {
            TextRenderer.DrawText(g, Title, tf, new Rectangle(left, 0, textW, Height), ink, Ui.Flags(TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis));
            return;
        }
        int th = TextRenderer.MeasureText(Title, tf).Height;
        int ty = Compact ? Ui.S(7) : Ui.S(10);
        TextRenderer.DrawText(g, Title, tf, new Rectangle(left, ty, textW, th), ink, Ui.Flags(TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis));
        TextRenderer.DrawText(g, Desc, df, new Rectangle(left, ty + th + Ui.S(2), textW, Height - ty - th - Ui.S(4)), Enabled ? Theme.Muted : Theme.Faint,
            Ui.Flags(TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis));
    }
}

enum Mark { Pending, Pass, Info, Warn, Fail }

/// <summary>Draws the pass, warning, fail and information symbols.</summary>
static class Marks
{
    public static void Draw(Graphics g, Mark m, Rectangle r)
    {
        Ui.Smooth(g);
        float t = Math.Max(1.5f, r.Width / 11f);
        float x = r.X, y = r.Y, w = r.Width;
        if (m == Mark.Pending)
        {
            using (var pen = new Pen(Theme.Faint, t)) { pen.DashStyle = DashStyle.Dot; g.DrawEllipse(pen, x + 1, y + 1, w - 2, w - 2); }
            return;
        }
        Color c = m == Mark.Pass ? Theme.Pass : m == Mark.Warn ? Theme.Warn : m == Mark.Fail ? Theme.Fail : Theme.Info;
        using (var fill = new SolidBrush(c))
        using (var ink = new Pen(Color.White, t))
        {
            ink.StartCap = ink.EndCap = LineCap.Round;
            if (m == Mark.Warn)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[] { new PointF(x + w / 2, y + 0.5f), new PointF(x + w - 0.5f, y + w - 1.5f), new PointF(x + 0.5f, y + w - 1.5f) });
                    using (var round = new Pen(c, t * 1.2f)) { round.LineJoin = LineJoin.Round; g.DrawPath(round, path); }
                    g.FillPath(fill, path);
                }
                g.DrawLine(ink, x + w / 2, y + w * 0.36f, x + w / 2, y + w * 0.60f);
                g.DrawLine(ink, x + w / 2, y + w * 0.76f, x + w / 2, y + w * 0.76f + 0.1f);
                return;
            }
            g.FillEllipse(fill, x, y, w, w);
            if (m == Mark.Pass) g.DrawLines(ink, new[] { new PointF(x + w * .28f, y + w * .52f), new PointF(x + w * .44f, y + w * .68f), new PointF(x + w * .72f, y + w * .36f) });
            else if (m == Mark.Fail)
            {
                g.DrawLine(ink, x + w * .33f, y + w * .33f, x + w * .67f, y + w * .67f);
                g.DrawLine(ink, x + w * .67f, y + w * .33f, x + w * .33f, y + w * .67f);
            }
            else
            {
                g.DrawLine(ink, x + w / 2, y + w * .46f, x + w / 2, y + w * .72f);
                g.DrawLine(ink, x + w / 2, y + w * .28f, x + w / 2, y + w * .28f + 0.1f);
            }
        }
    }
}

/// <summary>One line of the system check: symbol, title, detail.</summary>
class StatusRow : Control
{
    public Mark State = Mark.Pending;
    public string Title = "", Detail = "";
    public int Reserve; // pixels kept free for a button at the end of the row

    public StatusRow()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        AccessibleRole = AccessibleRole.StaticText;
    }

    public void Set(Mark state, string detail)
    {
        State = state;
        Detail = detail;
        AccessibleName = Title + ": " + detail;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : Color.White);
        int d = Ui.S(20), gap = Ui.S(12);
        Marks.Draw(g, State, new Rectangle(Ui.X(Width, 0, d), Ui.S(9), d, d));
        g.SmoothingMode = SmoothingMode.Default;
        int w = Width - d - gap - Reserve;
        int left = Ui.Rtl ? Reserve : d + gap;
        TextRenderer.DrawText(g, Title, Ui.Font(13, true), new Rectangle(left, Ui.S(2), w, Ui.S(19)), Theme.Ink, Ui.Flags(TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis));
        TextRenderer.DrawText(g, Detail, Ui.Font(12, false), new Rectangle(left, Ui.S(20), w, Ui.S(18)), Theme.Muted, Ui.Flags(TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis));
    }
}

class Bar : Control
{
    double value;
    public Bar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        AccessibleRole = AccessibleRole.ProgressBar;
    }

    public double Value
    {
        get { return value; }
        set
        {
            double v = Math.Max(0, Math.Min(1, value));
            if (Math.Abs(v - this.value) < 0.0005) return;
            this.value = v;
            AccessibleName = ((int)(v * 100)) + "%";
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.Smooth(g);
        g.Clear(Parent != null ? Parent.BackColor : Color.White);
        float r = Height / 2f;
        using (GraphicsPath p = Ui.Round(new RectangleF(0, 0, Width - 1, Height - 1), r - 0.5f))
        using (var b = new SolidBrush(Theme.Line)) g.FillPath(b, p);
        int w = (int)((Width - 1) * value);
        if (w < 2) return;
        w = Math.Max(w, Height);
        using (GraphicsPath p = Ui.Round(new RectangleF(Ui.Rtl ? Width - 1 - w : 0, 0, w, Height - 1), r - 0.5f))
        using (var b = new SolidBrush(Theme.Amber)) g.FillPath(b, p);
    }
}

/// <summary>The dark strip with the logo and the list of steps.</summary>
class SideBar : Control
{
    public List<string> Steps = new List<string>();
    public int Current;
    public string Version = "";

    public SideBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        BackColor = Theme.Side;
        AccessibleRole = AccessibleRole.List;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.Side);
        Ui.Smooth(g);
        int m = Ui.S(24), logo = Ui.S(38);
        g.DrawImage(Ui.Logo, new Rectangle(Ui.X(Width, m, logo), Ui.S(26), logo, logo));
        int tx = m + logo + Ui.S(11), tw = Width - tx - Ui.S(12);
        g.SmoothingMode = SmoothingMode.Default;
        TextRenderer.DrawText(g, "MovieShelf", Ui.Font(18, true), new Rectangle(Ui.X(Width, tx, tw), Ui.S(24), tw, Ui.S(25)), Color.White, Ui.Flags(TextFormatFlags.SingleLine));
        TextRenderer.DrawText(g, Version, Ui.Font(12, false), new Rectangle(Ui.X(Width, tx, tw), Ui.S(48), tw, Ui.S(18)), Theme.Faint, Ui.Flags(TextFormatFlags.SingleLine));

        int y = Ui.S(100), rowH = Ui.S(40), d = Ui.S(24);
        for (int i = 0; i < Steps.Count; i++)
        {
            bool now = i == Current, done = i < Current;
            if (i < Steps.Count - 1)
                using (var pen = new Pen(done ? Theme.Amber : Theme.SideLine, Math.Max(1f, Ui.Scale)))
                {
                    int cx = Ui.X(Width, m, d) + d / 2;
                    g.DrawLine(pen, cx, y + d + Ui.S(3), cx, y + rowH - Ui.S(3));
                }
            var dot = new Rectangle(Ui.X(Width, m, d), y, d, d);
            Ui.Smooth(g);
            if (now) using (var b = new SolidBrush(Theme.Amber)) g.FillEllipse(b, dot);
            else if (done)
            {
                using (var pen = new Pen(Theme.Amber, Math.Max(1f, Ui.Scale * 1.3f))) g.DrawEllipse(pen, dot.X + 1, dot.Y + 1, dot.Width - 2, dot.Height - 2);
                using (var pen = new Pen(Theme.Amber, Math.Max(1.5f, Ui.Scale * 1.8f)))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    float x0 = dot.X, y0 = dot.Y, w = d;
                    g.DrawLines(pen, new[] { new PointF(x0 + w * .29f, y0 + w * .52f), new PointF(x0 + w * .44f, y0 + w * .67f), new PointF(x0 + w * .72f, y0 + w * .35f) });
                }
            }
            else using (var pen = new Pen(Theme.SideLine, Math.Max(1f, Ui.Scale * 1.3f))) g.DrawEllipse(pen, dot.X + 1, dot.Y + 1, dot.Width - 2, dot.Height - 2);
            g.SmoothingMode = SmoothingMode.Default;
            if (!done)
                TextRenderer.DrawText(g, Fmt.Num(i + 1), Ui.Font(12, true), dot, now ? Theme.Ink : Theme.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            int sx = m + d + Ui.S(12), sw = Width - sx - Ui.S(10);
            TextRenderer.DrawText(g, Steps[i], Ui.Font(13, now), new Rectangle(Ui.X(Width, sx, sw), y, sw, d), now ? Theme.Amber : done ? Color.White : Theme.Faint,
                Ui.Flags(TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis));
            y += rowH;
        }
    }
}

/// <summary>Label and value rows, used for the summaries.</summary>
class Summary : Control
{
    readonly List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
    public int LabelWidth = 150;

    public Summary()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        AccessibleRole = AccessibleRole.Table;
    }

    public void Clear() { rows.Clear(); Invalidate(); }

    public void Add(string label, string value)
    {
        rows.Add(new KeyValuePair<string, string>(label, value));
        AccessibleName = (AccessibleName ?? "") + label + ": " + value + ". ";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : Color.White);
        int y = 0, lw = Ui.S(LabelWidth), gap = Ui.S(14), vw = Width - lw - gap;
        Font lf = Ui.Font(13, false), vf = Ui.Font(13, true);
        foreach (var r in rows)
        {
            int h = Math.Max(Ui.S(20), Ui.TextHeight(r.Value, vf, vw));
            TextRenderer.DrawText(g, r.Key, lf, new Rectangle(Ui.X(Width, 0, lw), y, lw, Ui.S(20)), Theme.Muted, Ui.Flags(TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis));
            TextRenderer.DrawText(g, r.Value, vf, new Rectangle(Ui.X(Width, lw + gap, vw), y, vw, h), Theme.Ink, Ui.Flags(TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl));
            y += h + Ui.S(10);
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, y - Ui.S(5), Width, y - Ui.S(5));
        }
    }
}

/// <summary>A picture that keeps its smoothness at any size.</summary>
class Picture : Control
{
    public Image Image;
    public Picture()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.Selectable, false);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Ui.Smooth(e.Graphics);
        e.Graphics.Clear(Parent != null ? Parent.BackColor : Color.White);
        if (Image != null) e.Graphics.DrawImage(Image, ClientRectangle);
    }
}

/// <summary>A big success, warning or failure symbol.</summary>
class MarkBox : Control
{
    public Mark State = Mark.Pass;
    public MarkBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.Selectable, false);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : Color.White);
        Marks.Draw(e.Graphics, State, new Rectangle(1, 1, Width - 3, Height - 3));
    }
}

/// <summary>A themed question box. The form is mirrored the same way as the wizard.</summary>
class Prompt : Form
{
    /// <summary>Test drive: the answer to the next question.</summary>
    public static bool? TestAnswer;

    bool answer;

    public static bool Ask(IWin32Window owner, string title, string text, string yes, string no, bool warning)
    {
        if (TestAnswer.HasValue) { bool a = TestAnswer.Value; TestAnswer = null; return a; }
        using (var p = new Prompt(title, text, yes, no, warning))
        {
            p.ShowDialog(owner);
            return p.answer;
        }
    }

    public Prompt(string title, string text, string yes, string no, bool warning)
    {
        Text = title;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.White;
        if (Ui.Rtl) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
        int w = Ui.S(440), m = Ui.S(22), icon = Ui.S(28), tx = m + icon + Ui.S(14), tw = w - tx - m;
        int th = Ui.TextHeight(text, Ui.Font(13, false), tw);
        int bodyH = Math.Max(Ui.S(30), Ui.S(28) + th);
        int h = m + bodyH + Ui.S(16) + Ui.S(58);
        ClientSize = new Size(w, h);
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        Controls.Add(root);
        root.Controls.Add(new MarkBox { State = warning ? Mark.Warn : Mark.Info, Bounds = new Rectangle(Ui.X(w, m, icon), m, icon, icon) });
        root.Controls.Add(new TextView { Text = title, Font = Ui.Font(15, true), ForeColor = Theme.Ink, Bounds = new Rectangle(Ui.X(w, tx, tw), m - Ui.S(2), tw, Ui.S(24)) });
        root.Controls.Add(new TextView { Text = text, Font = Ui.Font(13, false), ForeColor = Theme.Muted, Bounds = new Rectangle(Ui.X(w, tx, tw), m + Ui.S(26), tw, th) });
        var bar = new Panel { BackColor = Theme.Wash, Bounds = new Rectangle(0, h - Ui.S(58), w, Ui.S(58)) };
        root.Controls.Add(bar);
        int bw = Ui.S(120), bh = Ui.S(34), by = Ui.S(12);
        var bYes = new FlatBtn { Text = yes, Kind = BtnKind.Primary, Bounds = new Rectangle(Ui.X(w, w - m - bw * 2 - Ui.S(8), bw), by, bw, bh) };
        var bNo = new FlatBtn { Text = no, Bounds = new Rectangle(Ui.X(w, w - m - bw, bw), by, bw, bh) };
        bYes.Click += (o, e) => { answer = true; Close(); };
        bNo.Click += (o, e) => { answer = false; Close(); };
        bar.Controls.Add(bNo);
        bar.Controls.Add(bYes);
        AcceptButton = bNo; // the safe answer on Enter
        CancelButton = bNo;
        ActiveControl = bNo;
    }
}
