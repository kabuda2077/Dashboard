using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace Dashboard.Tests;

internal static class TestBrowser
{
    // Capture only browser processes belonging to these test controls. Never
    // enumerate/kill the user's WebView processes. Call while the STA pump lives.
    public static async Task DisposeAsync(Control owner)
    {
        var views = FindViews(owner).ToArray();
        var processes = new Dictionary<int, Process>();
        try
        {
            foreach (var view in views)
            {
                if (view.CoreWebView2 is not { } core) continue;
                var id = checked((int)core.BrowserProcessId);
                if (processes.ContainsKey(id)) continue;
                try
                {
                    var process = Process.GetProcessById(id);
                    _ = process.Handle; // hold the original process handle, not a reusable PID
                    processes.Add(id, process);
                }
                catch (ArgumentException) { } // already exited
            }
            foreach (var view in views) view.Dispose();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await Task.WhenAll(processes.Values.Select(process => process.WaitForExitAsync(timeout.Token)));
        }
        catch (Exception error)
        {
            var report = Path.Combine(TestDirectory.ReportDirectory("cleanup"), "browser-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(report, JsonSerializer.Serialize(new
            {
                recordedAt = DateTimeOffset.UtcNow, browserProcessIds = processes.Keys.ToArray(),
                error = error.ToString(), action = "No process was forcibly terminated."
            }, new JsonSerializerOptions { WriteIndented = true }));
            throw new IOException("Test WebView shutdown failed. Details: " + report, error);
        }
        finally { foreach (var process in processes.Values) process.Dispose(); }
    }

    private static IEnumerable<WebView2> FindViews(Control owner)
    {
        if (owner is WebView2 view) yield return view;
        foreach (Control child in owner.Controls)
            foreach (var nested in FindViews(child)) yield return nested;
    }
}
