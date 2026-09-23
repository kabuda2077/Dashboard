using System.Net;

namespace Dashboard.Tests;

public sealed class CoreUpgradeSupportTests
{
    [Fact]
    public async Task RetriesTransientReleaseMetadataFailure()
    {
        var handler = new RetryHandler();
        using var client = new HttpClient(handler);

        using var document = await CoreUpgradeSupport.GetReleaseJsonAsync(
            client,
            "https://api.github.com/repos/example/core/releases/latest",
            CancellationToken.None);

        Assert.Equal("v1.2.3", document.RootElement.GetProperty("tag_name").GetString());
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public void ReplacementCommitsCompleteCandidateAndRetainsBackup()
    {
        using var files = new UpgradeFiles();
        CoreUpgradeSupport.ReplaceCoreWithRollback(files.Candidate, files.Core, files.Backup);
        Assert.Equal("new executable", File.ReadAllText(files.Core));
        Assert.Equal("retained backup", File.ReadAllText(files.Backup));
        Assert.Empty(Directory.GetFiles(files.Root, "*.tmp*"));
    }

    [Fact]
    public void MissingCandidateLeavesInstalledCoreUntouched()
    {
        using var files = new UpgradeFiles();
        File.Delete(files.Candidate);
        Assert.Throws<FileNotFoundException>(() =>
            CoreUpgradeSupport.ReplaceCoreWithRollback(files.Candidate, files.Core, files.Backup));
        Assert.Equal("installed executable", File.ReadAllText(files.Core));
        Assert.Empty(Directory.GetFiles(files.Root, "*.tmp*"));
    }

    [Fact]
    public void LockedExecutableRejectsReplacementWithoutTruncatingIt()
    {
        using var files = new UpgradeFiles();
        using (var locked = File.Open(files.Core, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() =>
                CoreUpgradeSupport.ReplaceCoreWithRollback(files.Candidate, files.Core, files.Backup));
        }
        Assert.Equal("installed executable", File.ReadAllText(files.Core));
        Assert.Empty(Directory.GetFiles(files.Root, "*.tmp*"));
    }

    [Fact]
    public void FailureAfterCommitRestoresExactOriginalInsteadOfOlderBackup()
    {
        using var files = new UpgradeFiles();
        Assert.Throws<IOException>(() => CoreUpgradeSupport.ReplaceCoreWithRollback(
            files.Candidate, files.Core, files.Backup, (source, destination, backup) =>
            {
                Assert.Equal("new executable", File.ReadAllText(source));
                File.Replace(source, destination, backup);
                throw new IOException("commit failure after backup move");
            }));
        Assert.Equal("installed executable", File.ReadAllText(files.Core));
        Assert.Equal("retained backup", File.ReadAllText(files.Backup));
        Assert.Empty(Directory.GetFiles(files.Root, "*.tmp*"));
    }

    [Fact]
    public void FailedRollbackRetainsRecoveryFilesAndReportsBothErrors()
    {
        using var files = new UpgradeFiles();
        FileStream? locked = null;
        try
        {
            var error = Assert.Throws<IOException>(() => CoreUpgradeSupport.ReplaceCoreWithRollback(
                files.Candidate, files.Core, files.Backup, (source, destination, backup) =>
                {
                    File.Replace(source, destination, backup);
                    locked = File.Open(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
                    throw new IOException("commit failure");
                }));
            Assert.IsType<AggregateException>(error.InnerException);
            var recovery = Assert.Single(Directory.GetFiles(files.Root, "*.rollback"));
            Assert.Equal("installed executable", File.ReadAllText(recovery));
            Assert.Equal("retained backup", File.ReadAllText(files.Backup));
            Assert.Contains(recovery, error.Message);
        }
        finally { locked?.Dispose(); }
    }

    private sealed class UpgradeFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Dashboard.UpgradeTests", Guid.NewGuid().ToString("N"));
        public string Core => Path.Combine(Root, "core.exe");
        public string Candidate => Path.Combine(Root, "candidate.exe");
        public string Backup => Path.Combine(Root, "core.bak");

        public UpgradeFiles()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Core, "installed executable");
            File.WriteAllText(Candidate, "new executable");
            File.WriteAllText(Backup, "retained backup");
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class RetryHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (RequestCount == 1)
            {
                return Task.FromException<HttpResponseMessage>(
                    new HttpRequestException("response ended prematurely"));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v1.2.3"}""")
            });
        }
    }
}
