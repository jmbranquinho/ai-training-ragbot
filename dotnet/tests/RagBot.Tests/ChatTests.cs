// Mirrors python/tests/test_chat.py: same inputs, same expectations.
using RagBot.Core.Chat;

namespace RagBot.Tests;

public sealed class ChatTests
{
    private static readonly IReadOnlyList<RagBot.Core.Domain.ScoredChunk> Context = ChatTestData.Context;

    [Fact]
    public void Prompt_matches_the_shared_fixture()
    {
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "chat", "expected-prompt.txt"));

        Assert.Equal(expected, ChatTestData.Render(PromptBuilder.BuildMessages(ChatTestData.Question, Context)));
    }

    [Fact]
    public void ParseAnswer_keeps_only_real_cited_sources()
    {
        var answer = AnswerParser.Parse("""{"answer": "Lever den på stasjonen [1].", "sources": [2, 1, 2, 9]}""", Context);

        Assert.Equal("Lever den på stasjonen [1].", answer.Text);
        // cited in the text first, then the rest of the JSON list; 9 doesn't exist
        Assert.Equal([Context[0].Chunk.Id, Context[1].Chunk.Id], answer.Sources.Select(c => c.Id));
        Assert.False(answer.IsUnknown);
    }

    [Fact]
    public void ParseAnswer_renumbers_citations_to_match_the_source_list()
    {
        var answer = AnswerParser.Parse("""{"answer": "Glass [2]. Maling [1][7].", "sources": [1]}""", Context);

        Assert.Equal("Glass [1]. Maling [2].", answer.Text); // [7] points at nothing: removed
        Assert.Equal([Context[1].Chunk.Id, Context[0].Chunk.Id], answer.Sources.Select(c => c.Id));
    }

    [Fact]
    public void ParseAnswer_without_sources_is_unknown()
    {
        var answer = AnswerParser.Parse("""{"answer": "Jeg vet ikke.", "sources": []}""", Context);

        Assert.Equal("Jeg vet ikke.", answer.Text);
        Assert.True(answer.IsUnknown);
    }

    [Fact]
    public void ParseAnswer_tolerates_a_code_fence()
    {
        var answer = AnswerParser.Parse("```json\n{\"answer\": \"Ja [2].\", \"sources\": [2]}\n```", Context);

        Assert.Equal([Context[1].Chunk.Id], answer.Sources.Select(c => c.Id));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{"text": "missing answer"}""")]
    [InlineData("""{"answer": "x", "sources": "1"}""")]
    public void Unexpected_replies_are_unknown(string reply)
    {
        var answer = AnswerParser.Parse(reply, Context);

        Assert.True(answer.IsUnknown);
        Assert.Equal(reply, answer.Text);
    }

    [Fact]
    public async Task ChatService_passes_top_k_context_to_the_generator()
    {
        var retriever = new FakeRetriever(Context);
        var generator = new FakeAnswerGenerator();

        var answer = await new ChatService(retriever, generator, topK: 1).AskAsync("  Hvor leverer jeg maling?  ");

        Assert.Equal([("Hvor leverer jeg maling?", 1)], retriever.Calls); // question trimmed
        Assert.Equal([Context[0]], Assert.Single(generator.Contexts));
        Assert.Equal("svar", answer.Text);
    }

    [Fact]
    public async Task ChatService_says_it_does_not_know_without_calling_the_model()
    {
        var generator = new FakeAnswerGenerator();

        var answer = await new ChatService(new FakeRetriever([]), generator).AskAsync("Hvem vant VM i fotball?");

        Assert.True(answer.IsUnknown);
        Assert.Equal(ChatService.NoContextAnswer, answer.Text);
        Assert.Empty(generator.Contexts);
    }

    [Fact]
    public async Task ChatService_rejects_an_empty_question()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new ChatService(new FakeRetriever(Context), new FakeAnswerGenerator()).AskAsync("   "));
    }
}
