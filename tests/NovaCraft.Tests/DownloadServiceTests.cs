using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class DownloadServiceTests
{
    [Fact]
    public async Task DownloadsVerifiesAndMovesZipAfterStreamsAreClosed()
    {
        var payload = CreateResourcePackZip();
        var expectedSha512 = Convert.ToHexString(SHA512.HashData(payload));
        var directory = CreateTempDirectory();

        try
        {
            using var http = new HttpClient(new StaticResponseHandler(payload));
            var destination = await DownloadService.DownloadVerifiedAsync(
                http,
                new Uri("https://cdn.modrinth.com/data/test-pack.zip"),
                expectedSha512,
                directory,
                "test-pack.zip",
                new[] { ".zip" },
                2 * 1024 * 1024,
                null,
                CancellationToken.None,
                _ => true,
                (archivePath, _) =>
                {
                    CatalogArchiveValidator.Validate(archivePath, "resourcepack");
                    return Task.CompletedTask;
                });

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
    public async Task RejectsWrongSha512AndRemovesPartialFile()
    {
        var payload = CreateResourcePackZip();
        var directory = CreateTempDirectory();

        try
        {
            using var http = new HttpClient(new StaticResponseHandler(payload));
            await Assert.ThrowsAsync<CryptographicException>(() =>
                DownloadService.DownloadVerifiedAsync(
                    http,
                    new Uri("https://cdn.modrinth.com/data/test-pack.zip"),
                    new string('0', 128),
                    directory,
                    "test-pack.zip",
                    new[] { ".zip" },
                    2 * 1024 * 1024,
                    null,
                    CancellationToken.None));

            Assert.False(File.Exists(Path.Combine(directory, "test-pack.zip")));
            Assert.False(File.Exists(Path.Combine(directory, "test-pack.zip.download")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsRedirectOutsideOfficialModrinthCdn()
    {
        var payload = CreateResourcePackZip();
        var expectedSha512 = Convert.ToHexString(SHA512.HashData(payload));
        var directory = CreateTempDirectory();

        try
        {
            using var http = new HttpClient(new StaticResponseHandler(
                payload,
                new Uri("https://downloads.example.invalid/steal.zip")));

            await Assert.ThrowsAsync<System.Security.SecurityException>(() =>
                DownloadService.DownloadVerifiedAsync(
                    http,
                    new Uri("https://cdn.modrinth.com/data/test-pack.zip"),
                    expectedSha512,
                    directory,
                    "test-pack.zip",
                    new[] { ".zip" },
                    2 * 1024 * 1024,
                    null,
                    CancellationToken.None));

            Assert.False(File.Exists(Path.Combine(directory, "test-pack.zip")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsUnapprovedFileWithoutSendingRequest()
    {
        var handler = new StaticResponseHandler(CreateResourcePackZip());
        var directory = CreateTempDirectory();

        try
        {
            using var http = new HttpClient(handler);
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                DownloadService.DownloadVerifiedAsync(
                    http,
                    new Uri("https://cdn.modrinth.com/data/test-pack.zip"),
                    new string('0', 128),
                    directory,
                    "test-pack.zip",
                    new[] { ".zip" },
                    2 * 1024 * 1024,
                    null,
                    CancellationToken.None,
                    _ => false));

            Assert.Equal(0, handler.RequestCount);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] CreateResourcePackZip()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var metadata = archive.CreateEntry("pack.mcmeta");
            using (var writer = new StreamWriter(metadata.Open(), new UTF8Encoding(false)))
                writer.Write("""{"pack":{"pack_format":34,"description":"NovaCraft test"}}""");

            var asset = archive.CreateEntry("assets/minecraft/lang/en_us.json");
            using (var writer = new StreamWriter(asset.Open(), new UTF8Encoding(false)))
                writer.Write("""{"test":"ok"}""");
        }
        return buffer.ToArray();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "novacraft-download-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly byte[] payload;
        private readonly Uri? finalUri;
        public int RequestCount { get; private set; }

        public StaticResponseHandler(byte[] payload, Uri? finalUri = null)
        {
            this.payload = payload;
            this.finalUri = finalUri;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUri ?? request.RequestUri)
            };
            return Task.FromResult(response);
        }
    }
}
