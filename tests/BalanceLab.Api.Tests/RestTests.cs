using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BalanceLab.Multiplayer;
using Microsoft.AspNetCore.SignalR.Client;

namespace BalanceLab.Api.Tests;

/// <summary>REST obok hubu: katalog gier, pokoje i partie z dziennika.</summary>
[Collection(SharedApi.Name)]
public sealed class RestTests(ApiFixture api)
{
    [Fact]
    public async Task Catalog_lists_the_games()
    {
        var games = await api.Http().GetFromJsonAsync<GameInfo[]>("/api/games", ApiFixture.Json);

        Assert.Equal([new GameInfo("connectfour", "ConnectFour", 2, 2, false), new GameInfo("klejnoty", "Klejnoty", 2, 4, true)], games!);
    }

    [Fact]
    public async Task Health_answers()
    {
        Assert.Equal("ok", await api.Http().GetStringAsync("/health"));
    }
}
