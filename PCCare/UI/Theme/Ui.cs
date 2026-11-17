using AjyalPcCare.UI.Controls;
using System.Diagnostics;

namespace AjyalPcCare.UI.Theme;

/// <summary>مصنع عناصر موحّد الشكل — عشان كل الصفحات تبان نفس الهوية.</summary>
public static class Ui
{
    public static Button Button(string text, bool primary = false, EventHandler? onClick = null)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(UiTheme.Px(14), UiTheme.Px(3), UiTheme.Px(14), UiTheme.Px(3)),
            Margin = new Padding(0, UiTheme.Px(4), UiTheme.Px(8), UiTheme.Px(4)),
            MinimumSize = new Size(UiTheme.Px(90), Measure.LineHeight(UiTheme.Base) + UiTheme.Px(16)),
            FlatStyle = FlatStyle.Flat,
            Font = UiTheme.Base,
            Cursor = Cursors.Hand,
            BackColor = primary ? UiTheme.Brand : UiTheme.Card,
            ForeColor = primary ? Color.White : UiTheme.Text,
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderColor = primary ? UiTheme.Brand : UiTheme.Border;
        b.FlatAppearance.MouseOverBackColor = primary ? UiTheme.BrandDark : UiTheme.BrandLight;
        if (onClick is not null) b.Click += onClick;
        return b;
    }

    public static Button DangerButton(string text, EventHandler? onClick = null)
    {
        var b = Button(text, false, onClick);
        b.ForeColor = UiTheme.Danger;
        b.FlatAppearance.BorderColor = UiTheme.Danger;
        b.FlatAppearance.MouseOverBackColor = UiTheme.DangerLight;
        return b;
    }

    public static Label Label(string text, Font? font = null, Color? color = null, bool autoSize = true) => new()
    {
        Text = text,
        AutoSize = autoSize,
        Font = font ?? UiTheme.Base,
        ForeColor = color ?? UiTheme.Text,
        Margin = new Padding(4),
        BackColor = Color.Transparent
    };

    /// <summary>نص توضيحي بيلف (جوه FlowCard أو Card ارتفاعه بيتحسب تلقائي).</summary>
    public static Label Note(string text, Color? color = null, Font? font = null) => new()
    {
        Text = text,
        AutoSize = false,
        Font = font ?? UiTheme.Base,
        ForeColor = color ?? UiTheme.Muted,
        BackColor = Color.Transparent
    };

    /// <summary>يخلّي Label مثبت (Dock Top/Bottom) ياخد ارتفاع نصه الحقيقي بعد اللف — بدل ارتفاع ثابت بيقص النص.</summary>
    public static void FitHeight(Label l)
    {
        l.AutoSize = false;
        void Fit()
        {
            int h = Measure.TextHeight(l.Text, l.Font, Math.Max(50, l.Width - l.Padding.Horizontal)) + l.Padding.Vertical + 2;
            if (l.Height != h) l.Height = h;
        }
        l.SizeChanged += (_, _) => Fit();
        l.TextChanged += (_, _) => Fit();
        Fit();
    }

    public static FlowLayoutPanel Toolbar() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true,
        Padding = new Padding(0, UiTheme.Px(2), 0, UiTheme.Px(8)),
        BackColor = Color.Transparent,
        FlowDirection = FlowDirection.LeftToRight // مع RightToLeft = Yes بيتعكس تلقائي لليمين
    };

    public static DataGridView Grid(bool readOnly = true)
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = readOnly,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = UiTheme.Card,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = UiTheme.Border,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            Font = UiTheme.Small // الجداول بخط أصغر سنة: نفس المعلومة في مساحة أقل من غير زحمة
        };
        // ارتفاعات ثابتة محسوبة من الخط الفعلي — صفوف متساوية وشكل مرتب
        g.ColumnHeadersHeight = Measure.LineHeight(UiTheme.SmallBold) + UiTheme.Px(16);
        g.RowTemplate.Height = Measure.LineHeight(UiTheme.Small) + UiTheme.Px(14);
        g.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.BrandLight;
        g.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.BrandDark;
        g.ColumnHeadersDefaultCellStyle.Font = UiTheme.SmallBold;
        g.ColumnHeadersDefaultCellStyle.Padding = new Padding(UiTheme.Px(6), 0, UiTheme.Px(6), 0);
        g.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.BrandLight;
        g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);
        g.DefaultCellStyle.SelectionBackColor = UiTheme.BrandLight;
        g.DefaultCellStyle.SelectionForeColor = UiTheme.Text;
        g.DefaultCellStyle.Padding = new Padding(UiTheme.Px(6), 0, UiTheme.Px(6), 0);
        g.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        g.DataError += (_, e) => e.ThrowException = false;
        g.ColumnAdded += (_, e) =>
        {
            if (Equals(e.Column.Tag, "wrap")) g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            e.Column.MinimumWidth = UiTheme.Px(50);
        };
        return g;
    }

    public static DataGridViewTextBoxColumn TextColumn(string name, string header, float weight = 100, bool wrap = false)
    {
        var c = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = weight,
            SortMode = DataGridViewColumnSortMode.Automatic,
            ReadOnly = true
        };
        if (wrap)
        {
            // العمود ده بيلف: الصفوف بتكبر على قد النص (الجدول بيتحول AutoSize لما العمود يتضاف)
            c.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            c.Tag = "wrap";
        }
        return c;
    }

    public static ProgressBar Progress() => new()
    {
        Dock = DockStyle.Top,
        Height = UiTheme.Px(6),
        Style = ProgressBarStyle.Continuous,
        Visible = false,
        Margin = new Padding(0, 4, 0, 4)
    };

    public static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogs.Error(null, "تعذر الفتح: " + ex.Message);
        }
    }

    public static void ShowInExplorer(string file)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }
}

public static class Dialogs
{
    private const MessageBoxOptions Rtl = MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading;

    // في فحص الفتح (--smoke-test) مفيش نوافذ: أي نافذة كانت هتعلّق الفحص على GitHub
    public static bool Confirm(IWin32Window? owner, string text, string caption = "تأكيد", bool warning = false)
    {
        if (SmokeTest.Active) return false;
        return MessageBox.Show(owner, text, caption, MessageBoxButtons.YesNo, warning ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2, Rtl) == DialogResult.Yes;
    }

    public static void Info(IWin32Window? owner, string text, string caption = "AJYAL PC Care")
    {
        if (SmokeTest.Active) { Console.WriteLine("SMOKE info: " + text); return; }
        MessageBox.Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, Rtl);
    }

    public static void Error(IWin32Window? owner, string text, string caption = "خطأ")
    {
        if (SmokeTest.Active) { SmokeTest.Fail(text); return; }
        MessageBox.Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, Rtl);
    }
}
