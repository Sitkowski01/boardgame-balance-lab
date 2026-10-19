using BalanceLab.Catalog;
using BalanceLab.Multiplayer;
using BalanceLab.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BalanceLab.Api;

/// <summary>Gra w katalogu, tak jak widzi ja lobby.</summary>
public sealed record GameInfo(string Key, string Name, int MinPlayers, int MaxPlayers, bool HasVariants);

/// <summary>REST obok hubu: katalog gier, lista pokoi i partie z dziennika (do powtorek).</summary>
internal static class GameApi
{
    public static void MapGameApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/games", Games);
        api.MapGet("/games/{key}/rules", Rules);
        api.MapGet("/rooms", RoomsAsync);
        api.MapGet("/rooms/{id:guid}", Room);
        api.MapGet("/matches/{id:guid}", MatchAsync);
        api.MapGet("/matches/{id:guid}/frames/{moves:int}", FrameAsync);
        app.MapGet("/health", () => "ok");
    }

    private static IEnumerable<GameInfo> Games() =>
        GameCatalog.All.Select(g => new GameInfo(g.Key, g.Name, g.MinPlayers, g.MaxPlayers, g.HasVariants));

    /// <summary>Tabela zasad dla ekranu (np. karty wariantu); zly wariant to 400, gra bez tabeli to 404.</summary>
    private static Results<Ok<object>, NotFound, ProblemHttpResult> Rules(string key, string? variant)
    {
        if (GameCatalog.All.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase)) is not { } game)
        {
            return TypedResults.NotFound();
        }

        try
        {
            return game.Sheet(variant) is { } sheet ? TypedResults.Ok(sheet) : TypedResults.NotFound();
        }
        catch (FormatException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IReadOnlyList<RoomInfo>> RoomsAsync(Lobby lobby)
    {
        await lobby.PruneAsync();
        return lobby.Rooms;
    }

    private static Results<Ok<RoomInfo>, NotFound> Room(Guid id, Lobby lobby) =>
        lobby.Rooms.FirstOrDefault(r => r.Id == id) is { } room ? TypedResults.Ok(room) : TypedResults.NotFound();

    private static async Task<Results<Ok<MatchReplay>, NotFound, ProblemHttpResult>> MatchAsync(Guid id, MatchArchive archive, CancellationToken cancellationToken)
    {
        try
        {
            return await archive.LoadAsync(id, cancellationToken) is { } match ? TypedResults.Ok(match) : TypedResults.NotFound();
        }
        catch (JournalMismatchException ex)
        {
            return Unreadable(ex);
        }
    }

    /// <summary>Klatka odtwarzacza: widok widza po tylu ruchach.</summary>
    private static async Task<Results<Ok<ReplayFrame>, NotFound, ProblemHttpResult>> FrameAsync(Guid id, int moves, MatchArchive archive, CancellationToken cancellationToken)
    {
        try
        {
            return await archive.FrameAsync(id, moves, cancellationToken) is { } frame ? TypedResults.Ok(frame) : TypedResults.NotFound();
        }
        catch (ArgumentOutOfRangeException)
        {
            return TypedResults.Problem($"Partia nie ma pozycji po {moves} ruchach.", statusCode: StatusCodes.Status400BadRequest);
        }
        catch (JournalMismatchException ex)
        {
            return Unreadable(ex);
        }
    }

    private static ProblemHttpResult Unreadable(JournalMismatchException ex) =>
        TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Dziennik partii nie zgadza sie z zasadami gry");
}

/// <summary>Co minute usuwa z lobby skonczone i opuszczone pokoje.</summary>
internal sealed class LobbyPruner(Lobby lobby, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await lobby.PruneAsync();
        }
    }
}
