namespace Dashboard.Tests;

public sealed class HostLogFileWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Dashboard.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void RotatesLogAndRetainsConfiguredArchives()
    {
        var writer = new HostLogFileWriter(_directory, maxFileBytes: 10, archiveCount: 2);

        writer.Write("performance", "12345678");
        writer.Write("performance", "abcdefgh");
        writer.Write("performance", "ABCDEFGH");
        writer.Write("performance", "87654321");

        Assert.Equal("87654321", File.ReadAllText(Path.Combine(_directory, "performance.log")));
        Assert.Equal("ABCDEFGH", File.ReadAllText(Path.Combine(_directory, "performance.1.log")));
        Assert.Equal("abcdefgh", File.ReadAllText(Path.Combine(_directory, "performance.2.log")));
        Assert.DoesNotContain("12345678", Directory.GetFiles(_directory).Select(File.ReadAllText));
    }

    [Fact]
    public void SanitizesCategoryBeforeCreatingFile()
    {
        var writer = new HostLogFileWriter(_directory, maxFileBytes: 1024, archiveCount: 1);
        var invalidCharacter = Path.GetInvalidFileNameChars()[0];

        writer.Write($"host{invalidCharacter}bridge", "entry");

        Assert.True(File.Exists(Path.Combine(_directory, "host-bridge.log")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
