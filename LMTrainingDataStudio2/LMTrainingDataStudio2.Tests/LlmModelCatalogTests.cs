using System.Net;
using System.Net.Http;
using System.Text;
using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class LlmModelCatalogTests
{
    [Test]
    public async Task OpenAi_ListModels_ReturnsDistinctSortedIds()
    {
        HttpRequestMessage? captured = null;
        var catalog = CreateCatalog((request, _) =>
        {
            captured = request;
            return JsonResponse(
                HttpStatusCode.OK,
                """{"data":[{"id":"gpt-4o-mini"},{"id":"gpt-4o"},{"id":"gpt-4o"}]}""");
        });

        var models = await catalog.ListModelsAsync(LlmProviderIds.OpenAiChat, endpoint: null);

        Assert.That(models, Is.EqualTo(new[] { "gpt-4o", "gpt-4o-mini" }));
        Assert.That(captured!.RequestUri!.ToString(), Is.EqualTo("https://api.openai.com/v1/models"));
        Assert.That(captured.Headers.Authorization!.Parameter, Is.EqualTo("test-key"));
        Assert.That(captured.Method, Is.EqualTo(HttpMethod.Get));
    }

    [Test]
    public async Task Anthropic_ListModels_UsesV1ModelsAndHeaders()
    {
        HttpRequestMessage? captured = null;
        var catalog = CreateCatalog((request, _) =>
        {
            captured = request;
            return JsonResponse(
                HttpStatusCode.OK,
                """{"data":[{"id":"claude-sonnet-4-5"},{"id":"claude-haiku-4-5"}]}""");
        });

        var models = await catalog.ListModelsAsync(LlmProviderIds.Anthropic, endpoint: null);

        Assert.That(models, Is.EqualTo(new[] { "claude-haiku-4-5", "claude-sonnet-4-5" }));
        Assert.That(
            captured!.RequestUri!.ToString(),
            Is.EqualTo("https://api.anthropic.com/v1/models?limit=1000"));
        Assert.That(captured.Headers.GetValues("x-api-key").Single(), Is.EqualTo("test-key"));
        Assert.That(captured.Headers.GetValues("anthropic-version").Single(), Is.EqualTo("2023-06-01"));
    }

    [Test]
    public async Task ConfiguredEndpoint_OverridesBaseUrl()
    {
        HttpRequestMessage? captured = null;
        var catalog = CreateCatalog((request, _) =>
        {
            captured = request;
            return JsonResponse(HttpStatusCode.OK, """{"data":[{"id":"local-model"}]}""");
        });

        var models = await catalog.ListModelsAsync(
            LlmProviderIds.OpenAiChat, "https://proxy.example/v1/");

        Assert.That(models, Is.EqualTo(new[] { "local-model" }));
        Assert.That(captured!.RequestUri!.ToString(), Is.EqualTo("https://proxy.example/v1/models"));
    }

    [Test]
    public void ErrorResponse_ThrowsWithStatusAndBody()
    {
        var catalog = CreateCatalog((_, _) =>
            JsonResponse(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid key"}}"""));

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await catalog.ListModelsAsync(LlmProviderIds.OpenAiChat, endpoint: null));

        Assert.That(ex!.Message, Does.Contain("401"));
        Assert.That(ex.Message, Does.Contain("invalid key"));
    }

    [Test]
    public void MissingApiKey_ThrowsWithEnvironmentVariableName()
    {
        var catalog = CreateCatalog(
            (_, _) => JsonResponse(HttpStatusCode.OK, "{}"),
            apiKey: null);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await catalog.ListModelsAsync(LlmProviderIds.Anthropic, endpoint: null));

        Assert.That(ex!.Message, Does.Contain("ANTHROPIC_API_KEY"));
    }

    [Test]
    public void UnsupportedProvider_Throws()
    {
        var catalog = CreateCatalog((_, _) => JsonResponse(HttpStatusCode.OK, "{}"));

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await catalog.ListModelsAsync("mystery", endpoint: null));
    }

    private static LlmModelCatalog CreateCatalog(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder,
        string? apiKey = "test-key")
        => new(new HttpClient(new StubHandler(responder)), () => new FakeCredentials(apiKey));

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request, content);
        }
    }

    private sealed class FakeCredentials(string? apiKey) : ILlmCredentials
    {
        public string? GetApiKey(string provider) => apiKey;

        public string? GetBaseUrl(string provider) => null;
    }
}
