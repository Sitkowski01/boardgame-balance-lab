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
    public async Task Rules_give_the_card_table_of_the_variant()
    {
        var http = api.Http();

        var standard = await http.GetFromJsonAsync<JsonElement>("/api/games/KLEJNOTY/rules");
        var variant = await http.GetFromJsonAsync<JsonElement>("/api/games/klejnoty/rules?variant=card3.points=4");

        Assert.Equal(90, standard.GetProperty("cards").GetArrayLength());
        Assert.Equal(15, standard.GetProperty("winningPoints").GetInt32());
        Assert.Equal(4, variant.GetProperty("cards")[3].GetProperty("points").GetInt32());
    }

    [Theory]
    [InlineData("/api/games/szachy/rules", HttpStatusCode.NotFound)]
    [InlineData("/api/games/connectfour/rules", HttpStatusCode.NotFound)]
    [InlineData("/api/games/klejnoty/rules?variant=win=abc", HttpStatusCode.BadRequest)]
    [InlineData("/api/games/connectfour/rules?variant=win=9", HttpStatusCode.BadRequest)]
    public async Task Rules_need_a_game_with_a_table_and_a_valid_variant(string url, HttpStatusCode status)
    {
        Assert.Equal(status, (await api.Http().GetAsync(url)).StatusCode);
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

    [Fact]
    public async Task Journal_that_no_longer_matches_the_rules_is_unprocessable()
    {
        await using var ala = await api.ConnectAsync();
        await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));
        await ala.Hub.InvokeAsync("AddBot", 1, "random");
        await ala.Hub.InvokeAsync("Start");
        await ala.Hub.InvokeAsync("MakeMove", 0, "3");
        var room = await ala.RoomAsync(r => r.Moves == 2);

        // Ruch w dzienniku podmieniony z pominieciem triggerow: tak wyglada uszkodzenie albo zmiana kodeka gry.
        await ApiFixture.ExecuteAsync(api.ConnectionString,
            $"ALTER TABLE match_moves DISABLE TRIGGER USER; UPDATE match_moves SET move = 'x' WHERE match_id = '{room.MatchId}' AND seq = 1; ALTER TABLE match_moves ENABLE TRIGGER USER;");
        var http = api.Http();
        var match = await http.GetAsync($"/api/matches/{room.MatchId}");
        var frame = await http.GetAsync($"/api/matches/{room.MatchId}/frames/2");

        Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity), (match.StatusCode, frame.StatusCode));
        Assert.Contains("Dziennik partii nie zgadza sie z zasadami gry", await match.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("Dziennik partii nie zgadza sie z zasadami gry", await frame.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
