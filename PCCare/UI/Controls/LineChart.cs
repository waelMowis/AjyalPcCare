using System.Drawing.Drawing2D;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Controls;

public sealed class ChartSeries
{
    public required string Name { get; init; }
    public required Color Color { get; init; }
    public double[] Values { get; set; } = [];
    public bool Fill { get; init; } = true;
}

/// <summary>
/// رسم بياني خطي لآخر N ثانية (مرسوم يدويًا — من غير مكتبات).
/// ملاحظة RTL: بنشيل WS_EX_LAYOUTRTL من الكنترول ده عشان ويندوز ما يعكسش الرسم والنصوص.
/// المحور الزمني: الأحدث على اليمين (زي Task Manager).
/// </summary>
public sealed class LineChart : Control
{
    private const int WS_EX_LAYOUTRTL = 0x00400000;

    public List<ChartSeries> Series { get; } = [];
    public string Title { get; set; } = "";
    public string ValueText { get; set; } = "";
    /// <summary>null = تلقائي حسب أعلى قيمة.</summary>
    public double? FixedMax { get; set; } = 100;
    public int Capacity { get; set; } = 60;
    public Func<double, string> AxisFormatter { get; set; } = v => v.ToString("0");

    public LineChart()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Card;
        RightToLeft = RightToLeft.No;
        MinimumSize = new Size(200, 140);
        Margin = new Padding(6);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle &= ~WS_EX_LAYOUTRTL;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        using var borderPen = new Pen(UiTheme.Border);
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        var titleFont = UiTheme.Bold;
        var smallFont = UiTheme.Small;
        int titleH = (int)titleFont.GetHeight(g) + 10;

        // العنوان يمين والقيمة شمال (قراءة عربي)
        var rtlFormat = new StringFormat(StringFormatFlags.DirectionRightToLeft) { Alignment = StringAlignment.Near };
        using (var tb = new SolidBrush(UiTheme.Text))
            g.DrawString(Title, titleFont, tb, new RectangleF(8, 6, Width - 16, titleH), rtlFormat);
        using (var vb = new SolidBrush(Series.Count > 0 ? Series[0].Color : UiTheme.Text))
            g.DrawString(ValueText, titleFont, vb, new RectangleF(8, 6, Width - 16, titleH), new StringFormat { Alignment = StringAlignment.Near });

        var plot = new Rectangle(44, titleH + 4, Width - 56, Height - titleH - 16);
        if (plot.Width < 20 || plot.Height < 20) return;

        double max = FixedMax ?? Math.Max(1, Series.SelectMany(s => s.Values).DefaultIfEmpty(1).Max() * 1.15);

        using var gridPen = new Pen(Color.FromArgb(238, 240, 244)) { DashStyle = DashStyle.Dot };
        using var axisBrush = new SolidBrush(UiTheme.Muted);
        for (int i = 0; i <= 4; i++)
        {
            int y = plot.Bottom - i * plot.Height / 4;
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            string label = AxisFormatter(max * i / 4);
            var sz = g.MeasureString(label, smallFont);
            g.DrawString(label, smallFont, axisBrush, plot.Left - sz.Width - 2, y - sz.Height / 2);
        }

        foreach (var s in Series)
        {
            if (s.Values.Length < 2) continue;
            var pts = new PointF[s.Values.Length];
            float step = plot.Width / (float)Math.Max(1, Capacity - 1);
            int offset = Capacity - s.Values.Length; // نملا من اليمين
            for (int i = 0; i < s.Values.Length; i++)
            {
                float x = plot.Left + (offset + i) * step;
                float y = plot.Bottom - (float)(Math.Clamp(s.Values[i] / max, 0, 1) * plot.Height);
                pts[i] = new PointF(x, y);
            }

            if (s.Fill)
            {
                using var path = new GraphicsPath();
                path.AddLines(pts);
                path.AddLine(pts[^1], new PointF(pts[^1].X, plot.Bottom));
                path.AddLine(new PointF(pts[^1].X, plot.Bottom), new PointF(pts[0].X, plot.Bottom));
                path.CloseFigure();
                using var fill = new SolidBrush(Color.FromArgb(40, s.Color));
                g.FillPath(fill, path);
            }

            using var pen = new Pen(s.Color, 2f);
            g.DrawLines(pen, pts);
        }

        // مفتاح السلاسل لو أكتر من واحدة
        if (Series.Count > 1)
        {
            float lx = plot.Right;
            foreach (var s in Series)
            {
                var sz = g.MeasureString(s.Name, smallFont);
                lx -= sz.Width + 22;
                using var b = new SolidBrush(s.Color);
                g.FillRectangle(b, lx, plot.Top + 4, 10, 10);
                g.DrawString(s.Name, smallFont, axisBrush, lx + 13, plot.Top);
            }
        }
    }
}
