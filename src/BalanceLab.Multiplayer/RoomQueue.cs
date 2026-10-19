using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace BalanceLab.Multiplayer;

/// <summary>
/// Skrzynka pokoju (wzorzec aktora): praca wrzucona z wielu watkow wykonuje sie po jednej, w kolejnosci
/// wrzucenia, w jednej petli — wiec stan pokoju nie potrzebuje blokad. Wysylka do klientow ma druga,
/// osobna petle: wolny klient (pelny bufor gniazda) opoznia tylko wlasne wiadomosci, a nie ruchy i zegary.
/// </summary>
internal sealed partial class RoomQueue : IAsyncDisposable
{
    private readonly Channel<Func<Task>> _inbox = Channel.CreateUnbounded<Func<Task>>();
    private readonly Channel<Func<Task>> _outbox = Channel.CreateUnbounded<Func<Task>>();
    private readonly CancellationTokenSource _closing = new();
    private readonly ILogger _logger;
    private readonly Guid _room;
    private readonly Task _loop;
    private readonly Task _sender;

    public RoomQueue(Guid room, ILogger logger)
    {
        (_room, _logger) = (room, logger);
        _loop = Task.Run(() => DrainAsync(_inbox.Reader));
        _sender = Task.Run(() => DrainAsync(_outbox.Reader));
    }

    /// <summary>Anulowany przy zamknieciu: praca w tle (np. przerwa bota) konczy sie razem z pokojem.</summary>
    public CancellationToken Closing => _closing.Token;

    /// <summary>Praca z wynikiem dla wolajacego (takze bledem, np. <see cref="RoomException"/>).</summary>
    public Task<T> Post<T>(Func<Task<T>> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_inbox.Writer.TryWrite(async () =>
        {
            try
            {
                done.SetResult(await work());
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        }))
        {
            done.SetException(new RoomException("Pokoj jest juz zamkniety."));
        }

        return done.Task;
    }

    public Task<T> Post<T>(Func<T> work) => Post(() => Task.FromResult(work()));

    /// <summary>Praca bez wyniku, na ktora wolajacy czeka (i dostaje jej blad).</summary>
    public Task Post(Func<Task> work) => Post<bool>(async () =>
    {
        await work();
        return true;
    });

    public Task Post(Action work) => Post(() =>
    {
        work();
        return Task.CompletedTask;
    });

    /// <summary>Praca bez czekajacego (zegar, bot): blad trafia tylko do logu, a zamkniety pokoj ja pomija.</summary>
    public void Fire(Func<Task> work) => _inbox.Writer.TryWrite(() => Logged(work, LogBackgroundFailed));

    public void Fire(Action work) => Fire(() =>
    {
        work();
        return Task.CompletedTask;
    });

    /// <summary>Wysylka do klientow, po kolei; jej blad nie zatrzymuje partii (klient dostanie stan po powrocie).</summary>
    public void Send(Func<Task> send) => _outbox.Writer.TryWrite(() => Logged(send, LogSendFailed));

    /// <summary>Konczy prace w kolejce, a potem wysyla to, co zostalo do wyslania.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        await _closing.CancelAsync();
        _inbox.Writer.TryComplete();
        await _loop;
        _outbox.Writer.TryComplete();
        await _sender;
        _closing.Dispose();
    }

    private static async Task DrainAsync(ChannelReader<Func<Task>> reader)
    {
        await foreach (var work in reader.ReadAllAsync())
        {
            await work();
        }
    }

    private async Task Logged(Func<Task> work, Action<ILogger, Exception, Guid> log)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            log(_logger, ex, _room);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Pokoj {Room}: blad w tle")]
    private static partial void LogBackgroundFailed(ILogger logger, Exception exception, Guid room);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pokoj {Room}: nie udalo sie wyslac zmiany do klientow")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid room);
}
