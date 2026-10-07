namespace Dashboard.Tests;

public sealed class AtomicSettingsWriteTests : TemporaryDirectoryTest
{
    [Fact]
    public void ReplacesCompleteFileAndLeavesNoTemporaryFiles()
    {
        var path = Path.Combine(TestRoot, "settings.json");
        SettingsStore.WriteAtomically(path, "{\"old\":true}");
        SettingsStore.WriteAtomically(path, "{\"new\":true}");
        Assert.Equal("{\"new\":true}", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(TestRoot));
    }

    [Fact]
    public void FailedReplacementPreservesPreviousFile()
    {
        var path = Path.Combine(TestRoot, "settings.json");
        File.WriteAllText(path, "original");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => SettingsStore.WriteAtomically(path, "replacement"));
            Assert.True(error is IOException or UnauthorizedAccessException, $"Unexpected error: {error}");
        }
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(TestRoot));
    }
}
