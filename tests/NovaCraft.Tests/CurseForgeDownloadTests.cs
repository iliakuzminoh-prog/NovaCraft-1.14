using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class CurseForgeDownloadTests
{
    [Fact]
    public async Task DownloadsVerifiesAndMovesJarAfterStreamsAreClosed()
    {
        const string apiKey = "test-api-key-never-persisted";
        var payload = CreateJar();
        var expectedSha1 = Convert.ToHexString(SHA1.HashData(payload));
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "example-mod.jar");

        try
        {
            using var http = new HttpClient(new StaticResponseHandler(payload, apiKey));
            await MinecraftPackImportService.DownloadCurseForgeFileAsync(
                http,
                "https://forgecdn.net/files/example-mod.jar",
                apiKey,
                expectedSha1,
                destination,
                null,
                CancellationToken.None);

            Assert.True(File.Exists(destination));
            Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
            Assert.False(File.Exists(destination + ".download"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsWrongSha1AndRemovesPartialFile()
    {
        const string apiKey = "test-api-key";
        var payload = CreateJar();
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "example-mod.jar");

        try
        {
            using var http = new HttpClient(new StaticResponseHandler(payload, apiKey));
            await Assert.ThrowsAsync<CryptographicException>(() =>
                MinecraftPackImportService.DownloadCurseForgeFileAsync(
                    http,
                    "https://forgecdn.net/files/example-mod.jar",
                    apiKey,
                    new string('0', 40),
                    destination,
                    null,
                    CancellationToken.None));

            Assert.False(File.Exists(destination));
            Assert.False(File.Exists(destination + ".download"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsDownloadHostOutsideCurseForgeCdn()
    {
        const string apiKey = "test-api-key";
        var payload = CreateJar();
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "example-mod.jar");

        try
        {
            using var http = new HttpClient(new StaticResponseHandler(payload, apiKey));
            await Assert.ThrowsAsync<System.Security.SecurityException>(() =>
                MinecraftPackImportService.DownloadCurseForgeFileAsync(
                    http,
                    "https://attacker.example.invalid/files/example-mod.jar",
                    apiKey,
                    Convert.ToHexString(SHA1.HashData(payload)),
                    destination,
                    null,
                    CancellationToken.None));

            Assert.False(File.Exists(destination));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] CreateJar()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("META-INF/mods.toml");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write("modLoader=\"javafml\"");
        }
        return buffer.ToArray();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "novacraft-curseforge-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly byte[] payload;
        private readonly string expectedApiKey;

        public StaticResponseHandler(byte[] payload, string expectedApiKey)
        {
            this.payload = payload;
            this.expectedApiKey = expectedApiKey;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.True(request.Headers.TryGetValues("x-api-key", out var values));
            Assert.Equal(expectedApiKey, values!.Single());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, request.RequestUri)
            });
        }
    }
}
