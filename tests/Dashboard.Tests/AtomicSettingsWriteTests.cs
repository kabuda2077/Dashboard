namespace Dashboard.Tests;

public sealed class AtomicSettingsWriteTests
{
    [Fact]
    public void ReplacesCompleteFileAndLeavesNoTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Dashboard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            AppSettings.WriteSettingsAtomically(path, "{\"old\":true}");
            AppSettings.WriteSettingsAtomically(path, "{\"new\":true}");
            Assert.Equal("{\"new\":true}", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FailedReplacementPreservesPreviousFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Dashboard.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "original");
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var error = Record.Exception(() => AppSettings.WriteSettingsAtomically(path, "replacement"));
                Assert.True(error is IOException or UnauthorizedAccessException, $"Unexpected error: {error}");
            }
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
