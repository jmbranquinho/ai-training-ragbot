// Command-line entry point. Mirrors python/src/ragbot/cli.py.
// Run with: dotnet run --project src/RagBot.Cli -- ingest --max-pages 5
//
// This is the "composition root": the one place that picks concrete adapters and wires
// them together. (A bigger app would use Microsoft.Extensions.DependencyInjection here.)
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RagBot.Core;
using RagBot.Core.Chat;
using RagBot.Core.Chat.Adapters;
using RagBot.Core.Chat.Ports;
using RagBot.Core.Chat.Retrieval;
using RagBot.Core.Domain;
using RagBot.Core.Evaluation;
using RagBot.Core.Evaluation.Adapters;
using RagBot.Core.Indexing;
using RagBot.Core.Indexing.Adapters;
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
                     --section <name>    URL path section, or "all" for the whole site
                                         (default: renovasjon-og-miljo)
                     --max-pages <n>     0 = no limit (default: 10)
                     --delay <seconds>   between requests (default: 1.0)
                     --only-new          skip pages already stored (resume an interrupted crawl)
                     --data-dir <path>   where pages are stored (default: data/raw)
          update   ingest + index in one go; takes the options of both
                     e.g. update --section all --max-pages 0 --only-new
          chunk    Chunk stored pages and print statistics
                     --strategy <name>   headings | window (default: headings)
                     --max-chars <n>     headings: max body size (default: 1500)
                     --size <n>          window: window size (default: 1000)
                     --overlap <n>       window: overlap (default: 200)
                     --data-dir <path>   default: data/raw
                     --show <text>       print the chunks of the first page whose URL contains this
          index    Chunk, embed and store pages in pgvector
                     (same --strategy/--max-chars/--size/--overlap/--data-dir options as chunk)
                     --rebuild           drop the index first
          search   Show the chunks most relevant to a question
                     <question>          the question, in quotes
                     --top-k <n>         default: 5
                     --mode <name>       vector | keyword | hybrid (default: hybrid)
          ask      Answer one question with sources
                     <question>          the question, in quotes
                     --top-k <n>         chunks given to the model (default: 5)
                     --mode <name>       vector | keyword | hybrid (default: hybrid)
          chat     Interactive chat (empty line quits); same --top-k/--mode options as ask
          eval     Evaluate on eval/questions.json
                     retrieval           hit@k and MRR per mode
                       --modes <list>    default: vector,keyword,hybrid
                     answers             answer quality, graded by an LLM judge
                       --mode / --top-k  as for ask

        index, search, ask, chat and eval take --schema <name> (default: dotnet) to pick an
        index variant, e.g. one built with other chunking settings.
        """;

    private const string DbSchema = "dotnet"; // the Python version uses "python" in the same database

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep æøå readable
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

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
            case "update":
                await RunIngestAsync(args).ConfigureAwait(false);
                await RunIndexAsync(args).ConfigureAwait(false);
                return 0;
            case "chunk":
                await RunChunkAsync(args).ConfigureAwait(false);
                return 0;
            case "index":
                await RunIndexAsync(args).ConfigureAwait(false);
                return 0;
            case "search" when args.Length > 1:
                await RunSearchAsync(args[1], GetIntOption(args, "--top-k", 5), GetOption(args, "--mode", "hybrid"), GetOption(args, "--schema", DbSchema)).ConfigureAwait(false);
                return 0;
            case "ask" when args.Length > 1:
                await RunAskAsync(args[1], args).ConfigureAwait(false);
                return 0;
            case "chat":
                await RunChatAsync(args).ConfigureAwait(false);
                return 0;
            case "eval" when args.Length > 1 && args[1] == "retrieval":
                await RunEvalRetrievalAsync(args).ConfigureAwait(false);
                return 0;
            case "eval" when args.Length > 1 && args[1] == "answers":
                await RunEvalAnswersAsync(args).ConfigureAwait(false);
                return 0;
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    private static async Task RunIngestAsync(string[] args)
    {
        var section = GetOption(args, "--section", "renovasjon-og-miljo");
        var pathPrefix = section == "all" ? "/" : $"/{section.Trim('/')}/";
        var maxPagesOption = GetIntOption(args, "--max-pages", 10);
        int? maxPages = maxPagesOption > 0 ? maxPagesOption : null; // 0 = no limit

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
            pathPrefix,
            loggerFactory.CreateLogger<SiteMapUrlSource>());
        var scraper = new HttpScraper(
            client, robots, loggerFactory.CreateLogger<HttpScraper>(), TimeSpan.FromSeconds(delaySeconds));
        var store = new FileDocumentStore(dataDir);
        var service = new IngestService(source, scraper, store, loggerFactory.CreateLogger<IngestService>());

        var report = await service.RunAsync(onlyNew: args.Contains("--only-new"), maxPages: maxPages).ConfigureAwait(false);

        loggerFactory.Dispose(); // flush the console logger before printing the summary
        Console.WriteLine(
            $"\n{report.New.Count} new, {report.Updated.Count} updated, "
            + $"{report.Unchanged.Count} unchanged. Files in {dataDir}");
    }

    private static async Task RunIndexAsync(string[] args)
    {
        var settings = Settings.Load();
        var dataDir = Path.GetFullPath(GetOption(args, "--data-dir", Path.Combine("data", "raw")));

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options => options.SingleLine = true);
        });
        using var index = new PgVectorChunkIndex(settings.PostgresConnectionString, GetOption(args, "--schema", DbSchema), settings.EmbeddingDimensions);
        var service = new IndexService(
            new FileDocumentStore(dataDir),
            MakeChunker(args),
            MakeEmbedder(settings),
            index,
            loggerFactory.CreateLogger<IndexService>());

        var report = await service.RunAsync(rebuild: args.Contains("--rebuild")).ConfigureAwait(false);

        loggerFactory.Dispose(); // flush the console logger before printing the summary
        Console.WriteLine(
            $"\n{report.Indexed.Count} pages indexed ({report.ChunksEmbedded} chunks embedded), "
            + $"{report.Unchanged.Count} unchanged, {report.Deleted.Count} removed.");
    }

    private static async Task RunSearchAsync(string question, int topK, string mode, string schema)
    {
        var settings = Settings.Load();
        using var index = new PgVectorChunkIndex(settings.PostgresConnectionString, schema, settings.EmbeddingDimensions);
        var retriever = MakeRetriever(settings, mode, index);

        var hits = await retriever.RetrieveAsync(question, topK).ConfigureAwait(false);
        for (var i = 0; i < hits.Count; i++)
        {
            var firstLine = hits[i].Chunk.Text.Split('\n')[0];
            var score = hits[i].Score.ToString("0.000", CultureInfo.InvariantCulture);
            Console.WriteLine($"{i + 1}. [{score}] {firstLine}\n   {hits[i].Chunk.DocumentUrl}");
        }
    }

    private static async Task RunAskAsync(string question, string[] args)
    {
        var settings = Settings.Load();
        using var index = new PgVectorChunkIndex(settings.PostgresConnectionString, GetOption(args, "--schema", DbSchema), settings.EmbeddingDimensions);
        var service = MakeChatService(settings, args, index);

        PrintAnswer(await service.AskAsync(question).ConfigureAwait(false));
    }

    private static async Task RunChatAsync(string[] args)
    {
        var settings = Settings.Load();
        using var index = new PgVectorChunkIndex(settings.PostgresConnectionString, GetOption(args, "--schema", DbSchema), settings.EmbeddingDimensions);
        var service = MakeChatService(settings, args, index);

        Console.WriteLine("Spør om Stavanger kommune / Ask about Stavanger kommune.");
        Console.WriteLine("Tom linje avslutter / An empty line quits.");
        while (true)
        {
            Console.Write("\n> ");
            var question = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(question)) // empty line (or end of input) quits
            {
                return;
            }

            PrintAnswer(await service.AskAsync(question).ConfigureAwait(false));
        }
    }

    private static async Task RunEvalRetrievalAsync(string[] args)
    {
        var settings = Settings.Load();
        var schema = GetOption(args, "--schema", DbSchema);
        var questions = QuestionSet.Load(QuestionsPath(args));
        using var index = new PgVectorChunkIndex(settings.PostgresConnectionString, schema, settings.EmbeddingDimensions);

        Console.WriteLine($"Retrieval on {schema}: {questions.Count(q => q.Answerable)} answerable questions");
        Console.WriteLine($"{"mode",-8} {"hit@1",6} {"hit@3",6} {"hit@5",6} {"hit@10",6} {"MRR",6}  not found");
        foreach (var mode in GetOption(args, "--modes", "vector,keyword,hybrid").Split(','))
        {
            var (results, s) = await EvaluationService.EvaluateRetrievalAsync(MakeRetriever(settings, mode, index), questions).ConfigureAwait(false);
            var hits = string.Join(" ", EvaluationService.Ks.Select(k => s.HitAt[k].ToString("0%", CultureInfo.InvariantCulture).PadLeft(6)));
            var missed = results.Where(r => r.Rank is null).Select(r => r.QuestionId).ToList();
            var missedText = missed.Count > 0 ? $"[{string.Join(", ", missed)}]" : "";
            Console.WriteLine($"{mode,-8} {hits} {s.Mrr.ToString("0.000", CultureInfo.InvariantCulture),6}  {s.NotFound} {missedText}");
            SaveResults($"retrieval-{schema}-{mode}", results);
        }
    }

    private static async Task RunEvalAnswersAsync(string[] args)
    {
        var settings = Settings.Load();
        var schema = GetOption(args, "--schema", DbSchema);
        var mode = GetOption(args, "--mode", "hybrid");
        var topK = GetIntOption(args, "--top-k", 5);
        using var index = new PgVectorChunkIndex(settings.PostgresConnectionString, schema, settings.EmbeddingDimensions);
        var client = AzureOpenAIClientFactory.Create(settings.AzureOpenAIEndpoint, settings.AzureOpenAIApiKey);
        var judge = new AzureOpenAIJudge(client, settings.ChatDeployment);

        var results = await EvaluationService.EvaluateAnswersAsync(
            MakeChatService(settings, args, index), judge, QuestionSet.Load(QuestionsPath(args))).ConfigureAwait(false);

        foreach (var r in results)
        {
            Console.WriteLine($"{r.QuestionId} {r.Grade.ToString().ToLowerInvariant(),-9} cited={(r.CitedExpectedPage ? "y" : "-")}  {r.Reason}");
        }

        var answerable = results.Where(r => r.Answerable).ToList();
        var unanswerable = results.Where(r => !r.Answerable).ToList();
        string Share(List<AnswerResult> rows, Grade grade)
        {
            return $"{rows.Count(r => r.Grade == grade)}/{rows.Count}";
        }

        Console.WriteLine($"\n{schema} / {mode} / top_k={topK}");
        Console.WriteLine(
            $"  answerable:   correct {Share(answerable, Grade.Correct)}, partial {Share(answerable, Grade.Partial)}, "
            + $"incorrect {Share(answerable, Grade.Incorrect)}, cited a correct page {answerable.Count(r => r.CitedExpectedPage)}/{answerable.Count}, "
            + $"wrongly said 'don't know' {answerable.Count(r => r.SaidUnknown)}");
        Console.WriteLine($"  unanswerable: correctly refused {Share(unanswerable, Grade.Correct)}");
        SaveResults($"answers-{schema}-{mode}-k{topK}", results);
    }

    /// <summary>Full per-question results, so runs can be compared later (eval/results/, git-ignored).</summary>
    private static void SaveResults<T>(string name, IEnumerable<T> rows)
    {
        var folder = Path.Combine(RepoRoot(), "eval", "results");
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var json = JsonSerializer.Serialize(rows, JsonOptions);
        File.WriteAllText(Path.Combine(folder, $"{stamp}-dotnet-{name}.json"), json);
    }

    private static string QuestionsPath(string[] args)
    {
        return GetOption(args, "--questions", Path.Combine(RepoRoot(), "eval", "questions.json"));
    }

    /// <summary>The folder above dotnet/: found by walking up from the current folder.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "eval")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("eval/ folder not found above the current folder");
    }

    private static ChatService MakeChatService(Settings settings, string[] args, PgVectorChunkIndex index)
    {
        var client = AzureOpenAIClientFactory.Create(settings.AzureOpenAIEndpoint, settings.AzureOpenAIApiKey);
        var generator = new AzureOpenAIAnswerGenerator(client, settings.ChatDeployment);
        var retriever = MakeRetriever(settings, GetOption(args, "--mode", "hybrid"), index);
        return new ChatService(retriever, generator, GetIntOption(args, "--top-k", 5));
    }

    private static void PrintAnswer(Answer answer)
    {
        Console.WriteLine($"\n{answer.Text}");
        if (answer.Sources.Count == 0)
        {
            return;
        }

        Console.WriteLine("\nKilder:"); // "Sources"
        for (var i = 0; i < answer.Sources.Count; i++)
        {
            var source = answer.Sources[i];
            var where = source.Heading is null ? "" : $" > {source.Heading}";
            Console.WriteLine($"  {i + 1}. {source.DocumentTitle}{where}\n     {source.DocumentUrl}");
        }
    }

    private static IRetriever MakeRetriever(Settings settings, string mode, PgVectorChunkIndex index)
    {
        return mode switch
        {
            "keyword" => new KeywordRetriever(index), // no embedding call needed
            "vector" => new VectorRetriever(MakeEmbedder(settings), index),
            _ => new HybridRetriever(MakeEmbedder(settings), index),
        };
    }

    private static AzureOpenAIEmbedder MakeEmbedder(Settings settings)
    {
        var client = AzureOpenAIClientFactory.Create(settings.AzureOpenAIEndpoint, settings.AzureOpenAIApiKey);
        return new AzureOpenAIEmbedder(client, settings.EmbeddingDeployment, settings.EmbeddingDimensions);
    }

    private static IChunker MakeChunker(string[] args)
    {
        return GetOption(args, "--strategy", "headings") == "window"
            ? new SlidingWindowChunker(GetIntOption(args, "--size", 1000), GetIntOption(args, "--overlap", 200))
            : new HeadingChunker(GetIntOption(args, "--max-chars", 1500));
    }

    /// <summary>Chunk every stored page and print size statistics (nothing is saved).</summary>
    private static async Task RunChunkAsync(string[] args)
    {
        var chunker = MakeChunker(args);
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
