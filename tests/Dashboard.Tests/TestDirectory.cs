using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Dashboard.Tests;

// Test-owned files only. Runtime code deliberately continues to use system TEMP.
internal sealed class TestDirectory : IDisposable, IAsyncDisposable
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];
    private static readonly string RunRoot = System.IO.Path.Combine(RepositoryRoot, ".tmp", "tests", RunId);
    private bool _disposed;
    public string Path { get; }
    public string CleanupFailureReport { get; }

    public TestDirectory([CallerMemberName] string category = "test")
    {
        var name = new string(category.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').Take(24).ToArray());
        var id = name + "-" + Guid.NewGuid().ToString("N");
        Path = System.IO.Path.Combine(RunRoot, id);
        CleanupFailureReport = System.IO.Path.Combine(RepositoryRoot, ".tmp", "reports", "cleanup", id + ".json");
        Directory.CreateDirectory(Path);
    }

    public static string ReportDirectory(string category)
    {
        if (string.IsNullOrWhiteSpace(category) || category.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0
            || category is "." or "..") throw new ArgumentException("Use one report-directory name.", nameof(category));
        var directory = System.IO.Path.Combine(RepositoryRoot, ".tmp", "reports", category);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        Exception? last = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                DeleteWithoutFollowingLinks(Path);
                _disposed = true;
                // Empty per-run parents are best-effort: another test may be creating a child.
                try { if (Directory.Exists(RunRoot)) Directory.Delete(RunRoot, recursive: false); }
                catch (IOException) { }
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                last = error;
                if (attempt < 19) await Task.Delay(100).ConfigureAwait(false);
            }
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CleanupFailureReport)!);
        File.WriteAllText(CleanupFailureReport, JsonSerializer.Serialize(new
        {
            path = Path, recordedAt = DateTimeOffset.UtcNow, processId = Environment.ProcessId,
            error = last!.ToString(), action = "Directory retained; release its owner before retrying cleanup."
        }, new JsonSerializerOptions { WriteIndented = true }));
        throw new IOException($"Test cleanup failed: {Path}. Details: {CleanupFailureReport}", last);
    }

    // A test can contain junctions deliberately. Remove links themselves, never
    // recursively follow them into another test, the repository, or user data.
    private static void DeleteWithoutFollowingLinks(string path)
    {
        if (!System.IO.Path.Exists(path)) return;
        var info = new DirectoryInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) { info.Delete(); return; }
        foreach (var entry in info.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.Directory) != 0)
                DeleteWithoutFollowingLinks(entry.FullName);
            else
                entry.Delete();
        }
        info.Delete();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "Dashboard.csproj"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate Dashboard.csproj for isolated test storage.");
    }
}

// xUnit disposes each test instance even when its body fails before a local
// finally block. All directories in a test stay under this unique owned scope.
public abstract class TemporaryDirectoryTest : IDisposable
{
    private readonly TestDirectory _directory;
    protected TemporaryDirectoryTest() => _directory = new TestDirectory(GetType().Name);
    protected string TestRoot => _directory.Path;
    protected void CleanupTestDirectory() => Dispose();
    public virtual void Dispose() => _directory.Dispose();
}
