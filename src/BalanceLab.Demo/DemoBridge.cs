using System.Text.Json;
using BalanceLab.Experiments;
using BalanceLab.Multiplayer;

namespace BalanceLab.Demo;

/// <summary>
/// Granica z JavaScriptem: wywolania huba i zadania REST przychodza jako tekst JSON i jako tekst wracaja.
/// Odmowa pokoju (zly ruch, pelny stol) to <c>{"error": ..., "refusal": true}</c> — klient pokazuje ja jak
/// HubException z serwera; kazdy inny blad to blad demo, nie gracza.
/// </summary>
public sealed class DemoBridge : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();

    /// <param name="emit">Zmiana dla polaczenia: identyfikator, rodzaj ("room", "view", "move", "lobby") i JSON.</param>
    public DemoBridge(Action<string, string, string> emit, TimeProvider? clock = null, RoomOptions? rooms = null, ExperimentOptions? lab = null)
    {
        ArgumentNullException.ThrowIfNull(emit);
        Server = new DemoServer((connection, kind, payload) => emit(connection, kind, Serialize(payload)), clock, rooms, lab);
    }

    public DemoServer Server { get; }

    public string Connect() => Server.Hub.Connect();

    /// <summary>Metoda huba po nazwie (jak w SignalR) z argumentami w tablicy JSON; wynik jako JSON.</summary>
    public async Task<string> InvokeAsync(string connection, string method, string arguments)
    {
        try
        {
            using var json = JsonDocument.Parse(arguments);
            var args = json.RootElement;
            var hub = Server.Hub;
            object? result = method switch
            {
                "CreateRoom" => await hub.CreateRoomAsync(connection, args[0].Deserialize<CreateRoomArgs>(DemoApi.Json)!),
                "JoinRoom" => await hub.JoinRoomAsync(connection, args[0].GetGuid(), args[1].GetString()!),
                "Rejoin" => await hub.RejoinAsync(connection, args[0].GetGuid(), args[1].GetString()!),
                "Watch" => await hub.WatchAsync(connection, args[0].GetGuid()),
                "WatchLobby" => hub.WatchLobby(connection),
                "AddBot" => await Done(hub.AddBotAsync(connection, args[0].GetInt32(), args[1].GetString()!)),
                "Start" => await Done(hub.StartAsync(connection)),
                "MakeMove" => await Done(hub.MakeMoveAsync(connection, args[0].GetInt32(), args[1].GetString()!)),
                "Close" => await Done(hub.CloseAsync(connection)),
                _ => throw new RoomException($"Hub nie ma metody {method}."),
            };
            return JsonSerializer.Serialize(new { value = result }, DemoApi.Json);
        }
        catch (RoomException ex)
        {
            return Error(ex.Message, refusal: true);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or IndexOutOfRangeException or KeyNotFoundException)
        {
            // Zle argumenty to blad klienta demo, nie odmowa gry: tak jak serwer, ktory nie zrozumialby wywolania.
            return Error($"Nieczytelne wywołanie {method}: {ex.Message}", refusal: false);
        }
    }

    /// <summary>Zadanie REST; wynik to <c>{"status": kod, "body": tresc}</c>.</summary>
    public string Fetch(string method, string path, string? body)
    {
        var response = Server.Api.Handle(method, path, body);
        return JsonSerializer.Serialize(new { status = response.Status, body = response.Body }, DemoApi.Json);
    }

    /// <summary>
    /// Pokazowe partie po kolei, jedna za druga — zeby lobby od pierwszej chwili mialo co pokazac.
    /// Bledy pokazu nie moga zatrzymac demo.
    /// </summary>
    public async Task PlayShowcaseAsync(TimeSpan thinking)
    {
        foreach (var game in Showcase.Games)
        {
            try
            {
                await Showcase.PlayAsync(Server, game, thinking, _stop.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Pokaz jest dodatkiem: nieudana partia (np. pelne lobby) nie zatrzymuje nastepnych ani demo.
                Console.Error.WriteLine($"Pokaz {game.Game} nie wyszedl: {ex.Message}");
            }
        }
    }

    /// <summary>Maly eksperyment na start: laboratorium od razu ma wynik do obejrzenia.</summary>
    public void SubmitShowcaseExperiment() => Server.Lab.Submit(Showcase.Experiment, DemoApi.Owner);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await Server.DisposeAsync();
        _stop.Dispose();
    }

    /// <summary>Wiadomosc z typem z chwili wyslania (widok gry to <c>object</c>), w JSON-ie jak z serwera.</summary>
    internal static string Serialize(object payload) => JsonSerializer.Serialize(payload, payload.GetType(), DemoApi.Json);

    private static async Task<object?> Done(Task call)
    {
        await call;
        return null;
    }

    private static string Error(string message, bool refusal) =>
        JsonSerializer.Serialize(new { error = message, refusal }, DemoApi.Json);
}

