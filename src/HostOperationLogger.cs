namespace Dashboard;

internal static class HostOperationLogger
{
    public static void Info(string category, string message)
    {
        Write(category, message, null);
    }

    public static void Error(string category, string message, Exception exception)
    {
        Write(category, message, exception);
    }

    private static void Write(string category, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.LogDirectory);
            var path = Path.Combine(AppSettings.LogDirectory, $"{Sanitize(category)}.log");
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            if (exception is not null)
            {
                text += $"{Environment.NewLine}{exception}";
            }

            File.AppendAllText(path, text + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static string Sanitize(string category)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safe = new string(category.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "host" : safe;
    }
}
