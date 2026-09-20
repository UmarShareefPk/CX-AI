using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Cx.Ai.Agent;

/// <summary>
/// Turns whatever a tool returned into display text. An MCP tool invoked as an AIFunction yields a TextContent for one
/// content block, AIContent[] for several, and a JsonElement CallToolResult envelope when the tool reported an error.
/// </summary>
public static class ToolResultFormatter
{
    public static (string Text, bool IsError) Describe(object? result) => result switch
    {
        null => ("", false),
        string s => (s, false),
        TextContent t => (t.Text, false),
        JsonElement json => DescribeJson(json),
        IEnumerable<AIContent> items => (Join(items), false),
        AIContent other => (other.ToString() ?? "", false),
        _ => (JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions), false),
    };

    private static (string, bool) DescribeJson(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.String) return (json.GetString() ?? "", false);

        // CallToolResult envelope: { "content": [ { "type": "text", "text": "..." } ], "isError": true }
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var isError = json.TryGetProperty("isError", out var e) && e.ValueKind == JsonValueKind.True;
            var text = new StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    text.AppendLine(t.GetString());
            }
            return (text.ToString().TrimEnd(), isError);
        }

        return (json.GetRawText(), false);
    }

    private static string Join(IEnumerable<AIContent> items) =>
        string.Join("\n", items.Select(i => i is TextContent t ? t.Text : i.ToString()));
}
