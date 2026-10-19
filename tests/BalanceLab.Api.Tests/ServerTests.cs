using System.Net;
using System.Net.Http.Json;
using BalanceLab.Multiplayer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
