using System.Text;
using System.Text.RegularExpressions;

namespace Rettungskarten.Infrastructure.Http;

/// <summary>
/// The allow/disallow rules from one host's robots.txt that apply to this tool, following RFC 9309:
/// the group(s) naming this tool's product token apply if any exist, otherwise the "*" group(s); the
/// longest matching rule wins, and <c>Allow</c> wins a tie. Patterns support the RFC's <c>*</c>
/// wildcard and trailing <c>$</c> end anchor - which is exactly what the Stellantis sites'
/// <c>Disallow: /*.pdf$</c> relies on. Anything else in the file (Sitemap, Crawl-delay, unknown
/// directives) is ignored.
/// </summary>
public sealed class RobotsTxtRules
{
    public static readonly RobotsTxtRules AllowAll = new([]);

    private readonly IReadOnlyList<Rule> _rules;

    private RobotsTxtRules(IReadOnlyList<Rule> rules) => _rules = rules;

    public static RobotsTxtRules Parse(string content, string productToken)
    {
        var groups = new List<(List<string> Agents, List<Rule> Rules)>();
        List<string>? currentAgents = null;
        List<Rule>? currentRules = null;
        var lastLineWasAgent = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            if (key == "user-agent")
            {
                if (!lastLineWasAgent || currentAgents is null)
                {
                    currentAgents = [];
                    currentRules = [];
                    groups.Add((currentAgents, currentRules));
                }

                currentAgents.Add(value.ToLowerInvariant());
                lastLineWasAgent = true;
                continue;
            }

            lastLineWasAgent = false;
            if (currentRules is null || (key != "allow" && key != "disallow"))
            {
                continue;
            }

            // An empty "Disallow:" means "nothing is disallowed" - it adds no rule.
            if (value.Length > 0)
            {
                currentRules.Add(new Rule(key == "allow", value, ToRegex(value)));
            }
        }

        var token = productToken.ToLowerInvariant();
        var specific = groups.Where(g => g.Agents.Any(a => a != "*" && token.Contains(a, StringComparison.Ordinal))).ToList();
        var applicable = specific.Count > 0 ? specific : groups.Where(g => g.Agents.Contains("*")).ToList();

        return new RobotsTxtRules(applicable.SelectMany(g => g.Rules).ToList());
    }

    public bool IsAllowed(string pathAndQuery)
    {
        Rule? best = null;
        foreach (var rule in _rules)
        {
            if (!rule.Regex.IsMatch(pathAndQuery))
            {
                continue;
            }

            if (best is null || rule.Pattern.Length > best.Pattern.Length ||
                (rule.Pattern.Length == best.Pattern.Length && rule.Allow))
            {
                best = rule;
            }
        }

        return best?.Allow ?? true;
    }

    private static string StripComment(string line)
    {
        var hash = line.IndexOf('#');
        return hash < 0 ? line : line[..hash];
    }

    private static Regex ToRegex(string pattern)
    {
        var anchoredAtEnd = pattern.EndsWith('$');
        var body = anchoredAtEnd ? pattern[..^1] : pattern;

        var sb = new StringBuilder("^");
        var parts = body.Split('*');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(".*");
            }

            sb.Append(Regex.Escape(parts[i]));
        }

        if (anchoredAtEnd)
        {
            sb.Append('$');
        }

        return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private sealed record Rule(bool Allow, string Pattern, Regex Regex);
}
