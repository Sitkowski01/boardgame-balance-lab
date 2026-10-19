using System.Collections.Concurrent;
using BalanceLab.Engine;
using BalanceLab.Persistence;
using Microsoft.Extensions.Time.Testing;

namespace BalanceLab.Multiplayer.Tests;

/// <summary>Zbiera wszystko, co pokoje wysylaja, i pozwala poczekac na konkretne zdarzenie.</summary>
internal sealed class RecordingNotifier : IRoomNotifier, IDisposable
{
    private readonly ConcurrentQueue<object> _events = new();
    private readonly SemaphoreSlim _changed = new(0);

    /// <summary>Wysylka rzuca wyjatek — pokoj ma to przezyc.</summary>
    public bool Broken { get; set; }

    public IReadOnlyList<RoomInfo> Rooms => [.. _events.OfType<RoomInfo>()];

    public IReadOnlyList<(int? Seat, TableView View)> Views => [.. _events.OfType<(int?, TableView)>()];

    public IReadOnlyList<MovePlayed> Moves => [.. _events.OfType<MovePlayed>()];

    public Task RoomChangedAsync(RoomInfo room) => Add(room);

    public Task ViewChangedAsync(Guid room, int? seat, TableView view) => Add((seat, view));

    public Task MovePlayedAsync(MovePlayed move) => Add(move);

    /// <summary>Czeka, az ostatni stan pokoju spelni warunek (pokoj pracuje w tle).</summary>
    public Task<RoomInfo> RoomAsync(Guid room, Func<RoomInfo, bool> until) =>
        WaitAsync(() => Rooms.LastOrDefault(r => r.Id == room) is { } last && until(last) ? last : null);

    /// <summary>Czeka na widok miejsca (albo widza) po <paramref name="moves"/> ruchach.</summary>
    public Task<TableView> ViewAsync(Guid room, int? seat, int moves) =>
        WaitAsync(() => Views.LastOrDefault(v => v.View.Room == room && v.Seat == seat && v.View.Moves == moves).View);

    /// <summary>Czeka, az dotrze co najmniej <paramref name="count"/> ruchow pokoju (wysylka idzie w tle).</summary>
    public Task<IReadOnlyList<MovePlayed>> MovesAsync(Guid room, int count) =>
        WaitAsync<IReadOnlyList<MovePlayed>>(() => Moves.Where(m => m.Room == room).ToArray() is { } moves && moves.Length >= count ? moves : null);

    public async Task<T> WaitAsync<T>(Func<T?> probe)
        where T : class
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            if (probe() is { } found)
            {
                return found;
            }

            // Zdarzenie albo co 20 ms: niektore warunki (np. nieudany zapis) nie wysylaja zadnego zdarzenia.
            await _changed.WaitAsync(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    public void Dispose() => _changed.Dispose();

    private Task Add(object item)
    {
        _events.Enqueue(item);
        _changed.Release();
        return Broken ? throw new InvalidOperationException("siec lezy") : Task.CompletedTask;
    }
}

/// <summary>Dziennik w pamieci; mozna kazac mu odmowic kolejnych zapisow.</summary>
internal sealed class MemoryRecorders : IMatchRecorderFactory
{
    public ConcurrentDictionary<Guid, (GameSetup Setup, IReadOnlyList<string> Players, string? Variant)> Started { get; } = new();

    public ConcurrentDictionary<Guid, ConcurrentQueue<string>> Moves { get; } = new();

    /// <summary>Ile najblizszych zapisow (start albo ruch) ma sie nie udac.</summary>
    public int Failures { get; set; }

    public IMatchRecorder<TMove> Create<TState, TMove>(string game, Func<string?, StoredGame<TState, TMove>> rules)
        where TState : notnull
        where TMove : notnull => new Recorder<TState, TMove>(this, rules);

    private void MaybeFail()
    {
        if (Failures > 0)
        {
            Failures--;
            throw new InvalidOperationException("baza lezy");
        }
    }

    private sealed class Recorder<TState, TMove>(MemoryRecorders owner, Func<string?, StoredGame<TState, TMove>> rules) : IMatchRecorder<TMove>
        where TState : notnull
        where TMove : notnull
    {
        private string? _variant;

        public Task<Guid> StartAsync(GameSetup setup, IReadOnlyList<string> players, string? variant, CancellationToken cancellationToken = default)
        {
            owner.MaybeFail();
            var id = Guid.NewGuid();
            _variant = variant;
            owner.Started[id] = (setup, players, variant);
            owner.Moves[id] = new();
            return Task.FromResult(id);
        }

        public Task AppendAsync(Guid match, int expectedMoves, TMove move, CancellationToken cancellationToken = default)
        {
            owner.MaybeFail();
            Assert.Equal(expectedMoves, owner.Moves[match].Count);
            owner.Moves[match].Enqueue(rules(_variant).Moves.EncodeMove(move));
            return Task.CompletedTask;
        }
    }
}

/// <summary>Lobby z podstawionym zegarem, zapisem i odbiorca; boty bez przerwy przed ruchem.</summary>
internal sealed class Table : IAsyncDisposable
{
    public Table(RoomOptions? options = null, ulong seed = 7)
    {
        Options = options ?? new RoomOptions { BotDelay = TimeSpan.Zero, FillBot = "random", AutopilotBot = "random" };
        Lobby = new Lobby(Notifier, Recorders, Options, Clock, seeds: () => seed);
    }

    public RoomOptions Options { get; }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

    public RecordingNotifier Notifier { get; } = new();

    public MemoryRecorders Recorders { get; } = new();

    public Lobby Lobby { get; }

    public async Task<(IRoom Room, SeatTicket Host)> CreateAsync(string game = "connectfour", int seats = 2, string? variant = null)
    {
        var host = await Lobby.CreateAsync(new RoomRequest(game, seats, variant), "Ala", "c-ala");
        return (Lobby.Find(host.Room), host);
    }

    public async ValueTask DisposeAsync()
    {
        await Lobby.DisposeAsync();
        Notifier.Dispose();
    }
}
