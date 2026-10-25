using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BalanceLab.Catalog;
using BalanceLab.Experiments;
using BalanceLab.Multiplayer;

namespace BalanceLab.Demo;

/// <summary>Opis bledu jak ProblemDetails z ASP.NET Core: front czyta z niego <c>detail</c> i <c>title</c>.</summary>
public sealed record Problem(string? Title, string? Detail, int Status);

/// <summary>Odpowiedz na zadanie: kod HTTP i tresc (null = pusta).</summary>
public sealed record DemoResponse(int Status, object? Body);

/// <summary>
/// REST serwera bez serwera: te same adresy, kody i ksztalty odpowiedzi co GameApi i ExperimentApi, zeby front
/// nie wiedzial, czy rozmawia z ASP.NET Core, czy z WebAssembly w swojej karcie.
/// </summary>
public sealed partial class DemoApi(Lobby lobby, MemoryMatches matches, ExperimentLab lab)
{
    /// <summary>Wlasciciel eksperymentow w demo: w karcie przegladarki jest tylko jeden klient.</summary>
    internal const string Owner = "demo";

    /// <summary>JSON jak w API: nazwy camelCase (domyslne ASP.NET Core) i wyliczenia tekstem.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public DemoResponse Handle(string method, string path, string? body)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);
        var (route, query) = path.IndexOf('?', StringComparison.Ordinal) is var q and >= 0 ? (path[..q], path[(q + 1)..]) : (path, "");
        try
        {
            // Jak routing ASP.NET Core: nieznany adres to 404, znany adres z inna metoda to 405.
            return Endpoint(method.ToUpperInvariant(), route, query, body, out var known) is { } endpoint
                ? endpoint()
                : new DemoResponse(known ? 405 : 404, null);
        }
        catch (JsonException)
        {
            return Fail(400, "Nieczytelne żądanie", "Treść żądania nie jest poprawnym JSON-em.");
        }
    }

    /// <summary>Obsluga adresu i metody; null, gdy adresu nie ma (<paramref name="known"/> = false) albo metoda nie pasuje.</summary>
    private Func<DemoResponse>? Endpoint(string verb, string route, string query, string? body, out bool known)
    {
        known = true;
        switch (route)
        {
            case "/api/games":
                return verb == "GET" ? () => Ok(GameInfo.All) : null;
            case "/api/rooms":
                return verb == "GET" ? () => Ok(Rooms()) : null;
            case "/api/experiments" or "/api/experiments/":
                return verb switch { "GET" => () => Ok(lab.Recent), "POST" => () => Submit(body), _ => null };
            case "/api/experiments/limits":
                return verb == "GET" ? () => Ok(lab.Options.Limits) : null;
        }

        if (RulesRoute().Match(route) is { Success: true } rules)
        {
            return verb == "GET" ? () => Rules(Uri.UnescapeDataString(rules.Groups[1].Value), Query(query, "variant")) : null;
        }

        if (RoomRoute().Match(route) is { Success: true } room)
        {
            var id = Guid.Parse(room.Groups[1].Value, CultureInfo.InvariantCulture);
            return verb == "GET" ? () => lobby.Rooms.FirstOrDefault(r => r.Id == id) is { } info ? Ok(info) : NotFound() : null;
        }

        if (MatchRoute().Match(route) is { Success: true } match)
        {
            var id = Guid.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return verb == "GET" ? () => matches.Load(id) is { } replay ? Ok(replay) : NotFound() : null;
        }

        // Liczba spoza int nie pasuje do trasy {moves:int} serwera — to 404, a nie 400.
        if (FrameRoute().Match(route) is { Success: true } frame
            && int.TryParse(frame.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var moves))
        {
            var id = Guid.Parse(frame.Groups[1].Value, CultureInfo.InvariantCulture);
            return verb == "GET" ? () => Frame(id, moves) : null;
        }

        if (ExperimentRoute().Match(route) is { Success: true } experiment)
        {
            var id = Guid.Parse(experiment.Groups[1].Value, CultureInfo.InvariantCulture);
            return verb switch
            {
                "GET" => () => lab.Find(id) is { } info ? Ok(info) : NotFound(),
                "DELETE" => () => lab.Cancel(id, Owner) is { } info ? Ok(info) : NotFound(),
                _ => null,
            };
        }

        known = false;
        return null;
    }

    private IReadOnlyList<RoomInfo> Rooms()
    {
        lobby.Prune();
        return lobby.Rooms;
    }

    /// <summary>Jak w GameApi: zly wariant to 400, gra bez tabeli zasad albo nieznana to 404.</summary>
    private static DemoResponse Rules(string key, string? variant)
    {
        if (GameCatalog.All.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase)) is not { } game)
        {
            return NotFound();
        }

        try
        {
            return game.Sheet(variant) is { } sheet ? Ok(sheet) : NotFound();
        }
        catch (FormatException ex)
        {
            return Fail(400, null, ex.Message);
        }
    }

    private DemoResponse Frame(Guid id, int moves)
    {
        try
        {
            return matches.Frame(id, moves) is { } frame ? Ok(frame) : NotFound();
        }
        catch (ArgumentOutOfRangeException)
        {
            return Fail(400, null, $"Partia nie ma pozycji po {moves} ruchach.");
        }
    }

    /// <summary>Jak ExperimentApi: przyjete to 202, zle zamowienie 400, pelna kolejka 503.</summary>
    private DemoResponse Submit(string? body)
    {
        var request = JsonSerializer.Deserialize<ExperimentRequest>(body ?? "", Json)
            ?? throw new JsonException("Puste zamówienie.");
        try
        {
            return new DemoResponse(202, lab.Submit(request, Owner));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return Fail(400, "Złe zamówienie eksperymentu", ex.Message);
        }
        catch (LabBusyException ex)
        {
            return Fail(503, "Laboratorium zajęte", ex.Message);
        }
    }

    private static string? Query(string query, string name) =>
        query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p[0] == name)
            .Select(p => Uri.UnescapeDataString((p.Length > 1 ? p[1] : "").Replace('+', ' ')))
            .FirstOrDefault();

    private static DemoResponse Ok(object body) => new(200, body);

    private static DemoResponse NotFound() => new(404, null);

    private static DemoResponse Fail(int status, string? title, string detail) => new(status, new Problem(title, detail, status));

    private const string Id = "([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})";

    [GeneratedRegex("^/api/games/([^/]+)/rules$")]
    private static partial Regex RulesRoute();

    [GeneratedRegex($"^/api/rooms/{Id}$")]
    private static partial Regex RoomRoute();

    [GeneratedRegex($"^/api/matches/{Id}$")]
    private static partial Regex MatchRoute();

    [GeneratedRegex($"^/api/matches/{Id}/frames/(-?[0-9]+)$")]
    private static partial Regex FrameRoute();

    [GeneratedRegex($"^/api/experiments/{Id}$")]
    private static partial Regex ExperimentRoute();
}
