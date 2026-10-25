using System.Collections.Concurrent;
using System.Security.Cryptography;
using BalanceLab.Experiments;
using BalanceLab.Multiplayer;

namespace BalanceLab.Demo;

/// <summary>
/// Ustawienia demo. Wszystko liczy jeden watek przegladarki, wiec boty i laboratorium sa lzejsze niz na serwerze,
/// a skonczone partie zostaja w lobby do zamkniecia karty (sa tez pokazem dla kogos, kto dopiero wszedl).
/// </summary>
public static class DemoOptions
{
    public static RoomOptions Rooms { get; } = new()
    {
        // Boty musza dzialac w kazdej grze katalogu (Czworki nie maja oceny pozycji dla greedy).
        FillBot = "ismcts:150",
        AutopilotBot = "ismcts:100",
        MaxBotIterations = 600,
        MaxBotSearches = 1,
        MaxRooms = 30,
        KeepFinished = TimeSpan.FromDays(1),
    };

    /// <summary>
    /// Laboratorium liczy w osobnym workerze (z wlasnym .NET), bo eksperyment zajmuje jego jedyny watek az do konca:
    /// stol by wtedy stal. Ten sam powod sprawia, ze limit czasu (zegar) nie przerwie liczenia — trzyma je budzet pracy.
    /// </summary>
    public static ExperimentOptions Lab { get; } = new()
    {
        MaxGames = 200,
        MaxBots = 4,
        MaxBotIterations = 50,
        MaxQueued = 2,
        MaxQueuedPerOwner = 2,
        Threads = 1,
        // Partie botow prostych kosztuja 1, MCTS — liczbe iteracji. 6000 miesci domyslne zamowienia formularza
        // (np. 100 partii MCTS 50 z losowym w Czworki) i trzyma eksperymenty w granicach sekund do minuty.
        MaxWork = 6_000,
        MaxDuration = TimeSpan.FromMinutes(3),
    };
}

/// <summary>
/// Serwer Balance Lab w jednym obiekcie, bez sieci: lobby z pokojami, partie w pamieci, laboratorium i REST.
/// Przegladarka (WebAssembly) wola <see cref="Hub"/> i <see cref="Api"/>, a zmiany dostaje przez <see cref="DemoSend"/>.
/// </summary>
public sealed class DemoServer : IAsyncDisposable
{
    private readonly AsyncLocal<ulong?> _seed = new();
    private readonly ConcurrentDictionary<string, Action<string, object>> _local = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _lab;

    public DemoServer(DemoSend send, TimeProvider? clock = null, RoomOptions? rooms = null, ExperimentOptions? lab = null)
    {
        Clock = clock ?? TimeProvider.System;
        ArgumentNullException.ThrowIfNull(send);
        // Polaczenia pokazu (gracz grajacy sam w lobby) dostaja zmiany tutaj; reszta idzie do przegladarki.
        Hub = new DemoHub((connection, kind, payload) =>
        {
            if (_local.TryGetValue(connection, out var local))
            {
                local(kind, payload);
            }
            else
            {
                send(connection, kind, payload);
            }
        });
        Matches = new MemoryMatches(Clock);
        // Ziarno pokazu (pokaz musi znac rozdanie swojego stolu) albo losowe.
        Lobby = new Lobby(Hub, Matches, rooms ?? DemoOptions.Rooms, Clock, seeds: NextSeed);
        Hub.Lobby = Lobby;
        Lab = new ExperimentLab(lab ?? DemoOptions.Lab, Clock);
        Api = new DemoApi(Lobby, Matches, Lab);
        _lab = Lab.RunAsync(_stop.Token);
    }

    public TimeProvider Clock { get; }

    public DemoHub Hub { get; }

    public DemoApi Api { get; }

    public Lobby Lobby { get; }

    public MemoryMatches Matches { get; }

    public ExperimentLab Lab { get; }

    /// <summary>Polaczenie, ktorego zmiany dostaje kod w .NET (pokaz), a nie przegladarka.</summary>
    internal string ConnectLocal(Action<string, object> receive)
    {
        var connection = Hub.Connect();
        _local[connection] = receive;
        return connection;
    }

    /// <summary>
    /// Pokoj zalozony w <paramref name="create"/> dostaje to ziarno rozdania. Ziarno idzie z przeplywem wywolania
    /// (AsyncLocal), a nie przez wspolna kolejke: nieudane zalozenie nie zostawi go dla pokoju kogos innego.
    /// </summary>
    internal async Task<T> WithSeedAsync<T>(ulong seed, Func<Task<T>> create)
    {
        // Zmiana AsyncLocal w metodzie async nie wraca do wolajacego: po wyjsciu ziarno samo znika.
        _seed.Value = seed;
        return await create();
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _lab;
        }
        catch (OperationCanceledException)
        {
            // zwykle zatrzymanie
        }

        await Lobby.DisposeAsync();
        _stop.Dispose();
    }

    private ulong NextSeed() =>
        _seed.Value ?? BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));
}
