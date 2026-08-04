using LongBetterWindows.PluginCatalogGenerator;

try
{
    var options = CatalogGeneratorArguments.Parse(args);
    var result = await new PluginCatalogSynchronizer().SynchronizeAsync(
        options.RepositoryRoot,
        options.CheckOnly);

    foreach (var path in result.OutputPaths)
        Console.WriteLine($"{(options.CheckOnly ? "Checked" : "Generated")}: {path}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Plugin catalog synchronization failed: {exception.Message}");
    return 1;
}

internal sealed record CatalogGeneratorArguments(string RepositoryRoot, bool CheckOnly)
{
    public static CatalogGeneratorArguments Parse(string[] args)
    {
        string? root = null;
        var checkOnly = false;
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--check", StringComparison.OrdinalIgnoreCase))
            {
                checkOnly = true;
                continue;
            }

            if (string.Equals(args[index], "--root", StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length)
            {
                root = args[++index];
                continue;
            }

            throw new ArgumentException(
                "Usage: LongBetterWindows.PluginCatalogGenerator [--root <repository>] [--check]");
        }

        return new CatalogGeneratorArguments(
            Path.GetFullPath(root ?? FindRepositoryRoot()),
            checkOnly);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongBetterWindows.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
