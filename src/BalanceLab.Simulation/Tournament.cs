using System.Collections.Immutable;
using BalanceLab.Engine;

namespace BalanceLab.Simulation;

/// <summary>
/// Rozgrywa wiele partii rownolegle i liczy statystyki. Wynik zalezy tylko od gry, uczestnikow
/// i <see cref="TournamentSpec.Seed"/> — nie od liczby watkow ani kolejnosci ich pracy.
/// </summary>
public static class Tournament
{
    public static TournamentResult Run<TState, TMove>(
        IGame<TState, TMove> game,
        IReadOnlyList<Contestant<TState, TMove>> contestants,
        TournamentSpec spec,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
        where TState : notnull
        where TMove : notnull
    {
        Validate(game, contestants, spec);

        // Kazda partia pisze do swojej komorki tablicy, wiec watki niczego nie wspoldziela.
        var records = new GameRecord[spec.Games];
        var failures = new GameFailedException?[spec.Games];
        var finished = 0;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = spec.MaxDegreeOfParallelism,
            CancellationToken = cancellationToken,
        };
        Parallel.For(0, spec.Games, options, index =>
        {
            try
            {
                records[index] = PlayGame(game, contestants, spec, index);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures[index] = new GameFailedException(index, SeedFor(spec.Seed, index, 0), ex);
            }

            progress?.Report(Interlocked.Increment(ref finished));
        });

        // Zglaszamy blad z najnizszym numerem partii, a nie ten, ktory akurat wystapil pierwszy —
        // inaczej komunikat zalezalby od liczby watkow.
        var failure = Array.Find(failures, f => f is not null);
        if (failure is not null)
        {
            throw failure;
        }

        ItemInfo[] items = game is IItemTracking<TState> tracking
            ? [.. Enumerable.Range(0, tracking.ItemCount).Select(i => new ItemInfo(tracking.ItemName(i), tracking.ItemGroup(i)))]
            : [];
        var report = Statistics.Summarize(game.Name, spec, [.. contestants.Select(c => c.Name)], records, items)
            with { ItemNoun = game is IItemTracking<TState> named ? named.Noun : ItemNoun.Cards };
        return new TournamentResult(report, [.. records]);
    }

    /// <summary>
    /// Ziarno partii i graczy wyprowadzone z ziarna turnieju i numeru partii: ta sama partia
    /// wychodzi tak samo niezaleznie od tego, ktory watek i kiedy ja rozegral.
    /// </summary>
    public static ulong SeedFor(ulong tournamentSeed, int game, int purpose) =>
        StableHash.Empty.Add(tournamentSeed).Add((ulong)game).Add((ulong)purpose).Value;

    private static GameRecord PlayGame<TState, TMove>(
        IGame<TState, TMove> game,
        IReadOnlyList<Contestant<TState, TMove>> contestants,
        TournamentSpec spec,
        int index)
        where TState : notnull
        where TMove : notnull
    {
        // Rotacja miejsc: w partii i uczestnik k siada na miejscu (k + i) mod n, wiec kazdy
        // zaczyna rownie czesto i przewaga pierwszego gracza nie przechyla porownania botow.
        var n = contestants.Count;
        var seating = Enumerable.Range(0, n).Select(seat => contestants[(seat - (index % n) + n) % n]).ToArray();

        var seed = SeedFor(spec.Seed, index, 0);
        var players = seating.Select((c, seat) => c.Create(SeedFor(spec.Seed, index, seat + 1))).ToArray();
        var match = Match.Play(game, new GameSetup(n, seed), players, spec.MaxMoves);

        return new GameRecord(index, seed, [.. seating.Select(c => c.Name)], match.Scores, match.Moves.Length, match.Completed)
        {
            Items = game is IItemTracking<TState> tracking
                ? [.. Enumerable.Range(0, n).Select(seat => CheckedItems(tracking, match.FinalState, seat))]
                : [],
        };
    }

    /// <summary>
    /// Numer spoza 0..ItemCount−1 to blad gry; zlapany tu, przerywa partie z jej numerem i ziarnem,
    /// zamiast wysypac statystyki po calym turnieju.
    /// </summary>
    private static ImmutableArray<int> CheckedItems<TState>(IItemTracking<TState> tracking, TState state, int seat)
        where TState : notnull
    {
        var items = tracking.ItemsOf(state, seat).ToImmutableArray();
        foreach (var item in items)
        {
            if (item < 0 || item >= tracking.ItemCount)
            {
                throw new InvalidOperationException($"Gra podala element {item} spoza zakresu 0-{tracking.ItemCount - 1}.");
            }
        }

        return items;
    }

    /// <summary>
    /// Sprawdza turniej bez rozgrywania — CLI wola to przed startem, zeby bledny parametr wyszedl
    /// od razu, a nie po godzinie symulacji. Jedyne zrodlo tych regul.
    /// </summary>
    public static void Validate<TState, TMove>(
        IGame<TState, TMove> game,
        IReadOnlyList<Contestant<TState, TMove>> contestants,
        TournamentSpec spec)
        where TState : notnull
        where TMove : notnull
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(contestants);
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Games <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), spec.Games, "Liczba partii musi byc wieksza od 0.");
        }

        if (spec.MaxMoves <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), spec.MaxMoves, "Limit ruchow musi byc wiekszy od 0.");
        }

        if (spec.MaxDegreeOfParallelism is 0 or < -1)
        {
            throw new ArgumentOutOfRangeException(nameof(spec), spec.MaxDegreeOfParallelism, "Liczba watkow: -1 albo wiecej niz 0.");
        }

        if (contestants.Count < game.MinPlayers || contestants.Count > game.MaxPlayers)
        {
            throw new ArgumentException(
                $"{game.Name} to gra dla {game.MinPlayers}-{game.MaxPlayers} graczy, a uczestnikow jest {contestants.Count}.",
                nameof(contestants));
        }

        var duplicate = contestants.GroupBy(c => c.Name).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Nazwa uczestnika '{duplicate.Key}' powtarza sie - raport by ich pomieszal.", nameof(contestants));
        }

        if (spec.Games % contestants.Count != 0)
        {
            throw new ArgumentException(
                $"Liczba partii ({spec.Games}) musi byc wielokrotnoscia liczby uczestnikow ({contestants.Count}), zeby kazdy zaczynal tyle samo razy.",
                nameof(spec));
        }
    }
}
