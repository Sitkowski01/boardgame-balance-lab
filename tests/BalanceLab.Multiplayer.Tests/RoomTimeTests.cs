using BalanceLab.Games.Klejnoty;

namespace BalanceLab.Multiplayer.Tests;

/// <summary>Partia nie stoi: limit czasu, rozlaczenia, powroty i awarie zapisu.</summary>
public sealed class RoomTimeTests
{
    [Fact]
    public async Task Rejoin_needs_a_ticket_of_this_room()
    {
        await using var table = new Table();
        var (room, _) = await table.CreateAsync();

        var ex = await Assert.ThrowsAsync<RoomException>(() => room.RejoinAsync("zly", "c"));

        Assert.Equal("Nieznany bilet miejsca w tym pokoju.", ex.Message);
    }
}
