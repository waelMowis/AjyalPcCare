using System.Drawing.Text;
using AjyalPcCare.Core.Common;
using AjyalPcCare.Native;

namespace AjyalPcCare.UI.Theme;

/// <summary>
/// ألوان وخطوط الواجهة. الخط الرسمي للمدارس Readex Pro:
/// 1) لو متسطب على الجهاز → يستخدم مباشرة.
/// 2) لو فيه ملفات .ttf في مجلد Fonts جنب الـ exe → يتحمل للبروسيس بس (من غير تسطيب).
/// 3) غير كده → Segoe UI.
/// </summary>
public static class UiTheme
{
    public static readonly Color Brand = Color.FromArgb(15, 92, 77);
    public static readonly Color BrandDark = Color.FromArgb(10, 66, 55);
    public static readonly Color BrandLight = Color.FromArgb(226, 241, 237);
    public static readonly Color Background = Color.FromArgb(245, 247, 250);
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(228, 231, 236);
    public static readonly Color Text = Color.FromArgb(29, 37, 51);
    public static readonly Color Muted = Color.FromArgb(102, 112, 133);
    public static readonly Color Danger = Color.FromArgb(197, 48, 48);
    public static readonly Color DangerLight = Color.FromArgb(253, 236, 236);
    public static readonly Color Warning = Color.FromArgb(183, 121, 31);
    public static readonly Color WarningLight = Color.FromArgb(255, 247, 230);
    public static readonly Color Success = Color.FromArgb(26, 127, 55);
    public static readonly Color Info = Color.FromArgb(47, 111, 223);
    public static readonly Color ChartCpu = Color.FromArgb(47, 111, 223);
    public static readonly Color ChartMemory = Color.FromArgb(15, 92, 77);
    public static readonly Color ChartDisk = Color.FromArgb(183, 121, 31);
    public static readonly Color ChartNet = Color.FromArgb(128, 90, 213);

    private static FontFamily? _family;
    private static PrivateFontCollection? _private;
    private static readonly Dictionary<(float, FontStyle), Font> Cache = new();

    public static float BaseSize { get; private set; } = 10f;
    public static string FontName => _family?.Name ?? "Segoe UI";
    public static bool UsingBrandFont { get; private set; }

    public static void Initialize(UiSettings s)
    {
        BaseSize = s.FontSize <= 6 ? 10f : s.FontSize;
        try
        {
            using var installed = new InstalledFontCollection();
            var match = installed.Families.FirstOrDefault(f => f.Name.Equals(s.FontName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _family = new FontFamily(match.Name);
                UsingBrandFont = true;
            }
            else
            {
                string dir = Path.Combine(AppContext.BaseDirectory, "Fonts");
                if (Directory.Exists(dir))
                {
                    var files = Directory.GetFiles(dir, "*.ttf").Concat(Directory.GetFiles(dir, "*.otf")).ToArray();
                    if (files.Length > 0)
                    {
                        _private = new PrivateFontCollection();
                        foreach (var f in files)
                        {
                            FontApi.LoadPrivate(f);
                            _private.AddFontFile(f);
                        }
                        _family = _private.Families.FirstOrDefault(f => f.Name.Equals(s.FontName, StringComparison.OrdinalIgnoreCase))
                                  ?? _private.Families.FirstOrDefault();
                        UsingBrandFont = _family is not null;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UI", "تعذر تحميل الخط: " + ex.Message);
        }

        if (_family is null)
        {
            try { _family = new FontFamily(s.FallbackFontName); }
            catch { _family = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif; }
        }
        AppLogger.Info("UI", $"الخط المستخدم: {FontName}{(UsingBrandFont ? "" : " (بديل — Readex Pro غير متاح)")}");
    }

    public static Font Font(float? size = null, FontStyle style = FontStyle.Regular)
    {
        float sz = size ?? BaseSize;
        lock (Cache)
        {
            if (Cache.TryGetValue((sz, style), out var f)) return f;
            var family = _family ?? FontFamily.GenericSansSerif;
            // الخطوط المتغيرة (Variable) أحيانًا ما بتعلنش عن Bold — نرجع للعادي بدل Exception
            var st = family.IsStyleAvailable(style) ? style : FontStyle.Regular;
            f = new Font(family, sz, st, GraphicsUnit.Point);
            Cache[(sz, style)] = f;
            return f;
        }
    }

    // سلّم خطوط متدرج (بدل القفزات الكبيرة القديمة +22 / +8):
    //   Small 9  ← الجداول والملاحظات      Base 10 ← النص العادي
    //   Subtitle 11.5 Bold ← عناوين الكروت   Title 15 Bold ← عنوان الصفحة
    //   Metric 17 Bold ← أرقام كروت المراقبة   Huge 26 Bold ← مؤشر الصحة فقط
    public static Font Base => Font();
    public static Font Bold => Font(null, FontStyle.Bold);
    public static Font Small => Font(BaseSize - 1);
    public static Font SmallBold => Font(BaseSize - 1, FontStyle.Bold);
    public static Font Subtitle => Font(BaseSize + 1.5f, FontStyle.Bold);
    public static Font Title => Font(BaseSize + 5, FontStyle.Bold);
    public static Font Metric => Font(BaseSize + 7, FontStyle.Bold);
    public static Font Large => Metric;
    public static Font Huge => Font(BaseSize + 16, FontStyle.Bold);
    public static Font Nav => Font(BaseSize + 0.5f);
    public static Font NavActive => Font(BaseSize + 0.5f, FontStyle.Bold);

    private static readonly Lazy<float> _dpiScale = new(() =>
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiY / 96f;
        }
        catch
        {
            return 1f;
        }
    });

    /// <summary>
    /// تحويل بكسل مصمم على 96 DPI لبكسل فعلي (125% / 150% ...).
    /// الخطوط بالـ Point بتتظبط لوحدها، لكن الارتفاعات الثابتة لازم تتضرب في النسبة.
    /// </summary>
    public static int Px(int designPixels) => (int)Math.Round(designPixels * _dpiScale.Value);

    public static Color ScoreColor(int score) => score switch
    {
        >= 85 => Success,
        >= 70 => Info,
        >= 50 => Warning,
        _ => Danger
    };

    public static Color SeverityColor(Severity s) => s switch
    {
        Severity.Critical => Danger,
        Severity.Warning => Warning,
        _ => Info
    };
}
