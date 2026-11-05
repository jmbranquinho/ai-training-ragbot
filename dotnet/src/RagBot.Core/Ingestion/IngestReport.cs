// Mirrors IngestReport in python/src/ragbot/ingestion/service.py.
namespace RagBot.Core.Ingestion;

public sealed class IngestReport
{
    public IList<Uri> New { get; } = new List<Uri>();

    public IList<Uri> Updated { get; } = new List<Uri>();

    public IList<Uri> Unchanged { get; } = new List<Uri>();
}
