using BalanceLab.Games.ConnectFour;
using BalanceLab.Games.Klejnoty;

namespace BalanceLab.Multiplayer.Tests;

/// <summary>Partia: ruchy ludzi, sedziowanie przez zasady, boty i koniec gry.</summary>
public sealed class RoomPlayTests
{
    [Fact]
    public async Task Two_players_finish_a_game_and_every_move_is_recorded_first()
    {
        await using var table = new Table();
        var (room, ala, ola) = await table.StartedPairAsync();

        // Ala wrzuca do kolumny 0, Ola do 1: Ala ma pionowa czworke po siedmiu ruchach.
        int[] columns = [0, 1, 0, 1, 0, 1, 0];
        for (var i = 0; i < columns.Length; i++)
        {
            await room.MoveAsync(i % 2 == 0 ? ala.Token : ola.Token, i, columns[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var info = room.Info;
        Assert.Equal((RoomStatus.Finished, 7, (int?)null, (DateTimeOffset?)null), (info.Status, info.Moves, info.ToMove, info.Deadline));
        Assert.Equal([1.0, 0.0], info.Scores.ToArray());
        Assert.Equal(["0", "1", "0", "1", "0", "1", "0"], table.MovesOf(room));
        var moves = await table.Notifier.MovesAsync(room.Id, 7);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], moves.Select(m => m.Number));
        Assert.Equal(new MovePlayed(room.Id, 7, 0, "0", false), moves[^1]);
        var last = (ConnectFourView)(await table.Notifier.ViewAsync(room.Id, 1, 7)).View;
        Assert.Equal((0, true), (last.Winner, last.Over));
        Assert.Empty((await table.Notifier.ViewAsync(room.Id, 0, 7)).LegalMoves);
    }

    [Fact]
    public async Task Server_judges_every_move()
    {
        await using var table = new Table();
        var (room, ala, ola) = await table.StartedPairAsync();

        async Task<string> Rejected(string token, int number, string move) =>
            (await Assert.ThrowsAsync<RoomException>(() => room.MoveAsync(token, number, move))).Message;

        Assert.Equal("Nie twoja kolej.", await Rejected(ola.Token, 0, "3"));
        Assert.Equal("Ruch do nieaktualnej pozycji: partia ma juz 0 ruchow, a ruch zakladal 1.", await Rejected(ala.Token, 1, "3"));
        Assert.Equal("Nieznany ruch 'x'.", await Rejected(ala.Token, 0, "x"));
        Assert.Equal("Nieznany ruch ''.", await Rejected(ala.Token, 0, null!));
        Assert.Equal("Ruch '9' jest nielegalny.", await Rejected(ala.Token, 0, "9"));
        Assert.Equal("Nieznany bilet miejsca w tym pokoju.", await Rejected("cudzy", 0, "3"));
        Assert.Equal(0, room.Info.Moves);
        Assert.Empty(table.MovesOf(room));

        await room.MoveAsync(ala.Token, 0, "3");
        Assert.Equal("Ruch do nieaktualnej pozycji: partia ma juz 1 ruchow, a ruch zakladal 0.", await Rejected(ala.Token, 0, "3"));
    }

    [Fact]
    public async Task Move_before_the_start_is_rejected()
    {
        await using var table = new Table();
        var (room, host) = await table.CreateAsync();

        var ex = await Assert.ThrowsAsync<RoomException>(() => room.MoveAsync(host.Token, 0, "3"));

        Assert.Equal("Partia sie nie toczy.", ex.Message);
    }

    [Fact]
    public async Task Bot_answers_every_human_move_until_the_end()
    {
        await using var table = new Table();
        var (room, host) = await table.CreateAsync();
        await room.AddBotAsync(host.Token, 1, "random");
        await room.StartAsync(host.Token);

        while (room.Info.Status == RoomStatus.Playing)
        {
            var view = await table.Notifier.WaitAsync(() =>
                room.Info.Status != RoomStatus.Playing ? null
                : table.Notifier.Views.LastOrDefault(v => v.Seat == 0 && v.View.Moves == room.Info.Moves && v.View.LegalMoves.Length > 0).View);
            if (view is null)
            {
                break;
            }

            await room.MoveAsync(host.Token, view.Moves, view.LegalMoves[0]);
            await table.Notifier.RoomAsync(room.Id, r => r.Moves > view.Moves + 1 || r.Status == RoomStatus.Finished);
        }

        var moves = await table.Notifier.MovesAsync(room.Id, room.Info.Moves);
        Assert.Equal(RoomStatus.Finished, room.Info.Status);
        Assert.All(moves, m => Assert.Equal(m.Seat == 1, m.ByBot));
        Assert.Equal(moves.Select(m => m.Move), table.MovesOf(room));
    }

    [Fact]
    public async Task Bots_alone_play_the_same_game_from_the_same_seed()
    {
        // Ziarna botow wynikaja z ziarna partii i numeru ruchu, wiec dwa pokoje z tym samym ziarnem graja identycznie.
        async Task<IReadOnlyList<string>> PlayAsync()
        {
            await using var table = new Table(seed: 99);
            var (room, host) = await table.CreateAsync("klejnoty", 3);
            await room.AddBotAsync(host.Token, 1, "greedy");
            await room.StartAsync(host.Token);
            await room.DisconnectAsync("c-ala");
            await table.Notifier.RoomAsync(room.Id, r => r.Status == RoomStatus.Finished);
            return table.MovesOf(room);
        }

        var first = await PlayAsync();

        Assert.True(first.Count > 30, $"ruchow: {first.Count}");
        Assert.Equal(first, await PlayAsync());
    }

    [Fact]
    public async Task Klejnoty_variant_is_played_by_its_rules()
    {
        await using var table = new Table();
        var (room, host) = await table.CreateAsync("klejnoty", 2, "win=3");
        await room.StartAsync(host.Token);
        await room.DisconnectAsync("c-ala");

        var info = await table.Notifier.RoomAsync(room.Id, r => r.Status == RoomStatus.Finished);

        var final = (KlejnotyView)(await table.Notifier.ViewAsync(room.Id, null, info.Moves)).View;
        Assert.Contains(final.Players, p => p.Points >= 3);
        Assert.True(final.Over);
    }
}
