using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Amazon.S3;
using Amazon.Runtime;
using System.Security.Cryptography.X509Certificates;

namespace AsistenteAyuntamiento.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
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


        AddSemanticKernelServices(builder);

        return builder;
    }

    private static void AddSemanticKernelServices(IHostApplicationBuilder builder)
    {
#pragma warning disable SKEXP0070 // Ollama connector is experimental
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
        else if (chatProvider.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            var chatEndpointUrl = builder.Configuration["Ai:Chat:EndpointUrl"];
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
        else if (embProvider.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
#pragma warning disable SKEXP0010
            if (!string.IsNullOrEmpty(aiEmbeddingsConfig["EndpointUrl"]))
            {
                var httpClient = new HttpClient { BaseAddress = new Uri(aiEmbeddingsConfig["EndpointUrl"]!) };
                kernelBuilder.AddOpenAIEmbeddingGenerator(embModel, embApiKey, httpClient: httpClient);
            }
            else
            {
                kernelBuilder.AddOpenAIEmbeddingGenerator(embModel, embApiKey);
            }
#pragma warning restore SKEXP0010
        }
        else
        {
            var embUri = embEndpoint.StartsWith("Endpoint=") ? embEndpoint.Split(';').First(p => p.StartsWith("Endpoint=")).Substring("Endpoint=".Length) : embEndpoint;
#pragma warning disable SKEXP0001
            kernelBuilder.AddOllamaEmbeddingGenerator(embModel, new Uri(embUri));
#pragma warning restore SKEXP0001
        }
#pragma warning restore SKEXP0070
    }
}
