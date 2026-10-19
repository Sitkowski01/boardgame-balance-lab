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

    public async Task<SeatTicket> JoinRoom(Guid room, string name)
    {
        var target = Reserve(room);
        var ticket = await SitAsync(() => target.JoinAsync(name, Context.ConnectionId));
        await BindAsync(target, ticket);
        return ticket;
    }

    /// <summary>Powrot na miejsce z biletem; klient od razu dostaje stan pokoju i swoj widok.</summary>
    public async Task<SeatTicket> Rejoin(Guid room, string token)
    {
        var target = Reserve(room);
        var ticket = await SitAsync(() => target.RejoinAsync(token, Context.ConnectionId));
        await BindAsync(target, ticket);
        if (await target.ViewAsync(token) is { } view)
        {
            await Clients.Caller.View(view);
        }

        return ticket;
    }

    /// <summary>Widz: dostaje zmiany pokoju i widok bez niczyich ukrytych kart.</summary>
    public async Task<TableView?> Watch(Guid room)
    {
        EnsureUnbound();
        var target = lobby.Find(room);
        Context.Items[BindingKey] = new Binding(room, null);
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroups.Room(room));
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroups.Watchers(room));
        await Clients.Caller.Room(target.Info);
        return await target.SpectateAsync();
    }

    /// <summary>Lista pokoi teraz i kazda zmiana pozniej.</summary>
    public async Task<IReadOnlyList<RoomInfo>> WatchLobby()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroups.Lobby);
        await lobby.PruneAsync();
        return lobby.Rooms;
    }

    public Task AddBot(int seat, string bot)
    {
        var (room, token) = Seat();
        return room.AddBotAsync(token, seat, bot);
    }

    public Task Start()
    {
        var (room, token) = Seat();
        return room.StartAsync(token);
    }

    /// <param name="number">Ile ruchow juz zagrano (z <see cref="TableView.Moves"/>).</param>
    public Task MakeMove(int number, string move)
    {
        var (room, token) = Seat();
        return room.MoveAsync(token, number, move);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Takze wiazanie bez biletu: rozlaczenie w trakcie JoinRoom nie moze zostawic miejsca "polaczonego" na zawsze.
        if (Context.Items.TryGetValue(BindingKey, out var value) && value is Binding binding)
        {
            try
            {
                await lobby.Find(binding.Room).DisconnectAsync(Context.ConnectionId);
            }
            catch (RoomException)
            {
                // Pokoj juz zniknal z lobby — nie ma kogo powiadamiac.
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task BindAsync(IRoom room, SeatTicket ticket)
    {
        Context.Items[BindingKey] = new Binding(ticket.Room, ticket.Token);
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroups.Room(ticket.Room));
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroups.Seat(ticket.Room, ticket.Seat));
        // Zmiana, ktora pokoj wyslal przed dolaczeniem do grupy, nie dotarla: stan idzie jeszcze raz.
        await Clients.Caller.Room(room.Info);
    }

    /// <summary>
    /// Wiazanie z pokojem zapisane przed wejsciem do niego: jesli klient rozlaczy sie w trakcie, OnDisconnectedAsync
    /// i tak zglosi pokojowi rozlaczenie.
    /// </summary>
    private IRoom Reserve(Guid room)
    {
        EnsureUnbound();
        var target = lobby.Find(room);
        Context.Items[BindingKey] = new Binding(room, null);
        return target;
    }

    /// <summary>Nieudane wejscie (pelny stol, zly bilet) zwalnia polaczenie do innej proby.</summary>
    private async Task<SeatTicket> SitAsync(Func<Task<SeatTicket>> sit)
    {
        try
        {
            return await sit();
        }
        catch
        {
            Context.Items.Remove(BindingKey);
            throw;
        }
    }

    private void EnsureUnbound()
    {
        if (Context.Items.ContainsKey(BindingKey))
        {
            throw new HubException("To polaczenie jest juz w pokoju - do innego pokoju potrzebne jest nowe polaczenie.");
        }
    }

    private (IRoom Room, string Token) Seat() =>
        Context.Items.TryGetValue(BindingKey, out var value) && value is Binding { Token: { } token } binding
            ? (lobby.Find(binding.Room), token)
            : throw new HubException("Najpierw usiadz przy stole (CreateRoom, JoinRoom albo Rejoin).");

    /// <summary>Pokoj polaczenia i bilet miejsca; widz nie ma biletu.</summary>
    private sealed record Binding(Guid Room, string? Token);
}
