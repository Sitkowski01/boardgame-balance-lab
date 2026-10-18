using System.Collections.Immutable;
using BalanceLab.Engine;
using BalanceLab.Persistence;
using BalanceLab.Simulation;

namespace BalanceLab.Cli;

/// <summary>Partia zapisana przez <c>play</c>.</summary>
internal sealed record PlayedMatch(Guid Id, string Game, int Moves, bool Completed, ImmutableArray<double> Scores, ulong FinalHash);

/// <summary>Partia odczytana przez <c>replay</c>: ruchy jako tekst z dziennika i wynik sprawdzenia.</summary>
/// <param name="At">Stan po tylu ruchach, jesli o niego proszono; hash (odtworzony z migawki) tylko, gdy dziennik sie zgadza.</param>
internal sealed record ReplayedMatch(
    MatchInfo Match,
    string GameName,
    ImmutableArray<string> Moves,
    ImmutableArray<ulong> Hashes,
    JournalVerification Verification,
    (int Moves, ulong? Hash)? At);

internal sealed partial class GameEntry<TState, TMove, TGame>
{
    public Func<Task<PlayedMatch>> PreparePlay(IReadOnlyList<string> bots, TournamentSpec spec, string? variant, JournalDb db)
    {
        var (played, contestants, seed) = (WithVariant(variant), Contestants(bots), spec.Seed);
        // Te same reguly co w turnieju (liczba graczy, unikalne nazwy, limit ruchow); liczba partii tylko je spelnia.
        Tournament.Validate(played, contestants, spec with { Games = Math.Max(contestants.Length, 1) });
        return async () =>
        {
            // Ziarna graczy jak w pierwszej partii turnieju z tym ziarnem, ale bez rotacji miejsc:
            // gracze siedza w kolejnosci z --bots.
            // Najpierw baza: zle polaczenie ma wyjsc od razu, a nie po dlugiej partii, ktora przepadnie.
            await db.MigrateAsync();
            var setup = new GameSetup(contestants.Length, seed);
            var players = contestants.Select((c, seat) => c.Create(Tournament.SeedFor(seed, 0, seat + 1))).ToArray();
            var result = Match.Play(played, setup, players, spec.MaxMoves);
            var id = await Journal(db).RecordAsync(setup, [.. contestants.Select(c => c.Name)], result.Moves, variant);
            return new PlayedMatch(id, played.Name, result.Moves.Length, result.Completed, result.Scores, played.Hash(result.FinalState));
        };
    }

    public async Task<ReplayedMatch> ReplayAsync(JournalDb db, Guid match, int? at)
    {
        var journal = Journal(db);
        var log = await journal.LoadAsync(match);
        if (at > log.Match.Moves)
        {
            throw new ArgumentException($"--at: partia ma tylko {log.Match.Moves} ruchow.", nameof(at));
        }

        var rules = WithVariant(log.Match.Variant);
        var verification = await journal.VerifyAsync(match);
        // Stan po N ruchach tylko z dziennika, ktory sie zgadza — inaczej wystarczy opis rozjazdu.
        (int, ulong?)? state = at is { } moves ? (moves, verification.Ok ? rules.Hash(await journal.StateAtAsync(match, moves)) : null) : null;
        return new ReplayedMatch(log.Match, rules.Name, [.. log.Moves.Select(rules.EncodeMove)], log.Hashes, verification, state);
    }

    private MatchJournal<TState, TMove> Journal(JournalDb db) =>
        new(db, key, variant =>
        {
            var rules = WithVariant(variant);
            return new StoredGame<TState, TMove>(rules, rules, rules);
        });
}
