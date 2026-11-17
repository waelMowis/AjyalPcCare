using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AjyalPcCare.Core.Common;

public sealed record ProcessOutput(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public bool Success => ExitCode == 0 && !TimedOut;
}

/// <summary>تشغيل أدوات النظام (dism / powercfg / sc / powershell) مع إلغاء ومهلة وقراءة سطر بسطر.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessOutput> RunAsync(
        string fileName,
        string arguments,
        CancellationToken ct,
        Action<string>? onLine = null,
        TimeSpan? timeout = null,
        Encoding? outputEncoding = null)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = outputEncoding ?? Encoding.UTF8,
            StandardErrorEncoding = outputEncoding ?? Encoding.UTF8,
            WorkingDirectory = Environment.SystemDirectory
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdout) stdout.AppendLine(e.Data);
            try { onLine?.Invoke(e.Data); } catch { /* ignore */ }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderr) stderr.AppendLine(e.Data);
        };

        try
        {
            if (!proc.Start()) return new ProcessOutput(-1, "", "تعذر تشغيل " + fileName, false);
        }
        catch (Exception ex)
        {
            return new ProcessOutput(-1, "", ex.Message, false);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        bool timedOut = false;
        try
        {
            await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts.IsCancellationRequested;
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            if (!timedOut) throw;
        }

        return new ProcessOutput(timedOut ? -2 : proc.ExitCode, stdout.ToString(), stderr.ToString(), timedOut);
    }
}

/// <summary>
/// تشغيل سكريبتات Windows PowerShell 5.1 (موجود على كل ويندوز 10/11) عن طريق EncodedCommand
/// عشان نتفادى مشاكل الـ quoting، والمخرجات UTF-8 عشان الأسماء العربي.
/// </summary>
public static class PowerShell
{
    private static string Exe => Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    public static Task<ProcessOutput> RunAsync(string script, CancellationToken ct, TimeSpan? timeout = null, Action<string>? onLine = null)
    {
        string full = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $ProgressPreference = 'SilentlyContinue'; " + script;
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return ProcessRunner.RunAsync(Exe, $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", ct, onLine, timeout);
    }

    /// <summary>يشغّل السكريبت ويحوّل الناتج JSON. بيتعامل مع إن ConvertTo-Json بيرجع Object مش Array لو عنصر واحد.</summary>
    public static async Task<List<T>> RunJsonListAsync<T>(string script, CancellationToken ct, TimeSpan? timeout = null)
    {
        var output = await RunAsync($"@({script}) | ConvertTo-Json -Depth 5 -Compress", ct, timeout).ConfigureAwait(false);
        if (!output.Success && string.IsNullOrWhiteSpace(output.StdOut))
        {
            if (!string.IsNullOrWhiteSpace(output.StdErr)) AppLogger.Debug("PowerShell", output.StdErr.Trim());
            return [];
        }

        string json = output.StdOut.Trim();
        if (json.Length == 0) return [];
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };
        try
        {
            if (json.StartsWith('['))
                return JsonSerializer.Deserialize<List<T>>(json, opts) ?? [];
            var single = JsonSerializer.Deserialize<T>(json, opts);
            return single is null ? [] : [single];
        }
        catch (JsonException ex)
        {
            AppLogger.Warn("PowerShell", "تعذر قراءة JSON: " + ex.Message);
            return [];
        }
    }

    /// <summary>تهريب نص لاستخدامه جوه single quotes في PowerShell.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
