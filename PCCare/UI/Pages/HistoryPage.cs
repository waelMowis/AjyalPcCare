using AjyalPcCare.Core.Common;
using AjyalPcCare.Core.Safety;
using AjyalPcCare.Core.Startup;
using AjyalPcCare.UI.Controls;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Pages;

public sealed class HistoryPage : PageBase
{
    public override string PageTitle => "السجل والتراجع";
    public override string PageDescription => "كل تغيير عملته الأداة متسجل — والتغييرات القابلة للتراجع ترجع بضغطة";

    private readonly DataGridView _grid = Ui.Grid();
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(250, 250, 252),
        Font = new Font("Consolas", 9.5f),
        RightToLeft = RightToLeft.No
    };
    private readonly Button _undo;
    private const int MaxLogLines = 400;

    public HistoryPage()
    {
        _grid.Columns.AddRange(
            Ui.TextColumn("Time", "الوقت", 70),
            Ui.TextColumn("Module", "الوحدة", 50),
            Ui.TextColumn("Action", "الإجراء", 110),
            Ui.TextColumn("Target", "الهدف", 140),
            Ui.TextColumn("Desc", "التفاصيل", 180),
            Ui.TextColumn("Rev", "قابل للتراجع", 50),
            Ui.TextColumn("State", "الحالة", 70));
        _grid.SelectionChanged += (_, _) => UpdateButtons();

        _undo = Ui.Button("تراجع عن المحدد", true, async (_, _) => await UndoAsync());
        var bar = Ui.Toolbar();
        bar.Controls.AddRange([
            _undo,
            Ui.Button("تحديث", false, (_, _) => LoadJournal()),
            Ui.Button("فتح مجلد اللوج", false, (_, _) => Ui.OpenPath(AppEnvironment.LogsDirectory)),
            Ui.Button("فتح مجلد التقارير", false, (_, _) => Ui.OpenPath(AppEnvironment.ReportsDirectory))
        ]);

        var journalCard = new Card("سجل التغييرات") { Dock = DockStyle.Fill };
        journalCard.AddContent(bar);
        journalCard.AddContent(_grid);

        var logCard = new Card("اللوج المباشر (اليوم)") { Dock = DockStyle.Fill };
        logCard.AddContent(_log);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = UiTheme.Background,
            SplitterWidth = 8
        };
        split.Panel1.Controls.Add(journalCard);
        split.Panel2.Controls.Add(logCard);
        Body.Controls.Add(split);
        Load += (_, _) =>
        {
            try { split.SplitterDistance = Math.Max(100, split.Height / 2); }
            catch (InvalidOperationException) { /* ارتفاع صغير جدًا — نسيب الافتراضي */ }
        };

        ChangeJournal.Changed += OnJournalChanged;
        AppLogger.EntryWritten += OnLog;
        Disposed += (_, _) =>
        {
            ChangeJournal.Changed -= OnJournalChanged;
            AppLogger.EntryWritten -= OnLog;
        };
    }

    public override void OnActivated()
    {
        LoadJournal();
        LoadLogFile();
    }

    private void OnJournalChanged() => OnUi(LoadJournal);

    private void LoadJournal()
    {
        _grid.Rows.Clear();
        foreach (var r in ChangeJournal.All)
        {
            int i = _grid.Rows.Add(r.Time.ToString("yyyy-MM-dd HH:mm"), r.Module, r.Action, r.Target, r.Description,
                r.Reversible ? "نعم" : "لا", r.Undone ? $"تم التراجع {r.UndoneAt:MM-dd HH:mm}" : "مطبّق");
            _grid.Rows[i].Tag = r;
            if (r.Undone) _grid.Rows[i].DefaultCellStyle.ForeColor = UiTheme.Muted;
        }
        UpdateButtons();
    }

    private void UpdateButtons() =>
        _undo.Enabled = !IsBusy && _grid.CurrentRow?.Tag is ChangeRecord { Reversible: true, Undone: false };

    private async Task UndoAsync()
    {
        if (_grid.CurrentRow?.Tag is not ChangeRecord r) return;
        if (!Dialogs.Confirm(FindForm(), $"التراجع عن:\n{r.Action}\n{r.Target}\n\nبتاريخ {r.Time:yyyy-MM-dd HH:mm}؟")) return;
        await RunGuardedAsync(async ct =>
        {
            var res = await StartupManager.UndoAsync(r, ct);
            if (!res.Success) Dialogs.Error(FindForm(), res.Message);
            LoadJournal();
        }, _ => UpdateButtons());
    }

    private void LoadLogFile()
    {
        try
        {
            string path = AppLogger.CurrentLogFile;
            if (!File.Exists(path)) { _log.Clear(); return; }
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            var lines = sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            _log.Text = string.Join(Environment.NewLine, lines.TakeLast(MaxLogLines).Select(l => l.TrimEnd('\r')));
            ScrollToEnd();
        }
        catch (Exception ex)
        {
            _log.Text = "تعذر قراءة اللوج: " + ex.Message;
        }
    }

    private void OnLog(LogEntry e)
    {
        if (e.Level < AppLogger.MinimumLevel) return;
        OnUi(() =>
        {
            if (!Visible) return;
            _log.AppendText($"{Environment.NewLine}{e.Time:HH:mm:ss} [{e.Level}] [{e.Module}] {e.Message}");
            if (_log.Lines.Length > MaxLogLines * 2) LoadLogFile();
        });
    }

    private void ScrollToEnd()
    {
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}
