// Mirrors python/tests/conftest.py: reads the shared testdata/ files.
namespace RagBot.Tests;

internal static class TestData
{
    public static string Read(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "stavanger", name));
    }
}
