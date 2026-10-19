using System.Collections.Immutable;
using System.Globalization;
using BalanceLab.Engine;

namespace BalanceLab.Multiplayer.Tests;

/// <summary>Co psuje bota w <see cref="CountdownGame"/>: wyobrazony stan (widok bota) wywraca albo podsuwa zly ruch.</summary>
public enum Trick
{
    None,
    BotCrashes,
    BotPlaysIllegal,
}

/// <param name="Imagined">Stan z <c>Determinize</c> — tak widzi go bot, nigdy pokoj.</param>
internal sealed record CountdownState(int Left, int ToMove, bool Imagined);

internal readonly record struct Take(int Count);

/// <summary>
/// Odliczanie: dwoch graczy na zmiane zabiera 1 albo 2 z 9, wygrywa ten, kto zabierze ostatni. Gra tylko do testow:
/// przez <see cref="Trick"/> bot dostaje widok, na ktorym sie wywraca albo wybiera nielegalny ruch.
/// </summary>
internal sealed class CountdownGame(Trick trick) : IGame<CountdownState, Take>, IMoveCodec<Take>, IStateCodec<CountdownState>, IPlayerView<CountdownState, CountdownState>
{
    public string Name => "Odliczanie";

    public int MinPlayers => 2;

    public int MaxPlayers => 2;

    public CountdownState Setup(GameSetup setup) => new(9, 0, false);

    public int PlayerCount(CountdownState state) => 2;

    public int CurrentPlayer(CountdownState state) => state.ToMove;

    public IReadOnlyList<Take> LegalMoves(CountdownState state) => (state.Imagined, trick) switch
    {
        (true, Trick.BotCrashes) => throw new InvalidOperationException("bot sie wywrocil"),
        (true, Trick.BotPlaysIllegal) => [new Take(3)],
        _ => [.. Enumerable.Range(1, Math.Min(2, state.Left)).Select(n => new Take(n))],
    };

    public CountdownState Apply(CountdownState state, Take move) =>
        move.Count >= 1 && move.Count <= Math.Min(2, state.Left)
            ? new(state.Left - move.Count, 1 - state.ToMove, false)
            : throw new IllegalMoveException($"Nie mozna zabrac {move.Count}.");

    public bool IsTerminal(CountdownState state) => state.Left == 0;

    public ImmutableArray<double> Scores(CountdownState state) => state.ToMove == 1 ? [1.0, 0.0] : [0.0, 1.0];

    public ulong Hash(CountdownState state) => StableHash.Empty.Add((ulong)state.Left).Add((ulong)state.ToMove).Value;

    public CountdownState Determinize(CountdownState state, int observer, ulong seed) => state with { Imagined = true };

    public CountdownState View(CountdownState state, int? observer) => state;

    public string EncodeMove(Take move) => move.Count.ToString(CultureInfo.InvariantCulture);

    public Take DecodeMove(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? new Take(n) : throw new FormatException(text);

    public string EncodeState(CountdownState state) => string.Create(CultureInfo.InvariantCulture, $"{state.Left},{state.ToMove}");

    public CountdownState DecodeState(string text) =>
        text.Split(',') is [var left, var toMove] ? new(int.Parse(left, CultureInfo.InvariantCulture), int.Parse(toMove, CultureInfo.InvariantCulture), false) : throw new FormatException(text);
}
