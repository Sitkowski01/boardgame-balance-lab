using System.Diagnostics;
using BalanceLab.Catalog;
using BalanceLab.Engine;
using BalanceLab.Simulation;

namespace BalanceLab.Experiments;

/// <summary>Eksperyment sprawdzony i gotowy do liczenia: ile partii obejmie i jak go uruchomic.</summary>
/// <param name="Work">Szacowana praca (<see cref="ExperimentOptions.MaxWork"/>); 0, gdy nie liczona (testy).</param>
internal sealed record PreparedExperiment(int Total, Func<IProgress<int>, CancellationToken, object> Run, long Work = 0);

/// <summary>
/// Sklada eksperyment z katalogu gier tak jak CLI (te same boty, warianty i ziarna), wiec wynik z przegladarki
/// jest ten sam co z <c>balance-lab simulate</c>. Bledy zamowienia wychodza tu, zanim cokolwiek trafi do kolejki.
/// </summary>
internal sealed class Preparation(ExperimentRequest request, ExperimentOptions options) : IGameVisitor<PreparedExperiment>
{
    /// <exception cref="FormatException">Nieznana gra, bot albo zly wariant.</exception>
    /// <exception cref="ArgumentException">Zamowienie poza limitami albo zasadami turnieju.</exception>
    public static PreparedExperiment Prepare(ExperimentRequest request, ExperimentOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(request.Game))
        {
            throw new ArgumentException("Brak gry w zamówieniu.", nameof(request));
        }

        if (request.Bots.IsDefaultOrEmpty || request.Bots.Length > options.MaxBots)
        {
            throw new ArgumentException($"Eksperyment potrzebuje od 1 do {options.MaxBots} botów.", nameof(request));
        }

        if (request.Bots.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Opis bota nie może być pusty.", nameof(request));
        }

        if (request.Games < 1 || request.Games > options.MaxGames)
        {
            throw new ArgumentException($"Liczba partii: od 1 do {options.MaxGames}.", nameof(request));
        }

        if (!Enum.IsDefined(request.Kind))
        {
            throw new ArgumentException($"Nieznany rodzaj eksperymentu {request.Kind}.", nameof(request));
        }

        if (request.Kind == ExperimentKind.Comparison && string.IsNullOrWhiteSpace(request.Variant))
        {
            throw new ArgumentException("Porównanie potrzebuje wariantu zasad - to on jest \"co jeśli\".", nameof(request));
        }

        if (request.Kind == ExperimentKind.League && request.Bots.Length < 2)
        {
            throw new ArgumentException("Liga potrzebuje co najmniej dwóch botów.", nameof(request));
        }

        var prepared = GameCatalog.Find(request.Game).Accept(new Preparation(request, options));
        return prepared.Work > options.MaxWork
            ? throw new ArgumentException(
                $"Eksperyment za duży dla laboratorium: {prepared.Work} jednostek pracy, najwyżej {options.MaxWork}. Mniej partii, botów albo iteracji MCTS - pełne turnieje liczy CLI.",
                nameof(request))
            : prepared;
    }

    /// <summary>
    /// Waga bota w budzecie pracy: tyle "jednostek" kosztuje jego ruch. Proste boty to 1, przeszukiwanie — tyle,
    /// ile iteracji (kazda to dogrywka partii), bez liczby — domyslne 1000.
    /// </summary>
    internal static long Weight(string bot)
    {
        var parts = bot.Split(':', 2);
        if (parts[0].ToLowerInvariant() is not ("mcts" or "ismcts"))
        {
            return 1;
        }

        return parts.Length == 2 && long.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n
            : Bots.MctsOptions.DefaultIterations;
    }

    /// <summary>Praca eksperymentu: partie razy wagi botow, ktore w nich graja.</summary>
    internal static long WorkOf(ExperimentRequest request)
    {
        var weights = request.Bots.Sum(Weight);
        return request.Kind switch
        {
            // Liga: kazdy bot gra z kazdym z pozostalych n-1 botow po Games partii.
            ExperimentKind.League => (long)request.Games * (request.Bots.Length - 1) * weights,
            ExperimentKind.Comparison => 2L * request.Games * weights,
            _ => (long)request.Games * weights,
        };
    }

    public PreparedExperiment Visit<TState, TMove, TGame>(GameDefinition<TState, TMove, TGame> definition)
        where TState : notnull
        where TMove : notnull
        where TGame : IGame<TState, TMove>, IMoveCodec<TMove>, IStateCodec<TState>, IPlayerView<TState, object>
    {
        var played = definition.Rules(string.IsNullOrWhiteSpace(request.Variant) ? null : request.Variant);
        var names = BotSpec.UniqueNames(request.Bots);
        Contestant<TState, TMove>[] bots = [.. request.Bots.Select((bot, i) => definition.Bot(bot, names[i], options.MaxBotIterations))];
        var spec = new TournamentSpec(request.Games, request.Seed) { MaxDegreeOfParallelism = options.Threads };
        switch (request.Kind)
        {
            case ExperimentKind.Tournament:
                Tournament.Validate(played, bots, spec);
                return new(spec.Games, (progress, token) => Tournament.Run(played, bots, spec, progress, token).Report, WorkOf(request));
            case ExperimentKind.League:
                // Liga gra parami: sprawdzamy pierwsza pare, reszta ma te same parametry.
                Tournament.Validate(played, bots[..2], spec);
                var pairs = bots.Length * (bots.Length - 1) / 2;
                return new(pairs * spec.Games, (progress, token) => League.Run(played, bots, spec, progress, token), WorkOf(request));
            case ExperimentKind.Comparison:
                Tournament.Validate(definition.Game, bots, spec);
                return new(2 * spec.Games, (progress, token) => Comparison.Run(definition.Game, played, bots, spec, progress, token), WorkOf(request));
            default:
                throw new UnreachableException($"Rodzaj {request.Kind} przeszedł sprawdzenie w Prepare.");
        }
    }
}
