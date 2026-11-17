using System.Drawing.Drawing2D;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Controls;

/// <summary>
/// لوحة بيضا بحواف مدورة وحد خفيف (شكل الكروت المرفوعة).
/// الزوايا بتترسم بلون خلفية الأب عشان تبان مدورة فعلًا.
/// </summary>
public class RoundedPanel : Panel
{
    public int Radius { get; set; } = UiTheme.Px(10);
    public Color BorderColor { get; set; } = UiTheme.Border;
    public Color FillColor { get; set; } = UiTheme.Card;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = UiTheme.Card;
        Margin = new Padding(0);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor is { A: > 0 } pc ? pc : UiTheme.Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rect.Width <= 2 || rect.Height <= 2) return;
        using var path = RoundRect(rect, Radius);
        using var fill = new SolidBrush(FillColor);
        using var pen = new Pen(BorderColor);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }

    internal static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = Math.Max(2, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>
/// كارت بارتفاع ثابت فيه عنوان ومحتوى (غالبًا جدول بيملا الباقي).
/// النصوص التوضيحية جواه ارتفاعها بيتحسب من طول النص — مش سطرين ثابتين.
/// </summary>
public class Card : RoundedPanel
{
    private readonly Label? _title;

    public Card(string? title = null)
    {
        Padding = new Padding(UiTheme.Px(16), UiTheme.Px(title is null ? 14 : 10), UiTheme.Px(16), UiTheme.Px(14));
        if (title is not null)
        {
            _title = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = Measure.LineHeight(UiTheme.Subtitle) + UiTheme.Px(12),
                Font = UiTheme.Subtitle,
                ForeColor = UiTheme.Text,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft // بداية السطر = يمين في RTL
            };
            Controls.Add(_title);
        }
    }

    public string? TitleText
    {
        get => _title?.Text;
        set { if (_title is not null) _title.Text = value; }
    }

    /// <summary>يضيف عنصر تحت العنوان بالترتيب. الجداول Fill، والباقي Top.</summary>
    public void AddContent(Control c)
    {
        if (c.Dock == DockStyle.None) c.Dock = DockStyle.Top;
        if (c is Label l && c.Dock == DockStyle.Top)
        {
            l.BackColor = Color.Transparent;
            l.Padding = new Padding(0, UiTheme.Px(2), 0, UiTheme.Px(6));
            Ui.FitHeight(l);
        }
        Controls.Add(c);
        c.BringToFront();
    }
}

/// <summary>كارت رقم واحد: عنوان + قيمة كبيرة + سطر توضيحي + شريط نسبة.</summary>
public sealed class MetricCard : RoundedPanel, IHeightForWidth
{
    private readonly Label _caption;
    private readonly Label _value;
    private readonly Label _sub;
    private readonly Panel _bar;
    private double _percent;
    private readonly int _height;

    public Color Accent { get; set; } = UiTheme.Brand;

    public MetricCard(string caption)
    {
        Padding = new Padding(UiTheme.Px(16), UiTheme.Px(12), UiTheme.Px(16), UiTheme.Px(14));
        int capH = Measure.LineHeight(UiTheme.Base) + UiTheme.Px(4);
        int valH = Measure.LineHeight(UiTheme.Metric) + UiTheme.Px(4);
        int subH = Measure.LineHeight(UiTheme.Small) + UiTheme.Px(4);
        int barH = UiTheme.Px(5);
        // الارتفاع محسوب من مقاسات الخط الفعلية — عمره ما هيقص النص
        _height = Padding.Vertical + capH + valH + subH + UiTheme.Px(8) + barH;
        Height = _height;

        _caption = MakeLabel(caption, UiTheme.Base, UiTheme.Muted, capH);
        _value = MakeLabel("—", UiTheme.Metric, UiTheme.Text, valH);
        _sub = MakeLabel("", UiTheme.Small, UiTheme.Muted, subH);
        _sub.AutoEllipsis = true;
        _bar = new Panel { Dock = DockStyle.Bottom, Height = barH, BackColor = UiTheme.BrandLight };
        _bar.Paint += (_, e) =>
        {
            int w = (int)(_bar.Width * Math.Clamp(_percent, 0, 100) / 100);
            using var b = new SolidBrush(Accent);
            // الفورم RightToLeftLayout = إحداثيات الرسم معكوسة: x=0 هو اليمين فعليًا
            e.Graphics.FillRectangle(b, 0, 0, w, _bar.Height);
        };

        Controls.Add(_bar);
        Controls.Add(_sub);
        Controls.Add(_value);
        Controls.Add(_caption);
    }

    private static Label MakeLabel(string text, Font font, Color color, int height) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        AutoSize = false,
        Height = height,
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent,
        TextAlign = ContentAlignment.MiddleLeft
    };

    public int GetHeightForWidth(int width) => _height;

    public void SetValue(string value, string sub, double percent)
    {
        _value.Text = value;
        _sub.Text = sub;
        _percent = percent;
        _value.ForeColor = percent >= 90 ? UiTheme.Danger : percent >= 75 ? UiTheme.Warning : UiTheme.Text;
        _bar.Invalidate();
    }
}
