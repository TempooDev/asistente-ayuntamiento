using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace AsistenteAyuntamiento.ArchitectureTests;

/// <summary>Single-responsibility chat services and tracked background work (spec: clean-architecture-boundaries).</summary>
public partial class ChatServiceRulesTests
{
    private const int MaxLinesPerFile = 400;
    private const int MaxInjectedServices = 7;
    private const string ChatNamespace = "AsistenteAyuntamiento.Application.Features.Chat";

    private static IEnumerable<string> ChatSourceFiles() =>
        RepositoryFiles.SourceFiles("AsistenteAyuntamiento.Application")
            .Where(f => RepositoryFiles.Relative(f).StartsWith("src/AsistenteAyuntamiento.Application/Features/Chat/", StringComparison.Ordinal));

    [Fact]
    public void Chat_files_stay_under_the_size_limit()
    {
        var oversized = ChatSourceFiles()
            .Select(f => (File: RepositoryFiles.Relative(f), Lines: File.ReadLines(f).Count()))
            .Where(f => f.Lines > MaxLinesPerFile);

        oversized.Should().BeEmpty($"classes in Features/Chat must stay under {MaxLinesPerFile} lines");
    }

    [Fact]
    public void Chat_services_inject_at_most_seven_dependencies()
    {
        var chatTypes = typeof(Application.Features.Chat.AiChatService).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !IsRecord(t) && t.Namespace?.StartsWith(ChatNamespace, StringComparison.Ordinal) == true);

        var tooMany = chatTypes
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Select(c => (Type: t.FullName, Parameters: c.GetParameters().Length)))
            .Where(c => c.Parameters > MaxInjectedServices);

        tooMany.Should().BeEmpty($"a chat service with more than {MaxInjectedServices} dependencies should be split");
    }

    [Fact]
    public void Chat_services_do_not_cast_to_concrete_services()
    {
        var casts = ChatSourceFiles()
            .Where(f => ConcreteServiceCast().IsMatch(File.ReadAllText(f)))
            .Select(RepositoryFiles.Relative);

        casts.Should().BeEmpty("resolve the needed abstraction instead of casting (service locator)");
    }

    [Theory]
    [InlineData("AsistenteAyuntamiento.Application")]
    [InlineData("AsistenteAyuntamiento.ApiService")]
    public void No_untracked_fire_and_forget_work(string project)
    {
        var offenders = RepositoryFiles.SourceFiles(project)
            .Where(f => FireAndForget().IsMatch(File.ReadAllText(f)))
            .Select(RepositoryFiles.Relative);

        offenders.Should().BeEmpty("use IBackgroundWorkQueue (tracked, tenant-scoped, cancelled on shutdown) instead of Task.Run or discarded tasks");
    }

    /// <summary>Records carry data (positional parameters), not injected services.</summary>
    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$") is not null;

    [GeneratedRegex(@"\bas\s+[A-Z]\w*Service\b")]
    private static partial Regex ConcreteServiceCast();

    [GeneratedRegex(@"Task\.Run\(|\b_\s*=\s*(Task\.|[\w.]+Async\()")]
    private static partial Regex FireAndForget();
}
