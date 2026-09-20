using System.Text.Json;
using Cx.Ai.Agent;
using Cx.Core.Rag;
using Cx.Core.Security;
using Microsoft.Extensions.AI;

namespace Cx.Tests;

public class DataScopeTests
{
    [Fact]
    public void A_dealer_always_resolves_to_its_own_dealer()
    {
        var scope = DataScope.ForDealer("HND-1003");
        Assert.Equal("HND-1003", scope.ResolveDealer(null));
        Assert.Equal("HND-1003", scope.ResolveDealer("hnd-1003"));
    }

    [Theory]
    [InlineData("HND-1001")]
    [InlineData("HND-1003 ; drop")]
    public void A_dealer_cannot_address_another_dealer(string other) =>
        Assert.Throws<ForbiddenException>(() => DataScope.ForDealer("HND-1003").ResolveDealer(other));

    [Fact]
    public void An_admin_may_see_everything_or_pick_a_dealer()
    {
        Assert.Null(DataScope.Admin.ResolveDealer(null));
        Assert.Null(DataScope.Admin.ResolveDealer("  "));
        Assert.Equal("HND-1001", DataScope.Admin.ResolveDealer("HND-1001"));
    }

    [Fact]
    public void Admin_only_operations_are_refused_for_dealers()
    {
        DataScope.Admin.RequireAdmin();
        Assert.Throws<ForbiddenException>(() => DataScope.ForDealer("HND-1003").RequireAdmin());
    }
}

public class PolicyChunkerTests
{
    private const string Doc = """
        # Sample Policy

        Intro text that belongs to no section.

        ## First topic
        Alpha beta gamma.

        ## Second topic!
        Delta epsilon.
        """;

    [Fact]
    public void Splits_on_second_level_headings_and_prefixes_the_document_title()
    {
        var passages = PolicyChunker.Split("sample", Doc);
        Assert.Equal(2, passages.Count);
        Assert.Equal("Sample Policy: First topic", passages[0].Title);
        Assert.Equal("sample#first-topic", passages[0].Id);
        Assert.Equal("sample#second-topic", passages[1].Id);
        Assert.Equal("Alpha beta gamma.", passages[0].Text);
    }

    [Fact]
    public void A_long_section_is_split_into_numbered_parts_on_line_boundaries()
    {
        var body = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"Line number {i} with some padding text."));
        var passages = PolicyChunker.Split("long", "# T\n\n## Big\n" + body, maxChars: 300);
        Assert.True(passages.Count > 1);
        Assert.All(passages, p => Assert.True(p.Text.Length <= 300));
        Assert.Equal("long#big-1", passages[0].Id);
    }

    [Fact]
    public void The_shipped_policy_documents_produce_unique_reasonably_sized_chunks()
    {
        var passages = PolicyIngestor.LoadPassages();
        Assert.True(passages.Count >= 20, $"expected a real knowledge base, got {passages.Count}");
        Assert.Equal(passages.Count, passages.Select(p => p.Id).Distinct().Count());
        Assert.All(passages, p => Assert.InRange(p.Text.Length, 40, 1200));
    }
}

public class ToolResultFormatterTests
{
    [Fact]
    public void A_single_text_block_is_shown_as_is()
    {
        var (text, isError) = ToolResultFormatter.Describe(new TextContent("{\"score\":80}"));
        Assert.Equal("{\"score\":80}", text);
        Assert.False(isError);
    }

    [Fact]
    public void Several_blocks_are_joined()
    {
        var (text, _) = ToolResultFormatter.Describe(new AIContent[] { new TextContent("one"), new TextContent("two") });
        Assert.Equal("one\ntwo", text);
    }

    [Fact]
    public void A_call_tool_result_envelope_reports_errors()
    {
        var json = JsonDocument.Parse("""{"content":[{"type":"text","text":"You can only access data for your own dealership."}],"isError":true}""").RootElement;
        var (text, isError) = ToolResultFormatter.Describe(json);
        Assert.True(isError);
        Assert.Equal("You can only access data for your own dealership.", text);
    }

    [Fact]
    public void Plain_json_and_strings_pass_through()
    {
        Assert.Equal("hello", ToolResultFormatter.Describe("hello").Text);
        Assert.Equal("{\"a\":1}", ToolResultFormatter.Describe(JsonDocument.Parse("{\"a\":1}").RootElement).Text);
        Assert.Equal("", ToolResultFormatter.Describe(null).Text);
    }
}
