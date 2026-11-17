using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Controls;

/// <summary>
/// عنصر بيعرف يحسب ارتفاعه المطلوب لعرض معيّن (نصوص بتلف، كروت محتواها متغير).
/// ده أساس نظام الترتيب كله: مفيش ارتفاعات ثابتة للنصوص — كل حاجة بتتقاس.
/// </summary>
public interface IHeightForWidth
{
    int GetHeightForWidth(int width);
}

/// <summary>أدوات قياس موحدة (بنفس محرك الرسم اللي بتستخدمه عناصر WinForms: GDI / TextRenderer).</summary>
public static class Measure
{
    private const TextFormatFlags WrapFlags =
        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.RightToLeft;

    /// <summary>ارتفاع نص ملفوف على عرض معيّن.</summary>
    public static int TextHeight(string? text, Font font, int width)
    {
        if (width <= 0) width = 1;
        string t = string.IsNullOrEmpty(text) ? " " : text;
        var sz = TextRenderer.MeasureText(t, font, new Size(width, int.MaxValue), WrapFlags);
        return sz.Height;
    }

    /// <summary>ارتفاع سطر واحد فعلي للخط (Readex Pro أطول من Segoe UI بسبب الحروف العربي).</summary>
    public static int LineHeight(Font font) =>
        TextRenderer.MeasureText("أجيال Ag", font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height;

    /// <summary>ارتفاع أي عنصر لعرض معيّن، أيًا كان نوعه.</summary>
    public static int HeightOf(Control c, int width)
    {
        switch (c)
        {
            case IHeightForWidth h:
                return h.GetHeightForWidth(width);
            case TextBoxBase { Multiline: true }:
                return c.Height; // مربع نص متعدد الأسطر: ارتفاعه ثابت ومعاه Scroll
            case Label l:
                return TextHeight(l.Text, l.Font, Math.Max(1, width - l.Padding.Horizontal)) + l.Padding.Vertical + 2;
            default:
                if (c.AutoSize) return c.GetPreferredSize(new Size(width, 0)).Height;
                return c.Height;
        }
    }

    /// <summary>عرض العنصر الطبيعي (لو مش مطلوب يتمدد).</summary>
    public static int NaturalWidth(Control c, int maxWidth)
    {
        if (c is Label l)
        {
            int w = TextRenderer.MeasureText(string.IsNullOrEmpty(l.Text) ? " " : l.Text, l.Font).Width + l.Padding.Horizontal + 4;
            return Math.Min(w, maxWidth);
        }
        if (c.AutoSize) return Math.Min(c.GetPreferredSize(new Size(maxWidth, 0)).Width, maxWidth);
        return Math.Min(c.Width, maxWidth);
    }
}

/// <summary>
/// عمود الصفحة الرئيسي: بيرص العناصر تحت بعض بعرض الصفحة، كل عنصر بارتفاعه المقاس،
/// ويعمل Scroll لو المحتوى أطول من الشاشة. بديل TableLayoutPanel اللي كانت حساباته
/// بتدخل في بعض مع العناصر AutoSize (سبب التكدس).
/// </summary>
public sealed class StackPanel : Panel
{
    public int Gap { get; set; } = UiTheme.Px(12);

    public StackPanel()
    {
        AutoScroll = true;
        DoubleBuffered = true;
        BackColor = UiTheme.Background;
        Padding = new Padding(0, 0, UiTheme.Px(4), UiTheme.Px(12));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int width = ClientSize.Width - Padding.Horizontal;
        if (width <= 0) return;

        int y = Padding.Top;
        foreach (Control c in Controls)
        {
            if (!c.Visible) continue;
            int h = Measure.HeightOf(c, width);
            c.SetBounds(Padding.Left + AutoScrollPosition.X, y + AutoScrollPosition.Y, width, h);
            y += h + Gap;
        }

        var min = new Size(0, y - Gap + Padding.Bottom);
        if (AutoScrollMinSize != min) AutoScrollMinSize = min;
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        PerformLayout();
    }

    public void Add(Control c) => Controls.Add(c); // ترتيب الإضافة = ترتيب العرض من فوق لتحت
}

/// <summary>
/// صف أعمدة متساوية العرض (كروت جنب بعض). ارتفاع الصف = أطول عمود،
/// وكل الأعمدة بتتمد لنفس الارتفاع عشان الصف يبان مستوي.
/// </summary>
public sealed class ColumnsPanel : Panel, IHeightForWidth
{
    public int Gap { get; set; } = UiTheme.Px(12);

    /// <summary>لو العرض أقل من كده، الأعمدة بتنزل تحت بعض بدل ما تتكدس.</summary>
    public int MinColumnWidth { get; set; } = UiTheme.Px(220);

    public ColumnsPanel(params Control[] columns)
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        DoubleBuffered = true;
        foreach (var c in columns) Controls.Add(c);
    }

    private int ColumnsPerRow(int width)
    {
        int n = Math.Max(1, Controls.Cast<Control>().Count(c => c.Visible));
        while (n > 1 && (width - Gap * (n - 1)) / n < MinColumnWidth) n--;
        return n;
    }

    public int GetHeightForWidth(int width)
    {
        var items = Controls.Cast<Control>().Where(c => c.Visible).ToList();
        if (items.Count == 0) return 0;
        int perRow = ColumnsPerRow(width);
        int colW = (width - Gap * (perRow - 1)) / perRow;
        int total = 0;
        for (int i = 0; i < items.Count; i += perRow)
        {
            int rowH = items.Skip(i).Take(perRow).Max(c => Measure.HeightOf(c, colW));
            total += rowH + (i > 0 ? Gap : 0);
        }
        return total;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        var items = Controls.Cast<Control>().Where(c => c.Visible).ToList();
        if (items.Count == 0 || Width <= 0) return;
        int perRow = ColumnsPerRow(Width);
        int colW = (Width - Gap * (perRow - 1)) / perRow;
        int y = 0;
        for (int i = 0; i < items.Count; i += perRow)
        {
            var row = items.Skip(i).Take(perRow).ToList();
            int rowH = row.Max(c => Measure.HeightOf(c, colW));
            for (int j = 0; j < row.Count; j++)
                row[j].SetBounds(j * (colW + Gap), y, colW, rowH);
            y += rowH + Gap;
        }

        // لو محتوى عمود كبر/صغر، الصف كله ارتفاعه اتغير — نبلّغ عمود الصفحة يعيد الترتيب
        int needed = y - Gap;
        if (needed != Height && needed != _lastRequested)
        {
            _lastRequested = needed;
            Parent?.PerformLayout();
        }
    }

    private int _lastRequested = -1;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        PerformLayout();
    }
}

/// <summary>
/// سطر "عنوان: قيمة" — العنوان بعرض ثابت (الكارت بيوحّده لكل السطور عشان تبان متراصة)،
/// والقيمة بتلف لو طويلة بدل ما تخرج برا الكارت.
/// </summary>
public sealed class FieldRow : Control, IHeightForWidth
{
    public Label Caption { get; }
    public Label Value { get; }
    public int CaptionWidth { get; set; }
    private int Gap => UiTheme.Px(10);

    public FieldRow(string caption, string value)
    {
        // Control العادي بيرمي Exception مع Transparent من غير الـ Style ده
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
        Caption = new Label { Text = caption, AutoSize = false, Font = UiTheme.Base, ForeColor = UiTheme.Muted, BackColor = Color.Transparent };
        Value = new Label { Text = value, AutoSize = false, Font = UiTheme.Bold, ForeColor = UiTheme.Text, BackColor = Color.Transparent };
        Controls.Add(Caption);
        Controls.Add(Value);
        BackColor = Color.Transparent;
        Value.TextChanged += (_, _) => Parent?.PerformLayout();
    }

    public int NaturalCaptionWidth => TextRenderer.MeasureText(Caption.Text, Caption.Font).Width + 6;

    private int CapW(int width) => Math.Min(CaptionWidth > 0 ? CaptionWidth : NaturalCaptionWidth, (int)(width * 0.45));

    public int GetHeightForWidth(int width)
    {
        int cw = CapW(width);
        int capH = Measure.TextHeight(Caption.Text, Caption.Font, cw);
        int valH = Measure.TextHeight(Value.Text, Value.Font, Math.Max(40, width - cw - Gap));
        return Math.Max(capH, valH) + UiTheme.Px(6);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        int cw = CapW(Width);
        int pad = UiTheme.Px(3);
        // x=0 هو "بداية السطر" — الفورم RightToLeftLayout فبيتعكس لليمين تلقائيًا
        Caption.SetBounds(0, pad, cw, Height - pad);
        Value.SetBounds(cw + Gap, pad, Math.Max(40, Width - cw - Gap), Height - pad);
    }
}
