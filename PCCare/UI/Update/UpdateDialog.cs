using AjyalPcCare.Core.Update;
using AjyalPcCare.UI.Controls;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Update;

/// <summary>
/// نافذة "فيه تحديث": الإصدار الجديد + الجديد فيه + الحجم.
/// OK = حدّث دلوقتي، Ignore = تخطّى الإصدار ده، Cancel = بعدين.
/// </summary>
public sealed class UpdateDialog : Form
{
    public UpdateDialog(UpdateChecker.Release r)
    {
        Text = "تحديث جديد — AJYAL PC Care";
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiTheme.Card;
        Font = UiTheme.Base;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(UiTheme.Px(580), UiTheme.Px(460));
        Padding = new Padding(UiTheme.Px(22), UiTheme.Px(18), UiTheme.Px(22), UiTheme.Px(16));

        var header = new Label
        {
            Text = $"الإصدار {r.Version.ToString(3)} متاح",
            Dock = DockStyle.Top,
            Font = UiTheme.Title,
            ForeColor = UiTheme.Brand,
            Height = Measure.LineHeight(UiTheme.Title) + UiTheme.Px(8),
            TextAlign = ContentAlignment.MiddleLeft
        };

        string sizeText = r.SetupSize > 0 ? $"  •  حجم التحديث: {r.SetupSize / 1024.0 / 1024.0:0} ميجا" : "";
        var note = new Label
        {
            Text = $"إنت على {UpdateChecker.CurrentVersionText}{sizeText}\n" +
                   "التحديث بيتنزّل ويتأكد منه (نفس المستودع + البصمة)، وبعدين البرنامج يقفل ويتثبت التحديث ويفتح تاني لوحده.\n" +
                   "الإعدادات والسجل والتقارير والمهمة المجدولة مش بيتلمسوا.",
            Dock = DockStyle.Top,
            ForeColor = UiTheme.Muted,
            Padding = new Padding(0, 0, 0, UiTheme.Px(10))
        };
        Ui.FitHeight(note);

        var notesTitle = new Label
        {
            Text = "الجديد في الإصدار ده",
            Dock = DockStyle.Top,
            Font = UiTheme.Subtitle,
            Height = Measure.LineHeight(UiTheme.Subtitle) + UiTheme.Px(8),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var notes = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            BackColor = UiTheme.Background,
            BorderStyle = BorderStyle.FixedSingle,
            Text = string.IsNullOrWhiteSpace(r.Notes) ? "(مفيش ملاحظات للإصدار ده)" : Truncate(r.Notes, 5000).Replace("\r\n", "\n").Replace("\n", "\r\n")
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, UiTheme.Px(12), 0, 0),
            BackColor = Color.Transparent
        };
        var update = Ui.Button(r.SetupUrl is null ? "افتح صفحة التحميل" : "حدّث دلوقتي", true, (_, _) => { DialogResult = DialogResult.OK; Close(); });
        var later = Ui.Button("بعدين", false, (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        var skip = Ui.Button("تخطّى الإصدار ده", false, (_, _) => { DialogResult = DialogResult.Ignore; Close(); });
        buttons.Controls.AddRange([update, later, skip]);

        // ترتيب الـ Dock: آخر واحد بيتحط الأول
        Controls.Add(notes);
        Controls.Add(notesTitle);
        Controls.Add(note);
        Controls.Add(header);
        Controls.Add(buttons);

        AcceptButton = update;
        CancelButton = later;
        Shown += (_, _) => { notes.SelectionStart = 0; notes.SelectionLength = 0; update.Focus(); };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "\n…";
}
