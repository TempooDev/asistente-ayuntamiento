using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Amazon.S3;
using Amazon.Runtime;
using System.Security.Cryptography.X509Certificates;
using AsistenteAyuntamiento.Infrastructure.Data;
using AsistenteAyuntamiento.Application.Common.Interfaces;

namespace AsistenteAyuntamiento.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        // Blob Storage
        var blobEndpoint = builder.Configuration["Blob:Endpoint"];
        if (!string.IsNullOrEmpty(blobEndpoint))
        {
            var accessKeyId = builder.Configuration["Blob:AccessKeyId"] ?? "admin";
            var secretAccessKey = builder.Configuration["Blob:SecretAccessKey"] ?? "password123";
            var s3Config = new AmazonS3Config { ServiceURL = blobEndpoint, ForcePathStyle = true };
            var credentials = new BasicAWSCredentials(accessKeyId, secretAccessKey);
            builder.Services.AddSingleton<IAmazonS3>(new AmazonS3Client(credentials, s3Config));
        }


        builder.AddQdrantClient("qdrant");
        builder.Services.AddQdrantVectorStore();

        AddSemanticKernelServices(builder);

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

        var kernelBuilder = builder.Services.AddKernel();

        if (chatProvider.Equals("google", StringComparison.OrdinalIgnoreCase))
        {
            var handler = new SocketsHttpHandler { SslOptions = new System.Net.Security.SslClientAuthenticationOptions { CertificateRevocationCheckMode = X509RevocationMode.NoCheck } };
            if (builder.Environment.IsDevelopment()) handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true;
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
            var handler = new SocketsHttpHandler { SslOptions = new System.Net.Security.SslClientAuthenticationOptions { CertificateRevocationCheckMode = X509RevocationMode.NoCheck } };
            if (builder.Environment.IsDevelopment()) handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true;
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
        
        builder.Services.AddHttpClient("IngestionClient");

        builder.Services.AddKeyedTransient<Kernel>("IngestionKernel", (sp, key) =>
        {
            var kBuilder = Kernel.CreateBuilder();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            
            if (ingestionProvider.Equals("google", StringComparison.OrdinalIgnoreCase))
            {
                var handler = new SocketsHttpHandler { SslOptions = new System.Net.Security.SslClientAuthenticationOptions { CertificateRevocationCheckMode = X509RevocationMode.NoCheck } };
                kBuilder.AddGoogleAIGeminiChatCompletion(ingestionModel, ingestionApiKey, httpClient: new HttpClient(handler));
            }
            else if (ingestionProvider.Equals("openai", StringComparison.OrdinalIgnoreCase) || ingestionProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
            {
                var endpointUrl = builder.Configuration["Ai:Ingestion:EndpointUrl"];
                if (ingestionProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(endpointUrl))
                    endpointUrl = "https://openrouter.ai/api/v1";

                if (!string.IsNullOrEmpty(endpointUrl))
                {
                    var client = httpClientFactory.CreateClient("IngestionClient");
                    client.BaseAddress = new Uri(endpointUrl);
                    kBuilder.AddOpenAIChatCompletion(ingestionModel, ingestionApiKey, httpClient: client);
                }
                else
                    kBuilder.AddOpenAIChatCompletion(ingestionModel, ingestionApiKey);
            }
            else
            {
                kBuilder.AddOllamaChatCompletion(ingestionModel, new Uri(ollamaEndpoint));
            }

            return kBuilder.Build();
        });
    }
}
