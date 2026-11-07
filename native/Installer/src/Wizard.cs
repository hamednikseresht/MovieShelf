// The frame shared by the install and uninstall wizards: sidebar, title, bottom bar, keyboard, and the Page base class.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

/// <summary>Wrapped text that measures and draws with the same flags, so it never clips at any scale.</summary>
class TextView : Control
{
    /// <summary>Align to the far edge: right in English, left in Persian.</summary>
    public bool Far;

    public TextView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        AccessibleRole = AccessibleRole.StaticText;
    }

    protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : Color.White);
        TextFormatFlags f = Ui.Flags(TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        if (Far) f = Ui.Rtl ? f & ~TextFormatFlags.Right : f | TextFormatFlags.Right;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, f);
    }
}

/// <summary>One page of a wizard. Layout is in logical pixels inside a 568 by 400 area.</summary>
abstract class Page
{
    public const int AreaW = 568, AreaH = 400;

    public Wizard W;
    public Panel Host;
    public bool Built;

    public abstract string Id { get; }
    public abstract string SideName { get; }
    public abstract string Title { get; }
    public abstract string Sub { get; }
    public abstract void Build();

    public virtual bool Shown { get { return true; } }
    public virtual bool CanNext { get { return true; } }
    public virtual bool CanBack { get { return true; } }
    public virtual bool CanCancel { get { return true; } }
    public virtual bool HidesCancel { get { return false; } }
    public virtual string NextText { get { return Texts.Next(); } }
    public virtual bool NextShield { get { return false; } }
    public virtual void Enter() { }
    /// <summary>Runs on Next. False stays on the page.</summary>
    public virtual bool Leave() { return true; }
    /// <summary>The test drive sets a control through this, using the control's own handlers.</summary>
    public virtual void Set(string key, string value) { throw new InvalidOperationException("Page " + Id + " has no " + key); }

    protected T Add<T>(T c, int x, int y, int w, int h) where T : Control
    {
        c.SetBounds(Ui.X(Host.Width, Ui.S(x), Ui.S(w)), Ui.S(y), Ui.S(w), Ui.S(h));
        c.TabIndex = Host.Controls.Count;
        Host.Controls.Add(c);
        return c;
    }

    /// <summary>Moves a control already on the page.</summary>
    protected void Place(Control c, int x, int y, int w, int h) { c.SetBounds(Ui.X(Host.Width, Ui.S(x), Ui.S(w)), Ui.S(y), Ui.S(w), Ui.S(h)); }

    /// <summary>Adds wrapped text at y and moves y below it.</summary>
    protected TextView Text(string text, int x, ref int y, int w, float size, bool semi, Color color, int after)
    {
        Font f = Ui.Font(size, semi);
        int h = Ui.Logical(Ui.TextHeight(text, f, Ui.S(w)));
        TextView t = Add(new TextView { Text = text, Font = f, ForeColor = color }, x, y, w, h);
        y += h + after;
        return t;
    }

    protected CheckBox Check(string text, int x, int y, int w, bool on)
    {
        var c = new CheckBox { Text = text, Font = Ui.Font(13, false), ForeColor = Theme.Ink, Checked = on, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.White };
        return Add(c, x, y, w, 26);
    }

    protected LinkLabel Link(string text, int x, int y, int w)
    {
        var l = new LinkLabel { Text = text, Font = Ui.Font(13, false), LinkColor = Theme.Link, ActiveLinkColor = Theme.AmberHot, VisitedLinkColor = Theme.Link, LinkBehavior = LinkBehavior.HoverUnderline, AutoSize = false };
        return Add(l, x, y, w, 24);
    }
}

abstract class Wizard : Form
{
    public readonly List<Page> Pages = new List<Page>();
    public int Index;
    public FlatBtn BackBtn, NextBtn, CancelBtn;
    public bool CloseWithoutAsking;
    /// <summary>The process exit code once the wizard closes.</summary>
    public int Code = Exit.Cancelled;

    Panel body;
    SideBar side;
    TextView title, sub;

    public Page Current { get { return Pages[Index]; } }

    protected abstract void MakePages();
    protected abstract string WindowTitle { get; }
    /// <summary>The question Cancel asks. Returns true when the wizard may close.</summary>
    protected abstract void OnCancelPressed();

    protected Wizard()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.White;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Env.Self); } catch (Exception) { }
    }

    /// <summary>Builds everything for the current language and scale. Called again when the language changes.</summary>
    public void BuildAll()
    {
        Text = WindowTitle;
        SuspendLayout();
        foreach (Control c in ToList(Controls)) c.Dispose();
        Controls.Clear();
        Pages.Clear();

        RightToLeft = Ui.Rtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = Ui.Rtl;
        Font = Ui.Font(13, false);
        int w = Ui.S(Ui.BaseW), h = Ui.S(Ui.BaseH), sideW = Ui.S(216), contentW = w - sideW, barH = Ui.S(68), m = Ui.S(28);
        ClientSize = new Size(w, h);

        var root = new Panel { Bounds = new Rectangle(0, 0, w, h), Dock = DockStyle.Fill, BackColor = Color.White, RightToLeft = RightToLeft };
        Controls.Add(root);
        side = new SideBar { Bounds = new Rectangle(Ui.X(w, 0, sideW), 0, sideW, h), Version = Fmt.Num(BuildInfo.Version) };
        root.Controls.Add(side);

        var content = new Panel { Bounds = new Rectangle(Ui.X(w, sideW, contentW), 0, contentW, h), BackColor = Color.White };
        root.Controls.Add(content);
        title = new TextView { Font = Ui.Font(26, true), ForeColor = Theme.Ink, Bounds = new Rectangle(m, Ui.S(22), contentW - 2 * m, Ui.S(38)) };
        sub = new TextView { Font = Ui.Font(14, false), ForeColor = Theme.Muted, Bounds = new Rectangle(m, Ui.S(62), contentW - 2 * m, Ui.S(24)) };
        body = new Panel { Bounds = new Rectangle(m, Ui.S(100), contentW - 2 * m, h - Ui.S(100) - barH - Ui.S(8)), BackColor = Color.White, TabIndex = 0 };
        content.Controls.Add(title);
        content.Controls.Add(sub);
        content.Controls.Add(body);

        var bar = new Panel { Bounds = new Rectangle(0, h - barH, contentW, barH), BackColor = Theme.Wash, TabIndex = 1 };
        content.Controls.Add(bar);
        bar.Controls.Add(new Panel { Bounds = new Rectangle(0, 0, contentW, 1), BackColor = Theme.Line });
        int bw = Ui.S(104), bh = Ui.S(36), by = (barH - bh) / 2, gap = Ui.S(10);
        int xCancel = contentW - m - bw, xNext = xCancel - bw - gap * 2, xBack = xNext - bw - gap;
        BackBtn = new FlatBtn { Text = Texts.Back(), Bounds = new Rectangle(Ui.X(contentW, xBack, bw), by, bw, bh), TabIndex = 0 };
        NextBtn = new FlatBtn { Text = Texts.Next(), Kind = BtnKind.Primary, Bounds = new Rectangle(Ui.X(contentW, xNext, bw), by, bw, bh), TabIndex = 1 };
        CancelBtn = new FlatBtn { Text = Texts.Cancel(), Bounds = new Rectangle(Ui.X(contentW, xCancel, bw), by, bw, bh), TabIndex = 2 };
        BackBtn.Click += (o, e) => Back();
        NextBtn.Click += (o, e) => Next();
        CancelBtn.Click += (o, e) => OnCancelPressed();
        bar.Controls.Add(BackBtn);
        bar.Controls.Add(NextBtn);
        bar.Controls.Add(CancelBtn);
        AcceptButton = NextBtn;
        CancelButton = CancelBtn;

        MakePages();
        foreach (Page p in Pages)
        {
            p.W = this;
            p.Host = new Panel { Bounds = new Rectangle(0, 0, body.Width, body.Height), BackColor = Color.White, Visible = false };
            body.Controls.Add(p.Host);
        }
        ResumeLayout(false);
        if (Index >= Pages.Count) Index = 0;
        ShowPage(Index);
    }

    static List<Control> ToList(Control.ControlCollection c)
    {
        var l = new List<Control>();
        foreach (Control x in c) l.Add(x);
        return l;
    }

    public void ShowPage(int i)
    {
        Index = i;
        Page p = Pages[i];
        if (!p.Built)
        {
            p.Host.SuspendLayout();
            p.Build();
            p.Host.ResumeLayout(false);
            p.Built = true;
        }
        foreach (Page o in Pages) o.Host.Visible = o == p;
        title.Text = p.Title;
        sub.Text = p.Sub;
        side.Steps.Clear();
        int at = 0;
        foreach (Page o in Pages)
        {
            if (!o.Shown) continue;
            if (o == p) at = side.Steps.Count;
            side.Steps.Add(o.SideName);
        }
        side.Current = at;
        side.Invalidate();
        p.Enter();
        UpdateButtons();
        Control first = FirstFocus(p.Host);
        if (first != null) first.Select(); else if (NextBtn.Enabled) NextBtn.Select();
    }

    static Control FirstFocus(Control host)
    {
        Control best = null;
        foreach (Control c in host.Controls)
            if (c.Visible && c.Enabled && c.TabStop && (c is Card || (c is TextBox && !((TextBox)c).ReadOnly)) && (best == null || c.TabIndex < best.TabIndex)) best = c;
        return best;
    }

    public void UpdateButtons()
    {
        Page p = Current;
        BackBtn.Enabled = p.CanBack && PrevShown(Index) >= 0;
        NextBtn.Enabled = p.CanNext;
        NextBtn.Text = p.NextText;
        NextBtn.Shield = p.NextShield;
        NextBtn.Invalidate();
        CancelBtn.Visible = !p.HidesCancel;
        CancelBtn.Enabled = p.CanCancel;
    }

    int PrevShown(int i) { for (i--; i >= 0; i--) if (Pages[i].Shown) return i; return -1; }
    int NextShown(int i) { for (i++; i < Pages.Count; i++) if (Pages[i].Shown) return i; return -1; }

    public void Next()
    {
        if (!NextBtn.Enabled) return;
        if (!Current.Leave()) return;
        int i = NextShown(Index);
        if (i < 0) { CloseWithoutAsking = true; Close(); }
        else ShowPage(i);
    }

    public void Back()
    {
        int i = PrevShown(Index);
        if (i >= 0) ShowPage(i);
    }

    public Page Find(string id) { return Pages.Find(p => p.Id == id); }
    public void GoTo(string id) { ShowPage(Pages.IndexOf(Find(id))); }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Access keys by physical key, so they work in the Persian UI and on a Persian keyboard layout too.
        if (keyData == (Keys.Alt | Keys.N) && NextBtn != null) { NextBtn.PerformClick(); return true; }
        if (keyData == (Keys.Alt | Keys.B) && BackBtn != null) { BackBtn.PerformClick(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!CloseWithoutAsking && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (Current.HidesCancel) { CloseWithoutAsking = true; e.Cancel = false; }
            else if (CancelBtn.Enabled) OnCancelPressed();
        }
        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Env.Test("DRIVE") == null) Activate();
    }
}
