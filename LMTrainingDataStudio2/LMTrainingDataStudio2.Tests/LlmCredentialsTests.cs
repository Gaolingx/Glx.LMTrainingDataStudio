using LMTrainingDataStudio2.Models;
using LMTrainingDataStudio2.Services.Executors;

namespace LMTrainingDataStudio2.Tests;

[TestFixture]
public sealed class LlmCredentialsTests
{
    [Test]
    public void GetApiKey_PrefersSettingsOverEnvironment()
    {
        var settings = new AppSettings
        {
            OpenAiApiKey = " settings-openai ",
            AnthropicApiKey = "settings-anthropic"
        };
        var credentials = new LlmCredentials(settings, _ => "env-key");

        Assert.That(credentials.GetApiKey(LlmProviderIds.OpenAiChat), Is.EqualTo("settings-openai"));
        Assert.That(credentials.GetApiKey(LlmProviderIds.Anthropic), Is.EqualTo("settings-anthropic"));
    }

    [Test]
    public void GetApiKey_FallsBackToEnvironmentWhenSettingsBlank()
    {
        var credentials = new LlmCredentials(
            new AppSettings { OpenAiApiKey = "   ", AnthropicApiKey = null },
            name => name switch
            {
                "OPENAI_API_KEY" => "env-openai",
                "ANTHROPIC_API_KEY" => "env-anthropic",
                "OPENAI_BASE_URL" => "https://env.example/v1",
                _ => null
            });

        Assert.That(credentials.GetApiKey(LlmProviderIds.OpenAiChat), Is.EqualTo("env-openai"));
        Assert.That(credentials.GetApiKey(LlmProviderIds.OpenAiResponses), Is.EqualTo("env-openai"));
        Assert.That(credentials.GetApiKey(LlmProviderIds.Anthropic), Is.EqualTo("env-anthropic"));
        Assert.That(credentials.GetBaseUrl(LlmProviderIds.OpenAiChat), Is.EqualTo("https://env.example/v1"));
    }

    [Test]
    public void GetApiKey_UnknownProvider_ReturnsNull()
    {
        var credentials = new LlmCredentials(new AppSettings(), _ => "env-key");

        Assert.That(credentials.GetApiKey("mystery"), Is.Null);
    }

    [Test]
    public void GetBaseUrl_WithoutEnvironment_ReturnsNull()
    {
        var credentials = new LlmCredentials(new AppSettings(), _ => null);

        Assert.That(credentials.GetBaseUrl(LlmProviderIds.OpenAiChat), Is.Null);
        Assert.That(credentials.GetBaseUrl(LlmProviderIds.Anthropic), Is.Null);
    }
}
