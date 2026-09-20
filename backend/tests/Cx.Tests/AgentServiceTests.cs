using System.Text.Json;
using Cx.Ai;
using Cx.Ai.Agent;
using Cx.Core.Domain;
using Cx.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Cx.Tests.Support.ScriptedChatClient;

namespace Cx.Tests;

public class AgentServiceTests
{
    private static readonly AgentIdentity Dealer = new("user-1", "Summit Honda", UserRole.Dealer, "HND-1003", "Summit Honda");

    private readonly InMemoryConversationStore _store = new();
    private int _toolInvocations;

    private AIFunction ScoreTool() => AIFunctionFactory.Create(
        (string? period) =>
        {
            _toolInvocations++;
            return "{\"score\":80,\"surveyCount\":12}";
        },
        "get_dealer_score", "Returns the dealer score.");

    private AgentService Agent(IChatClient model, int maxIterations = 10, AiOptions? options = null) => new(
        new FakeChatClientFactory(model, maxIterations), new FakeToolSource(ScoreTool()), _store,
        Options.Create(options ?? new AiOptions()), TimeProvider.System, NullLogger<AgentService>.Instance);

    private static async Task<List<AgentEvent>> Run(AgentService agent, string message, string? conversationId = null, AgentIdentity? user = null, string provider = "ollama")
    {
        var events = new List<AgentEvent>();
        await foreach (var e in agent.RunAsync(new AgentRequest(user ?? Dealer, provider, "gemma4:e4b", message, conversationId, null, null)))
            events.Add(e);
        return events;
    }

    private static List<ChatMessage> Stored(Conversation c) =>
        c.Messages.Select(m => JsonSerializer.Deserialize<ChatMessage>(m, AIJsonUtilities.DefaultOptions)!).ToList();

    [Fact]
    public async Task The_model_calls_a_tool_reads_the_result_and_answers()
    {
        var model = new ScriptedChatClient(Call("get_dealer_score", new() { ["period"] = "this_month" }), Say("Your score is 80."));
        var events = await Run(Agent(model), "What is my score?");

        Assert.Equal(["conversation", "tool_call", "tool_result", "text", "done"], events.Select(e => e.Type));
        Assert.Equal("get_dealer_score", events[1].ToolName);
        Assert.Contains("\"score\":80", events[2].Result);
        Assert.Equal(1, _toolInvocations);
        Assert.Equal(2, model.Calls); // one round to decide on the tool, one to write the answer
    }

    [Fact]
    public async Task The_whole_turn_is_persisted_but_the_system_prompt_is_not()
    {
        var model = new ScriptedChatClient(Call("get_dealer_score"), Say("Your score is 80."));
        var events = await Run(Agent(model), "What is my score?");

        var saved = Stored(_store.Items[events[0].ConversationId!]);
        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant], saved.Select(m => m.Role));
        Assert.Contains(saved[1].Contents, c => c is FunctionCallContent);
        Assert.Contains(saved[2].Contents, c => c is FunctionResultContent);
        Assert.Equal("Your score is 80.", saved[3].Text);

        var firstPrompt = model.Seen[0];
        Assert.Equal(ChatRole.System, firstPrompt[0].Role);
        Assert.Contains("HND-1003", firstPrompt[0].Text);
    }

    [Fact]
    public async Task A_follow_up_turn_replays_the_earlier_turn_to_the_model()
    {
        var model = new ScriptedChatClient(Call("get_dealer_score"), Say("First answer."), Say("Second answer."));
        var agent = Agent(model);
        var first = await Run(agent, "What is my score?");
        await Run(agent, "And last month?", first[0].ConversationId);

        var secondTurnPrompt = model.Seen[2];
        Assert.Contains(secondTurnPrompt, m => m.Role == ChatRole.User && m.Text == "What is my score?");
        Assert.Contains(secondTurnPrompt, m => m.Contents.OfType<FunctionResultContent>().Any());
        Assert.Equal("And last month?", secondTurnPrompt[^1].Text);
    }

    [Fact]
    public async Task A_failed_model_call_is_rolled_back_out_of_the_history()
    {
        var okModel = new ScriptedChatClient(Say("First answer."));
        var first = await Run(Agent(okModel), "hello");
        var conversationId = first[0].ConversationId!;
        var countBefore = _store.Items[conversationId].Messages.Count;

        var failing = new ScriptedChatClient(Call("get_dealer_score"), Fail("model exploded"));
        var events = await Run(Agent(failing), "second question", conversationId);

        Assert.Equal("error", events[^1].Type);
        Assert.DoesNotContain("exploded", events[^1].Text); // internals are not leaked to the user
        Assert.Equal(countBefore, _store.Items[conversationId].Messages.Count);
    }

    [Fact]
    public async Task A_failed_first_turn_leaves_no_conversation_behind()
    {
        var events = await Run(Agent(new ScriptedChatClient(Fail("boom"))), "hi");
        Assert.Equal("error", events[^1].Type);
        Assert.Empty(_store.Items);
    }

    [Fact]
    public async Task Hitting_the_tool_call_limit_discards_the_turn_instead_of_saving_a_dangling_tool_call()
    {
        var endless = new ScriptedChatClient(Call("get_dealer_score")); // asks for the tool forever
        var events = await Run(Agent(endless, maxIterations: 2), "loop please");

        Assert.Equal("error", events[^1].Type);
        Assert.Empty(_store.Items);
        Assert.True(_toolInvocations <= 3, $"the loop must be bounded, but the tool ran {_toolInvocations} times");
    }

    [Fact]
    public async Task An_empty_answer_is_treated_as_a_failure()
    {
        var events = await Run(Agent(new ScriptedChatClient(Say("   "))), "hi");
        Assert.Equal("error", events[^1].Type);
        Assert.Empty(_store.Items);
    }

    [Fact]
    public async Task A_conversation_cannot_switch_provider()
    {
        var first = await Run(Agent(new ScriptedChatClient(Say("hi"))), "hello", provider: "ollama");
        var events = await Run(Agent(new ScriptedChatClient(Say("hi"))), "again", first[0].ConversationId, provider: "openai");

        Assert.Equal("error", events[^1].Type);
        Assert.Contains("switch provider", events[^1].Text);
    }

    [Fact]
    public async Task Another_users_conversation_cannot_be_continued()
    {
        var first = await Run(Agent(new ScriptedChatClient(Say("hi"))), "hello");
        var intruder = Dealer with { UserId = "user-2" };
        var events = await Run(Agent(new ScriptedChatClient(Say("hi"))), "hi", first[0].ConversationId, intruder);

        Assert.Equal("error", events[^1].Type);
        Assert.Equal("Conversation not found.", events[^1].Text);
    }

    [Fact]
    public void History_is_trimmed_at_a_user_message_so_tool_pairs_stay_intact()
    {
        var history = new List<ChatMessage>();
        for (var turn = 1; turn <= 4; turn++)
        {
            history.Add(new ChatMessage(ChatRole.User, $"q{turn}"));
            history.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"c{turn}", "t")]));
            history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent($"c{turn}", "r")]));
            history.Add(new ChatMessage(ChatRole.Assistant, $"a{turn}"));
        }

        var trimmed = AgentService.TrimToRecentTurns(history, 2);

        Assert.Equal(8, trimmed.Count);
        Assert.Equal("q3", trimmed[0].Text);
        Assert.Same(history, AgentService.TrimToRecentTurns(history, 10));
    }

    [Fact]
    public void The_system_prompt_scopes_dealers_and_admins_differently()
    {
        var today = new DateOnly(2026, 9, 20);
        var dealer = AgentPrompt.Build(Dealer, today, new DateOnly(2026, 9, 1), today);
        var admin = AgentPrompt.Build(Dealer with { Role = UserRole.Admin, DealerId = null }, today, null, null);

        Assert.Contains("only see this dealership", dealer);
        Assert.Contains("2026-09-01 to 2026-09-20", dealer);
        Assert.Contains("ADMINISTRATOR", admin);
        Assert.DoesNotContain("dashboard is currently filtered", admin);
    }
}
