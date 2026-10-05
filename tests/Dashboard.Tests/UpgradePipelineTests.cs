using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dashboard.Tests;

public sealed class UpgradePipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Dashboard.UpgradePipeline", Guid.NewGuid().ToString("N"));
    private sealed class Handler(string release, byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.Host == "api.github.com" ? new StringContent(release) : new ByteArrayContent(archive) });
    }
    private (string Core, byte[] Zip) Prepare()
    {
        Directory.CreateDirectory(_root);
        var core = Path.Combine(_root, "sing-box.exe");
        File.WriteAllText(core, "old-core");
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry("release/sing-box.exe").Open())) writer.Write("new-core");
        return (core, stream.ToArray());
    }
    private static string Release(string? digest) => JsonSerializer.Serialize(new[] { new
    {
        tag_name = "v2.0.0", prerelease = false, published_at = "2026-09-01T00:00:00Z",
        assets = new[] { new { name = "sing-box-windows-amd64v3.zip", browser_download_url = "https://downloads.example/release.zip", digest } }
    } });

    [Fact]
    public async Task MissingDigestRequiresConfirmationBeforeCandidateExecutionOrStop()
    {
        var (core, zip) = Prepare();
        using var client = new HttpClient(new Handler(Release(null), zip));
        var versionReads = 0; var stops = 0;
        await Assert.ThrowsAsync<UnverifiedReleaseException>(() => SingBoxUpdater.UpgradeAsync(core, () => stops++, default, false, client,
            (_, _, _) => { versionReads++; return Task.FromResult("sing-box version 1.0.0"); }));
        Assert.Equal(1, versionReads); Assert.Equal(0, stops);
        Assert.Equal("old-core", File.ReadAllText(core));
    }

    [Fact]
    public async Task WrongDigestNeverExecutesCandidateOrReplacesInstalledCore()
    {
        var (core, zip) = Prepare();
        using var client = new HttpClient(new Handler(Release("sha256:" + new string('0', 64)), zip));
        var versionReads = 0; var stops = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => SingBoxUpdater.UpgradeAsync(core, () => stops++, default, false, client,
            (_, _, _) => { versionReads++; return Task.FromResult("sing-box version 1.0.0"); }));
        Assert.Equal(1, versionReads); Assert.Equal(0, stops);
        Assert.Equal("old-core", File.ReadAllText(core));
    }

    [Theory]
    [InlineData("2.0.0", true)]
    [InlineData("3.0.0", false)]
    public async Task CandidateVersionIsValidatedBeforeStopAndAtomicCommit(string candidateVersion, bool success)
    {
        var (core, zip) = Prepare();
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        using var client = new HttpClient(new Handler(Release(digest), zip));
        var stops = 0;
        var task = SingBoxUpdater.UpgradeAsync(core, () => { Assert.Equal("old-core", File.ReadAllText(core)); stops++; }, default, false, client,
            (path, _, _) => Task.FromResult("sing-box version " + (path == core ? "1.0.0" : candidateVersion)));
        if (success)
        {
            var result = await task;
            Assert.Equal(1, stops); Assert.Equal("new-core", File.ReadAllText(core));
            Assert.Equal("old-core", File.ReadAllText(result.BackupPath));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => task);
            Assert.Equal(0, stops); Assert.Equal("old-core", File.ReadAllText(core));
        }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
