using System.Net.Http;
using Amazon.Runtime;
using Amazon.S3;
using AsistenteAyuntamiento.Application.Common.Background;
using AsistenteAyuntamiento.Application.Common.Interfaces;
using AsistenteAyuntamiento.Application.Common.Messaging;
using AsistenteAyuntamiento.Application.Common.Notifications;
using AsistenteAyuntamiento.Application.Common.Search;
using AsistenteAyuntamiento.Application.Common.Storage;
using AsistenteAyuntamiento.Application.Common.VectorStore;
using AsistenteAyuntamiento.Application.Features.Chat;
using AsistenteAyuntamiento.Application.Features.Ingestion;
using AsistenteAyuntamiento.Infrastructure.Ai;
using AsistenteAyuntamiento.Infrastructure.Background;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Infrastructure.Data.Search;
using AsistenteAyuntamiento.Infrastructure.Messaging;
using AsistenteAyuntamiento.Infrastructure.Storage;
using AsistenteAyuntamiento.Infrastructure.VectorStore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.SemanticKernel;

namespace AsistenteAyuntamiento.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        builder.Services.AddScoped<IFullTextSearch, PostgresFullTextSearch>();
        builder.Services.AddScoped<IIngestionMaintenance, IngestionMaintenance>();

        // Hosts with a real transport (SignalR in ApiService, RabbitMQ in Worker) register their own.
        builder.Services.TryAddSingleton<INotificationService, NullNotificationService>();

        // Tracked background work (replaces fire-and-forget Task.Run); items carry their tenant.
        builder.Services.AddSingleton<BackgroundWorkQueue>();
        builder.Services.AddSingleton<IBackgroundWorkQueue>(sp => sp.GetRequiredService<BackgroundWorkQueue>());
        builder.Services.AddHostedService<BackgroundWorkProcessor>();

        // Blob Storage
        var blobEndpoint = builder.Configuration["Blob:Endpoint"];
        if (!string.IsNullOrEmpty(blobEndpoint))
        {
            var accessKeyId = builder.Configuration["Blob:AccessKeyId"];
            var secretAccessKey = builder.Configuration["Blob:SecretAccessKey"];
            if (string.IsNullOrEmpty(accessKeyId) || string.IsNullOrEmpty(secretAccessKey))
            {
                throw new InvalidOperationException("Blob:Endpoint is configured but Blob:AccessKeyId or Blob:SecretAccessKey is missing.");
            }

            var s3Config = new AmazonS3Config { ServiceURL = blobEndpoint, ForcePathStyle = true };
            var credentials = new BasicAWSCredentials(accessKeyId, secretAccessKey);
            builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client(credentials, s3Config));
            builder.Services.AddSingleton<IBlobStorage, S3BlobStorage>();
        }
        builder.Services.AddOptions<BlobStorageOptions>().Bind(builder.Configuration.GetSection(BlobStorageOptions.SectionName));


        if (builder.Environment.IsProduction())
        {
            var qdrantConnection = builder.Configuration.GetConnectionString("qdrant") ?? string.Empty;
            var hasApiKey = qdrantConnection.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part.StartsWith("Key=", StringComparison.OrdinalIgnoreCase) && part.Length > "Key=".Length);
            if (!hasApiKey)
            {
                throw new InvalidOperationException("ConnectionStrings:qdrant must include an API key (Key=...) in Production.");
            }
        }

        builder.AddQdrantClient("qdrant");
        builder.Services.AddOptions<VectorStoreOptions>().Bind(builder.Configuration.GetSection(VectorStoreOptions.SectionName));
        builder.Services.AddSingleton<IVectorStore, QdrantVectorStore>();

        AddMessaging(builder);
        AddSemanticKernelServices(builder);

        return builder;
    }

    /// <summary>
    /// Durable ingestion publishing and consumer health. Consumers themselves are registered by the
    /// hosts that run them (<see cref="AddIngestionConsumer{TConsumer}"/>).
    /// </summary>
    private static void AddMessaging(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IIngestionQueuePublisher, RabbitMqIngestionPublisher>();
        builder.Services.AddSingleton<RabbitMqConsumerHealth>();
        builder.Services.AddHealthChecks().AddCheck<RabbitMqConsumerHealth>("rabbitmq-consumers");
    }

    public static IHostApplicationBuilder AddIngestionConsumer<TConsumer>(this IHostApplicationBuilder builder)
        where TConsumer : class, IHostedService
    {
        builder.Services.AddHostedService<TConsumer>();
        return builder;
    }

    private static void AddSemanticKernelServices(IHostApplicationBuilder builder)
    {
        var ollamaConnString = builder.Configuration.GetConnectionString("ollama") ?? "http://localhost:11434";
        var ollamaEndpoint = ollamaConnString.StartsWith("Endpoint=")
            ? ollamaConnString.Split(';').First(p => p.StartsWith("Endpoint=")).Substring("Endpoint=".Length)
            : ollamaConnString;

        var aiEmbeddingsConfig = builder.Configuration.GetSection("Ai:Embeddings");
        var embProvider = aiEmbeddingsConfig["Provider"] ?? "ollama";
        var embModel = aiEmbeddingsConfig["Model"] ?? "nomic-embed-text";
        var embEndpoint = aiEmbeddingsConfig["EndpointUrl"] ?? ollamaEndpoint;
        var embApiKey = aiEmbeddingsConfig["ApiKey"] ?? "";

        var chatProvider = builder.Configuration["Ai:Chat:Provider"] ?? "ollama";
        var chatModel = builder.Configuration["Ai:Chat:Model"] ?? "llama3.2";
        var chatApiKey = builder.Configuration["Ai:Chat:ApiKey"] ?? "";

        var allowInsecureTls = AllowInsecureTls(builder);

        // Per-tenant chat clients, the platform fallback and the IngestionKernel are built over this pooled client.
        // LLM calls are slow and each tenant may point at its own host, so the default resilience handler (10 s per
        // attempt, one circuit for the whole client) is replaced by one with LLM-sized timeouts and a circuit per host.
        // RemoveAllResilienceHandlers is the documented way to replace the ServiceDefaults handler (experimental API).
#pragma warning disable EXTEXP0001
        builder.Services.AddHttpClient(ChatModelFactory.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => CreateLlmHandler(allowInsecureTls))
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = LlmAttemptTimeout;
                options.TotalRequestTimeout.Timeout = LlmTotalRequestTimeout;
                options.CircuitBreaker.SamplingDuration = 2 * LlmAttemptTimeout;
            })
            .SelectPipelineByAuthority();
#pragma warning restore EXTEXP0001
        builder.Services.AddSingleton<IChatModelFactory, ChatModelFactory>();

        var kernelBuilder = builder.Services.AddKernel();

        if (chatProvider.Equals("google", StringComparison.OrdinalIgnoreCase))
        {
            var handler = CreateLlmHandler(allowInsecureTls);
            kernelBuilder.AddGoogleAIGeminiChatCompletion(chatModel, chatApiKey, httpClient: new HttpClient(handler));
        }
        else if (chatProvider.Equals("openai", StringComparison.OrdinalIgnoreCase) || chatProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            var chatEndpointUrl = builder.Configuration["Ai:Chat:EndpointUrl"];
            if (chatProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(chatEndpointUrl))
            {
                chatEndpointUrl = "https://openrouter.ai/api/v1";
            }

            if (!string.IsNullOrEmpty(chatEndpointUrl))
            {
                var httpClient = new HttpClient { BaseAddress = new Uri(chatEndpointUrl) };
                kernelBuilder.AddOpenAIChatCompletion(chatModel, chatApiKey, httpClient: httpClient);
            }
            else
            {
                kernelBuilder.AddOpenAIChatCompletion(chatModel, chatApiKey);
            }
        }
        else
        {
            kernelBuilder.AddOllamaChatCompletion(chatModel, new Uri(ollamaEndpoint));
        }

        if (embProvider.Equals("google", StringComparison.OrdinalIgnoreCase))
        {
            var handler = CreateLlmHandler(allowInsecureTls);
            kernelBuilder.AddGoogleAIEmbeddingGenerator(embModel, embApiKey, httpClient: new HttpClient(handler));
        }
        else if (embProvider.Equals("openai", StringComparison.OrdinalIgnoreCase) || embProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
        {
            var endpointUrl = aiEmbeddingsConfig["EndpointUrl"];
            if (embProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(endpointUrl))
            {
                endpointUrl = "https://openrouter.ai/api/v1";
            }

            if (!string.IsNullOrEmpty(endpointUrl))
            {
                var httpClient = new HttpClient { BaseAddress = new Uri(endpointUrl) };
                kernelBuilder.AddOpenAIEmbeddingGenerator(embModel, embApiKey, httpClient: httpClient);
            }
            else
            {
                kernelBuilder.AddOpenAIEmbeddingGenerator(embModel, embApiKey);
            }
        }
        else
        {
            var embUri = embEndpoint.StartsWith("Endpoint=") ? embEndpoint.Split(';').First(p => p.StartsWith("Endpoint=")).Substring("Endpoint=".Length) : embEndpoint;
            kernelBuilder.AddOllamaEmbeddingGenerator(embModel, new Uri(embUri));
        }

        // --- Ingestion / Tasks Kernel (Ultra-fast, cheap model) ---
        var ingestionProvider = builder.Configuration["Ai:Ingestion:Provider"] ?? chatProvider;
        var ingestionModel = builder.Configuration["Ai:Ingestion:Model"] ?? chatModel;
        var ingestionApiKey = builder.Configuration["Ai:Ingestion:ApiKey"] ?? chatApiKey;

        // Transient because a Kernel is mutable (plugins, filters); building one is cheap since its HttpClient comes
        // from the pooled "llm" handlers instead of a new handler per resolution.
        builder.Services.AddKeyedTransient<Kernel>("IngestionKernel", (sp, key) =>
        {
            var kBuilder = Kernel.CreateBuilder();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

            HttpClient CreateLlmClient(string? baseAddress = null)
            {
                var client = httpClientFactory.CreateClient(ChatModelFactory.HttpClientName);
                if (!string.IsNullOrEmpty(baseAddress))
                {
                    client.BaseAddress = new Uri(baseAddress);
                }
                return client;
            }

            if (ingestionProvider.Equals("google", StringComparison.OrdinalIgnoreCase))
            {
                kBuilder.AddGoogleAIGeminiChatCompletion(ingestionModel, ingestionApiKey, httpClient: CreateLlmClient());
            }
            else if (ingestionProvider.Equals("openai", StringComparison.OrdinalIgnoreCase) || ingestionProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
            {
                var endpointUrl = builder.Configuration["Ai:Ingestion:EndpointUrl"];
                if (ingestionProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(endpointUrl))
                    endpointUrl = "https://openrouter.ai/api/v1";

                kBuilder.AddOpenAIChatCompletion(ingestionModel, ingestionApiKey, httpClient: CreateLlmClient(endpointUrl));
            }
            else
            {
                kBuilder.AddOllamaChatCompletion(ingestionModel, CreateLlmClient(ollamaEndpoint));
            }

            return kBuilder.Build();
        });
    }

    /// <summary>
    /// <c>Ai:Http:AllowInsecureTls</c> disables certificate validation for LLM calls. It is only honoured in
    /// Development (e.g. behind a TLS-intercepting proxy) and refuses to start anywhere else.
    /// </summary>
    private static bool AllowInsecureTls(IHostApplicationBuilder builder)
    {
        var allow = builder.Configuration.GetValue("Ai:Http:AllowInsecureTls", false);
        if (allow && !builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("Ai:Http:AllowInsecureTls can only be enabled in Development.");
        }
        return allow;
    }

    private static readonly TimeSpan LlmAttemptTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LlmTotalRequestTimeout = TimeSpan.FromMinutes(5);

    private static SocketsHttpHandler CreateLlmHandler(bool allowInsecureTls)
    {
        var handler = new SocketsHttpHandler();
        if (allowInsecureTls)
        {
            // Only reachable in Development (guarded by AllowInsecureTls above).
#pragma warning disable CA5359
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
        }
        return handler;
    }
}
