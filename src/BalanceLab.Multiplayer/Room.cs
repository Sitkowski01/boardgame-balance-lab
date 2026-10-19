using System.Collections.Immutable;
using BalanceLab.Catalog;
using BalanceLab.Engine;
using BalanceLab.Simulation;
using Microsoft.Extensions.Logging;

namespace BalanceLab.Multiplayer;

/// <summary>
/// Pokoj z jedna partia. Serwer jest sedzia: stan gry jest tylko tutaj, a klient wysyla ruch jako tekst.
/// Wszystko, co zmienia pokoj (prosby graczy, ruchy botow, uplyw czasu), idzie przez <see cref="RoomQueue"/>
/// i jest wykonywane po kolei — bez blokad, a dwa ruchy do tej samej pozycji nie moga przejsc oba.
/// </summary>
internal sealed partial class Room<TState, TMove, TGame> : IRoom
    where TState : notnull
    where TMove : notnull
    where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object>
{
    private readonly GameDefinition<TState, TMove, TGame> _definition;
    private readonly TGame _rules;
    private readonly string? _variant;
    private readonly ulong _seed;
    private readonly RoomOptions _options;
    private readonly IRoomNotifier _notifier;
    private readonly IMatchRecorder<TMove> _recorder;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;
    private readonly Contestant<TState, TMove> _fill;
    private readonly Contestant<TState, TMove> _autopilot;
    private readonly Seat[] _seats;
    private readonly DateTimeOffset _createdAt;
    private readonly RoomQueue _queue;

    private RoomStatus _status = RoomStatus.Waiting;
    private int _host = -1;
    private TState _state = default!;
    private ImmutableArray<double> _scores = [];
    private long _version;
    private volatile RoomInfo _info;
    private long _lastActivity;

    /// <param name="rules">Zasady w wariancie tej partii.</param>
    /// <param name="seed">Ziarno rozdania i botow; z nim partie da sie odtworzyc.</param>
    public Room(
        Guid id,
        GameDefinition<TState, TMove, TGame> definition,
        TGame rules,
        string? variant,
        int seats,
        ulong seed,
        RoomOptions options,
        IRoomNotifier notifier,
        IMatchRecorder<TMove> recorder,
        TimeProvider clock,
        ILogger logger)
    {
        (Id, _definition, _rules, _variant, _seed, _options, _notifier, _recorder, _clock, _logger) =
            (id, definition, rules, variant, seed, options, notifier, recorder, clock, logger);
        // Boty z ustawien sprawdzone od razu: zly opis to blad konfiguracji, a nie partia, ktora stanie w polowie.
        _fill = definition.Bot(options.FillBot, options.FillBot);
        _autopilot = definition.Bot(options.AutopilotBot, options.AutopilotBot);
        _seats = [.. Enumerable.Range(0, seats).Select(_ => new Seat())];
        _createdAt = clock.GetUtcNow();
        _lastActivity = _createdAt.UtcTicks;
        _info = BuildInfo();
        _queue = new RoomQueue(id, logger);
    }

    public Guid Id { get; }

    public RoomInfo Info => _info;

    public DateTimeOffset LastActivity => new(Interlocked.Read(ref _lastActivity), TimeSpan.Zero);

    public async ValueTask DisposeAsync()
    {
        await _queue.DisposeAsync();
    }

    private int SeatOf(string token)
    {
        var seat = Array.FindIndex(_seats, s => s.Token is not null && s.Token == token);
        return seat >= 0 ? seat : throw new RoomException("Nieznany bilet miejsca w tym pokoju.");
    }

    private void EnsureHost(string token)
    {
        if (SeatOf(token) != _host)
        {
            throw new RoomException("Tylko gospodarz moze to zrobic.");
        }
    }

    private void Ensure(RoomStatus status, string otherwise)
    {
        if (_status != status)
        {
            throw new RoomException(otherwise);
        }
    }

    /// <summary>Miejsce przy stole; czytane i zmieniane tylko w kolejce pokoju.</summary>
    private sealed class Seat
    {
        public SeatKind Kind { get; set; }

        public string? Name { get; set; }

        /// <summary>Bilet czlowieka; bot go nie ma.</summary>
        public string? Token { get; init; }

        public HashSet<string> Connections { get; } = new(StringComparer.Ordinal);

        /// <summary>Bot miejsca albo, u czlowieka, bot grajacy za niego.</summary>
        public Contestant<TState, TMove>? Bot { get; set; }
    }
}
