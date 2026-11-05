// Command-line entry point. Mirrors python/src/ragbot/cli.py.
// Run with: dotnet run --project src/RagBot.Cli -- ingest --max-pages 5
//
// This is the "composition root": the one place that picks concrete adapters and wires
// them together. (A bigger app would use Microsoft.Extensions.DependencyInjection here.)
using System.Globalization;
using Microsoft.Extensions.Logging;
using RagBot.Core.Ingestion.Adapters;

namespace RagBot.Cli;

internal static class Program
{
    private const string BaseUrl = "https://www.stavanger.kommune.no";
    private const string UserAgent = "RagBot-training/0.1";
    private const string Usage = """
        Usage: ragbot <command> [options]

        Commands:
          ingest   Scrape the website (offline pipeline)
                     --section <name>    URL path section (default: renovasjon-og-miljo)
                     --max-pages <n>     default: 10
                     --delay <seconds>   between requests (default: 1.0)
                     --show              print the first page's markdown
          chat     Ask questions (not implemented yet)
        """;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        switch (args[0])
        {
            case "ingest":
                await RunIngestAsync(args).ConfigureAwait(false);
                return 0;
            case "chat":
                Console.WriteLine("'chat' is not implemented yet. Coming in later steps.");
                return 0;
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    private static async Task RunIngestAsync(string[] args)
    {
        var section = GetOption(args, "--section", "renovasjon-og-miljo");
        var maxPages = int.Parse(GetOption(args, "--max-pages", "10"), CultureInfo.InvariantCulture);
        var delaySeconds = double.Parse(GetOption(args, "--delay", "1.0"), CultureInfo.InvariantCulture);
        var show = args.Contains("--show");

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options => options.SingleLine = true);
        });
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        var robots = await RobotsPolicy.LoadAsync(client, new Uri(BaseUrl), UserAgent).ConfigureAwait(false);
        var source = new SiteMapUrlSource(
            client,
            robots,
            new Uri($"{BaseUrl}/nettstedskart/"),
            $"/{section}/",
            maxPages,
            loggerFactory.CreateLogger<SiteMapUrlSource>());
        var scraper = new HttpScraper(
            client, robots, loggerFactory.CreateLogger<HttpScraper>(), TimeSpan.FromSeconds(delaySeconds));
        var store = new InMemoryDocumentStore(); // Step 3 replaces this with a persistent store

        var urls = await source.DiscoverAsync().ConfigureAwait(false);
        await foreach (var doc in scraper.FetchAsync(urls).ConfigureAwait(false))
        {
            await store.SaveAsync(doc).ConfigureAwait(false);
            Console.WriteLine($"- {doc.Title}  ({doc.Content.Length} chars, updated {doc.UpdatedOn:yyyy-MM-dd})");
            Console.WriteLine($"  {doc.Url}");
        }

        var documents = await store.AllAsync().ToListAsync().ConfigureAwait(false);
        Console.WriteLine($"\nFetched {documents.Count} documents.");
        if (show && documents.Count > 0)
        {
            Console.WriteLine($"\n--- {documents[0].Url} ---\n{documents[0].Content}");
        }
    }

    // Minimal option parsing, so the comparison with Python's argparse stays readable.
    // (System.CommandLine is the full-featured .NET option.)
    private static string GetOption(string[] args, string name, string fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
}
