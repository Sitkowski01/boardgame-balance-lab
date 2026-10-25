using System.Text.Json;
using BalanceLab.Catalog;
using BalanceLab.Experiments;
using BalanceLab.Multiplayer;

namespace BalanceLab.Demo.Tests;

/// <summary>REST demo: te same adresy, kody i ksztalty odpowiedzi co serwer (GameApi i ExperimentApi).</summary>
public sealed class DemoApiTests : IAsyncLifetime, IAsyncDisposable
{
    private readonly DemoServer _server = new((_, _, _) => { }, rooms: TestOptions.Rooms, lab: TestOptions.Lab);

    private DemoApi Api => _server.Api;

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    // xUnit 2 zamyka klase testowa przez IAsyncLifetime (IAsyncDisposable zna dopiero xUnit 3).
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    [Fact]
    public void Lists_the_games_of_the_catalog()
    {
        var response = Api.Handle("GET", "/api/games", null);

        Assert.Equal(200, response.Status);
        Assert.Equal(GameInfo.All.Select(g => g.Key), ((IEnumerable<GameInfo>)response.Body!).Select(g => g.Key));
    }

    [Theory]
    [InlineData("/api/games/klejnoty/rules", 200)]
    [InlineData("/api/games/KLEJNOTY/rules?variant=win%3D13", 200)]
    [InlineData("/api/games/klejnoty/rules?variant=bzdura", 400)]
    [InlineData("/api/games/connectfour/rules", 404)]
    [InlineData("/api/games/szachy/rules", 404)]
    public void Rules_answer_like_the_server(string path, int status)
    {
        var response = Api.Handle("GET", path, null);

        Assert.Equal(status, response.Status);
        if (status == 400)
        {
            Assert.Equal(400, Assert.IsType<Problem>(response.Body).Status);
        }
    }

    [Fact]
    public async Task Rooms_and_a_single_room_come_from_the_lobby()
    {
        var connection = _server.Hub.Connect();
        var ticket = await _server.Hub.CreateRoomAsync(connection, new CreateRoomArgs("connectfour", 2, null, "Ala"));

        var rooms = (IReadOnlyList<RoomInfo>)Api.Handle("GET", "/api/rooms", null).Body!;
        var room = Api.Handle("GET", $"/api/rooms/{ticket.Room}", null);

        Assert.Equal(ticket.Room, Assert.Single(rooms).Id);
        Assert.Equal(ticket.Room, Assert.IsType<RoomInfo>(room.Body).Id);
        Assert.Equal(404, Api.Handle("GET", $"/api/rooms/{Guid.NewGuid()}", null).Status);
    }

    [Fact]
    public async Task Matches_and_frames_come_from_the_memory_journal()
    {
        var room = await Showcase.PlayAsync(_server, Showcase.Games[0], TimeSpan.Zero);
        var match = _server.Lobby.Find(room).Info.MatchId!.Value;

        var replay = Assert.IsType<MatchReplay>(Api.Handle("GET", $"/api/matches/{match}", null).Body);
        var frame = Assert.IsType<ReplayFrame>(Api.Handle("GET", $"/api/matches/{match}/frames/2", null).Body);

        Assert.Equal((match, 2), (frame.Match, frame.Moves));
        Assert.Equal(replay.Hashes[1], frame.Hash);
        var beyond = Api.Handle("GET", $"/api/matches/{match}/frames/{replay.Moves.Length + 1}", null);
        Assert.Equal(400, beyond.Status);
        Assert.Equal($"Partia nie ma pozycji po {replay.Moves.Length + 1} ruchach.", ((Problem)beyond.Body!).Detail);
        Assert.Equal(400, Api.Handle("GET", $"/api/matches/{match}/frames/-1", null).Status);
        Assert.Equal(404, Api.Handle("GET", $"/api/matches/{match}/frames/99999999999", null).Status);
        Assert.Equal(404, Api.Handle("GET", $"/api/matches/{Guid.NewGuid()}", null).Status);
        Assert.Equal(404, Api.Handle("GET", $"/api/matches/{Guid.NewGuid()}/frames/0", null).Status);
    }

    [Fact]
    public async Task Experiment_is_accepted_computed_and_found()
    {
        var limits = Assert.IsType<ExperimentLimits>(Api.Handle("GET", "/api/experiments/limits", null).Body);
        Assert.Equal(DemoOptions.Lab.MaxGames, limits.MaxGames);

        var submitted = Api.Handle("POST", "/api/experiments", """{"game":"connectfour","kind":"Tournament","bots":["random","random"],"games":20,"seed":"5"}""");

        Assert.Equal(202, submitted.Status);
        var id = Assert.IsType<ExperimentInfo>(submitted.Body).Id;
        ExperimentInfo info;
        var tries = 0;
        do
        {
            await Task.Delay(20);
            info = Assert.IsType<ExperimentInfo>(Api.Handle("GET", $"/api/experiments/{id}", null).Body);
        }
        while (info.Status != ExperimentStatus.Done && ++tries < 500);

        Assert.Equal((ExperimentStatus.Done, 20), (info.Status, info.Done));
        Assert.NotNull(info.Result);
        Assert.Equal(id, Assert.Single((IReadOnlyList<ExperimentInfo>)Api.Handle("GET", "/api/experiments", null).Body!).Id);
        Assert.Equal(200, Api.Handle("DELETE", $"/api/experiments/{id}", null).Status);
        Assert.Equal(404, Api.Handle("DELETE", $"/api/experiments/{Guid.NewGuid()}", null).Status);
    }

    [Theory]
    [InlineData("""{"game":"szachy","kind":"Tournament","bots":["random","random"],"games":20,"seed":"5"}""", 400)]
    [InlineData("""{"game":"connectfour","kind":"Tournament","bots":["random","random"],"games":5000,"seed":"5"}""", 400)]
    [InlineData("""to nie json""", 400)]
    public void Bad_order_is_a_problem_with_a_reason(string body, int status)
    {
        var response = Api.Handle("POST", "/api/experiments", body);

        Assert.Equal(status, response.Status);
        Assert.False(string.IsNullOrEmpty(Assert.IsType<Problem>(response.Body).Detail));
    }

    [Fact]
    public void Full_lab_queue_is_503()
    {
        const string slow = """{"game":"klejnoty","kind":"Tournament","bots":["greedy","greedy"],"games":200,"seed":"5"}""";
        for (var i = 0; i < DemoOptions.Lab.MaxQueued; i++)
        {
            Assert.Equal(202, Api.Handle("POST", "/api/experiments/", slow).Status);
        }

        var busy = Api.Handle("POST", "/api/experiments", slow);

        Assert.Equal((503, "Laboratorium zajęte"), (busy.Status, ((Problem)busy.Body!).Title));
    }

    [Theory]
    [InlineData("GET", "/api/nie-ma", 404)]
    [InlineData("GET", "/api/rooms/nie-guid", 404)]
    [InlineData("POST", "/api/games", 405)]
    [InlineData("DELETE", "/api/rooms", 405)]
    [InlineData("PUT", "/api/experiments", 405)]
    [InlineData("POST", "/api/games/klejnoty/rules", 405)]
    [InlineData("DELETE", "/api/matches/00000000-0000-0000-0000-000000000001", 405)]
    public void Unknown_address_is_404_and_a_wrong_method_405_like_aspnet_routing(string method, string path, int status) =>
        Assert.Equal(status, Api.Handle(method, path, null).Status);

    [Fact]
    public void Json_is_camel_case_with_enums_as_text_like_the_server()
    {
        var json = JsonSerializer.Serialize(new SeatInfo(1, SeatKind.Bot, "greedy", false, false), DemoApi.Json);

        Assert.Equal("""{"seat":1,"kind":"Bot","name":"greedy","connected":false,"autopilot":false}""", json);
    }
}
