namespace DndMcpAICsharpFun.Tests;

/// <summary>Resolves repo files relative to the test binary, so tests work on any machine / CI.</summary>
public static class TestPaths
{
    /// <summary>Absolute path to a file under the repo root, e.g. RepoFile("books/canonical/dragonborn-slice.json").</summary>
    public static string RepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "books", "canonical")))
            dir = dir.Parent;
        if (dir is null)
            throw new DirectoryNotFoundException("Could not locate repo root (no books/canonical above the test binary).");
        return Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
