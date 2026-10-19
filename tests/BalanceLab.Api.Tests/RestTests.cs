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

    [Fact]
    public async Task Rooms_are_listed_and_found_with_status_as_text()
    {
        await using var ala = await api.ConnectAsync();
        var host = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));
        var http = api.Http();

        var rooms = await http.GetFromJsonAsync<RoomInfo[]>("/api/rooms", ApiFixture.Json);
        var room = await http.GetFromJsonAsync<JsonElement>($"/api/rooms/{host.Room}");

        Assert.Contains(rooms!, r => r.Id == host.Room);
        Assert.Equal("Waiting", room.GetProperty("status").GetString());
        Assert.Equal("Human", room.GetProperty("seats")[0].GetProperty("kind").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/rooms/{Guid.NewGuid()}")).StatusCode);
    }

    [Theory]
    [InlineData("/api/matches/00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/matches/00000000-0000-0000-0000-000000000001/frames/0")]
    [InlineData("/api/matches/nie-guid")]
    public async Task Unknown_match_is_not_found(string url)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await api.Http().GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Running_match_hides_its_seed_and_hashes_and_has_no_frame_past_the_last_move()
    {
        await using var ala = await api.ConnectAsync();
        await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));
        await ala.Hub.InvokeAsync("AddBot", 1, "random");
        await ala.Hub.InvokeAsync("Start");
        await ala.Hub.InvokeAsync("MakeMove", 0, "3");
        var room = await ala.RoomAsync(r => r.Moves == 2);
        var http = api.Http();

        var match = await http.GetFromJsonAsync<MatchReplay>($"/api/matches/{room.MatchId}", ApiFixture.Json);
        var frame = await http.GetFromJsonAsync<JsonElement>($"/api/matches/{room.MatchId}/frames/2", ApiFixture.Json);
        var response = await http.GetAsync($"/api/matches/{room.MatchId}/frames/3");

        // Z ziarnem albo hashami trwajacej partii daloby sie odtworzyc talie i zakryte karty.
        Assert.Equal((2, "3", Persistence.MatchStatus.InProgress), (match!.Moves.Length, match.Moves[0], match.Status));
        Assert.Null(match.Seed);
        Assert.Empty(match.Hashes);
        Assert.Equal(JsonValueKind.Null, frame.GetProperty("hash").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("po 3 ruchach", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
