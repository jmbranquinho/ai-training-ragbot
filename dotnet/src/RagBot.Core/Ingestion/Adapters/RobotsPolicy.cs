// robots.txt policy. Mirrors python/src/ragbot/ingestion/adapters/robots.py.
// .NET has no built-in parser (Python does), so this is a small one following RFC 9309:
// - User-agent groups; all groups naming the same agent are merged (wiki.gg has two "*" groups)
// - Allow/Disallow patterns with "*" (any characters) and a trailing "$" (end of URL)
// - the longest matching pattern wins; on a tie, Allow wins
// Python's urllib.robotparser supports neither wildcards nor merged groups, which didn't matter
// for stavanger.kommune.no (the only site the Python version crawls).
using System.Text.RegularExpressions;

namespace RagBot.Core.Ingestion.Adapters;

public sealed class RobotsPolicy
{
    private readonly List<Rule> _rules;

    public RobotsPolicy(string robotsTxt, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(robotsTxt);
        var groups = Parse(robotsTxt);
        var specific = groups
            .Where(g => g.Agents.Any(a => a != "*" && userAgent.Contains(a, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var chosen = specific.Count > 0 ? specific : groups.Where(g => g.Agents.Contains("*")).ToList();
        _rules = chosen.SelectMany(g => g.Rules).ToList();
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
            .Where(r => r.Pattern.IsMatch(path))
            .OrderByDescending(r => r.Path.Length)
            .ThenByDescending(r => r.Allow) // on a tie, Allow wins
            .FirstOrDefault();
        return match is null || match.Allow;
    }

    private static List<Group> Parse(string text)
    {
        var groups = new List<Group>();
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
                    groups.Add(new Group([], []));
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
                groups[^1].Rules.Add(new Rule(value, key == "ALLOW", ToRegex(value)));
            }
        }

        return groups;
    }

    /// <summary>"/*?action=" → ^/.*\?action= ; "/*.css$" → ^/.*\.css$ ; otherwise a prefix match.</summary>
    private static Regex ToRegex(string pattern)
    {
        var anchored = pattern.EndsWith('$');
        var body = anchored ? pattern[..^1] : pattern;
        var regex = "^" + string.Join(".*", body.Split('*').Select(Regex.Escape)) + (anchored ? "$" : "");
        return new Regex(regex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private sealed record Rule(string Path, bool Allow, Regex Pattern);

    private sealed record Group(List<string> Agents, List<Rule> Rules);
}
