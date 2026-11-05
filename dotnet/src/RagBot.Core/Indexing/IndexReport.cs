// Mirrors IndexReport in python/src/ragbot/indexing/service.py.
namespace RagBot.Core.Indexing;

public sealed class IndexReport
{
    public IList<Uri> Indexed { get; } = new List<Uri>();

    public IList<Uri> Unchanged { get; } = new List<Uri>();

    public IList<Uri> Deleted { get; } = new List<Uri>();

    public int ChunksEmbedded { get; set; }
}
