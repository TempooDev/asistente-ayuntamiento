using AsistenteAyuntamiento.Worker;
using Microsoft.Extensions.Configuration;

namespace AsistenteAyuntamiento.Worker.Tests;

public class RequiredConfigurationTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static readonly Dictionary<string, string?> Complete = new()
    {
        ["ConnectionStrings:asistente-ayuntamiento-db"] = "Host=db",
        ["ConnectionStrings:messaging"] = "amqp://rabbit",
        ["ConnectionStrings:qdrant"] = "Endpoint=http://qdrant:6334;Key=k",
        ["Blob:Endpoint"] = "https://r2",
        ["Blob:AccessKeyId"] = "id",
        ["Blob:SecretAccessKey"] = "secret",
        ["Ai:Chat:Provider"] = "google",
        ["Ai:Chat:ApiKey"] = "chat-key",
        ["Ai:Embeddings:Provider"] = "google",
        ["Ai:Embeddings:ApiKey"] = "embeddings-key",
    };

    [Fact]
    public void Complete_configuration_is_valid()
    {
        Assert.Empty(RequiredConfiguration.FindMissing(Config(Complete)));
    }

    [Theory]
    [InlineData("ConnectionStrings:asistente-ayuntamiento-db")]
    [InlineData("ConnectionStrings:messaging")]
    [InlineData("ConnectionStrings:qdrant")]
    [InlineData("Blob:Endpoint")]
    [InlineData("Blob:SecretAccessKey")]
    [InlineData("Ai:Embeddings:ApiKey")]
    public void Missing_setting_fails_startup(string key)
    {
        var values = new Dictionary<string, string?>(Complete) { [key] = "" };

        var ex = Assert.Throws<InvalidOperationException>(() => RequiredConfiguration.Validate(Config(values)));

        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void Ingestion_model_without_any_key_fails_startup()
    {
        var values = new Dictionary<string, string?>(Complete) { ["Ai:Chat:ApiKey"] = null };

        var ex = Assert.Throws<InvalidOperationException>(() => RequiredConfiguration.Validate(Config(values)));

        Assert.Contains("Ai:Ingestion:ApiKey", ex.Message);
    }

    [Fact]
    public void Ingestion_key_can_replace_the_chat_key()
    {
        var values = new Dictionary<string, string?>(Complete) { ["Ai:Chat:ApiKey"] = null, ["Ai:Ingestion:ApiKey"] = "ingestion-key" };

        Assert.Empty(RequiredConfiguration.FindMissing(Config(values)));
    }

    [Fact]
    public void Local_ollama_models_need_no_api_key()
    {
        var values = new Dictionary<string, string?>(Complete)
        {
            ["Ai:Chat:Provider"] = null,
            ["Ai:Chat:ApiKey"] = null,
            ["Ai:Embeddings:Provider"] = "ollama",
            ["Ai:Embeddings:ApiKey"] = null,
        };

        Assert.Empty(RequiredConfiguration.FindMissing(Config(values)));
    }
}
