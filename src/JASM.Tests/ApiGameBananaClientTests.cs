using System.Net;
using System.Text;
using GIMI_ModManager.Core.Services.GameBanana;
using GIMI_ModManager.Core.Services.GameBanana.Models;
using Polly;
using Polly.Registry;
using Serilog;
using Xunit;

namespace JASM.Tests;

public class ApiGameBananaClientTests
{
    private class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(handlerFunc(request));
        }
    }

    private static (ApiGameBananaClient client, MockHttpMessageHandler handler) CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc)
    {
        var handler = new MockHttpMessageHandler(handlerFunc);
        var httpClient = new HttpClient(handler);
        var registry = new ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder(ApiGameBananaClient.HttpClientName, (builder, _) => { });
        var logger = new LoggerConfiguration().CreateLogger();
        var client = new ApiGameBananaClient(logger, httpClient, registry);
        return (client, handler);
    }

    [Fact]
    public async Task HealthCheckAsync_CallsAllowedItemTypesUrl_AndReturnsTrueOn200()
    {
        var (client, handler) = CreateClient(req => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await client.HealthCheckAsync();

        Assert.True(result);
        Assert.Single(handler.Requests);
        Assert.Equal("https://api.gamebanana.com/Core/Item/Data/AllowedItemTypes", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task HealthCheckAsync_ReturnsFalseOnNotFound()
    {
        var (client, handler) = CreateClient(req => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await client.HealthCheckAsync();

        Assert.False(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetModProfileAsync_ReturnsNull_WhenModNotFound()
    {
        var (client, _) = CreateClient(req => new HttpResponseMessage(HttpStatusCode.NotFound));

        var profile = await client.GetModProfileAsync(new GbModId("999999999"));

        Assert.Null(profile);
    }

    [Fact]
    public async Task GetModProfileAsync_ThrowsHttpRequestException_On500()
    {
        var (client, _) = CreateClient(req => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await client.GetModProfileAsync(new GbModId("123456"));
        });
    }

    [Fact]
    public async Task GetModProfileAsync_ParsesValidResponse_On200()
    {
        var json = """
        {
            "_idRow": 691126,
            "_sName": "Detective Gilberta",
            "_sProfileUrl": "https://gamebanana.com/mods/691126",
            "_sVersion": "1.2",
            "_nDownloadCount": 5173,
            "_aFiles": []
        }
        """;

        var (client, handler) = CreateClient(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        var profile = await client.GetModProfileAsync(new GbModId("691126"));

        Assert.NotNull(profile);
        Assert.Equal(691126, profile.ModId);
        Assert.Equal("Detective Gilberta", profile.ModName);
        Assert.Equal("https://gamebanana.com/apiv13/Mod/691126/ProfilePage", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task GetModFilesInfoAsync_ReturnsNull_WhenNotFound()
    {
        var (client, _) = CreateClient(req => new HttpResponseMessage(HttpStatusCode.NotFound));

        var filesInfo = await client.GetModFilesInfoAsync(new GbModId("999999999"));

        Assert.Null(filesInfo);
    }

    [Fact]
    public async Task GetModUpdatesAsync_ReturnsEmptyList_WhenNotFound()
    {
        var (client, _) = CreateClient(req => new HttpResponseMessage(HttpStatusCode.NotFound));

        var updates = await client.GetModUpdatesAsync("999999999");

        Assert.NotNull(updates);
        Assert.Empty(updates);
    }
}
