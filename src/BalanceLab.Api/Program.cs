using System.Net;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BalanceLab.Api;
using BalanceLab.Experiments;
using BalanceLab.Multiplayer;
using BalanceLab.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// Jak w CLI: polaczenie z konfiguracji albo ze zmiennej BALANCE_LAB_DB.
var connection = builder.Configuration.GetConnectionString("Journal") is { Length: > 0 } configured
    ? configured
    : Environment.GetEnvironmentVariable("BALANCE_LAB_DB")
        ?? throw new InvalidOperationException("Brak polaczenia z baza: ustaw ConnectionStrings:Journal albo zmienna BALANCE_LAB_DB.");

builder.Services.AddSingleton(new JournalDb(connection));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(builder.Configuration.GetSection("Rooms").Get<RoomOptions>() ?? new RoomOptions());
builder.Services.AddSingleton<IRoomNotifier, HubNotifier>();
builder.Services.AddSingleton<IMatchRecorderFactory>(sp => new JournalRecorderFactory(sp.GetRequiredService<JournalDb>()));
builder.Services.AddSingleton(sp => new Lobby(
    sp.GetRequiredService<IRoomNotifier>(),
    sp.GetRequiredService<IMatchRecorderFactory>(),
    sp.GetRequiredService<RoomOptions>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton(sp => new MatchArchive(sp.GetRequiredService<JournalDb>()));
builder.Services.AddHostedService<LobbyPruner>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Experiments").Get<ExperimentOptions>() ?? new ExperimentOptions());
builder.Services.AddSingleton(sp => new ExperimentLab(sp.GetRequiredService<ExperimentOptions>(), sp.GetRequiredService<TimeProvider>()));
builder.Services.AddHostedService<ExperimentWorker>();

// Wyliczenia jako tekst ("Playing"), takze w SignalR: klient nie musi znac kolejnosci w C#.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Limit wywolan huba na klienta, przed zamiana bledow pokoju: odmowa to zwykly HubException.
var hubCalls = builder.Configuration.GetValue("RateLimit:HubCallsPerMinute", 300);
builder.Services.AddSingleton(PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(
    key, _ => new FixedWindowRateLimiterOptions { PermitLimit = hubCalls, Window = TimeSpan.FromMinutes(1) })));
builder.Services.AddSignalR(o =>
    {
        o.AddFilter<HubCallLimiter>();
        o.AddFilter<RoomErrorFilter>();
    })
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();

// Limit zadan HTTP na adres IP (takze nowych polaczen z hubem): jeden klient nie zajmie lobby ani bazy.
var perMinute = builder.Configuration.GetValue("RateLimit:PerMinute", 120);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey.Of(context),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) }));
});

// Za odwrotnym proxy (nginx, Caddy) adres klienta jest w X-Forwarded-For; ufamy mu tylko od znanych proxy —
// inaczej kazdy moglby podac dowolny adres i obejsc limity. Bez konfiguracji: adres polaczenia.
var proxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in proxies)
    {
        o.KnownProxies.Add(IPAddress.Parse(proxy));
    }
});

var app = builder.Build();

if (proxies.Length > 0)
{
    app.UseForwardedHeaders();
}

if (app.Configuration.GetValue("Journal:Migrate", true))
{
    await app.Services.GetRequiredService<JournalDb>().MigrateAsync();
}

// Nieczytelne zadanie (zly JSON, nieznany rodzaj eksperymentu) to 400 jak w produkcji, a nie 500 w trybie deweloperskim.
app.UseExceptionHandler(new ExceptionHandlerOptions { StatusCodeSelector = ex => ex is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError });

// Zbudowany front (web/dist) z tego samego adresu co API: bez CORS i jeden proces do uruchomienia.
// Pliki ida przed limitem zadan (strona to kilka plikow naraz), a kazda sciezka bez rozszerzenia poza /api
// i /hubs to strona aplikacji (np. /rooms/{id} po odswiezeniu). Brakujacy plik (/assets/stary.js) to 404,
// a nie index.html, ktory przegladarka probowalaby wykonac jako skrypt.
if (app.Configuration["Web:Root"] is { Length: > 0 } root)
{
    var path = Path.GetFullPath(root);
    if (!Directory.Exists(path))
    {
        throw new InvalidOperationException($"Web:Root wskazuje katalog {path}, ktorego nie ma - zbuduj front (cd web && pnpm build).");
    }

    var provider = new PhysicalFileProvider(path);
    var files = new StaticFileOptions { FileProvider = provider, OnPrepareResponse = WebCache.Apply };
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
    app.UseStaticFiles(files);
    app.MapFallbackToFile("{*path:regex(^(?!api/|hubs/)):nonfile}", "index.html", files);
}

// Routing dopiero po plikach: inaczej trasa zapasowa (index.html) wygrywa takze z istniejacym plikiem.
app.UseRouting();
app.UseRateLimiter();
app.MapHub<GameHub>("/hubs/game");
app.MapGameApi();
app.MapExperimentApi();

await app.RunAsync();

/// <summary>Punkt wejscia; publiczny dla testow (WebApplicationFactory).</summary>
public partial class Program;
