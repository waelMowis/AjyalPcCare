using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Controls;

/// <summary>
/// كارت ارتفاعه بيتحسب من محتواه (نصوص، إعدادات، أزرار).
/// كل عنصر في سطر لوحده، والنصوص بتلف على عرض الكارت — مفيش قص ولا تداخل.
/// الترتيب بيتعمل يدويًا في OnLayout (مش TableLayoutPanel) عشان القياس يبقى مضمون.
/// </summary>
public sealed class FlowCard : RoundedPanel, IHeightForWidth
{
    private readonly HashSet<Control> _stretch = [];
    private int _lastRequested = -1;

    public int ItemGap { get; set; } = UiTheme.Px(6);

    public FlowCard(string? title = null)
    {
        Padding = new Padding(UiTheme.Px(18), UiTheme.Px(14), UiTheme.Px(18), UiTheme.Px(16));
        if (title is not null)
        {
            var t = new Label { Text = title, Font = UiTheme.Subtitle, ForeColor = UiTheme.Text, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, UiTheme.Px(6)) };
            Add(t, stretch: true);
        }
    }

    /// <summary>يضيف عنصر في سطر جديد. stretch = ياخد عرض الكارت كله.</summary>
    public T Add<T>(T control, bool stretch = false) where T : Control
    {
        if (control is Label l)
        {
            // كل النصوص بتلف — الارتفاع بيتقاس، مش ثابت
            l.AutoSize = false;
            stretch = true;
            l.TextChanged += (_, _) => RequestLayout();
        }
        if (stretch) _stretch.Add(control);
        control.Dock = DockStyle.None;
        control.Anchor = AnchorStyles.None;
        Controls.Add(control);
        control.SizeChanged += (_, _) => { if (!_inLayout) RequestLayout(); };
        control.VisibleChanged += (_, _) => RequestLayout();
        return control;
    }

    /// <summary>صف أزرار/عناصر أفقي بيلف لو العرض ضيق.</summary>
    public FlowLayoutPanel AddButtons(params Control[] items)
    {
        var flow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        foreach (var i in items)
        {
            if (i is Label { AutoSize: true } lbl)
            {
                // عناوين جنب الحقول: في نص ارتفاع الحقل
                lbl.Padding = new Padding(0, UiTheme.Px(6), 0, 0);
            }
        }
        flow.Controls.AddRange(items);
        return Add(flow, stretch: true);
    }

    /// <summary>سطر "عنوان: قيمة" — يرجع Label القيمة لتحديثها.</summary>
    public Label AddField(string caption, string value = "—")
    {
        var row = new FieldRow(caption, value);
        Add(row, stretch: true);
        return row.Value;
    }

    private int InnerWidth(int width) => Math.Max(50, width - Padding.Horizontal);

    private int HeightOfChild(Control c, int inner) =>
        _stretch.Contains(c) ? Measure.HeightOf(c, inner) : Measure.HeightOf(c, Measure.NaturalWidth(c, inner));

    public int GetHeightForWidth(int width)
    {
        int inner = InnerWidth(width);
        AlignFieldCaptions(inner);
        int h = Padding.Vertical;
        bool first = true;
        foreach (Control c in Controls)
        {
            if (!c.Visible) continue;
            if (!first) h += ItemGap;
            h += HeightOfChild(c, inner) + c.Margin.Vertical;
            first = false;
        }
        return h;
    }

    private bool _inLayout;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (Width <= 0) return;
        _inLayout = true;
        try
        {
            int inner = InnerWidth(Width);
            AlignFieldCaptions(inner);
            int y = Padding.Top;
            bool first = true;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                if (!first) y += ItemGap;
                y += c.Margin.Top;
                int w = _stretch.Contains(c) ? inner : Measure.NaturalWidth(c, inner);
                // العناصر AutoSize (صفوف الأزرار) لازم تعرف أقصى عرض عشان تلف بدل ما تخرج برا الكارت
                if (c.AutoSize) c.MaximumSize = new Size(w, 0);
                int h = HeightOfChild(c, inner);
                // x = Padding.Left = بداية السطر (يمين فعليًا بسبب RightToLeftLayout)
                c.SetBounds(Padding.Left, y, w, h);
                y += h + c.Margin.Bottom;
                first = false;
            }
        }
        finally
        {
            _inLayout = false;
        }

        // لو المحتوى اتغير والارتفاع المطلوب بقى مختلف — نطلب من الأب يعيد الترتيب
        int needed = GetHeightForWidth(Width);
        if (needed != Height && needed != _lastRequested)
        {
            _lastRequested = needed;
            Parent?.PerformLayout();
        }
    }

    private void RequestLayout()
    {
        if (IsDisposed) return;
        _lastRequested = -1;
        PerformLayout();
    }

    /// <summary>توحيد عرض عناوين FieldRow في نفس الكارت عشان القيم تبدأ من نفس العمود.</summary>
    private void AlignFieldCaptions(int inner)
    {
        var rows = Controls.OfType<FieldRow>().ToList();
        if (rows.Count == 0) return;
        int w = Math.Min(rows.Max(r => r.NaturalCaptionWidth), (int)(inner * 0.45));
        foreach (var r in rows) r.CaptionWidth = w;
    }
}
