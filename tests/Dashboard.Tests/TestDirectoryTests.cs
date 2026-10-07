using System.Diagnostics;

namespace Dashboard.Tests;

public sealed class TestDirectoryTests
{
    [Fact]
    public void ScopesAreUniqueProjectLocalAndDisposedIndependently()
    {
        using var first = new TestDirectory("same-category");
        using var second = new TestDirectory("same-category");
        Assert.NotEqual(first.Path, second.Path);
        Assert.StartsWith(Path.Combine(TestDirectory.RepositoryRoot, ".tmp", "tests") + Path.DirectorySeparatorChar, first.Path);
        File.WriteAllText(Path.Combine(first.Path, "file"), "one");
        File.WriteAllText(Path.Combine(second.Path, "file"), "two");
        first.Dispose();
        Assert.False(Directory.Exists(first.Path));
        Assert.Equal("two", File.ReadAllText(Path.Combine(second.Path, "file")));
    }

    [Fact]
    public async Task CleanupRetriesTransientFileLocks()
    {
        await using var directory = new TestDirectory();
        var file = Path.Combine(directory.Path, "locked");
        await File.WriteAllTextAsync(file, "test");
        using var locked = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = Task.Run(async () => { await Task.Delay(150); locked.Dispose(); });
        await directory.DisposeAsync();
        await release;
        Assert.False(Directory.Exists(directory.Path));
        Assert.False(File.Exists(directory.CleanupFailureReport));
    }

    [Fact]
    public void PersistentFailureReportsItsPathAndCanBeRetried()
    {
        using var directory = new TestDirectory();
        var file = Path.Combine(directory.Path, "locked");
        File.WriteAllText(file, "test");
        using (var locked = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Assert.Throws<IOException>(directory.Dispose);
            Assert.Contains(directory.Path, error.Message);
            Assert.True(File.Exists(directory.CleanupFailureReport));
            Assert.True(Directory.Exists(directory.Path));
        }
        directory.Dispose();
        Assert.False(Directory.Exists(directory.Path));
        // Keep this deliberately induced error apart from unexpected cleanup failures.
        File.Move(directory.CleanupFailureReport,
            Path.Combine(TestDirectory.ReportDirectory("test-directory"), "expected-cleanup-failure.json"), overwrite: true);
    }

    [Fact]
    public void DirectoryLinksAreRemovedWithoutFollowingTheirTargets()
    {
        using var outside = new TestDirectory();
        using var directory = new TestDirectory();
        var retained = Path.Combine(outside.Path, "keep");
        File.WriteAllText(retained, "outside");
        var link = Path.Combine(directory.Path, "link");
        using var command = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{link}\" \"{outside.Path}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
        Assert.NotNull(command);
        Assert.True(command.WaitForExit(5000));
        Assert.Equal(0, command.ExitCode);
        directory.Dispose();
        Assert.False(Directory.Exists(directory.Path));
        Assert.Equal("outside", File.ReadAllText(retained));
    }
}
