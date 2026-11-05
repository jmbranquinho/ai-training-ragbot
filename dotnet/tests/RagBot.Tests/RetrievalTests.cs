// Mirrors python/tests/test_retrieval.py: same inputs, same expectations.
using RagBot.Core.Chat.Ports;
using RagBot.Core.Chat.Retrieval;
using RagBot.Core.Domain;
using RagBot.Core.Indexing.Adapters;

namespace RagBot.Tests;

public sealed class RetrievalTests
{
    [Fact]
    public void Rrf_rewards_chunks_ranked_well_by_both_lists()
    {
        var fused = RankFusion.ReciprocalRankFusion([Ranking("a", "b", "c"), Ranking("c", "a", "d")], k: 60);

        Assert.Equal(["a", "c", "b", "d"], fused.Select(s => s.Chunk.Id));
        Assert.Equal((1.0 / 61) + (1.0 / 62), fused[0].Score); // rank 1 in the first list, rank 2 in the second
        Assert.Equal(1.0 / 62, fused[2].Score); // only in the first list, rank 2
    }

    [Fact]
    public void Rrf_breaks_ties_by_chunk_id()
    {
        var fused = RankFusion.ReciprocalRankFusion([Ranking("z"), Ranking("y")]);

        Assert.Equal(["y", "z"], fused.Select(s => s.Chunk.Id));
    }

    [Fact]
    public async Task Vector_and_keyword_retrievers_use_the_index()
    {
        var index = await MakeIndexAsync();
        IRetriever vector = new VectorRetriever(new FakeEmbedder(), index);
        IRetriever keyword = new KeywordRetriever(index);

        Assert.Equal("glass", (await vector.RetrieveAsync("hvor leveres glass", topK: 1))[0].Chunk.Id);
        Assert.Equal(["brikke"], (await keyword.RetrieveAsync("brikke container", topK: 5)).Select(s => s.Chunk.Id));
    }

    [Fact]
    public async Task Hybrid_retriever_returns_top_k_fused_results()
    {
        var retriever = new HybridRetriever(new FakeEmbedder(), await MakeIndexAsync(), candidates: 3);

        var results = await retriever.RetrieveAsync("farlig avfall maling", topK: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("maling", results[0].Chunk.Id); // first in both lists
        Assert.True(results[0].Score > results[1].Score);
    }

    private static Chunk MakeChunk(string id, string text = "")
    {
        return new Chunk(id, new Uri($"https://x.no/{id}/"), id, text.Length > 0 ? text : id, 0);
    }

    private static List<ScoredChunk> Ranking(params string[] ids)
    {
        return ids.Select(id => new ScoredChunk(MakeChunk(id), 1.0)).ToList();
    }

    private static async Task<InMemoryChunkIndex> MakeIndexAsync()
    {
        var index = new InMemoryChunkIndex();
        var texts = new Dictionary<string, string>
        {
            ["glass"] = "Glass og metall leveres i glassbeholderen.",
            ["maling"] = "Maling og batterier er farlig avfall.",
            ["brikke"] = "Bestill brikke til nedgravd container på nett.",
        };
        foreach (var (id, text) in texts)
        {
            await index.ReplaceDocumentAsync(new Uri($"https://x.no/{id}/"), "v1", [MakeChunk(id, text)], [FakeEmbedder.EmbedWords(text)]);
        }

        return index;
    }
}
