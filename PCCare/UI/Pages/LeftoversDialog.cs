using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Uninstall;
using AjyalPcCare.UI.Controls;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Pages;

/// <summary>
/// مراجعة البقايا قبل المسح: "مؤكد" متعلّم تلقائي، "محتمل" من غير علامة.
/// OK = امسح المختار، Cancel = سيب كل حاجة زي ما هي.
/// </summary>
public sealed class LeftoversDialog : Form
{
    private readonly List<Leftover> _items;
    private readonly DataGridView _grid = Ui.Grid(readOnly: false);
    private readonly Label _summary = new() { Dock = DockStyle.Top, Font = UiTheme.Bold, ForeColor = UiTheme.Text };
    private readonly Button _remove;

    public LeftoversDialog(InstalledProgram program, List<Leftover> items, string uninstallMessage)
    {
        _items = items;
        Text = $"بقايا {program.Name}";
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = UiTheme.Card;
        Font = UiTheme.Base;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(UiTheme.Px(1000), UiTheme.Px(600));
        MinimumSize = new Size(UiTheme.Px(700), UiTheme.Px(420));
        Padding = new Padding(UiTheme.Px(18), UiTheme.Px(14), UiTheme.Px(18), UiTheme.Px(14));

        var header = new Label
        {
            Text = items.Count == 0 ? "مفيش بقايا ✓" : $"لقيت {items.Count} عنصر من بقايا \"{program.Name}\"",
            Dock = DockStyle.Top,
            Font = UiTheme.Title,
            ForeColor = items.Count == 0 ? UiTheme.Success : UiTheme.Brand,
            Height = Measure.LineHeight(UiTheme.Title) + UiTheme.Px(6),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var note = new Label
        {
            Text = uninstallMessage + "\n" +
                   "• \"مؤكد\" (متعلّم): جوه مكان تثبيت البرنامج أو اسمه مطابق بالظبط.  • \"محتمل\" (مش متعلّم): راجعه بنفسك — خصوصًا بيانات المستخدم.\n" +
                   "• الفولدرات بتروح سلة المحذوفات، والريجستري والخدمات والمهام بيتاخدلها نسخة احتياطية وتترجع من صفحة \"السجل والتراجع\".",
            Dock = DockStyle.Top,
            ForeColor = UiTheme.Muted,
            Padding = new Padding(0, 0, 0, UiTheme.Px(8))
        };
        Ui.FitHeight(note);
        _summary.Height = Measure.LineHeight(UiTheme.Bold) + UiTheme.Px(8);
        _summary.TextAlign = ContentAlignment.MiddleLeft;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Sel", HeaderText = "مسح", FillWeight = 30 });
        _grid.Columns.AddRange(
            Ui.TextColumn("Conf", "الثقة", 45),
            Ui.TextColumn("Kind", "النوع", 65),
            Ui.TextColumn("Path", "المسار", 330),
            Ui.TextColumn("Size", "الحجم", 55),
            Ui.TextColumn("Reason", "السبب", 200, wrap: true));
        _grid.Columns["Path"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        foreach (var it in items)
        {
            int i = _grid.Rows.Add(it.Selected, it.ConfidenceDisplay, it.KindDisplay, it.Path, it.SizeBytes > 0 ? Format.Bytes(it.SizeBytes) : "", it.Reason);
            var row = _grid.Rows[i];
            row.Tag = it;
            row.Cells["Conf"].Style.ForeColor = it.Confidence == LeftoverConfidence.High ? UiTheme.Success : UiTheme.Warning;
        }
        _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 0 && _grid.Rows[e.RowIndex].Tag is Leftover l)
            {
                l.Selected = (bool)(_grid.Rows[e.RowIndex].Cells[0].Value ?? false);
                UpdateSummary();
            }
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, UiTheme.Px(10), 0, 0), BackColor = Color.Transparent };
        _remove = Ui.DangerButton("امسح المختار", (_, _) => Confirm());
        var selectAll = Ui.Button("علّم الكل", false, (_, _) => SetAll(true));
        var selectNone = Ui.Button("شيل العلامات", false, (_, _) => SetAll(false));
        var skip = Ui.Button(items.Count == 0 ? "تمام" : "سيبهم وخلّص", false, (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        buttons.Controls.AddRange([_remove, selectAll, selectNone, skip]);
        if (items.Count == 0) _remove.Visible = selectAll.Visible = selectNone.Visible = false;

        Controls.Add(_grid);
        Controls.Add(_summary);
        Controls.Add(note);
        Controls.Add(header);
        Controls.Add(buttons);
        CancelButton = skip;
        UpdateSummary();
    }

    private void SetAll(bool value)
    {
        foreach (DataGridViewRow r in _grid.Rows)
        {
            r.Cells[0].Value = value;
            if (r.Tag is Leftover l) l.Selected = value;
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var sel = _items.Where(i => i.Selected).ToList();
        _summary.Text = $"المختار: {sel.Count} من {_items.Count} — حجم الملفات: {Format.Bytes(sel.Sum(i => i.SizeBytes))}";
        _remove.Enabled = sel.Count > 0;
    }

    private void Confirm()
    {
        var sel = _items.Where(i => i.Selected).ToList();
        int medium = sel.Count(i => i.Confidence == LeftoverConfidence.Medium);
        string warn = medium > 0 ? $"\n\n⚠ منهم {medium} عنصر \"محتمل\" — اتأكد إنهم مش تبع برنامج تاني أو بيانات محتاجها." : "";
        if (!Dialogs.Confirm(this, $"مسح {sel.Count} عنصر؟{warn}\n\nالفولدرات ← سلة المحذوفات، والريجستري ← نسخة احتياطية.", "تأكيد", medium > 0)) return;
        DialogResult = DialogResult.OK;
        Close();
    }
}
