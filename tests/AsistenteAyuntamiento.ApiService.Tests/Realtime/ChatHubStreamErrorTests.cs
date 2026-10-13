using System.Runtime.CompilerServices;
using AsistenteAyuntamiento.ApiService.Features.Chat;
using AsistenteAyuntamiento.ApiService.Tests.Infrastructure;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Chat.DTOs;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AsistenteAyuntamiento.ApiService.Tests.Realtime;

/// <summary>A failure in the middle of a stream reaches the client as a typed error, never as exception details.</summary>
public class ChatHubStreamErrorTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Secret = "connection string with password=hunter2";

    /// <summary>The message that makes <see cref="FailingChatService"/> fail like the model stream would.</summary>
    private const string ModelFailure = "fallo del modelo";

    /// <summary>Streams one chunk, then fails like a provider or a database would.</summary>
    private sealed class FailingChatService : IAiChatService
    {
        public Task<AiCompletionResult> GetCompletionAsync(ChatHistory history, string tenantId, string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<string> GetStreamingCompletionAsync(ChatHistory history, string tenantId, string userId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return "Respuesta parcial";
            await Task.Yield();
            if (history[^1].Content == ModelFailure)
            {
                throw new LlmStreamInterruptedException(LlmErrorCodes.Unavailable, new IOException(Secret));
            }
            throw new InvalidOperationException(Secret);
        }

        public async IAsyncEnumerable<ArenaStreamChunk> GetArenaStreamingCompletionAsync(ChatHistory history, string tenantId, string userId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ArenaStreamChunk("Alfa", "Parcial");
            await Task.Yield();
            throw new InvalidOperationException(Secret);
        }
    }

    private async Task<HubConnection> ConnectAsync()
    {
        var server = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiChatService>();
            services.AddScoped<IAiChatService, FailingChatService>();
        })).Server;

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/chat"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Headers[TestAuthHandler.UserHeader] = "auth0|stream-user";
                options.Headers[TestAuthHandler.OrgHeader] = "org_stream";
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    [Fact]
    public async Task A_chat_stream_that_fails_midway_ends_with_the_typed_internal_error()
    {
        await using var connection = await ConnectAsync();
        var sessionId = await connection.InvokeAsync<Guid>("CreateNewSession");

        var chunks = new List<string>();
        var error = await Assert.ThrowsAsync<HubException>(async () =>
        {
            await foreach (var chunk in connection.StreamAsync<string>("StreamMessage", sessionId.ToString(), "Hola")) chunks.Add(chunk);
        });

        Assert.Equal(["Respuesta parcial"], chunks);
        Assert.Contains("[INTERNAL]", error.Message);
        Assert.DoesNotContain("hunter2", error.Message);
    }

    [Fact]
    public async Task A_model_failure_after_some_text_ends_the_stream_with_its_llm_code()
    {
        await using var connection = await ConnectAsync();
        var sessionId = await connection.InvokeAsync<Guid>("CreateNewSession");

        var chunks = new List<string>();
        var error = await Assert.ThrowsAsync<HubException>(async () =>
        {
            await foreach (var chunk in connection.StreamAsync<string>("StreamMessage", sessionId.ToString(), ModelFailure)) chunks.Add(chunk);
        });

        Assert.Equal(["Respuesta parcial"], chunks);
        Assert.Contains($"[{LlmErrorCodes.Unavailable}]", error.Message);
        Assert.DoesNotContain("hunter2", error.Message);
    }

    [Fact]
    public async Task An_arena_stream_that_fails_midway_ends_with_the_typed_internal_error()
    {
        await using var connection = await ConnectAsync();
        var sessionId = await connection.InvokeAsync<Guid>("CreateNewSession");

        var error = await Assert.ThrowsAsync<HubException>(async () =>
        {
            await foreach (var _ in connection.StreamAsync<ArenaStreamChunk>("StreamArenaMessage", sessionId.ToString(), "Hola")) { }
        });

        Assert.Contains("[INTERNAL]", error.Message);
        Assert.DoesNotContain("hunter2", error.Message);
    }
}
