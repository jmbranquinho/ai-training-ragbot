// Mirrors Document in python/src/ragbot/domain/models.py.
using System.Security.Cryptography;
using System.Text;

namespace RagBot.Core.Domain;

/// <summary>A single page fetched from the source website, converted to markdown.</summary>
public sealed record Document(
    Uri Url,
    string Title,
    string Content,
    DateTimeOffset FetchedAt,
    string? Section = null,
    DateOnly? UpdatedOn = null)
{
    /// <summary>
    /// SHA-256 of the content. Used to detect whether a page changed since last run.
    /// The site sends no ETag/Last-Modified headers, so hashing what we extracted is the
    /// most reliable change signal we have.
    /// </summary>
    public string ContentHash
    {
        get
        {
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Content)));
        }
    }
}
