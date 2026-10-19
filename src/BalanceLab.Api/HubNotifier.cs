using BalanceLab.Multiplayer;
using Microsoft.AspNetCore.SignalR;

namespace BalanceLab.Api;

/// <summary>Nazwy grup SignalR: pokoj, miejsce przy stole, widzowie i lobby.</summary>
internal static class RoomGroups
{
    public const string Lobby = "lobby";

    public static string Room(Guid room) => $"room:{room}";

    public static string Seat(Guid room, int seat) => $"room:{room}:seat:{seat}";

    public static string Watchers(Guid room) => $"room:{room}:watch";
}

/// <summary>Blad pokoju (zly ruch, nie twoja kolej) idzie do klienta z czytelnym komunikatem.</summary>
internal sealed class RoomErrorFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (RoomException ex)
        {
            throw new HubException(ex.Message, ex);
        }
    }
}

/// <summary>Zmiany z pokoi do grup SignalR: widok miejsca trafia tylko do tego miejsca.</summary>
internal sealed class HubNotifier(IHubContext<GameHub, IGameClient> hub) : IRoomNotifier
{
    public Task RoomChangedAsync(RoomInfo room) =>
        Task.WhenAll(hub.Clients.Group(RoomGroups.Room(room.Id)).Room(room), hub.Clients.Group(RoomGroups.Lobby).Lobby(room));

    public Task ViewChangedAsync(Guid room, int? seat, TableView view) =>
        hub.Clients.Group(seat is { } s ? RoomGroups.Seat(room, s) : RoomGroups.Watchers(room)).View(view);

    public Task MovePlayedAsync(MovePlayed move) => hub.Clients.Group(RoomGroups.Room(move.Room)).Move(move);
}
