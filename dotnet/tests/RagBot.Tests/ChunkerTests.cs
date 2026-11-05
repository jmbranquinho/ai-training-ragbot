// Mirrors python/tests/test_chunkers.py: same inputs, same expectations.
using RagBot.Core.Domain;
using RagBot.Core.Indexing.Chunkers;
using RagBot.Core.Indexing.Ports;

namespace RagBot.Tests;

public sealed class ChunkerTests
{
    private static readonly Uri Url = new("https://example.com/avfall/");

    private const string Markdown = """
        # Farlig avfall

        Kort intro.

        ## Hva er farlig avfall?

        Batterier og maling.

        ### Trygg levering

        Bruk originalemballasjen.

        ## Kontaktinformasjon

        ## Gjenvinning

        Lever på stasjonen.
        """;

    private static Document MakeDoc(string content = Markdown)
    {
        return new Document(Url, "Farlig avfall", content, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "avfall");
    }

    private static List<string> Bodies(IEnumerable<Chunk> chunks)
    {
        return chunks.Select(c => c.Text.Split("\n\n", 2)[1]).ToList(); // drop the header line
    }

    private static string Words(int count)
    {
        return string.Join(' ', Enumerable.Range(0, count).Select(i => $"ord{i:000}"));
    }

    [Fact]
    public void HeadingChunker_makes_one_chunk_per_section()
    {
        IChunker chunker = new HeadingChunker();

        var chunks = chunker.Chunk(MakeDoc());

        Assert.Equal(
            [
                null, // text under the h1, which is just the page title
                "Hva er farlig avfall?",
                "Hva er farlig avfall? > Trygg levering", // nested heading keeps its parent
                "Gjenvinning", // "Kontaktinformasjon" has no body, so it is skipped
            ],
            chunks.Select(c => c.Heading));
        Assert.Equal(
            "Farlig avfall > Hva er farlig avfall? > Trygg levering\n\nBruk originalemballasjen.",
            chunks[2].Text);
    }

    [Fact]
    public void Chunks_carry_metadata_for_citations()
    {
        var chunk = new HeadingChunker().Chunk(MakeDoc())[1];

        Assert.Equal($"{Url}#1", chunk.Id);
        Assert.Equal(1, chunk.Position);
        Assert.Equal(Url, chunk.DocumentUrl);
        Assert.Equal("Farlig avfall", chunk.DocumentTitle);
        Assert.Equal(new Dictionary<string, string> { ["chunker"] = "headings", ["section"] = "avfall" }, chunk.Metadata);
    }

    [Fact]
    public void HeadingChunker_splits_long_sections_at_paragraphs()
    {
        var paragraphs = Enumerable.Range(0, 5).Select(i => $"Avsnitt {i} " + new string('x', 80)).ToList();
        var doc = MakeDoc("## Lang seksjon\n\n" + string.Join("\n\n", paragraphs));

        var chunks = new HeadingChunker(maxChars: 200).Chunk(doc);

        Assert.Equal(
            [$"{paragraphs[0]}\n\n{paragraphs[1]}", $"{paragraphs[2]}\n\n{paragraphs[3]}", paragraphs[4]],
            Bodies(chunks));
        Assert.All(chunks, c => Assert.Equal("Lang seksjon", c.Heading));
    }

    [Fact]
    public void HeadingChunker_hard_splits_a_too_long_paragraph_at_a_space()
    {
        var words = Words(60);

        var bodies = Bodies(new HeadingChunker(maxChars: 150).Chunk(MakeDoc(words)));

        Assert.All(bodies, b => Assert.True(b.Length <= 150));
        Assert.Equal(words, string.Join(' ', bodies)); // nothing lost, nothing duplicated
        Assert.All(bodies, b => Assert.Equal(b.Trim(), b));
    }

    [Fact]
    public void SlidingWindow_overlaps_and_cuts_at_words()
    {
        var chunks = new SlidingWindowChunker(size: 200, overlap: 50).Chunk(MakeDoc(Words(100)));

        var bodies = Bodies(chunks);
        Assert.Equal(5, bodies.Count);
        Assert.All(bodies, b => Assert.True(b.Length <= 200));
        Assert.All(bodies, b => Assert.True(b.StartsWith("ord", StringComparison.Ordinal) && char.IsDigit(b[^1])));
        foreach (var (first, second) in bodies.Zip(bodies.Skip(1))) // neighbours overlap
        {
            Assert.Contains(first.Split(' ')[^1], second.Split(' ').Take(9));
        }

        Assert.StartsWith("ord000", bodies[0], StringComparison.Ordinal);
        Assert.EndsWith("ord099", bodies[^1], StringComparison.Ordinal);
        Assert.All(chunks, c => Assert.Null(c.Heading));
        Assert.All(chunks, c => Assert.Equal("window", c.Metadata!["chunker"]));
    }

    [Fact]
    public void Short_document_is_one_window()
    {
        var chunks = new SlidingWindowChunker().Chunk(MakeDoc("Kort tekst."));

        Assert.Equal(["Farlig avfall\n\nKort tekst."], chunks.Select(c => c.Text));
    }

    [Theory]
    [InlineData(50, 0)]
    [InlineData(200, 100)]
    [InlineData(200, -1)]
    public void SlidingWindow_rejects_bad_settings(int size, int overlap)
    {
        Assert.ThrowsAny<ArgumentException>(() => new SlidingWindowChunker(size, overlap));
    }
}
