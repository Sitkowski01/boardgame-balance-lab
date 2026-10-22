using BalanceLab.Engine;
using BalanceLab.Persistence;

namespace BalanceLab.Multiplayer;

/// <summary>
/// Dokad pokoj wysyla zmiany. Serwer przekazuje je klientom (SignalR), testy zbieraja je w liscie.
/// Pokoj sam decyduje, kto co widzi: widok miejsca idzie tylko do tego miejsca.
/// </summary>
public interface IRoomNotifier
{
    /// <summary>Do wszystkich w pokoju i do lobby.</summary>
    Task RoomChangedAsync(RoomInfo room);

    /// <summary>Do jednego miejsca albo do widzow (<paramref name="seat"/> = null).</summary>
    Task ViewChangedAsync(Guid room, int? seat, TableView view);

    /// <summary>Do wszystkich w pokoju.</summary>
    Task MovePlayedAsync(MovePlayed move);
}

/// <summary>Zapis partii z pokoju. Kazdy ruch jest zapisany, zanim ktokolwiek go zobaczy.</summary>
public interface IMatchRecorder<in TMove>
    where TMove : notnull
{
    Task<Guid> StartAsync(GameSetup setup, IReadOnlyList<string> players, string? variant, CancellationToken cancellationToken = default);

    /// <param name="expectedMoves">Ile ruchow partia ma przed tym; inna liczba w dzienniku to blad zapisu.</param>
    Task AppendAsync(Guid match, int expectedMoves, TMove move, CancellationToken cancellationToken = default);

    /// <summary>Czy ostatni zapisany ruch to ten, po <paramref name="expectedMoves"/> wczesniejszych (zapis, ktory zglosil blad, mogl jednak wejsc).</summary>
    Task<bool> ContainsAsync(Guid match, int expectedMoves, TMove move, CancellationToken cancellationToken = default);

    /// <summary>Zamyka trwajaca partie jako porzucona; zamknieta zostaje bez zmian.</summary>
    Task AbandonAsync(Guid match, CancellationToken cancellationToken = default);
}

/// <summary>Tworzy zapis dla gry z katalogu.</summary>
public interface IMatchRecorderFactory
{
    /// <param name="game">Klucz gry w dzienniku.</param>
    /// <param name="rules">Zasady z kodekami dla wariantu partii.</param>
    IMatchRecorder<TMove> Create<TState, TMove>(string game, Func<string?, StoredGame<TState, TMove>> rules)
        where TState : notnull
        where TMove : notnull;
}

/// <summary>Zapis do dziennika partii w PostgreSQL.</summary>
public sealed class JournalRecorderFactory(JournalDb db) : IMatchRecorderFactory
{
    public IMatchRecorder<TMove> Create<TState, TMove>(string game, Func<string?, StoredGame<TState, TMove>> rules)
        where TState : notnull
        where TMove : notnull =>
        new JournalRecorder<TState, TMove>(new MatchJournal<TState, TMove>(db, game, rules));

    private sealed class JournalRecorder<TState, TMove>(MatchJournal<TState, TMove> journal) : IMatchRecorder<TMove>
        where TState : notnull
        where TMove : notnull
    {
        public Task<Guid> StartAsync(GameSetup setup, IReadOnlyList<string> players, string? variant, CancellationToken cancellationToken = default) =>
            journal.StartAsync(setup, players, variant, cancellationToken);

        public Task AppendAsync(Guid match, int expectedMoves, TMove move, CancellationToken cancellationToken = default) =>
            journal.AppendAsync(match, expectedMoves, move, cancellationToken);

        public Task<bool> ContainsAsync(Guid match, int expectedMoves, TMove move, CancellationToken cancellationToken = default) =>
            journal.ContainsAsync(match, expectedMoves, move, cancellationToken);

        public Task AbandonAsync(Guid match, CancellationToken cancellationToken = default) =>
            journal.AbandonAsync(match, cancellationToken);
    }
}

/// <summary>Zasady pokoi: czasy, boty i limity.</summary>
public sealed record RoomOptions
{
    /// <summary>Tyle ma czlowiek na ruch; potem za niego gra <see cref="AutopilotBot"/>.</summary>
    public TimeSpan MoveTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Przerwa przed ruchem bota, zeby ludzie widzieli, co sie dzieje.</summary>
    public TimeSpan BotDelay { get; init; } = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Ile czeka czlowiek rozlaczony na swoim ruchu, zanim zagra za niego bot. Odswiezenie strony to krotkie
    /// rozlaczenie: gracz zdazy wrocic (Rejoin) i zagrac sam.
    /// </summary>
    public TimeSpan DisconnectGrace { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Po nieudanym zapisie ruchu bota (np. baza chwilowo niedostepna) — tyle czekamy przed kolejna proba.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Bot na miejscach wolnych w chwili startu.</summary>
    public string FillBot { get; init; } = "ismcts:300";

    /// <summary>Bot, ktory gra za czlowieka po czasie albo podczas rozlaczenia.</summary>
    public string AutopilotBot { get; init; } = "ismcts:100";

    /// <summary>Najwiecej iteracji MCTS/ISMCTS dla bota z lobby: bot liczy na serwerze.</summary>
    public int MaxBotIterations { get; init; } = 5_000;

    /// <summary>
    /// Najwiecej botow liczacych naraz na calym serwerze. Liczenie zajmuje watek puli, z ktorej korzysta tez
    /// Kestrel i SignalR — bez limitu sto pokoi z MCTS zaglodziloby obsluge zadan.
    /// </summary>
    public int MaxBotSearches { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>Tyle czeka pokoj na zapis w dzienniku; wolna baza nie moze zatrzymac kolejki pokoju na dlugo.</summary>
    public TimeSpan RecordTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Tyle nieudanych zapisow ruchu bota z rzedu i partia jest porzucana, zamiast ponawiac bez konca.</summary>
    public int MaxRecordFailures { get; init; } = 5;

    /// <summary>Najdluzsza nazwa gracza i opis bota.</summary>
    public int MaxNameLength { get; init; } = 24;

    public int MaxRooms { get; init; } = 100;

    /// <summary>Najwiecej niezakonczonych pokoi zalozonych z jednego adresu: jeden klient nie zajmie calego lobby.</summary>
    public int MaxRoomsPerOwner { get; init; } = 3;

    /// <summary>Tyle skonczony pokoj zostaje w lobby (wyniki, rewanz), zanim zniknie.</summary>
    public TimeSpan KeepFinished { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Tyle czeka pokoj, w ktorym nic sie nie dzieje (a przed startem: nikt nie jest polaczony), zanim zniknie.</summary>
    public TimeSpan KeepIdle { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Najdluzej pokoj czeka na start, nawet z polaczonymi graczami — zeby nikt nie zajal lobby na stale.</summary>
    public TimeSpan KeepWaiting { get; init; } = TimeSpan.FromHours(2);
}

/// <summary>Pokoj bez typow gry, dla lobby i serwera. Wszystkie operacje ida po kolei przez jedna kolejke pokoju.</summary>
public interface IRoom : IAsyncDisposable
{
    Guid Id { get; }

    /// <summary>Ostatni stan pokoju; czytanie nie czeka na kolejke.</summary>
    RoomInfo Info { get; }

    /// <summary>Kiedy w pokoju ostatnio cos sie zmienilo.</summary>
    DateTimeOffset LastActivity { get; }

    Task<SeatTicket> JoinAsync(string name, string connection);

    /// <summary>Powrot na swoje miejsce z biletem (np. po zerwaniu polaczenia albo z drugiej karty).</summary>
    Task<SeatTicket> RejoinAsync(string token, string connection);

    /// <summary>Widok z miejsca z tym biletem; null, dopoki partia sie nie zaczela.</summary>
    Task<TableView?> ViewAsync(string token);

    /// <summary>Widok dla widza; null, dopoki partia sie nie zaczela.</summary>
    Task<TableView?> SpectateAsync();

    Task AddBotAsync(string token, int seat, string bot);

    Task StartAsync(string token);

    /// <param name="number">Ile ruchow juz zagrano (z <see cref="TableView.Moves"/>): ruch do innej pozycji jest odrzucany.</param>
    Task MoveAsync(string token, int number, string move);

    Task DisconnectAsync(string connection);
}
