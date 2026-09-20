using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cx.Core.Rag;

public sealed record PolicyPassage(string Id, string Source, string Title, string Text);

/// <summary>
/// Splits a markdown policy document into passages: one per "## " section, so each passage is a self-contained
/// topic. A section longer than <c>maxChars</c> is split further on line boundaries.
/// </summary>
public static partial class PolicyChunker
{
    public static IReadOnlyList<PolicyPassage> Split(string source, string markdown, int maxChars = 1200)
    {
        var docTitle = source;
        string? heading = null;
        var body = new StringBuilder();
        var passages = new List<PolicyPassage>();

        void Flush()
        {
            if (heading is null || body.Length == 0) return;
            var title = $"{docTitle}: {heading}";
            var parts = SplitLong(body.ToString().Trim(), maxChars);
            for (var i = 0; i < parts.Count; i++)
            {
                var id = $"{source}#{Slug(heading)}" + (parts.Count > 1 ? $"-{i + 1}" : "");
                passages.Add(new PolicyPassage(id, source, title, parts[i]));
            }
            body.Clear();
        }

        foreach (var line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("# ")) docTitle = line[2..].Trim();
            else if (line.StartsWith("## "))
            {
                Flush();
                heading = line[3..].Trim();
            }
            else if (heading is not null) body.AppendLine(line);
        }

        Flush();
        return passages;
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static List<string> SplitLong(string text, int maxChars)
    {
        if (text.Length <= maxChars) return [text];
        var parts = new List<string>();
        var current = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (current.Length > 0 && current.Length + line.Length + 1 > maxChars)
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
            }
            current.AppendLine(line);
        }
        if (current.Length > 0) parts.Add(current.ToString().Trim());
        return parts;
    }

    private static string Slug(string heading) => NonAlnum().Replace(heading.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();
}
