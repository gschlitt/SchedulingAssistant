using System.Diagnostics;
using System.Text;

namespace NetworkProbe;

/// <summary>
/// Reads this computer's SMB client settings (<c>Get-SmbClientConfiguration</c>), because the file-not-found and
/// file-information cache lifetimes are exactly what the share-semantics experiments (<c>--share-semantics</c>) depend on.
/// Both the runner and the partner call it, and both record the text in the report.
/// </summary>
internal static class SmbClientConfig
{
    /// <summary>How long to wait for PowerShell before giving up.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    /// <summary>The PowerShell script: the configuration as a plain list, wide enough not to wrap.</summary>
    private const string Script = "Get-SmbClientConfiguration | Select-Object -Property * -ExcludeProperty CimClass,CimInstanceProperties,CimSystemProperties,PSComputerName | Format-List | Out-String -Width 200";

    /// <summary>
    /// Runs <c>powershell -NoProfile</c> with the script above.
    /// </summary>
    /// <returns>
    /// <c>Ok</c> true with the text on success; false with a plain-words reason (PowerShell missing, non-zero exit, or
    /// timeout) in <c>Text</c>. Never throws.
    /// </returns>
    public static async Task<(bool Ok, string Text)> ReadAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(Script);

            using var process = Process.Start(psi);
            if (process is null) return (false, "powershell.exe could not be started.");

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(Timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Kill only the process this method started (by handle, never by image name).
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return (false, $"powershell.exe did not finish within {Timeout.TotalSeconds:F0} s and was stopped.");
            }

            var text = (await stdout.ConfigureAwait(false)).Trim();
            var err = (await stderr.ConfigureAwait(false)).Trim();
            if (process.ExitCode != 0 || text.Length == 0)
                return (false, $"powershell.exe exit code {process.ExitCode}: {(err.Length > 0 ? err : "no output")}");
            return (true, text);
        }
        catch (Exception ex)
        {
            return (false, $"Could not run powershell.exe ({ex.GetType().Name}: {ex.Message}).");
        }
    }
}
