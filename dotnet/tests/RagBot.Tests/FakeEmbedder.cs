// Deterministic "bag of words" embeddings: texts sharing words get similar vectors.
// Mirrors FakeEmbedder in python/tests/fakes.py. Good enough to test ranking without Azure.
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RagBot.Core.Indexing.Ports;

namespace RagBot.Tests;

internal sealed partial class FakeEmbedder : IEmbedder
{
    public const int Dimensions = 64;

    public List<IReadOnlyList<string>> Calls { get; } = [];

    public static ReadOnlyMemory<float> EmbedWords(string text)
    {
        var vector = new float[Dimensions];
        foreach (Match word in Word().Matches(text.ToLowerInvariant()))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(word.Value));
            vector[BitConverter.ToUInt32(hash, 0) % Dimensions] += 1f;
        }

        var norm = MathF.Sqrt(vector.Sum(x => x * x));
        return norm == 0 ? vector : vector.Select(x => x / norm).ToArray();
    }

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        Calls.Add(texts);
        IReadOnlyList<ReadOnlyMemory<float>> vectors = texts.Select(EmbedWords).ToList();
        return Task.FromResult(vectors);
    }

    [GeneratedRegex(@"\w+")]
    private static partial Regex Word();
}
