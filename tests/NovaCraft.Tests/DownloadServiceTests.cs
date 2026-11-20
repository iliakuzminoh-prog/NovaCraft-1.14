using System.Security;
using System.Security.Cryptography;
using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class DownloadServiceTests : IDisposable
{
    static readonly Uri GoodUri = new("https://cdn.modrinth.com/data/abc/versions/xyz/mod.jar");
    static readonly string[] JarOnly = { ".jar" };
    readonly string dir = Path.Combine(Path.GetTempPath(), "novacraft-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }

    sealed class StubHandler : HttpMessageHandler
    {
        readonly Func<HttpResponseMessage> respond;
        public int Calls { get; private set; }
        public StubHandler(Func<HttpResponseMessage> respond) => this.respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(respond());
        }
    }

    static byte[] ZipLike(int extra = 64)
    {
        var data = new byte[4 + extra];
        data[0] = (byte)'P'; data[1] = (byte)'K'; data[2] = 3; data[3] = 4;
        for (var i = 4; i < data.Length; i++) data[i] = (byte)(i % 251);
        return data;
    }

    static string Sha512Hex(byte[] data) => Convert.ToHexString(SHA512.HashData(data));

    static StubHandler Serving(byte[] data) =>
        new(() => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(data) });

    Task<string> Run(StubHandler handler, Uri uri, string sha, string name = "mod.jar",
        string[]? ext = null, long max = 1024 * 1024, Func<string, bool>? confirm = null)
    {
        var http = new HttpClient(handler);
        return DownloadService.DownloadVerifiedAsync(http, uri, sha, dir, name, ext ?? JarOnly, max,
            null, CancellationToken.None, confirm);
    }

    [Fact]
    public async Task ValidDownloadIsWrittenAndNoTempFileRemains()
    {
        var data = ZipLike();
        var path = await Run(Serving(data), GoodUri, Sha512Hex(data));

        Assert.Equal(Path.Combine(dir, "mod.jar"), path);
        Assert.Equal(data, await File.ReadAllBytesAsync(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(dir));
    }

    [Fact]
    public async Task HashComparisonIsCaseInsensitive()
    {
        var data = ZipLike();
        await Run(Serving(data), GoodUri, Sha512Hex(data).ToLowerInvariant());
        Assert.True(File.Exists(Path.Combine(dir, "mod.jar")));
    }

    [Fact]
    public async Task WrongHostIsRejectedBeforeAnyRequest()
    {
        var handler = Serving(ZipLike());
        await Assert.ThrowsAsync<SecurityException>(() =>
            Run(handler, new Uri("https://evil.example.com/mod.jar"), new string('A', 128)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task LookalikeHostIsRejected()
    {
        var handler = Serving(ZipLike());
        await Assert.ThrowsAsync<SecurityException>(() =>
            Run(handler, new Uri("https://cdn.modrinth.com.evil.example/mod.jar"), new string('A', 128)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task PlainHttpIsRejected()
    {
        var handler = Serving(ZipLike());
        await Assert.ThrowsAsync<SecurityException>(() =>
            Run(handler, new Uri("http://cdn.modrinth.com/mod.jar"), new string('A', 128)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task WrongHashIsRejectedAndNothingIsLeftBehind()
    {
        var data = ZipLike();
        await Assert.ThrowsAsync<CryptographicException>(() =>
            Run(Serving(data), GoodUri, new string('A', 128)));
        Assert.Empty(Directory.GetFiles(dir));
    }

    [Fact]
    public async Task OversizedDownloadIsRejectedAndNothingIsLeftBehind()
    {
        var data = ZipLike(2000);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Run(Serving(data), GoodUri, Sha512Hex(data), max: 1000));
        Assert.Empty(Directory.GetFiles(dir));
    }

    [Theory]
    [InlineData("..\\evil.jar")]
    [InlineData("../evil.jar")]
    [InlineData("sub/evil.jar")]
    [InlineData("C:\\evil.jar")]
    [InlineData("")]
    public async Task PathTraversalFileNamesAreRejected(string name)
    {
        var data = ZipLike();
        var handler = Serving(data);
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(handler, GoodUri, Sha512Hex(data), name));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task DisallowedExtensionIsRejected()
    {
        var data = ZipLike();
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(Serving(data), GoodUri, Sha512Hex(data), "run.exe"));
    }

    [Fact]
    public async Task NonZipContentIsRejectedEvenWithMatchingHash()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(Serving(data), GoodUri, Sha512Hex(data)));
        Assert.Empty(Directory.GetFiles(dir));
    }

    [Fact]
    public async Task MalformedHashStringIsRejected()
    {
        var handler = Serving(ZipLike());
        await Assert.ThrowsAsync<InvalidDataException>(() => Run(handler, GoodUri, "abc"));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ExistingFileIsNotOverwritten()
    {
        Directory.CreateDirectory(dir);
        var existing = Path.Combine(dir, "mod.jar");
        await File.WriteAllTextAsync(existing, "original");
        var data = ZipLike();

        await Assert.ThrowsAsync<IOException>(() => Run(Serving(data), GoodUri, Sha512Hex(data)));
        Assert.Equal("original", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task StaleTemporaryFileFromEarlierCrashDoesNotBlockDownload()
    {
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "mod.jar.download"), "stale");
        var data = ZipLike();

        var path = await Run(Serving(data), GoodUri, Sha512Hex(data));

        Assert.Equal(data, await File.ReadAllBytesAsync(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(dir));
    }

    [Fact]
    public async Task DeclinedConfirmationMakesNoRequestAndWritesNothing()
    {
        var data = ZipLike();
        var handler = Serving(data);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Run(handler, GoodUri, Sha512Hex(data), confirm: _ => false));
        Assert.Equal(0, handler.Calls);
        Assert.Empty(Directory.GetFiles(dir));
    }
}
