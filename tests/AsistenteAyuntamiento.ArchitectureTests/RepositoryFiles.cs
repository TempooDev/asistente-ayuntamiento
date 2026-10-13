namespace AsistenteAyuntamiento.ArchitectureTests;

/// <summary>Locates source files of the repository the tests run from.</summary>
internal static class RepositoryFiles
{
    public static string Root { get; } = FindRoot();

    public static string Project(string name) => Path.Combine(Root, "src", name);

    /// <summary>C# source files of a project, excluding generated, migration and build output files.</summary>
    public static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(Project(project), "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsUnder(f, "bin") && !IsUnder(f, "obj") && !IsUnder(f, "Migrations"));

    public static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static bool IsUnder(string path, string folder) =>
        path.Contains($"{Path.DirectorySeparatorChar}{folder}{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AsistenteAyuntamiento.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (AsistenteAyuntamiento.slnx) not found.");
    }
}
