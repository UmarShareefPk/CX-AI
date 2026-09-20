using System.Text.Json;
using Cx.Tests.Support;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Cx.Tests;

/// <summary>Starts the real, built MCP server as a child process over stdio, exactly as the API does.</summary>
public class McpServerProcessTests(SeededDatabase seeded) : IClassFixture<SeededDatabase>
{
    private static readonly string ServerDll = Path.Combine(AppContext.BaseDirectory, "mcp", "Cx.McpServer.dll");

    private async Task<McpClient> StartAsync(string? role, string? dealerId)
    {
        Assert.True(File.Exists(ServerDll), $"MCP server was not built next to the tests: {ServerDll}");
        var env = new Dictionary<string, string?>
        {
            ["Mongo__ConnectionString"] = MongoSupport.ConnectionString,
            ["Mongo__Database"] = seeded.Name,
        };
        if (role is not null) env["Cx__Scope__Role"] = role;
        if (dealerId is not null) env["Cx__Scope__DealerId"] = dealerId;

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "cx-mcp-test", Command = "dotnet", Arguments = [ServerDll], EnvironmentVariables = env,
        });
        return await McpClient.CreateAsync(transport);
    }

    private static string Text(CallToolResult result) => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    [MongoFact]
    public async Task The_server_advertises_its_tools()
    {
        await using var client = await StartAsync("Dealer", "HND-1003");
        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();

        Assert.Superset(new HashSet<string>
        {
            "get_dealer_score", "get_dealer_rank", "get_top_ranked_score", "get_leaderboard", "get_score_trend", "search_policy",
        }, names);
    }

    [MongoFact]
    public async Task A_dealer_scoped_server_returns_that_dealers_score()
    {
        await using var client = await StartAsync("Dealer", "HND-1003");
        var result = await client.CallToolAsync("get_dealer_score", Args(("startDate", "2026-03-01"), ("endDate", "2026-09-20")));

        Assert.NotEqual(true, result.IsError);
        using var json = JsonDocument.Parse(Text(result));
        var mine = seeded.Data.Responses.Where(r => r.DealerId == "HND-1003").ToList();
        Assert.Contains("HND-1003", json.RootElement.GetProperty("dealer").GetString());
        Assert.Equal(mine.Count, json.RootElement.GetProperty("surveyCount").GetInt32());
        Assert.Equal(Math.Round(mine.Average(r => r.NetScore), 2), json.RootElement.GetProperty("score").GetDouble());
    }

    [MongoFact]
    public async Task A_dealer_scoped_server_refuses_to_look_at_another_dealer_even_if_asked_to()
    {
        await using var client = await StartAsync("Dealer", "HND-1003");

        foreach (var tool in new[] { "get_dealer_score", "get_dealer_rank", "get_score_trend" })
        {
            var result = await client.CallToolAsync(tool, Args(("dealerCode", "HND-1001"), ("period", "last_6_months")));
            Assert.True(result.IsError, $"{tool} must refuse another dealer's code");
            Assert.Contains("own dealership", Text(result));
            Assert.DoesNotContain("Northgate", Text(result));
        }
    }

    [MongoFact]
    public async Task The_leaderboard_is_admin_only_and_the_top_dealer_name_is_hidden_from_dealers()
    {
        await using var dealer = await StartAsync("Dealer", "HND-1003");
        var denied = await dealer.CallToolAsync("get_leaderboard", Args(("period", "last_6_months")));
        Assert.True(denied.IsError);
        Assert.Contains("administrators", Text(denied));

        var top = await dealer.CallToolAsync("get_top_ranked_score", Args(("period", "last_6_months")));
        Assert.NotEqual(true, top.IsError);
        Assert.DoesNotContain("topDealer", Text(top));
        foreach (var d in seeded.Data.Dealers) Assert.DoesNotContain(d.Name, Text(top));

        await using var admin = await StartAsync("Admin", null);
        var board = await admin.CallToolAsync("get_leaderboard", Args(("period", "last_6_months")));
        Assert.NotEqual(true, board.IsError);
        using var json = JsonDocument.Parse(Text(board));
        Assert.Equal(10, json.RootElement.GetProperty("dealers").GetArrayLength());
    }

    [MongoFact]
    public async Task An_administrator_must_name_the_dealer_for_a_rank_lookup()
    {
        await using var admin = await StartAsync("Admin", null);
        var result = await admin.CallToolAsync("get_dealer_rank", Args(("period", "last_6_months")));
        Assert.True(result.IsError);

        var ok = await admin.CallToolAsync("get_dealer_rank", Args(("period", "last_6_months"), ("dealerCode", "HND-1001")));
        Assert.NotEqual(true, ok.IsError);
    }

    [MongoFact]
    public async Task Bad_dates_produce_a_readable_tool_error()
    {
        await using var client = await StartAsync("Dealer", "HND-1003");
        var result = await client.CallToolAsync("get_dealer_score", Args(("startDate", "yesterday")));
        Assert.True(result.IsError);
        Assert.Contains("yyyy-MM-dd", Text(result));
    }

    [MongoFact]
    public async Task The_server_refuses_to_start_without_an_explicit_scope_so_it_can_never_default_to_admin()
    {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await StartAsync(role: null, dealerId: null);
        });
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await StartAsync("Dealer", dealerId: null);
        });
    }
}
