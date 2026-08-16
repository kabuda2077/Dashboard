using System.Text;
using System.Threading.Channels;

namespace Dashboard;

internal static class HostOperationLogger
{
    private const long MaxLogFileBytes = 2 * 1024 * 1024;
    private const int RetainedArchiveCount = 3;
    private static readonly Channel<LogEntry> Entries = Channel.CreateUnbounded<LogEntry>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private static readonly HostLogFileWriter FileWriter = new(
        AppSettings.LogDirectory,
        MaxLogFileBytes,
        RetainedArchiveCount);
    private static readonly Task WriterTask = Task.Run(ProcessEntriesAsync);
    private static int _diagnosticEnabled = DefaultDiagnosticEnabled ? 1 : 0;
    private static int _shutdownStarted;

#if DEBUG
    private const bool DefaultDiagnosticEnabled = true;
#else
    private const bool DefaultDiagnosticEnabled = false;
#endif

    internal static bool IsDiagnosticEnabled => Volatile.Read(ref _diagnosticEnabled) != 0;

    public static void Configure(bool enableDiagnostic)
    {
        Volatile.Write(
            ref _diagnosticEnabled,
            DefaultDiagnosticEnabled || enableDiagnostic ? 1 : 0);
    }

    public static void Diagnostic(string category, string message)
    {
        if (IsDiagnosticEnabled)
        {
            Enqueue(category, message, null);
        }
    }

    public static void Info(string category, string message)
    {
        Enqueue(category, message, null);
    }

    public static void Error(string category, string message, Exception exception)
    {
        Enqueue(category, message, exception);
    }

    public static void Critical(string category, string message, Exception exception)
    {
        FileWriter.Write(category, FormatEntry(message, exception));
    }

    public static void Shutdown(TimeSpan timeout)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) == 0)
        {
            Entries.Writer.TryComplete();
        }

        try
        {
            WriterTask.Wait(timeout);
        }
        catch
        {
        }
    }

    private static void Enqueue(string category, string message, Exception? exception)
    {
        if (Volatile.Read(ref _shutdownStarted) != 0)
        {
            return;
        }

        Entries.Writer.TryWrite(new LogEntry(category, FormatEntry(message, exception)));
    }

    private static string FormatEntry(string message, Exception? exception)
    {
        var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        if (exception is not null)
        {
            text += $"{Environment.NewLine}{exception}";
        }

        return text + Environment.NewLine + Environment.NewLine;
    }

    private static async Task ProcessEntriesAsync()
    {
        try
        {
            await foreach (var entry in Entries.Reader.ReadAllAsync())
            {
                FileWriter.Write(entry.Category, entry.Text);
            }
        }
        catch
        {
        }
    }

    private sealed record LogEntry(string Category, string Text);
}

internal sealed class HostLogFileWriter
{
    private readonly string _directory;
    private readonly long _maxFileBytes;
    private readonly int _archiveCount;
    private readonly object _writeLock = new();

    public HostLogFileWriter(string directory, long maxFileBytes, int archiveCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(archiveCount);

        _directory = directory;
        _maxFileBytes = maxFileBytes;
        _archiveCount = archiveCount;
    }

    public void Write(string category, string text)
    {
        try
        {
            lock (_writeLock)
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, $"{Sanitize(category)}.log");
                RotateIfNeeded(path, Encoding.UTF8.GetByteCount(text));
                File.AppendAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch
        {
        }
    }

    private void RotateIfNeeded(string path, int incomingBytes)
    {
        if (!File.Exists(path) || new FileInfo(path).Length + incomingBytes <= _maxFileBytes)
        {
            return;
        }

        if (_archiveCount == 0)
        {
            File.Delete(path);
            return;
        }

        var oldestArchive = GetArchivePath(path, _archiveCount);
        if (File.Exists(oldestArchive))
        {
            File.Delete(oldestArchive);
        }

        for (var index = _archiveCount - 1; index >= 1; index--)
        {
            var source = GetArchivePath(path, index);
            if (File.Exists(source))
            {
                File.Move(source, GetArchivePath(path, index + 1));
            }
        }

        File.Move(path, GetArchivePath(path, 1));
    }

    private static string GetArchivePath(string path, int index)
    {
        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directory, $"{name}.{index}{extension}");
    }

    internal static string Sanitize(string category)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safe = new string(category.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "host" : safe;
    }
}
