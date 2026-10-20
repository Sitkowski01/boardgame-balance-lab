using System.Collections.Immutable;
using System.Text.Json.Serialization;
using BalanceLab.Engine;

namespace BalanceLab.Simulation;

/// <summary>Uczestnik turnieju: nazwa do raportu i fabryka gracza z ziarna (nowy gracz na kazda partie).</summary>
public sealed record Contestant<TState, TMove>(string Name, Func<ulong, IPlayer<TState, TMove>> Create)
    where TState : notnull
    where TMove : notnull;

/// <summary>Ile partii, z jakiego ziarna i na ilu watkach.</summary>
public sealed record TournamentSpec(int Games, ulong Seed)
{
    /// <summary>Liczba watkow (-1 = tyle, ile da pula watkow); nie wplywa na wyniki, tylko na czas.</summary>
    public int MaxDegreeOfParallelism { get; init; } = -1;

    /// <summary>Limit ruchow w partii; dluzsza partia jest liczona jako nieskonczona.</summary>
    public int MaxMoves { get; init; } = 10_000;
}

/// <summary>Jedna rozegrana partia: kto gdzie siedzial i z jakim wynikiem.</summary>
public sealed record GameRecord(
    int Index,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] ulong Seed,
    ImmutableArray<string> Seating,
    ImmutableArray<double> Scores,
    int Moves,
    bool Completed)
{
    /// <summary>
    /// Elementy (np. karty) kazdego miejsca na koniec partii; puste, gdy gra ich nie sledzi
    /// (<see cref="IItemTracking{TState}"/>).
    /// </summary>
    public ImmutableArray<ImmutableArray<int>> Items { get; init; } = [];

    /// <summary>Miejsce zwyciezcy albo null przy remisie (brak jedynego najlepszego wyniku) i partii nieskonczonej.</summary>
    [JsonIgnore]
    public int? WinningSeat
    {
        get
        {
            if (!Completed)
            {
                return null;
            }

            var best = Scores.Max();
            var winners = Enumerable.Range(0, Scores.Length).Where(seat => Scores[seat] == best).ToArray();
            return winners.Length == 1 ? winners[0] : null;
        }
    }
}

/// <summary>Wyniki uczestnika w ukonczonych partiach (ich liczba to <see cref="Proportion.Trials"/>).</summary>
public sealed record ContestantStats(string Name, Proportion Wins, double MeanScore);

/// <summary>
/// Wyniki miejsca przy stole. Przy remisach uczciwa miara przewagi to <see cref="MeanScore"/>
/// (remis = pol wygranej); brak przewagi to 1 / liczba graczy.
/// </summary>
public sealed record SeatStats(int Seat, Proportion Wins, double MeanScore);

/// <summary>Dlugosc ukonczonych partii w ruchach.</summary>
public sealed record LengthStats(double Mean, int Min, int Median, int P90, int Max)
{
    /// <summary>Odchylenie standardowe (z proby); potrzebne, zeby porownac srednie dwoch wariantow.</summary>
    public double StdDev { get; init; }
}

/// <summary>Opis elementu gry dla raportu: nazwa i grupa elementow projektowanych jako rownowazne.</summary>
public sealed record ItemInfo(string Name, string Group);

/// <summary>
/// Element gry (np. karta) w ukonczonych partiach. <see cref="Taken"/>: w jakiej czesci partii ktos go
/// mial; <see cref="OwnerWins"/>: jak czesto jego wlasciciel wygral. Rzadko brany = martwy; wlasciciel
/// wygrywa wyraznie czesciej niz przy przecietnym elemencie jego grupy (<see cref="GroupStats"/>)
/// = podejrzanie mocny. To korelacja, nie dowod: silny gracz kupuje tez dobre karty.
/// </summary>
public sealed record ItemStats(int Id, string Name, string Group, Proportion Taken, Proportion OwnerWins)
{
    /// <summary>Ocena wobec grupy (<see cref="ItemVerdicts.Of"/>) — w raporcie, zeby kazdy odbiorca ocenial tak samo.</summary>
    public ItemVerdict Verdict { get; init; }
}

/// <summary>Jak czesto wygrywa wlasciciel przecietnego elementu grupy — punkt odniesienia dla jej elementow.</summary>
public sealed record GroupStats(string Group, Proportion OwnerWins);

/// <summary>Podsumowanie turnieju — to trafia do raportu JSON.</summary>
public sealed record TournamentReport(
    string Game,
    int Games,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] ulong Seed,
    int Completed,
    int Draws,
    ImmutableArray<ContestantStats> Contestants,
    ImmutableArray<SeatStats> Seats,
    LengthStats? Length)
{
    /// <summary>Statystyki elementow gry; puste, gdy gra ich nie sledzi.</summary>
    public ImmutableArray<ItemStats> Items { get; init; } = [];

    /// <summary>Jak nazywaja sie elementy tej gry (np. karty, pola sciany).</summary>
    public ItemNoun ItemNoun { get; init; } = ItemNoun.Cards;

    /// <summary>
    /// Grupy elementow (np. poziomy kart). Tanie karty kupuje kazdy, drogie glownie prowadzacy, wiec
    /// karte porownujemy tylko z jej grupa — srednia ze wszystkich kart myli koszt z sila.
    /// </summary>
    public ImmutableArray<GroupStats> ItemGroups { get; init; } = [];

    /// <summary>Punkt odniesienia dla elementu: wygrane wlasciciela w jego grupie.</summary>
    public Proportion GroupOf(ItemStats item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ItemGroups.First(g => g.Group == item.Group).OwnerWins;
    }
}

public sealed record TournamentResult(TournamentReport Report, ImmutableArray<GameRecord> Games);
