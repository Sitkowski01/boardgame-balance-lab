using BalanceLab.Games.Klejnoty;

namespace BalanceLab.Demo.Tests;

/// <summary>Routing REST demo jak ASP.NET Core: parametry zapytania, 405 dla innej metody i tytuly bledow.</summary>
public sealed class DemoApiRoutingTests
{
    [Theory]
    [InlineData("/api/games/klejnoty/rules?inne=1&variant=win%3D13", 13)]
    [InlineData("/api/games/klejnoty/rules?variant=win%3D12+", 12)]
    [InlineData("/api/games/klejnoty/rules?variant", 15)]
    [InlineData("/api/games/klejnoty/rules", 15)]
    public async Task Rules_read_the_variant_from_the_query(string path, int winningPoints)
    {
        await using var server = new DemoServer((_, _, _) => { }, rooms: TestOptions.Rooms, lab: TestOptions.Lab);

        var sheet = Assert.IsType<KlejnotySheet>(server.Api.Handle("GET", path, null).Body);

        Assert.Equal(winningPoints, sheet.WinningPoints);
    }

    [Theory]
    [InlineData("POST", "/api/experiments/limits")]
    [InlineData("POST", "/api/rooms/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/matches/00000000-0000-0000-0000-000000000001/frames/0")]
    [InlineData("DELETE", "/api/games")]
    public async Task Known_address_with_another_method_is_405(string method, string path)
    {
        await using var server = new DemoServer((_, _, _) => { }, rooms: TestOptions.Rooms, lab: TestOptions.Lab);

        Assert.Equal(405, server.Api.Handle(method, path, null).Status);
    }

    [Theory]
    [InlineData("null", "Nieczytelne żądanie")]
    [InlineData("{", "Nieczytelne żądanie")]
    [InlineData("""{"game":"szachy","kind":"Tournament","bots":["random","random"],"games":20,"seed":"5"}""", "Złe zamówienie eksperymentu")]
    public async Task Bad_experiment_order_has_a_title_like_the_server(string body, string title)
    {
        await using var server = new DemoServer((_, _, _) => { }, rooms: TestOptions.Rooms, lab: TestOptions.Lab);

        var problem = Assert.IsType<Problem>(server.Api.Handle("POST", "/api/experiments", body).Body);

        Assert.Equal((title, 400), (problem.Title, problem.Status));
    }
}
