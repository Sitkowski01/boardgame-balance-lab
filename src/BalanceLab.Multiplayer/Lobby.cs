using System.Collections.Concurrent;
using System.Security.Cryptography;
using BalanceLab.Catalog;
using BalanceLab.Engine;
using BalanceLab.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BalanceLab.Multiplayer;

/// <summary>
/// Wszystkie pokoje serwera. Pokoje zyja w pamieci (partie sa w dzienniku); skonczone i opuszczone
/// znikaja same przy kolejnym zakladaniu albo przegladaniu lobby.
/// </summary>
public sealed class Lobby : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, IRoom> _rooms = new();
    private readonly SemaphoreSlim _creating = new(1, 1);
    private readonly IRoomNotifier _notifier;
    private readonly IMatchRecorderFactory _recorders;
    private readonly RoomOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILoggerFactory _logs;
    private readonly Func<ulong> _seeds;

    /// <param name="seeds">Ziarno nowej partii; domyslnie losowe (testy podaja stale).</param>
    public Lobby(
        IRoomNotifier notifier,
        IMatchRecorderFactory recorders,
        RoomOptions? options = null,
        TimeProvider? clock = null,
        ILoggerFactory? logs = null,
        Func<ulong>? seeds = null)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(recorders);
        (_notifier, _recorders, _options, _clock, _logs) =
            (notifier, recorders, options ?? new RoomOptions(), clock ?? TimeProvider.System, logs ?? NullLoggerFactory.Instance);
        _seeds = seeds ?? (() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))));
    }

    /// <summary>Pokoje od najstarszego.</summary>
    public IReadOnlyList<RoomInfo> Rooms => [.. _rooms.Values.Select(r => r.Info).OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)];

    /// <summary>Zaklada pokoj i sadza zakladajacego na pierwszym miejscu jako gospodarza.</summary>
    public async Task<SeatTicket> CreateAsync(RoomRequest request, string name, string connection)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Zla nazwa wychodzi przed zalozeniem pokoju, ktory inaczej zostalby pusty.
        PlayerName.Validate(name, _options.MaxNameLength);
        IGameDefinition definition;
        try
        {
            definition = GameCatalog.Find(request.Game ?? throw new FormatException("Brak nazwy gry."));
        }
        catch (FormatException ex)
        {
            throw new RoomException(ex.Message, ex);
        }

        IRoom room;
        // Sprawdzenie limitu i dodanie pokoju razem: dwa rownolegle zalozenia nie przekrocza limitu.
        await _creating.WaitAsync();
        try
        {
            await PruneAsync();
            if (_rooms.Count >= _options.MaxRooms)
            {
                throw new RoomException($"Serwer ma juz {_options.MaxRooms} pokoi - sprobuj pozniej.");
            }

            room = definition.Accept(new Builder(this, request, Guid.CreateVersion7(_clock.GetUtcNow()), _seeds()));
            _rooms[room.Id] = room;
        }
        finally
        {
            _creating.Release();
        }

        return await room.JoinAsync(name, connection);
    }

    /// <exception cref="RoomException">Nie ma takiego pokoju (albo juz zniknal).</exception>
    public IRoom Find(Guid id) =>
        _rooms.TryGetValue(id, out var room) ? room : throw new RoomException($"Nie ma pokoju {id}.");

    /// <summary>
    /// Usuwa pokoje skonczone (albo porzucone) dawniej niz <see cref="RoomOptions.KeepFinished"/>, czekajace bez nikogo
    /// dluzej niz <see cref="RoomOptions.KeepIdle"/> albo w ogole dluzej niz <see cref="RoomOptions.KeepWaiting"/>,
    /// i trwajace, w ktorych od <see cref="RoomOptions.KeepIdle"/> nic sie nie stalo (boty graja same, wiec to pokoj zawieszony).
    /// </summary>
    public async Task PruneAsync()
    {
        var now = _clock.GetUtcNow();
        foreach (var room in _rooms.Values)
        {
            var info = room.Info;
            var idle = now - room.LastActivity;
            var stale = info.Status switch
            {
                RoomStatus.Finished or RoomStatus.Abandoned => idle >= _options.KeepFinished,
                RoomStatus.Waiting => (idle >= _options.KeepIdle && !info.Seats.Any(s => s.Connected)) || now - info.CreatedAt >= _options.KeepWaiting,
                _ => idle >= _options.KeepIdle,
            };
            if (stale && _rooms.TryRemove(room.Id, out _))
            {
                await room.DisposeAsync();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _creating.Dispose();
        foreach (var id in _rooms.Keys)
        {
            if (_rooms.TryRemove(id, out var room))
            {
                await room.DisposeAsync();
            }
        }
    }

    /// <summary>Tworzy pokoj z pelnymi typami gry wybranej z katalogu.</summary>
    private sealed class Builder(Lobby lobby, RoomRequest request, Guid id, ulong seed) : IGameVisitor<IRoom>
    {
        public IRoom Visit<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition)
            where TState : notnull
            where TMove : notnull
            where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object>
        {
            TGame rules;
            try
            {
                rules = definition.Rules(request.Variant);
            }
            catch (FormatException ex)
            {
                throw new RoomException(ex.Message, ex);
            }

            if (request.Seats < rules.MinPlayers || request.Seats > rules.MaxPlayers)
            {
                throw new RoomException($"{rules.Name}: od {rules.MinPlayers} do {rules.MaxPlayers} graczy, a podano {request.Seats}.");
            }

            var recorder = lobby._recorders.Create<TState, TMove>(definition.Key, variant =>
            {
                var played = definition.Rules(variant);
                return new StoredGame<TState, TMove>(played, played, played);
            });
            return new Room<TState, TMove, TGame>(
                id, definition, rules, request.Variant, request.Seats, seed, lobby._options, lobby._notifier, recorder, lobby._clock,
                lobby._logs.CreateLogger("BalanceLab.Multiplayer.Room"));
        }
    }
}
