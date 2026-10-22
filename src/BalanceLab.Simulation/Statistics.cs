using System.Collections.Immutable;

namespace BalanceLab.Simulation;

/// <summary>Liczy raport z rozegranych partii. Partie nieskonczone nie wchodza do odsetkow wygranych.</summary>
internal static class Statistics
{
    public static TournamentReport Summarize(string game, TournamentSpec spec, IReadOnlyList<string> names, IReadOnlyList<GameRecord> records, IReadOnlyList<ItemInfo>? items = null)
    {
        var completed = records.Where(r => r.Completed).ToArray();
        var winners = completed.Select(r => r.WinningSeat).ToArray(); // liczone raz, nie dla kazdego uczestnika
        var (itemStats, groups) = Items(completed, winners, items ?? []);

        var contestants = names.Select(name =>
        {
            var wins = completed.Where((r, i) => winners[i] is { } seat && r.Seating[seat] == name).Count();
            var meanScore = completed.Length == 0 ? 0 : completed.Average(r => r.Scores[r.Seating.IndexOf(name)]);
            return new ContestantStats(name, Proportion.Of(wins, completed.Length), meanScore);
        });

        // Sredni wynik miejsca liczy remis jako pol wygranej; sam odsetek wygranych traktuje remis jak
        // przegrana, a wtedy w zbalansowanej grze z remisami kazde miejsce wyglada na stratne.
        var seats = Enumerable.Range(0, names.Count).Select(seat => new SeatStats(
            seat,
            Proportion.Of(winners.Count(w => w == seat), completed.Length),
            completed.Length == 0 ? 0 : completed.Average(r => r.Scores[seat])));

        return new TournamentReport(
            game,
            spec.Games,
            spec.Seed,
            completed.Length,
            winners.Count(w => w is null),
            [.. contestants],
            [.. seats],
            Lengths(completed))
        {
            Items = itemStats,
            ItemGroups = groups,
        };
    }

    private static (ImmutableArray<ItemStats> Items, ImmutableArray<GroupStats> Groups) Items(GameRecord[] completed, int?[] winners, IReadOnlyList<ItemInfo> items)
    {
        if (items.Count == 0)
        {
            return ([], []); // gra nie sledzi elementow
        }

        var games = new int[items.Count];
        var owned = new int[items.Count];
        var ownerWins = new int[items.Count];
        for (var g = 0; g < completed.Length; g++)
        {
            var seen = new HashSet<int>();
            for (var seat = 0; seat < completed[g].Items.Length; seat++)
            {
                foreach (var item in completed[g].Items[seat])
                {
                    owned[item]++;
                    ownerWins[item] += winners[g] == seat ? 1 : 0;
                    games[item] += seen.Add(item) ? 1 : 0; // dwoch graczy z tym samym elementem to dalej jedna partia
                }
            }
        }

        var stats = items.Select((info, item) =>
            new ItemStats(item, info.Name, info.Group, Proportion.Of(games[item], completed.Length), Proportion.Of(ownerWins[item], owned[item])));
        // Grupa w kolejnosci pierwszego wystapienia; jej odsetek liczony po wszystkich posiadaniach jej elementow.
        var groups = Enumerable.Range(0, items.Count).GroupBy(item => items[item].Group).Select(group =>
            new GroupStats(group.Key, Proportion.Of(group.Sum(item => ownerWins[item]), group.Sum(item => owned[item]))));
        GroupStats[] all = [.. groups];
        return ([.. stats.Select(s => s with { Verdict = ItemVerdicts.Of(s, Rest(all.First(g => g.Group == s.Group).OwnerWins, s.OwnerWins)) })], [.. all]);
    }

    /// <summary>
    /// Poprzeczka elementu to jego grupa bez niego samego: w Mozaice grupa ma 5 pol, wiec pole liczone
    /// do wlasnej sredniej podnosiloby ja o 1/5 swojej przewagi i chowalo mocne pola.
    /// </summary>
    private static Proportion Rest(Proportion group, Proportion item) =>
        group.Trials > item.Trials
            ? Proportion.Of(group.Successes - item.Successes, group.Trials - item.Trials)
            : group; // jedyny element grupy: nie ma z czym porownac, a z soba samym nie odstaje


    private static LengthStats? Lengths(GameRecord[] completed)
    {
        if (completed.Length == 0)
        {
            return null;
        }

        var sorted = completed.Select(r => r.Moves).Order().ToArray();
        // Percentyl metoda "najblizszej rangi": wartosc, ponizej ktorej jest co najmniej p% partii.
        int Percentile(double p) => sorted[(int)Math.Ceiling(p * sorted.Length) - 1];

        var mean = sorted.Average();
        var variance = sorted.Length < 2 ? 0 : sorted.Sum(m => (m - mean) * (m - mean)) / (sorted.Length - 1);
        return new LengthStats(mean, sorted[0], Percentile(0.5), Percentile(0.9), sorted[^1]) { StdDev = Math.Sqrt(variance) };
    }
}
