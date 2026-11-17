using AjyalPcCare.Core.Common;
using AjyalPcCare.UI.Theme;

namespace AjyalPcCare.UI.Controls;

/// <summary>
/// أساس كل الصفحات: عنوان + وصف + منطقة محتوى قابلة للتمرير + أدوات مشتركة
/// (تشغيل مهمة async مع منع التكرار، تقرير التقدم، عرض الأخطاء بدون ما الأداة تقع).
/// </summary>
public abstract class PageBase : UserControl
{
    protected readonly Panel Body;
    private readonly Label _title;
    private readonly Label _subtitle;
    private CancellationTokenSource? _cts;

    public abstract string PageTitle { get; }
    public virtual string PageDescription => "";

    protected PageBase()
    {
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Background;
        Padding = new Padding(UiTheme.Px(24), UiTheme.Px(16), UiTheme.Px(24), UiTheme.Px(8));
        Font = UiTheme.Base;

        Body = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background };
        // الارتفاعات من مقاس الخط الفعلي + مسافة تحت الوصف تفصله عن أول كارت
        _subtitle = new Label
        {
            Dock = DockStyle.Top, AutoSize = false,
            Height = Measure.LineHeight(UiTheme.Base) + UiTheme.Px(18),
            Padding = new Padding(0, 0, 0, UiTheme.Px(12)),
            Font = UiTheme.Base, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.TopLeft, AutoEllipsis = true
        };
        _title = new Label
        {
            Dock = DockStyle.Top, AutoSize = false,
            Height = Measure.LineHeight(UiTheme.Title) + UiTheme.Px(6),
            Font = UiTheme.Title, ForeColor = UiTheme.Text, TextAlign = ContentAlignment.MiddleLeft
        };

        Controls.Add(Body);
        Controls.Add(_subtitle);
        Controls.Add(_title);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _title.Text = PageTitle;
        _subtitle.Text = PageDescription;
        _subtitle.Visible = PageDescription.Length > 0;
    }

    public bool IsBusy => _cts is not null;

    /// <summary>بيتنادى لما الصفحة تبقى ظاهرة.</summary>
    public virtual void OnActivated() { }

    /// <summary>بيتنادى لما المستخدم ينتقل لصفحة تانية.</summary>
    public virtual void OnDeactivated() { }

    protected void CancelRunning() => _cts?.Cancel();

    /// <summary>تشغيل عملية async بأمان: منع التكرار + إلغاء + التقاط الأخطاء.</summary>
    protected async Task RunGuardedAsync(Func<CancellationToken, Task> work, Action<bool>? setBusy = null)
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        setBusy?.Invoke(true);
        UseWaitCursor = true;
        try
        {
            await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // إلغاء من المستخدم
        }
        catch (Exception ex)
        {
            AppLogger.Error(GetType().Name, "خطأ في الواجهة", ex);
            if (!IsDisposed) Dialogs.Error(FindForm(), ex.Message);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                setBusy?.Invoke(false);
            }
        }
    }

    /// <summary>ينفّذ على الـ UI Thread بأمان (من Timers أو Tasks).</summary>
    protected void OnUi(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(a); } catch (ObjectDisposedException) { } catch (InvalidOperationException) { }
        }
        else a();
    }

    /// <summary>كروت جنب بعض بأعمدة متساوية (بتنزل تحت بعض تلقائيًا لو الشاشة ضيقة).</summary>
    protected static ColumnsPanel Columns(params Control[] items) => new(items);

    private StackPanel? _stack;

    /// <summary>
    /// يضيف عناصر للصفحة من فوق لتحت، كل عنصر بعرض الصفحة وبارتفاعه الحقيقي المقاس.
    /// الكروت ذات الارتفاع الثابت (جداول) ارتفاعها بيتحول حسب DPI.
    /// </summary>
    protected void Stack(params Control[] controls)
    {
        if (_stack is null)
        {
            _stack = new StackPanel { Dock = DockStyle.Fill };
            Body.Controls.Add(_stack);
        }

        foreach (var c in controls)
        {
            ScaleFixedHeights(c);
            c.Dock = DockStyle.None;
            _stack.Add(c);
        }
        _stack.PerformLayout();
    }

    private static void ScaleFixedHeights(Control c)
    {
        if (c is Card or LineChart) c.Height = UiTheme.Px(c.Height);
        else if (c is ColumnsPanel cp)
            foreach (Control child in cp.Controls) ScaleFixedHeights(child);
    }

    protected bool RequireAdmin()
    {
        if (AppEnvironment.IsAdministrator) return true;
        Dialogs.Info(FindForm(), "العملية دي محتاجة صلاحيات مسؤول.\nاستخدم زرار \"إعادة التشغيل كمسؤول\" اللي فوق.");
        return false;
    }
}
