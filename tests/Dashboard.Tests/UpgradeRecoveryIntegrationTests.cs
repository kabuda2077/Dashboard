using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Dashboard.Tests;

public sealed class UpgradeRecoveryIntegrationTests
{
    private sealed class ReleaseHandler(byte[] archive) : HttpMessageHandler
    {
        public int Downloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            HttpContent content;
            if (request.RequestUri!.Host == "api.github.com") content = new StringContent(JsonSerializer.Serialize(new[] { new
            {
                tag_name = "v99.0.0", prerelease = false, published_at = "2026-09-01T00:00:00Z",
                assets = new[] { new { name = "sing-box-windows-amd64v3.zip", browser_download_url = "https://fixture.test/core.zip",
                    digest = "sha256:" + Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant() } }
            } }));
            else { Downloads++; content = new ByteArrayContent(archive); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(-1)]
    [Trait("Category", "RealCoreIntegration")]
    public async Task VersionValidCandidateCommitsOrRestoresExactOriginalAndRunningApi(int exitCode)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "Dashboard.CandidateFixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            // Real standalone candidate: version succeeds; daemon either exits or
            // delegates to an isolated real core, owned by the candidate's process tree.
            var source = Path.Combine(temporary, "Candidate.cs");
            var candidate = Path.Combine(temporary, "sing-box.exe");
            var delegated = Path.Combine(temporary, "delegate-core.exe");
            var coreSources = Environment.GetEnvironmentVariable("DASHBOARD_TEST_CORES_DIR")
                ?? throw new InvalidOperationException("Prepare isolated validation cores first.");
            File.Copy(Path.Combine(coreSources, "sing-box", "sing-box.exe"), delegated);
            await File.WriteAllTextAsync(source, $$"""
                using System;
                using System.Diagnostics;
                class Candidate {
                    static int Main(string[] args) {
                        if(args.Length > 0 && args[0] == "version") { Console.WriteLine("sing-box version 99.0.0"); return 0; }
                        if ({{exitCode}} >= 0) { Console.Error.WriteLine("intentional candidate failure"); return {{exitCode}}; }
                        var start = new ProcessStartInfo({{JsonSerializer.Serialize(delegated)}}, string.Join(" ", Array.ConvertAll(args, arg => "\"" + arg + "\"")));
                        start.UseShellExecute = false; start.CreateNoWindow = true;
                        using(var child = Process.Start(start)) { child.WaitForExit(); return child.ExitCode; }
                    }
                }
                """);
            var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
            Assert.True(File.Exists(compiler), "This explicitly selected Windows integration test needs the .NET Framework compiler.");
            var start = new ProcessStartInfo(compiler) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "/nologo", "/target:exe", "/out:" + candidate, source }) start.ArgumentList.Add(arg);
            using (var compile = Process.Start(start)!)
            {
                var output = compile.StandardOutput.ReadToEndAsync(); var error = compile.StandardError.ReadToEndAsync();
                await compile.WaitForExitAsync();
                Assert.True(compile.ExitCode == 0, await output + await error);
            }
            Assert.Contains("99.0.0", await CoreVersionReader.ReadAsync(candidate, CoreKind.SingBox));
            using var bytes = new MemoryStream();
            using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
                zip.CreateEntryFromFile(candidate, "release/sing-box.exe");
            var handler = new ReleaseHandler(bytes.ToArray());
            using var client = new HttpClient(handler);
            await using var fixture = await IsolatedCore.CreateAsync(CoreKind.SingBox, client);
            var original = SHA256.HashData(await File.ReadAllBytesAsync(fixture.Executable));
            Assert.Equal("completed", (await fixture.Controller.ExecuteAsync(new() { Type = "start", CoreType = fixture.Kind })).Status);
            await fixture.ReadyAsync();
            var epoch = fixture.Controller.Epoch;
            var result = await fixture.Controller.ExecuteAsync(new() { Type = "upgradeCore", CoreType = fixture.Kind });
            Assert.Equal(exitCode < 0 ? "completed" : "failed", result.Status);
            Assert.Equal(1, handler.Downloads);
            var installed = SHA256.HashData(await File.ReadAllBytesAsync(fixture.Executable));
            if (exitCode < 0) Assert.NotEqual(original, installed);
            else Assert.Equal(original, installed);
            await fixture.ReadyAsync();
            Assert.True(fixture.Controller.Epoch > epoch);
            Assert.Equal("idle", fixture.Controller.Operation);
            Assert.True(fixture.Process.IsRunning);
            using var versionClient = new HttpClient();
            Assert.Contains("version", await versionClient.GetStringAsync(fixture.Store.Current.ActiveProfile.ApiUrl + "/version"));
            if (exitCode >= 0) Assert.Contains("intentional candidate failure", fixture.Process.GetLogTail(16000));
            Assert.All(Directory.GetFiles(Path.Combine(fixture.Root, "backups")), backup => Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(backup))));
        }
        finally { Directory.Delete(temporary, true); }
    }
}
