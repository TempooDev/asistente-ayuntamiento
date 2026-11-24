using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using AsistenteAyuntamiento.ApiService.Features.Query;
using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.DTOs;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.ApiService.Tests.Query;

/// <summary>The REST query endpoint answers with the chat pipeline, scoped to the caller's tenant.</summary>
public class QueryEndpointTests
{
    private sealed class FakeChat : IAiChatService
    {
        public string? Tenant { get; private set; }
        public string? Question { get; private set; }

        public async IAsyncEnumerable<string> GetStreamingCompletionAsync(ChatHistory history, string tenantId, string userId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Tenant = tenantId;
            Question = history[^1].Content;
            yield return "El plazo es de diez días.";
            await Task.Yield();
            yield return "\n\nFuentes consultadas: BOE-A-2026-1";
        }

        public IAsyncEnumerable<ArenaStreamChunk> GetArenaStreamingCompletionAsync(ChatHistory history, string tenantId, string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static (WebApplicationFactoryHandle Factory, FakeChat Chat) Create()
    {
        var chat = new FakeChat();
        var factory = new ApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddScoped<IAiChatService>(_ => chat)));
        return (new WebApplicationFactoryHandle(factory), chat);
    }

    private sealed class WebApplicationFactoryHandle(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) : IAsyncDisposable
    {
        public HttpClient Client(string? user, string? org)
        {
            var client = factory.CreateClient();
            if (user is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user);
            if (org is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.OrgHeader, org);
            return client;
        }

        public ValueTask DisposeAsync() => factory.DisposeAsync();
    }

    [Fact]
    public async Task Answers_with_the_pipeline_text_for_the_callers_tenant()
    {
        var (factory, chat) = Create();
        await using var _ = factory;

        var response = await factory.Client("auth0|u1", "org_sedella").PostAsJsonAsync("/api/v1/query", new { question = " ¿Plazo de alegaciones? " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<QueryResponse>();
        Assert.Equal("El plazo es de diez días.\n\nFuentes consultadas: BOE-A-2026-1", body!.Answer);
        Assert.True(body.AiGenerated);
        Assert.Equal("org_sedella", chat.Tenant);
        Assert.Equal("¿Plazo de alegaciones?", chat.Question);
    }

    [Fact]
    public async Task Rejects_an_empty_question()
    {
        var (factory, _) = Create();
        await using var _f = factory;

        var response = await factory.Client("auth0|u1", "org_sedella").PostAsJsonAsync("/api/v1/query", new { question = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Requires_an_authenticated_user()
    {
        var (factory, _) = Create();
        await using var _f = factory;

        var response = await factory.Client(null, null).PostAsJsonAsync("/api/v1/query", new { question = "¿Plazo?" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_user_without_organization()
    {
        var (factory, _) = Create();
        await using var _f = factory;

        var response = await factory.Client("auth0|u1", null).PostAsJsonAsync("/api/v1/query", new { question = "¿Plazo?" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Publishes_the_OpenAPI_document_with_the_query_endpoint()
    {
        var (factory, _) = Create();
        await using var _f = factory;

        var json = await factory.Client(null, null).GetStringAsync("/openapi/v1.json");

        Assert.Contains("/api/v1/query", json);
    }
}
