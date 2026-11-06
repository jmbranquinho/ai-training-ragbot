// Mirrors python/tests/conftest.py: reads the shared testdata/ files.
namespace RagBot.Tests;

internal static class TestData
{
    public static string PathOf(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "testdata", "stavanger", name);
    }

    public static string Read(string name)
    {
        return File.ReadAllText(PathOf(name));
    }

    /// <summary>A file from testdata/mediawiki/ (hand-written pages in satisfactory.wiki.gg's layout).</summary>
    public static string ReadMediaWiki(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "mediawiki", name));
    }

    public static byte[] ReadBytes(string name)
    {
        return File.ReadAllBytes(PathOf(name));
    }
}
