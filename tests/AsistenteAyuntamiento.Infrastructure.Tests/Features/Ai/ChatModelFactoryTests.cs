using System.Net;
using System.Text;
using AsistenteAyuntamiento.Application.Features.AiConfig.DTOs;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Ai;

/// <summary>
/// Credential scoping of LLM clients (spec: llm-provider-security): which host and which key each request uses.
/// </summary>
public class ChatModelFactoryTests
{
    private const string CompletionJson = """
        {"id":"chatcmpl-1","object":"chat.completion","created":0,"model":"m",
         "choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
        """;

    private readonly CapturingHandler _handler = new();

    private ChatModelFactory CreateFactory(Dictionary<string, string?> settings) =>
        new(new SingleClientFactory(_handler), new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

    [Fact]
    public void Fallback_is_disabled_without_its_own_api_key()
    {
        var factory = CreateFactory(new() { ["Ai:Fallback:ModelId"] = "fallback-model" });

        factory.CreateFallback().Should().BeNull();
    }

    [Fact]
    public async Task Fallback_uses_platform_credentials_and_endpoint_never_the_tenant_key()
    {
        var factory = CreateFactory(new()
        {
            ["Ai:Fallback:ApiKey"] = "platform-fallback-key",
            ["Ai:Fallback:Endpoint"] = "https://fallback.example/v1",
            ["Ai:Fallback:ModelId"] = "fallback-model",
        });

        var fallback = factory.CreateFallback();
        await fallback!.Service.GetChatMessageContentAsync("hola");

        _handler.LastRequest!.RequestUri!.Host.Should().Be("fallback.example");
        _handler.LastRequest.Headers.Authorization!.Parameter.Should().Be("platform-fallback-key");
    }

    [Fact]
    public async Task Tenant_client_sends_the_tenant_key_only_to_the_tenant_endpoint()
    {
        var factory = CreateFactory(new());
        var config = new AiConfigurationDto { Provider = "openai", Model = "tenant-model", EndpointUrl = "https://tenant-llm.example/v1" };

        var service = factory.Create(config, "tenant-key");
        await service.GetChatMessageContentAsync("hola");

        _handler.LastRequest!.RequestUri!.Host.Should().Be("tenant-llm.example");
        _handler.LastRequest.Headers.Authorization!.Parameter.Should().Be("tenant-key");
    }

    [Theory]
    [InlineData("google")]
    [InlineData("openai")]
    [InlineData("openrouter")]
    public void Provider_requiring_a_key_without_one_is_not_configured(string provider)
    {
        var factory = CreateFactory(new());

        var act = () => factory.Create(new AiConfigurationDto { Provider = provider, Model = "m" }, apiKey: null);

        act.Should().Throw<LlmNotConfiguredException>();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CompletionJson, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
