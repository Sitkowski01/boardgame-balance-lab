using System.Collections.Concurrent;
using BalanceLab.Multiplayer;

namespace BalanceLab.Demo;

/// <summary>Nowy pokoj, jak <c>CreateRoomArgs</c> huba: gra, liczba miejsc, wariant zasad i nazwa zakladajacego.</summary>
public sealed record CreateRoomArgs(string Game, int Seats, string? Variant, string Name);

/// <summary>Wiadomosc do jednego polaczenia: rodzaj jak metoda klienta huba ("room", "view", "move", "lobby").</summary>
public delegate void DemoSend(string connection, string kind, object payload);

/// <summary>
/// Hub gry bez SignalR: te same metody i te same zasady co <c>GameHub</c> (jedno polaczenie siedzi w jednym
/// pokoju, bilet zostaje w polaczeniu), a grupy to slownik polaczen.
/// </summary>
public sealed class DemoHub
{
    private readonly ConcurrentDictionary<string, Binding?> _connections = new();
    private readonly ConcurrentDictionary<string, byte> _lobbyWatchers = new();
    private readonly DemoSend _send;
    private Lobby? _lobby;
    private long _next;

    public DemoHub(DemoSend send)
    {
        ArgumentNullException.ThrowIfNull(send);
        _send = send;
    }

    /// <summary>Lobby, ktore wysyla zmiany przez ten hub; ustawiane raz, po zbudowaniu lobby z tym hubem.</summary>
    public Lobby Lobby
    {
        get => _lobby ?? throw new InvalidOperationException("Hub demo nie ma jeszcze lobby.");
        set => _lobby = _lobby is null ? value : throw new InvalidOperationException("Lobby huba ustawia sie raz.");
    }

    /// <summary>Nowe polaczenie (jak otwarcie WebSocketu); zwraca jego identyfikator.</summary>
    public string Connect()
    {
        var id = $"demo-{Interlocked.Increment(ref _next)}";
        _connections[id] = null;
        return id;
    }

    /// <summary>Zamkniecie polaczenia: pokoj dostaje rozlaczenie jak z OnDisconnectedAsync.</summary>
    public async Task CloseAsync(string connection)
    {
        _lobbyWatchers.TryRemove(connection, out _);
        // Zakladany pokoj (Room pusty) zglosi rozlaczenie sam, gdy zakladanie sie skonczy (BindAsync).
        if (_connections.TryRemove(connection, out var binding) && binding is not null && binding.Room != Guid.Empty)
        {
            try
            {
                await Lobby.Find(binding.Room).DisconnectAsync(connection);
            }
            catch (RoomException)
            {
                // Pokoj juz zniknal z lobby.
            }
        }
    }

    public async Task<SeatTicket> CreateRoomAsync(string connection, CreateRoomArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        // Zarezerwowane przed pierwszym await: SignalR wykonuje wywolania polaczenia po kolei, tu trzeba tego pilnowac.
        var creating = Reserve(connection, Guid.Empty);
        SeatTicket ticket;
        try
        {
            ticket = await Lobby.CreateAsync(new RoomRequest(args.Game, args.Seats, args.Variant), args.Name, connection);
        }
        catch
        {
            _connections.TryUpdate(connection, null, creating);
            throw;
        }

        await BindAsync(connection, creating, Lobby.Find(ticket.Room), ticket);
        return ticket;
    }

    public Task<SeatTicket> JoinRoomAsync(string connection, Guid room, string name) =>
        SitAsync(connection, room, target => target.JoinAsync(name, connection));

    public Task<SeatTicket> RejoinAsync(string connection, Guid room, string token) =>
        SitAsync(connection, room, target => target.RejoinAsync(token, connection));

    public async Task<TableView?> WatchAsync(string connection, Guid room)
    {
        var target = Lobby.Find(room);
        Reserve(connection, room, joined: true);
        _send(connection, "room", target.Info);
        return await target.SpectateAsync();
    }

    public IReadOnlyList<RoomInfo> WatchLobby(string connection)
    {
        Known(connection);
        _lobbyWatchers.TryAdd(connection, 0);
        Lobby.Prune();
        return Lobby.Rooms;
    }

    public Task AddBotAsync(string connection, int seat, string bot)
    {
        var (room, token) = Seat(connection);
        return room.AddBotAsync(token, seat, bot);
    }

    public Task StartAsync(string connection)
    {
        var (room, token) = Seat(connection);
        return room.StartAsync(token);
    }

    public Task MakeMoveAsync(string connection, int number, string move)
    {
        var (room, token) = Seat(connection);
        return room.MoveAsync(token, number, move);
    }

    private async Task<SeatTicket> SitAsync(string connection, Guid room, Func<IRoom, Task<SeatTicket>> sit)
    {
        var target = Lobby.Find(room);
        // Wiazanie przed wejsciem, jak w hubie: zamkniecie w trakcie i tak zglosi pokojowi rozlaczenie.
        // Zmiany pokoju dostanie dopiero po wejsciu (Joined), jak po dolaczeniu do grup SignalR.
        var sitting = Reserve(connection, room);
        SeatTicket ticket;
        try
        {
            ticket = await sit(target);
        }
        catch
        {
            _connections.TryUpdate(connection, null, sitting);
            throw;
        }

        await BindAsync(connection, sitting, target, ticket);
        return ticket;
    }

    /// <summary>Stan pokoju i widok jeszcze raz po zajeciu miejsca — zmiana sprzed wiazania nie dotarla.</summary>
    /// <remarks>
    /// Polaczenie zamkniete w trakcie wejscia (jak ConnectionAborted w hubie) nie wraca: pokoj dostaje rozlaczenie,
    /// inaczej miejsce zostaloby "polaczone", a stol czekalby na kogos, kogo juz nie ma.
    /// </remarks>
    private async Task BindAsync(string connection, Binding reserved, IRoom room, SeatTicket ticket)
    {
        if (!_connections.TryUpdate(connection, new Binding(ticket.Room, ticket.Token, ticket.Seat, Joined: true), reserved))
        {
            await room.DisconnectAsync(connection);
            return;
        }

        _send(connection, "room", room.Info);
        if (await room.ViewAsync(ticket.Token) is { } view)
        {
            _send(connection, "view", view);
        }
    }

    private void Known(string connection)
    {
        if (!_connections.ContainsKey(connection))
        {
            throw new RoomException("Połączenie jest zamknięte - otwórz nowe.");
        }
    }

    /// <summary>Wiaze wolne polaczenie z pokojem (Guid.Empty: pokoj dopiero powstaje) albo odmawia.</summary>
    private Binding Reserve(string connection, Guid room, bool joined = false)
    {
        var binding = new Binding(room, null, null, joined);
        if (!_connections.TryUpdate(connection, binding, null))
        {
            Known(connection);
            throw new RoomException("To połączenie jest już w pokoju - do innego pokoju potrzebne jest nowe połączenie.");
        }

        return binding;
    }

    private (IRoom Room, string Token) Seat(string connection) =>
        _connections.TryGetValue(connection, out var binding) && binding is { Token: { } token }
            ? (Lobby.Find(binding.Room), token)
            : throw new RoomException("Najpierw usiądź przy stole (CreateRoom, JoinRoom albo Rejoin).");

    /// <summary>Pokoj polaczenia; widz nie ma biletu ani miejsca. Joined: polaczenie dostaje juz zmiany pokoju.</summary>
    private sealed record Binding(Guid Room, string? Token, int? Seat, bool Joined = false);
}
