// robots.txt policy. Mirrors python/src/ragbot/ingestion/adapters/robots.py.
// .NET has no built-in parser (Python does), so this is a small one covering the common
// subset: User-agent groups plus Allow/Disallow path prefixes, where the longest match wins.
namespace RagBot.Core.Ingestion.Adapters;

public sealed class RobotsPolicy
{
    private readonly List<(string Path, bool Allow)> _rules;

    public RobotsPolicy(string robotsTxt, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(robotsTxt);
        var groups = Parse(robotsTxt);
        _rules = groups.FirstOrDefault(g => g.Agents.Any(a =>
                     a != "*" && userAgent.Contains(a, StringComparison.OrdinalIgnoreCase))).Rules
                 ?? groups.FirstOrDefault(g => g.Agents.Contains("*")).Rules
                 ?? [];
    }

    /// <summary>Download robots.txt. A missing file (404) means everything is allowed.</summary>
    public static async Task<RobotsPolicy> LoadAsync(
        HttpClient client, Uri baseUrl, string userAgent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var response = await client.GetAsync(new Uri(baseUrl, "/robots.txt"), ct)
            .ConfigureAwait(false);
        var text = response.IsSuccessStatusCode
            ? await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)
            : "";
        return new RobotsPolicy(text, userAgent);
    }

    public bool IsAllowed(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var path = url.PathAndQuery;
        var match = _rules
            .Where(r => path.StartsWith(r.Path, StringComparison.Ordinal))
            .OrderByDescending(r => r.Path.Length)
            .ThenByDescending(r => r.Allow) // on a tie, Allow wins
            .FirstOrDefault();
        return match == default || match.Allow;
    }

    private static List<(List<string> Agents, List<(string Path, bool Allow)> Rules)> Parse(
        string text)
    {
        var groups = new List<(List<string> Agents, List<(string, bool)> Rules)>();
        var lastLineWasAgent = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Split('#')[0].Trim().TrimStart('﻿'); // strip comments + BOM
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            var key = line[..colon].Trim().ToUpperInvariant();
            var value = line[(colon + 1)..].Trim();

            if (key == "USER-AGENT")
            {
                if (!lastLineWasAgent)
                {
                    groups.Add(([], []));
                }

                groups[^1].Agents.Add(value);
                lastLineWasAgent = true;
                continue;
            }

            lastLineWasAgent = false;
            if (groups.Count == 0 || value.Length == 0) // "Disallow:" (empty) = allow all
            {
                continue;
            }

            if (key is "ALLOW" or "DISALLOW")
            {
                groups[^1].Rules.Add((value, key == "ALLOW"));
            }
        }

        return groups;
    }
}
