using AjyalPcCare.Core.Common;

namespace AjyalPcCare.UI;

/// <summary>
/// فحص إن البرنامج بيفتح من غير ما يقع: بيبني الشباك الرئيسي، ويفتح كل الصفحات واحدة واحدة، وبعدين يقفل.
/// بيتشغّل في GitHub Actions قبل أي نشر:  PCCare.exe --smoke-test
/// أي Exception أو أي رسالة خطأ كانت هتظهر للمستخدم = الفحص يفشل والإصدار مايتنشرش.
/// </summary>
internal static class SmokeTest
{
    public static bool Active { get; private set; }

    private static readonly List<string> Failures = [];

    /// <summary>رسالة خطأ كانت هتظهر للمستخدم — في الفحص بتتسجل كفشل بدل ما تفتح نافذة وتعلّق الفحص.</summary>
    public static void Fail(string message)
    {
        lock (Failures) Failures.Add(message);
        Console.Error.WriteLine("SMOKE ERROR: " + message);
    }

    public static int Run()
    {
        Active = true;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => { Fail(e.Exception.ToString()); Application.ExitThread(); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine("SMOKE FAIL (unhandled): " + e.ExceptionObject);
            Environment.Exit(1);
        };

        try
        {
            using var form = new MainForm();
            form.Shown += (_, _) =>
            {
                var t = new System.Windows.Forms.Timer { Interval = 1500 };
                t.Tick += (_, _) =>
                {
                    t.Stop();
                    try
                    {
                        form.RunSmokeChecks();
                    }
                    catch (Exception ex)
                    {
                        Fail(ex.ToString());
                    }
                    form.Close();
                };
                t.Start();
            };
            Application.Run(form);
        }
        catch (Exception ex)
        {
            Fail(ex.ToString());
        }

        if (Failures.Count > 0)
        {
            Console.Error.WriteLine($"SMOKE FAIL: {Failures.Count} خطأ");
            AppLogger.Error("Smoke", string.Join(" || ", Failures));
            return 1;
        }
        Console.WriteLine("SMOKE OK");
        AppLogger.Info("Smoke", "فحص الفتح نجح");
        return 0;
    }
}
