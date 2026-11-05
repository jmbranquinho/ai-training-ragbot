// Command-line entry point. Mirrors python/src/ragbot/cli.py.
// Run with: dotnet run --project src/RagBot.Cli -- ingest --max-pages 5
//
// This is the "composition root": the one place that picks concrete adapters and wires
// them together. (A bigger app would use Microsoft.Extensions.DependencyInjection here.)
using System.Globalization;
using Microsoft.Extensions.Logging;
using RagBot.Core.Indexing;
using RagBot.Core.Indexing.Chunkers;
using RagBot.Core.Indexing.Ports;
using RagBot.Core.Ingestion;
using RagBot.Core.Ingestion.Adapters;

namespace RagBot.Cli;

internal static class Program
{
    private const string BaseUrl = "https://www.stavanger.kommune.no";
    private const string UserAgent = "RagBot-training/0.1";
    private const string Usage = """
        Usage: ragbot <command> [options]

        Commands:
          ingest   Scrape the website and store raw pages
                     --section <name>    URL path section (default: renovasjon-og-miljo)
                     --max-pages <n>     default: 10
                     --delay <seconds>   between requests (default: 1.0)
                     --data-dir <path>   where pages are stored (default: data/raw)
          chunk    Chunk stored pages and print statistics
                     --strategy <name>   headings | window (default: headings)
                     --max-chars <n>     headings: max body size (default: 1500)
                     --size <n>          window: window size (default: 1000)
                     --overlap <n>       window: overlap (default: 200)
                     --data-dir <path>   default: data/raw
                     --show <text>       print the chunks of the first page whose URL contains this
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
            case "chunk":
                await RunChunkAsync(args).ConfigureAwait(false);
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
        var dataDir = Path.GetFullPath(GetOption(args, "--data-dir", Path.Combine("data", "raw")));

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
        var store = new FileDocumentStore(dataDir);
        var service = new IngestService(source, scraper, store, loggerFactory.CreateLogger<IngestService>());

        var report = await service.RunAsync().ConfigureAwait(false);

        loggerFactory.Dispose(); // flush the console logger before printing the summary
        Console.WriteLine(
            $"\n{report.New.Count} new, {report.Updated.Count} updated, "
            + $"{report.Unchanged.Count} unchanged. Files in {dataDir}");
    }

    /// <summary>Chunk every stored page and print size statistics (nothing is saved yet).</summary>
    private static async Task RunChunkAsync(string[] args)
    {
        IChunker chunker = GetOption(args, "--strategy", "headings") == "window"
            ? new SlidingWindowChunker(GetIntOption(args, "--size", 1000), GetIntOption(args, "--overlap", 200))
            : new HeadingChunker(GetIntOption(args, "--max-chars", 1500));
        var dataDir = Path.GetFullPath(GetOption(args, "--data-dir", Path.Combine("data", "raw")));
        var show = GetOption(args, "--show", "");

        var stored = await new FileDocumentStore(dataDir).AllAsync().ToListAsync().ConfigureAwait(false);
        var documents = DocumentDeduplicator.Deduplicate(stored);
        Console.WriteLine($"{stored.Count} stored pages, {stored.Count - documents.Count} duplicates skipped");
        var chunksPerDoc = documents.Select(doc => (Doc: doc, Chunks: chunker.Chunk(doc))).ToList();
        var sizes = chunksPerDoc.SelectMany(d => d.Chunks).Select(c => c.Text.Length).Order().ToList();

        if (sizes.Count == 0)
        {
            Console.WriteLine($"No stored pages in {dataDir}. Run `ingest` first.");
            return;
        }

        // Nearest-rank percentile (same definition as the Python CLI)
        int Percentile(double p)
        {
            return sizes[Math.Max(0, (int)Math.Ceiling(p * sizes.Count) - 1)];
        }

        var perPage = ((double)sizes.Count / documents.Count).ToString("0.0", CultureInfo.InvariantCulture);
        Console.WriteLine($"{documents.Count} pages -> {sizes.Count} chunks ({perPage}/page)");
        Console.WriteLine($"chars: min {sizes[0]}, median {Percentile(0.5)}, p90 {Percentile(0.9)}, max {sizes[^1]}");
        Console.WriteLine($"chunks under 200 chars: {sizes.Count(s => s < 200)}");

        var match = chunksPerDoc.FirstOrDefault(d => show.Length > 0 && d.Doc.Url.ToString().Contains(show, StringComparison.Ordinal));
        if (match.Doc is not null)
        {
            Console.WriteLine($"\n=== {match.Doc.Url} ({match.Chunks.Count} chunks)");
            foreach (var c in match.Chunks)
            {
                Console.WriteLine($"\n--- #{c.Position} [{c.Text.Length} chars] heading={c.Heading ?? "None"}\n{c.Text}");
            }
        }
    }

    private static int GetIntOption(string[] args, string name, int fallback)
    {
        return int.Parse(GetOption(args, name, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
    }

    // Minimal option parsing, so the comparison with Python's argparse stays readable.
    // (System.CommandLine is the full-featured .NET option.)
    private static string GetOption(string[] args, string name, string fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
}
