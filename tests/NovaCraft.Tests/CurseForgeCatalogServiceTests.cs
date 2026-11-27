using System.Net;
using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class CurseForgeCatalogServiceTests
{
    private const string ApiKey = "unit-test-key";

    [Fact]
    public async Task SearchesMinecraftModsWithOfficialKeyAndResolvedModsClass()
    {
        var handler = new QueueHandler(
            request =>
            {
                Assert.Equal("/v1/categories", request.RequestUri!.AbsolutePath);
                Assert.Contains("gameId=432", request.RequestUri.Query);
                Assert.Contains("classesOnly=true", request.RequestUri.Query);
                return Json("""{"data":[{"id":6,"name":"Mods","slug":"mc-mods","isClass":true}]}""");
            },
            request =>
            {
                Assert.Equal("/v1/mods/search", request.RequestUri!.AbsolutePath);
                Assert.Contains("gameId=432", request.RequestUri.Query);
                Assert.Contains("classId=6", request.RequestUri.Query);
                Assert.Contains("gameVersion=1.21.1", request.RequestUri.Query);
                Assert.Contains("modLoaderType=4", request.RequestUri.Query);
                Assert.Contains("searchFilter=Sodium", request.RequestUri.Query);
                Assert.True(request.Headers.TryGetValues("x-api-key", out var keys));
                Assert.Equal(ApiKey, keys!.Single());
                return Json("""{"data":[{"id":42,"name":"Sodium","slug":"sodium","summary":"Rendering engine","downloadCount":1234,"allowModDistribution":true,"isAvailable":true,"links":{"websiteUrl":"https://www.curseforge.com/minecraft/mc-mods/sodium"},"logo":{"thumbnailUrl":"https://media.forgecdn.net/files/sodium.png"}}],"pagination":{"totalCount":1}}""");
            });

        using var http = new HttpClient(handler);
        var page = await CurseForgeCatalogService.SearchModsAsync(
            http, ApiKey, "Sodium", "1.21.1", "fabric", 0, 2, CancellationToken.None);

        var project = Assert.Single(page.Projects);
        Assert.Equal(42, project.Id);
        Assert.Equal("Sodium", project.Name);
        Assert.True(project.AllowsDistribution);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ReadsOnlyCompatibleJarsAndSha1Hashes()
    {
        var handler = new QueueHandler(request =>
        {
            Assert.Equal("/v1/mods/42/files", request.RequestUri!.AbsolutePath);
            Assert.Contains("gameVersion=1.21.1", request.RequestUri.Query);
            Assert.Contains("modLoaderType=4", request.RequestUri.Query);
            return Json("""{"data":[
              {"id":101,"fileName":"sodium-fabric.jar","displayName":"Sodium Fabric","fileLength":12345,"releaseType":1,"isAvailable":true,"downloadUrl":"https://edge.forgecdn.net/files/101/sodium.jar","hashes":[{"algo":2,"value":"md5"},{"algo":1,"value":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"gameVersions":["1.21.1"]},
              {"id":102,"fileName":"not-a-mod.txt","fileLength":10,"releaseType":1,"isAvailable":true,"hashes":[{"algo":1,"value":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}]}
            ]}""");
        });

        using var http = new HttpClient(handler);
        var files = await CurseForgeCatalogService.GetCompatibleFilesAsync(
            http, ApiKey, 42, "1.21.1", "fabric", CancellationToken.None);

        var file = Assert.Single(files);
        Assert.Equal(101, file.Id);
        Assert.Equal("sodium-fabric.jar", file.FileName);
        Assert.Equal(new string('a', 40), file.Sha1);
        Assert.Equal(12345, file.FileSize);
        Assert.True(file.IsAvailable);
    }

    [Fact]
    public async Task ResolvesDownloadUrlThroughOfficialEndpoint()
    {
        var handler = new QueueHandler(request =>
        {
            Assert.Equal("/v1/mods/42/files/101/download-url", request.RequestUri!.AbsolutePath);
            return Json("""{"data":"https://edge.forgecdn.net/files/101/mod.jar"}""");
        });

        using var http = new HttpClient(handler);
        var result = await CurseForgeCatalogService.GetFileDownloadUrlAsync(
            http, ApiKey, 42, 101, CancellationToken.None);

        Assert.Equal("https://edge.forgecdn.net/files/101/mod.jar", result);
    }

    [Fact]
    public async Task StopsIfOfficialApiRedirectsInsteadOfForwardingApiKey()
    {
        var handler = new QueueHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://attacker.example.invalid/collect") }
        });

        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<System.Security.SecurityException>(() =>
            CurseForgeCatalogService.SearchModsAsync(
                http, ApiKey, "", "1.21.1", "fabric", 0, 2, CancellationToken.None));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task RejectsMissingApiKeyBeforeSendingARequest()
    {
        var handler = new QueueHandler(_ => Json("""{"data":[]}"""));
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CurseForgeCatalogService.SearchModsAsync(
                http, " ", "", "1.21.1", "fabric", 0, 2, CancellationToken.None));
        Assert.Equal(0, handler.RequestCount);
    }

    private static HttpResponseMessage Json(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses;
        public int RequestCount { get; private set; }

        public QueueHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
            => this.responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.True(request.Headers.TryGetValues("x-api-key", out var values));
            Assert.Equal(ApiKey, values!.Single());
            if (responses.Count == 0)
                throw new InvalidOperationException("Unexpected API request.");
            return Task.FromResult(responses.Dequeue()(request));
        }
    }
}
