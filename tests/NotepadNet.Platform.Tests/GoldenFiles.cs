namespace NotepadNet.Platform.Tests;

internal static class GoldenFiles
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "encoding", name);

    public static byte[] Read(string name) => System.IO.File.ReadAllBytes(PathOf(name));

    public static IEnumerable<string> All => Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "encoding"))
        .Select(Path.GetFileName)
        .OfType<string>()
        .Where(name => !name.EndsWith(".md", StringComparison.Ordinal));
}
