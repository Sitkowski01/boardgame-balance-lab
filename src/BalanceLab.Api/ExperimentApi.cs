using BalanceLab.Experiments;
using Microsoft.AspNetCore.Http.HttpResults;

namespace BalanceLab.Api;

/// <summary>
/// Laboratorium balansu przez HTTP: zamowienie eksperymentu (202 i adres do odpytywania), stan z postepem
/// i wynikiem, lista ostatnich i anulowanie. Liczy <see cref="ExperimentLab"/> w tle, po jednym naraz.
/// </summary>
internal static class ExperimentApi
{
    public static void MapExperimentApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/experiments");
        api.MapPost("/", Submit);
        api.MapGet("/", (ExperimentLab lab) => lab.Recent);
        api.MapGet("/limits", (ExperimentLab lab) => lab.Options.Limits);
        api.MapGet("/{id:guid}", Find);
        api.MapDelete("/{id:guid}", Cancel);
    }

    /// <summary>Zle zamowienie to 400 z powodem, pelna kolejka to 503 — mozna sprobowac za chwile.</summary>
    private static Results<Accepted<ExperimentInfo>, ProblemHttpResult> Submit(ExperimentRequest request, ExperimentLab lab, HttpContext http)
    {
        try
        {
            var info = lab.Submit(request, ClientKey.Of(http));
            return TypedResults.Accepted($"/api/experiments/{info.Id}", info);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Złe zamówienie eksperymentu");
        }
        catch (LabBusyException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Laboratorium zajęte");
        }
    }

    private static Results<Ok<ExperimentInfo>, NotFound> Find(Guid id, ExperimentLab lab) =>
        lab.Find(id) is { } info ? TypedResults.Ok(info) : TypedResults.NotFound();

    private static Results<Ok<ExperimentInfo>, NotFound, ProblemHttpResult> Cancel(Guid id, ExperimentLab lab, HttpContext http)
    {
        try
        {
            return lab.Cancel(id, ClientKey.Of(http)) is { } info ? TypedResults.Ok(info) : TypedResults.NotFound();
        }
        catch (NotYourExperimentException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden, title: "Nie twój eksperyment");
        }
    }
}

/// <summary>Petla laboratorium przez caly czas zycia serwera; przy zatrzymaniu anuluje liczony eksperyment.</summary>
internal sealed class ExperimentWorker(ExperimentLab lab) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await lab.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // zwykle zatrzymanie serwera
        }
    }
}
