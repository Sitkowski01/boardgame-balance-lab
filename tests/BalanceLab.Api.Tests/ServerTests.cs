using System.Net;
using System.Net.Http.Json;
using BalanceLab.Multiplayer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace BalanceLab.Api.Tests;

/// <summary>Konfiguracja serwera: baza, migracje, limit zadan, obsluga bledow i sprzatanie lobby.</summary>
[Collection(SharedApi.Name)]
public sealed class ServerTests(ApiFixture api)
{
    [Fact]
    public async Task Each_address_has_a_request_limit_per_minute()
    {
        await using var limited = api.Factory.WithWebHostBuilder(web => web.UseSetting("RateLimit:PerMinute", "3"));
        var http = limited.CreateClient();

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            codes.Add((await http.GetAsync("/health")).StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], codes);
    }

    [Fact]
    public async Task Hub_calls_have_their_own_limit_per_client()
    {
        await using var limited = api.Factory.WithWebHostBuilder(web => web.UseSetting("RateLimit:HubCallsPerMinute", "2"));
        await using var browser = await ApiFixture.ConnectAsync(limited);

        await browser.Hub.InvokeAsync<List<RoomInfo>>("WatchLobby");
        await browser.Hub.InvokeAsync<List<RoomInfo>>("WatchLobby");
        var ex = await Assert.ThrowsAsync<HubException>(() => browser.Hub.InvokeAsync<List<RoomInfo>>("WatchLobby"));

        Assert.Contains("Za duzo prosb naraz - zwolnij na chwile.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_client_cannot_fill_the_lobby_with_rooms()
    {
        await using var limited = api.Factory.WithWebHostBuilder(web => web.UseSetting("Rooms:MaxRoomsPerOwner", "1"));
        await using var ala = await ApiFixture.ConnectAsync(limited);
        await using var again = await ApiFixture.ConnectAsync(limited);
        await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));

        var ex = await Assert.ThrowsAsync<HubException>(() => again.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala")));

        Assert.Contains("Masz juz 1 otwarte pokoje", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "local")]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
    public void Clients_are_told_apart_by_address_and_ipv6_by_its_network(string? address, string key) =>
        Assert.Equal(key, ClientKey.Of(address is null ? null : IPAddress.Parse(address)));

    [Fact]
    public async Task Client_address_comes_from_a_known_proxy_header_and_from_nobody_else()
    {
        await using var proxied = api.Factory.WithWebHostBuilder(web => web
            .UseSetting("ForwardedHeaders:KnownProxies:0", "10.0.0.5")
            .UseSetting("RateLimit:PerMinute", "1"));
        async Task<int> Health(string from, string forwardedFor)
        {
            var context = await proxied.Server.SendAsync(c =>
            {
                c.Request.Method = "GET";
                c.Request.Path = "/health";
                c.Connection.RemoteIpAddress = IPAddress.Parse(from);
                c.Request.Headers["X-Forwarded-For"] = forwardedFor;
            });
            return context.Response.StatusCode;
        }

        // Przez proxy: kazdy klient z naglowka ma wlasny limit. Wprost: naglowek nic nie zmienia, liczy sie polaczenie.
        int[] viaProxy = [await Health("10.0.0.5", "203.0.113.1"), await Health("10.0.0.5", "203.0.113.2"), await Health("10.0.0.5", "203.0.113.1")];
        int[] direct = [await Health("10.0.0.9", "203.0.113.3"), await Health("10.0.0.9", "203.0.113.4")];
        Assert.Equal([200, 200, 429], viaProxy);
        Assert.Equal([200, 429], direct);
    }

    [Fact]
    public async Task Server_without_migrations_answers_a_database_error_as_problem_details()
    {
        // Migracje wylaczone na pustej bazie: pytanie o partie konczy sie bledem bazy, a klient dostaje 500 bez stosu.
        var empty = await api.CreateDatabaseAsync();
        await using var server = api.Factory.WithWebHostBuilder(web => web
            .UseSetting("ConnectionStrings:Journal", empty)
            .UseSetting("Journal:Migrate", "false"));

        var response = await server.CreateClient().GetAsync($"/api/matches/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Server_serves_the_built_front_and_its_pages_but_not_unknown_api_paths()
    {
        var root = Directory.CreateTempSubdirectory("balance-lab-web").FullName;
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "<p>aplikacja</p>");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        await File.WriteAllTextAsync(Path.Combine(root, "assets", "app-1a2b.js"), "start()");
        await using var server = api.Factory.WithWebHostBuilder(web => web.UseSetting("Web:Root", root));
        var http = server.CreateClient();

        Assert.Equal("start()", await http.GetStringAsync("/assets/app-1a2b.js"));
        Assert.Equal("<p>aplikacja</p>", await http.GetStringAsync("/"));
        Assert.Equal("<p>aplikacja</p>", await http.GetStringAsync($"/rooms/{Guid.NewGuid()}"));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/nie-ma")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/hubs/nie-ma")).StatusCode);
        Assert.Equal("[", (await http.GetStringAsync("/api/games"))[..1]);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/assets/stary-0000.js")).StatusCode);
    }

    [Fact]
    public async Task Built_assets_are_cached_for_good_and_pages_are_always_checked()
    {
        var root = Directory.CreateTempSubdirectory("balance-lab-web").FullName;
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "<p>aplikacja</p>");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        await File.WriteAllTextAsync(Path.Combine(root, "assets", "app-1a2b.js"), "start()");
        await using var server = api.Factory.WithWebHostBuilder(web => web.UseSetting("Web:Root", root));
        var http = server.CreateClient();

        async Task<string?> CacheOf(string url) => (await http.GetAsync(url)).Headers.CacheControl?.ToString();

        Assert.Equal("public, max-age=31536000, immutable", await CacheOf("/assets/app-1a2b.js"));
        Assert.Equal("no-cache", await CacheOf("/"));
        Assert.Equal("no-cache", await CacheOf($"/matches/{Guid.NewGuid()}"));
    }

    [Fact]
    public async Task Front_files_do_not_count_towards_the_request_limit()
    {
        var root = Directory.CreateTempSubdirectory("balance-lab-web").FullName;
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "<p>aplikacja</p>");
        await using var server = api.Factory.WithWebHostBuilder(web => web.UseSetting("Web:Root", root).UseSetting("RateLimit:PerMinute", "1"));
        var http = server.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/index.html")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public void Missing_front_directory_stops_the_server_with_a_clear_message()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"balance-lab-brak-{Guid.NewGuid():N}");
        using var server = api.Factory.WithWebHostBuilder(web => web.UseSetting("Web:Root", missing));

        var ex = Assert.Throws<InvalidOperationException>(() => server.Server);

        Assert.Contains("zbuduj front", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_front_directory_pages_are_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await api.Http().GetAsync("/rooms/x")).StatusCode);
    }

    [Fact]
    public async Task Server_migrates_a_new_database_at_start()
    {
        var empty = await api.CreateDatabaseAsync();
        await using var server = api.Factory.WithWebHostBuilder(web => web.UseSetting("ConnectionStrings:Journal", empty));

        Assert.Equal(HttpStatusCode.NotFound, (await server.CreateClient().GetAsync($"/api/matches/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Connection_comes_from_the_environment_when_configuration_has_none()
    {
        var empty = await api.CreateDatabaseAsync();
        var before = Environment.GetEnvironmentVariable("BALANCE_LAB_DB");
        Environment.SetEnvironmentVariable("BALANCE_LAB_DB", empty);
        try
        {
            await using var server = api.Factory.WithWebHostBuilder(web => web.UseSetting("ConnectionStrings:Journal", ""));
            Assert.Equal(HttpStatusCode.NotFound, (await server.CreateClient().GetAsync($"/api/matches/{Guid.NewGuid()}")).StatusCode);

            Environment.SetEnvironmentVariable("BALANCE_LAB_DB", null);
            await using var none = api.Factory.WithWebHostBuilder(web => web.UseSetting("ConnectionStrings:Journal", ""));
            var ex = Assert.Throws<InvalidOperationException>(() => none.Server);
            Assert.Equal("Brak polaczenia z baza: ustaw ConnectionStrings:Journal albo zmienna BALANCE_LAB_DB.", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BALANCE_LAB_DB", before);
        }
    }

    [Fact]
    public async Task Listing_the_lobby_cleans_it_first()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var server = api.Factory.WithWebHostBuilder(web => web.ConfigureTestServices(s =>
        {
            s.AddSingleton<TimeProvider>(clock);
            // Bez sprzatania w tle: sprzatac maja same listy (REST i hub).
            s.RemoveAll<IHostedService>();
        }));
        await using var ala = await ApiFixture.ConnectAsync(server);
        await using var ola = await ApiFixture.ConnectAsync(server);
        await using var browser = await ApiFixture.ConnectAsync(server);
        var first = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));

        clock.Advance(new RoomOptions().KeepWaiting);
        var rooms = await server.CreateClient().GetFromJsonAsync<RoomInfo[]>("/api/rooms", ApiFixture.Json);
        Assert.DoesNotContain(rooms!, r => r.Id == first.Room);

        var second = await ola.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ola"));
        clock.Advance(new RoomOptions().KeepWaiting);
        Assert.DoesNotContain(await browser.Hub.InvokeAsync<List<RoomInfo>>("WatchLobby"), r => r.Id == second.Room);
    }

    [Fact]
    public async Task Lobby_is_cleaned_every_minute_in_the_background()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var server = api.Factory.WithWebHostBuilder(web => web.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(clock)));
        Assert.Contains(server.Services.GetServices<IHostedService>(), s => s is LobbyPruner);
        var lobby = server.Services.GetRequiredService<Lobby>();
        await using var ala = await ApiFixture.ConnectAsync(server);
        var room = await ala.Hub.InvokeAsync<SeatTicket>("CreateRoom", new CreateRoomArgs("connectfour", 2, null, "Ala"));

        clock.Advance(new RoomOptions().KeepWaiting);
        for (var i = 0; i < 100 && lobby.Rooms.Any(r => r.Id == room.Room); i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(20);
        }

        Assert.DoesNotContain(lobby.Rooms, r => r.Id == room.Room);
    }
}
