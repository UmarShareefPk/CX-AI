using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cx.Core.Domain;
using Cx.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cx.Tests;

/// <summary>HTTP-level tests of authentication, authorization and tenant isolation against a throw-away database.</summary>
public class ApiTests(SeededDatabase seeded) : IClassFixture<SeededDatabase>, IDisposable
{
    private const string From = "2026-03-01";
    private const string To = "2026-09-20";

    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("Mongo:ConnectionString", MongoSupport.ConnectionString);
        b.UseSetting("Jwt:SigningKey", "test-signing-key-test-signing-key-0123456789");
    });

    private AppUser Dealer(string id) => seeded.Data.Users.Single(u => u.DealerId == id);
    private AppUser Admin => seeded.Data.Users.Single(u => u.Role == UserRole.Admin);

    private HttpClient Client() =>
        _factory.WithWebHostBuilder(b => b.UseSetting("Mongo:Database", seeded.Name)).CreateClient();

    private async Task<HttpClient> LoginAsync(AppUser user)
    {
        var client = Client();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { user.Username, user.Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }

    [MongoFact]
    public async Task Requests_without_a_token_are_rejected_but_health_is_open()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/responses")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/ai/chat", new { message = "hi" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [MongoFact]
    public async Task Login_fails_identically_for_a_wrong_password_and_an_unknown_user()
    {
        var client = Client();
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { username = Admin.Username, password = "nope" });
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { username = "ghost", password = "nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        var a = await wrong.Content.ReadFromJsonAsync<JsonElement>();
        var b = await unknown.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("title").GetString(), b.GetProperty("title").GetString()); // no user enumeration
        Assert.Equal(a.GetProperty("detail").GetString(), b.GetProperty("detail").GetString());
    }

    [MongoFact]
    public async Task A_tampered_token_is_rejected()
    {
        var client = await LoginAsync(Dealer("HND-1003"));
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^4] + "AAAA");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/dashboard/summary")).StatusCode);
    }

    [MongoFact]
    public async Task A_dealer_sees_only_its_own_data_and_gets_403_for_anothers()
    {
        var client = await LoginAsync(Dealer("HND-1003"));

        var summary = await client.GetFromJsonAsync<JsonElement>($"/api/dashboard/summary?start={From}&end={To}");
        Assert.Equal("HND-1003", summary.GetProperty("dealerId").GetString());
        Assert.Equal(seeded.Data.Responses.Count(r => r.DealerId == "HND-1003"), summary.GetProperty("surveyCount").GetInt32());

        var responses = await client.GetFromJsonAsync<JsonElement>($"/api/responses?start={From}&end={To}&pageSize=200");
        Assert.All(responses.GetProperty("items").EnumerateArray(), r => Assert.Equal("HND-1003", r.GetProperty("dealerId").GetString()));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/dashboard/summary?dealerId=HND-1001&start={From}&end={To}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/responses?dealerId=HND-1001&start={From}&end={To}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/leaderboard?start={From}&end={To}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/admin/policies/reindex", null)).StatusCode);

        var dealers = await client.GetFromJsonAsync<JsonElement>("/api/dealers");
        Assert.Equal(1, dealers.GetArrayLength());
    }

    [MongoFact]
    public async Task An_administrator_sees_every_dealer_and_the_leaderboard()
    {
        var client = await LoginAsync(Admin);

        var summary = await client.GetFromJsonAsync<JsonElement>($"/api/dashboard/summary?start={From}&end={To}");
        Assert.Equal(1000, summary.GetProperty("surveyCount").GetInt32());

        var board = await client.GetFromJsonAsync<JsonElement>($"/api/leaderboard?start={From}&end={To}");
        Assert.Equal(10, board.GetProperty("dealers").GetArrayLength());

        var one = await client.GetFromJsonAsync<JsonElement>($"/api/dashboard/summary?dealerId=HND-1001&start={From}&end={To}");
        Assert.Equal("HND-1001", one.GetProperty("dealerId").GetString());

        Assert.Equal(10, (await client.GetFromJsonAsync<JsonElement>("/api/dealers")).GetArrayLength());
    }

    [MongoFact]
    public async Task Invalid_dates_and_chat_input_return_problem_details_not_500s()
    {
        var client = await LoginAsync(Dealer("HND-1003"));

        var badRange = await client.GetAsync("/api/dashboard/summary?start=2026-09-20&end=2026-09-01");
        Assert.Equal(HttpStatusCode.BadRequest, badRange.StatusCode);
        Assert.Equal("application/problem+json", badRange.Content.Headers.ContentType?.MediaType);

        var empty = await client.PostAsJsonAsync("/api/ai/chat", new { message = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var tooLong = await client.PostAsJsonAsync("/api/ai/chat", new { message = new string('x', 2001) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
