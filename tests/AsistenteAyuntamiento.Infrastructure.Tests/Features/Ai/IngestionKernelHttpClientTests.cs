using System.Net;
using System.Text;
using AsistenteAyuntamiento.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace AsistenteAyuntamiento.Infrastructure.Tests.Features.Ai;

/// <summary>
/// The keyed <c>IngestionKernel</c> sends its requests through the pooled <c>llm</c> named client for every provider
/// (spec: llm-provider-security, "Pooled HTTP Clients").
/// </summary>
public class IngestionKernelHttpClientTests
{
    private readonly CapturingHandler _handler = new();

    private ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:qdrant"] = "Endpoint=http://localhost:6334;Key=unused",
            ["ConnectionStrings:ollama"] = "Endpoint=http://ollama.test:11434",
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddInfrastructureServices();

        // Replaces only the network: the resilience handler and the pooling of the "llm" client stay in place.
        builder.Services.AddHttpClient(ChatModelFactory.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _handler);

        return builder.Services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("google", null, "generativelanguage.googleapis.com")]
    [InlineData("openrouter", null, "openrouter.ai")]
    [InlineData("openai", "https://ingestion-llm.example/v1", "ingestion-llm.example")]
    [InlineData("ollama", null, "ollama.test")]
    public async Task Ingestion_kernel_uses_the_pooled_llm_client(string provider, string? endpointUrl, string expectedHost)
    {
        using var services = BuildProvider(new()
        {
            ["Ai:Ingestion:Provider"] = provider,
            ["Ai:Ingestion:Model"] = "ingestion-model",
            ["Ai:Ingestion:ApiKey"] = "platform-key",
            ["Ai:Ingestion:EndpointUrl"] = endpointUrl,
        });

        var kernel = services.GetRequiredKeyedService<Kernel>("IngestionKernel");
        await CallIgnoringResponseAsync(kernel.GetRequiredService<IChatCompletionService>());

        _handler.Requests.Should().ContainSingle().Which.RequestUri!.Host.Should().Be(expectedHost);
    }

    [Fact]
    public void Ingestion_kernel_is_a_new_instance_per_resolution()
    {
        using var provider = BuildProvider(new() { ["Ai:Ingestion:Provider"] = "google", ["Ai:Ingestion:ApiKey"] = "k" });

        var first = provider.GetRequiredKeyedService<Kernel>("IngestionKernel");
        var second = provider.GetRequiredKeyedService<Kernel>("IngestionKernel");

        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public async Task Tenant_ollama_client_uses_the_pooled_llm_client()
    {
        using var provider = BuildProvider(new());
        var factory = provider.GetRequiredService<Application.Features.Chat.IChatModelFactory>();

        var service = factory.Create(new Application.Features.AiConfig.DTOs.AiConfigurationDto { Provider = "ollama", Model = "llama3.2" }, apiKey: null);
        await CallIgnoringResponseAsync(service);

        _handler.Requests.Should().ContainSingle().Which.RequestUri!.Host.Should().Be("ollama.test");
    }

    /// <summary>The stub answers 200 with a body no provider can parse: only the outgoing request matters.</summary>
    private static async Task CallIgnoringResponseAsync(IChatCompletionService service)
    {
        try
        {
            await service.GetChatMessageContentAsync("hola");
        }
        catch (Exception)
        {
            // Expected: the response is not a valid completion.
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
