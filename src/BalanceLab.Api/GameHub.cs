using BalanceLab.Multiplayer;
using Microsoft.AspNetCore.SignalR;

namespace BalanceLab.Api;

/// <summary>Co serwer wysyla klientom.</summary>
public interface IGameClient
{
    /// <summary>Stan pokoju (miejsca, kto na ruchu, termin) — do wszystkich w pokoju.</summary>
    Task Room(RoomInfo room);

    /// <summary>Gra z punktu widzenia odbiorcy, z legalnymi ruchami, gdy jest na ruchu.</summary>
    Task View(TableView view);

    Task Move(MovePlayed move);

    /// <summary>Zmiana w ktoryms pokoju — do ogladajacych lobby.</summary>
    Task Lobby(RoomInfo room);
}

/// <summary>Nowy pokoj z hubu: gra, liczba miejsc, wariant zasad i nazwa zakladajacego.</summary>
public sealed record CreateRoomArgs(string Game, int Seats, string? Variant, string Name);

/// <summary>
/// Hub gry. Jedno polaczenie siedzi w jednym pokoju (jako gracz albo widz); bilet miejsca zostaje
/// w polaczeniu, wiec kolejne wywolania go nie podaja. Zasad tu nie ma — sprawdza je pokoj.
/// </summary>
public sealed class GameHub(Lobby lobby) : Hub<IGameClient>
{
    private const string BindingKey = "room";

    public async Task<SeatTicket> CreateRoom(CreateRoomArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        EnsureUnbound();
        var ticket = await lobby.CreateAsync(new RoomRequest(args.Game, args.Seats, args.Variant), args.Name, Context.ConnectionId);
        await BindAsync(lobby.Find(ticket.Room), ticket);
        return ticket;
    }

    private async Task BindAsync(IRoom room, SeatTicket ticket)
    {
        Context.Items[BindingKey] = new Binding(ticket.Room, ticket.Token);
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroups.Room(ticket.Room));
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroups.Seat(ticket.Room, ticket.Seat));
        // Zmiana, ktora pokoj wyslal przed dolaczeniem do grupy, nie dotarla: stan idzie jeszcze raz.
        await Clients.Caller.Room(room.Info);
    }

    private void EnsureUnbound()
    {
        if (Context.Items.ContainsKey(BindingKey))
        {
            throw new HubException("To polaczenie jest juz w pokoju - do innego pokoju potrzebne jest nowe polaczenie.");
        }
    }

    /// <summary>Pokoj polaczenia i bilet miejsca; widz nie ma biletu.</summary>
    private sealed record Binding(Guid Room, string? Token);
}
