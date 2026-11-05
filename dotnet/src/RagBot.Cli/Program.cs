// Command-line entry point. Mirrors python/src/ragbot/cli.py.
// Run with: dotnet run --project src/RagBot.Cli -- ingest

const string Usage = """
    Usage: ragbot <command>

    Commands:
      ingest   Scrape the website and build the index (offline pipeline)
      chat     Ask questions (not implemented yet)
    """;

if (args is not ["ingest" or "chat"])
{
    Console.Error.WriteLine(Usage);
    return 2;
}

Console.WriteLine($"'{args[0]}' is not implemented yet. Coming in later steps.");
return 0;
