using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace BalanceLab.Api.Tests;

/// <summary>Laboratorium przez HTTP: zamowienie, odpytywanie o postep i wynik, anulowanie i limity.</summary>
[Collection(SharedApi.Name)]
public sealed class ExperimentTests(ApiFixture api)
{
    /// <summary>Eksperyment, ktory liczy dlugo: anulujemy go, zanim sie skonczy.</summary>
    private const string Slow = """{"game":"klejnoty","kind":"Tournament","bots":["ismcts:200","ismcts:200"],"games":1000,"seed":"1"}""";

    [Fact]
    public async Task Tournament_is_accepted_polled_and_returns_the_report()
    {
        var http = api.Http();

        var response = await PostAsync(http, """{"game":"mozaika","kind":"Tournament","bots":["greedy","random"],"games":6,"seed":"12345678901234567890"}""");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = accepted.GetProperty("id").GetString();
        Assert.Equal($"/api/experiments/{id}", response.Headers.Location!.OriginalString);
        Assert.Equal(6, accepted.GetProperty("total").GetInt32());
        Assert.Equal("12345678901234567890", accepted.GetProperty("request").GetProperty("seed").GetString()); // ziarno jako tekst (JS gubi cyfry)

        var done = await WaitAsync(http, id!, "Done");
        var result = done.GetProperty("result");
        Assert.Equal("Mozaika", result.GetProperty("game").GetString());
        Assert.Equal(2, result.GetProperty("seats").GetArrayLength());
        Assert.Equal(25, result.GetProperty("items").GetArrayLength());
        Assert.Equal("pola sciany", result.GetProperty("itemNoun").GetProperty("plural").GetString());
        Assert.Equal("Tournament", done.GetProperty("request").GetProperty("kind").GetString());

        var recent = await http.GetFromJsonAsync<JsonElement>("/api/experiments");
        var listed = recent.EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("result").ValueKind); // lista bez wynikow
    }

    [Fact]
    public async Task League_and_comparison_return_their_own_reports()
    {
        var http = api.Http();

        var league = await SubmitAsync(http, """{"game":"connectfour","kind":"League","bots":["random","mcts:20","random"],"games":4,"seed":"3"}""");
        var comparison = await SubmitAsync(http, """{"game":"klejnoty","kind":"Comparison","bots":["greedy","random"],"games":4,"seed":"3","variant":"win=8"}""");

        var ranking = (await WaitAsync(http, league, "Done")).GetProperty("result");
        var ab = (await WaitAsync(http, comparison, "Done")).GetProperty("result");
        Assert.Equal(3, ranking.GetProperty("ratings").GetArrayLength());
        Assert.Equal(3, ranking.GetProperty("pairs").GetArrayLength());
        Assert.Equal("Klejnoty (win=8)", ab.GetProperty("variant").GetProperty("game").GetString());
        Assert.Equal(2, ab.GetProperty("contestants").GetArrayLength());
    }

    [Theory]
    [InlineData("""{"game":"klejnoty","kind":"Tournament","bots":["random","alfa"],"games":10,"seed":"1"}""", "Nieznany bot 'alfa'")]
    [InlineData("""{"game":"klejnoty","kind":"Tournament","bots":["random","random"],"games":5000,"seed":"1"}""", "od 1 do 1000")]
    [InlineData("""{"game":"klejnoty","kind":"Comparison","bots":["random","random"],"games":10,"seed":"1"}""", "Porownanie potrzebuje wariantu")]
    [InlineData("""{"game":"szachy","kind":"League","bots":["random","random"],"games":10,"seed":"1"}""", "Nieznana gra 'szachy'")]
    public async Task Bad_order_is_a_400_with_the_reason(string body, string reason)
    {
        var response = await PostAsync(api.Http(), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(reason, problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("Zle zamowienie eksperymentu", problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("""{"game":"klejnoty","kind":"Ruletka","bots":["random"],"games":10,"seed":"1"}""")]
    [InlineData("nie json")]
    public async Task Unreadable_order_is_a_400(string body)
    {
        var response = await PostAsync(api.Http(), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Running_experiment_can_be_cancelled_and_unknown_ones_are_not_found()
    {
        var http = api.Http();
        var id = await SubmitAsync(http, Slow);

        var cancel = await http.DeleteAsync($"/api/experiments/{id}");

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelled = await WaitAsync(http, id, "Cancelled");
        Assert.True(cancelled.GetProperty("done").GetInt32() < 1000);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/experiments/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.DeleteAsync($"/api/experiments/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Full_lab_answers_503_until_something_finishes()
    {
        await using var factory = api.Factory.WithWebHostBuilder(b => b.UseSetting("Experiments:MaxQueued", "1"));
        var http = factory.CreateClient();
        var id = await SubmitAsync(http, Slow);

        var busy = await PostAsync(http, Slow);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        Assert.Equal("Laboratorium zajete", (await busy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        await http.DeleteAsync($"/api/experiments/{id}");
        await WaitAsync(http, id, "Cancelled");
        Assert.Equal(HttpStatusCode.Accepted, (await PostAsync(http, """{"game":"connectfour","kind":"Tournament","bots":["random","random"],"games":2,"seed":"1"}""")).StatusCode);
    }

    [Fact]
    public async Task Limits_are_published_for_the_form()
    {
        var limits = await api.Http().GetFromJsonAsync<JsonElement>("/api/experiments/limits");

        Assert.Equal((1000, 6, 200, 400_000L), (limits.GetProperty("maxGames").GetInt32(), limits.GetProperty("maxBots").GetInt32(),
            limits.GetProperty("maxBotIterations").GetInt32(), limits.GetProperty("maxWork").GetInt64()));
    }

    [Fact]
    public async Task Catalog_tells_the_lab_the_variant_syntax_and_greedy_profiles()
    {
        var games = await api.Http().GetFromJsonAsync<JsonElement>("/api/games");
        var mozaika = games.EnumerateArray().Single(g => g.GetProperty("key").GetString() == "mozaika");
        var connectFour = games.EnumerateArray().Single(g => g.GetProperty("key").GetString() == "connectfour");

        Assert.StartsWith("zmiany po srednikach: tiles=N", mozaika.GetProperty("variantSyntax").GetString(), StringComparison.Ordinal);
        Assert.Equal(["kolory", "kolumny", "ostrozny", "wyrownany"], mozaika.GetProperty("profiles").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(JsonValueKind.Null, connectFour.GetProperty("variantSyntax").ValueKind);
        Assert.Equal(0, connectFour.GetProperty("profiles").GetArrayLength());
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string order) =>
        http.PostAsync("/api/experiments", new StringContent(order, System.Text.Encoding.UTF8, "application/json"));

    private static async Task<string> SubmitAsync(HttpClient http, string order)
    {
        var response = await PostAsync(http, order);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> WaitAsync(HttpClient http, string id, string status)
    {
        for (var i = 0; i < 600; i++)
        {
            var info = await http.GetFromJsonAsync<JsonElement>($"/api/experiments/{id}");
            if (info.GetProperty("status").GetString() == status)
            {
                return info;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Eksperyment {id} nie doszedl do {status}.");
    }
}
