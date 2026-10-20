using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BalanceLab.Engine;

namespace BalanceLab.Simulation;

/// <summary>Wynik pary w lidze; <see cref="FirstScore"/> to sredni wynik pierwszego (remis = pol).</summary>
public sealed record PairResult(string First, string Second, int Completed, Proportion FirstWins, Proportion SecondWins, double FirstScore);

/// <summary>Miejsce w rankingu: Elo (srednia 1500) i sredni wynik ze wszystkich partii.</summary>
public sealed record Rating(string Name, double Elo, double MeanScore);

/// <summary>Ranking i macierz "kto bije kogo"; pary w kolejnosci (0,1), (0,2) ... (1,2) ...</summary>
public sealed record LeagueReport(
    string Game,
    int GamesPerPair,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] ulong Seed,
    ImmutableArray<Rating> Ratings,
    ImmutableArray<PairResult> Pairs);

/// <summary>
/// Liga: kazdy uczestnik gra z kazdym w partiach dwuosobowych. Pokazuje, czy jakas strategia
/// dominuje (wysokie Elo, bije wszystkich) albo czy jest "papier-kamien-nozyce" (kazdy kogos bije).
/// </summary>
public static class League
{
    private const int Iterations = 2_000;

    /// <param name="progress">Ile partii ligi juz rozegrano (wszystkie pary razem: pary * partie na pare).</param>
    public static LeagueReport Run<TState, TMove>(
        IGame<TState, TMove> game,
        IReadOnlyList<Contestant<TState, TMove>> contestants,
        TournamentSpec perPair,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
        where TState : notnull
        where TMove : notnull
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(contestants);
        ArgumentNullException.ThrowIfNull(perPair);
        if (contestants.Count < 2)
        {
            throw new ArgumentException("Liga potrzebuje co najmniej dwoch uczestnikow.", nameof(contestants));
        }

        var pairs = new List<PairResult>();
        for (var i = 0; i < contestants.Count; i++)
        {
            for (var j = i + 1; j < contestants.Count; j++)
            {
                var spec = perPair with { Seed = Tournament.SeedFor(perPair.Seed, PairIndex(i, j), 7) };
                var report = Tournament.Run(game, [contestants[i], contestants[j]], spec, Offset(progress, pairs.Count * perPair.Games), cancellationToken).Report;
                pairs.Add(new PairResult(contestants[i].Name, contestants[j].Name, report.Completed,
                    report.Contestants[0].Wins, report.Contestants[1].Wins, report.Contestants[0].MeanScore));
            }
        }

        return Summarize(game.Name, perPair, [.. contestants.Select(c => c.Name)], pairs);
    }

    /// <summary>
    /// Numer pary (i &lt; j) kolejno: (0,1)=0, (0,2)=1, (1,2)=2, (0,3)=3... Nie zalezy od liczby
    /// uczestnikow, wiec dopisanie bota na koncu listy nie zmienia ziaren par, ktore juz byly.
    /// </summary>
    internal static int PairIndex(int i, int j) => (j * (j - 1) / 2) + i;

    /// <summary>Postep turnieju czesciowego przesuniety o partie rozegrane wczesniej (null, gdy nikt nie slucha).</summary>
    internal static IProgress<int>? Offset(IProgress<int>? progress, int before) =>
        progress is null ? null : new Progress(n => progress.Report(before + n));

    /// <summary>Synchroniczne <see cref="IProgress{T}"/>: <c>Progress&lt;T&gt;</c> z .NET wysyla przez kontekst synchronizacji, czyli z opoznieniem.</summary>
    private sealed class Progress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    internal static LeagueReport Summarize(string game, TournamentSpec perPair, IReadOnlyList<string> names, IReadOnlyList<PairResult> pairs)
    {
        var elo = Elo(names, pairs);
        var ratings = names.Select((name, i) =>
        {
            var played = pairs.Where(p => p.First == name || p.Second == name).ToArray();
            var games = played.Sum(p => p.Completed);
            var points = played.Sum(p => p.Completed * (p.First == name ? p.FirstScore : 1 - p.FirstScore));
            return new Rating(name, elo[i], games == 0 ? 0 : points / games);
        });

        return new LeagueReport(game, perPair.Games, perPair.Seed,
            [.. ratings.OrderByDescending(r => r.Elo).ThenBy(r => r.Name, StringComparer.Ordinal)], [.. pairs]);
    }

    /// <summary>
    /// Elo z modelu Bradleya-Terry'ego: sila g_i taka, ze P(i bije j) = g_i / (g_i + g_j), dopasowana
    /// algorytmem MM (Hunter 2004) do wszystkich partii naraz. Klasyczne Elo aktualizowane partia po
    /// partii zalezy od kolejnosci partii, a tu kolejnosc nie istnieje — liczy sie tylko wynik.
    /// </summary>
    /// <remarks>
    /// Kazda para dostaje jeden wirtualny remis: bez niego gracz, ktory wygral wszystko, mialby sile
    /// nieskonczona. Przy setkach partii na pare wplyw na wynik jest pomijalny.
    /// </remarks>
    internal static double[] Elo(IReadOnlyList<string> names, IReadOnlyList<PairResult> pairs)
    {
        var n = names.Count;
        var games = new double[n, n];
        var score = new double[n];
        foreach (var pair in pairs)
        {
            var (i, j) = (Index(names, pair.First), Index(names, pair.Second));
            var count = pair.Completed + 1.0;
            games[i, j] += count;
            games[j, i] += count;
            score[i] += (pair.Completed * pair.FirstScore) + 0.5;
            score[j] += (pair.Completed * (1 - pair.FirstScore)) + 0.5;
        }

        var strength = Enumerable.Repeat(1.0, n).ToArray();
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var next = new double[n];
            for (var i = 0; i < n; i++)
            {
                var denominator = 0.0;
                for (var j = 0; j < n; j++)
                {
                    denominator += i == j ? 0 : games[i, j] / (strength[i] + strength[j]);
                }

                next[i] = denominator == 0 ? strength[i] : score[i] / denominator;
            }

            // Model nie zna skali (g i 2g daja te same szanse), wiec trzymamy sume rowna n — inaczej liczby by plynely.
            var sum = next.Sum();
            strength = [.. next.Select(g => g * n / sum)];
        }

        // Elo: 400 punktow roznicy = 10 razy wieksza sila, srednia 1500. Zaokraglenie do 0,1 chowa roznice
        // ostatniego bitu logarytmu miedzy systemami — raport z tego samego ziarna wyglada wszedzie tak samo.
        var elo = strength.Select(g => 400 * Math.Log10(g)).ToArray();
        var mean = elo.Average();
        return [.. elo.Select(e => Math.Round(1500 + e - mean, 1))];
    }

    private static int Index(IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i] == name)
            {
                return i;
            }
        }

        throw new ArgumentException($"Para z nieznanym uczestnikiem '{name}'.", nameof(name));
    }
}
