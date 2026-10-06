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
///
/// Patterns and request paths are compared in the same percent-encoded form (RFC 9309 section 2.2.2):
/// a pattern written with literal non-ASCII characters ("/rettungsdatenblätter/") is encoded as UTF-8
/// octets, and every escape is upper-cased, so it matches the escaped path HttpClient actually sends.
/// Only the rules of the applicable groups are turned into regular expressions, and only interpreted
/// ones - a large robots.txt full of other bots' groups costs nothing beyond reading it.
/// </summary>
public sealed class RobotsTxtRules
{
    public static readonly RobotsTxtRules AllowAll = new([]);

    private readonly IReadOnlyList<Rule> _rules;

    private RobotsTxtRules(IReadOnlyList<Rule> rules) => _rules = rules;

    public static RobotsTxtRules Parse(string content, string productToken)
    {
        var groups = new List<(List<string> Agents, List<(bool Allow, string Pattern)> Rules)>();
        List<string>? currentAgents = null;
        List<(bool Allow, string Pattern)>? currentRules = null;
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
                currentRules.Add((key == "allow", value));
            }
        }

        var token = productToken.ToLowerInvariant();
        var specific = groups
            .Where(g => g.Agents.Any(a => a.Length > 0 && a != "*" && token.Contains(a, StringComparison.Ordinal)))
            .ToList();
        var applicable = specific.Count > 0 ? specific : groups.Where(g => g.Agents.Contains("*")).ToList();

        return new RobotsTxtRules(applicable
            .SelectMany(g => g.Rules)
            .Select(r =>
            {
                var pattern = NormalizePercentEncoding(r.Pattern);
                return new Rule(r.Allow, pattern, ToRegex(pattern));
            })
            .ToList());
    }

    public bool IsAllowed(string pathAndQuery)
    {
        if (_rules.Count == 0)
        {
            return true;
        }

        var path = NormalizePercentEncoding(pathAndQuery);
        Rule? best = null;
        foreach (var rule in _rules)
        {
            if (!rule.Regex.IsMatch(path))
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

    /// <summary>Percent-encodes every non-ASCII character as its UTF-8 octets and upper-cases the hex
    /// digits of existing escapes, so a pattern and a path that name the same octets compare equal.</summary>
    internal static string NormalizePercentEncoding(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '%' && i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                sb.Append('%').Append(char.ToUpperInvariant(value[i + 1])).Append(char.ToUpperInvariant(value[i + 2]));
                i += 2;
            }
            else if (c > 0x7F)
            {
                var length = char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]) ? 2 : 1;
                foreach (var octet in Encoding.UTF8.GetBytes(value.Substring(i, length)))
                {
                    sb.Append('%').Append(octet.ToString("X2"));
                }

                i += length - 1;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
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

        return new Regex(sb.ToString(), RegexOptions.CultureInvariant);
    }

    private sealed record Rule(bool Allow, string Pattern, Regex Regex);
}
