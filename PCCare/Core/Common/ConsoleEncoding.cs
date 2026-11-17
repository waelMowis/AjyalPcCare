using System.Text;
using AjyalPcCare.Native;

namespace AjyalPcCare.Core.Common;

/// <summary>ترميز مخرجات أدوات الكونسول القديمة (OEM) — مثلًا 720 على ويندوز عربي.</summary>
public static class ConsoleEncoding
{
    private static readonly Lazy<Encoding> _oem = new(() =>
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(SystemTimesApi.OemCodePage);
        }
        catch
        {
            return Encoding.UTF8;
        }
    });

    public static Encoding Oem => _oem.Value;
}
