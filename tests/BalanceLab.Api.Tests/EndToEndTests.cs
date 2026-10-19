using System.Net.Http.Json;
using System.Text.Json;
using BalanceLab.Multiplayer;
using BalanceLab.Persistence;
using Microsoft.AspNetCore.SignalR.Client;

namespace BalanceLab.Api.Tests;

/// <summary>
/// Cala sciezka: dwoch ludzi i bot graja Klejnoty przez SignalR do konca, a potem partia z dziennika
/// w PostgreSQL powtarza sie z tymi samymi hashami i daje sie przewijac klatka po klatce.
/// </summary>
[Collection(SharedApi.Name)]
public sealed class EndToEndTests(ApiFixture api)
{
    [Fact]
    public async Task Two_players_and_a_bot_finish_a_game_that_replays_from_the_journal()
    {
        await using var ala = await api.ConnectAsync();
        await using var ola = await api.ConnectAsync();

        var host = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("klejnoty", 3, "win=8", "Ala"));
        var guest = await ola.Hub.InvokeAsync<SeatTicket>("JoinRoom", host.Room, "Ola");
        await ala.Hub.InvokeAsync("AddBot", 2, "greedy");
        await ala.Hub.InvokeAsync("Start");

        await Task.WhenAll(PlayFirstLegalMoveAsync(ala), PlayFirstLegalMoveAsync(ola));

        var end = await ala.RoomAsync(r => r.Status == RoomStatus.Finished);
        Assert.Equal((0, 1), (host.Seat, guest.Seat));
        Assert.Equal(["Ala", "Ola", "greedy"], end.Seats.Select(s => s.Name));
        Assert.Equal(1.0, end.Scores.Sum(), 9);
        Assert.Contains(ala.Moves, m => m.ByBot && m.Seat == 2);
        Assert.Contains(ala.Moves, m => !m.ByBot && m.Seat == 1);

        var http = api.Http();
        var match = await http.GetFromJsonAsync<MatchReplay>($"/api/matches/{end.MatchId}", ApiFixture.Json);
        Assert.NotNull(match);
        Assert.True(match.Verified, match.Problem);
        Assert.Equal((MatchStatus.Finished, "klejnoty", "win=8", end.Moves), (match.Status, match.Game, match.Variant, match.Moves.Length));
        Assert.Equal(["Ala", "Ola", "greedy"], match.Players.ToArray());
        Assert.NotNull(match.Seed);
        Assert.Equal(end.Moves, match.Hashes.Length);
        Assert.All(match.Hashes, h => Assert.Matches("^[0-9a-f]{16}$", h));
        Assert.Equal(end.Scores.ToArray(), match.Scores.ToArray());
        // Ruchy w dzienniku to dokladnie te, ktore widzieli gracze, w tej samej kolejnosci.
        Assert.Equal(ala.Moves.OrderBy(m => m.Number).Select(m => m.Move), match.Moves);

        var frame = await http.GetFromJsonAsync<JsonElement>($"/api/matches/{end.MatchId}/frames/{end.Moves}", ApiFixture.Json);
        Assert.Equal(match.Hashes[^1], frame.GetProperty("hash").GetString());
        Assert.True(frame.GetProperty("view").GetProperty("over").GetBoolean());
        var start = await http.GetFromJsonAsync<JsonElement>($"/api/matches/{end.MatchId}/frames/0", ApiFixture.Json);
        Assert.False(start.GetProperty("view").GetProperty("over").GetBoolean());
    }

    [Fact]
    public async Task Card_taken_blind_never_reaches_the_other_players()
    {
        await using var ala = await api.ConnectAsync();
        await using var ola = await api.ConnectAsync();
        await using var watcher = await api.ConnectAsync();
        var host = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("klejnoty", 2, null, "Ala"));
        await ola.Hub.InvokeAsync<SeatTicket>("JoinRoom", host.Room, "Ola");
        await watcher.Hub.InvokeAsync<TableView?>("Watch", host.Room);
        await ala.Hub.InvokeAsync("Start");

        await ala.Hub.InvokeAsync("MakeMove", 0, "deck:3");

        static JsonElement Reserved(TableView view) =>
            ((JsonElement)view.View).GetProperty("players")[0].GetProperty("reserved")[0];
        var own = await ala.WaitAsync(() => ala.Views.LastOrDefault(v => v.Moves == 1));
        var other = await ola.WaitAsync(() => ola.Views.LastOrDefault(v => v.Moves == 1));
        var spectator = await watcher.WaitAsync(() => watcher.Views.LastOrDefault(v => v.Moves == 1));
        Assert.Equal(JsonValueKind.Number, Reserved(own).GetProperty("card").ValueKind);
        Assert.Equal(JsonValueKind.Null, Reserved(other).GetProperty("card").ValueKind);
        Assert.Equal(JsonValueKind.Null, Reserved(spectator).GetProperty("card").ValueKind);
        Assert.Equal(3, Reserved(other).GetProperty("tier").GetInt32());
        Assert.NotEmpty(other.LegalMoves);
        Assert.Empty(spectator.LegalMoves);
        // Ola dostaje tylko swoje widoki, nigdy widoku Ali.
        Assert.All(ola.Views, v => Assert.Equal(1, v.Seat));
    }

    /// <summary>Gracz zawsze gra pierwszy legalny ruch, dopoki partia trwa.</summary>
    private static async Task PlayFirstLegalMoveAsync(Player player)
    {
        var played = -1;
        while (true)
        {
            var next = await player.WaitAsync<object>(() =>
                player.Rooms is [.., { Status: RoomStatus.Finished } finished] ? finished
                : player.Views.LastOrDefault(v => v.LegalMoves.Length > 0 && v.Moves > played));
            if (next is not TableView view)
            {
                return;
            }

            played = view.Moves;
            await player.Hub.InvokeAsync("MakeMove", view.Moves, view.LegalMoves[0]);
        }
    }
}
